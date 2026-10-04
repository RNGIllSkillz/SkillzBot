#!/usr/bin/env python3
"""Build SkillzBot and the web panel and push them to the Alpine host over SSH (password login).

First-time population of an empty host (installs packages, service and nginx, copies the bot data,
the proxy binary, then the bot and the panel, and starts the service):

    python deploy/deploy.py --host 192.168.254.154 --init ^
        --data "\\\\192.168.255.10\\skillzbot_data\\skillzbotdata\\Channels_Data" ^
        --channel general_hs_ --tz Europe/Moscow [--proxy-bin C:\\tools\\xray]

Every later update:

    python deploy/deploy.py --host 192.168.254.154

Options: --skip-bot / --skip-web (deploy only one part), --no-build (reuse deploy/out), --no-restart
(stage files; the next restart from the panel picks them up), --dry-run (build and package only).

The password is asked interactively (hidden). It can also come from the SKILLZBOT_SSH_PASSWORD
environment variable or --password; nothing is ever written to disk by this script.

Needs on this machine: dotnet SDK, node + npm (for the panel) and the paramiko package:
    pip install paramiko
"""
import argparse
import getpass
import os
import shutil
import stat
import subprocess
import sys
import tarfile
import time
from pathlib import Path

# absolute(), not resolve(): on Windows resolve() turns a mapped drive (Z:\...) into its UNC path,
# and cmd.exe (which runs npm.cmd) cannot use a UNC path as the working directory.
ROOT = Path(__file__).absolute().parents[1]
OUT = ROOT / "deploy" / "out"
ALPINE = ROOT / "deploy" / "alpine"


# ----------------------------------------------------------------------------- local build

def run(cmd, cwd=None):
    print("$", " ".join(str(c) for c in cmd), flush=True)
    if os.name == "nt" and cwd is not None and str(cwd).startswith("\\\\"):
        # Still a UNC path (script started from \\server\share\...): pushd maps a temporary drive letter for cmd.exe.
        line = subprocess.list2cmdline([str(c) for c in cmd])
        subprocess.run(f'pushd "{cwd}" && {line}', shell=True, check=True)
        return
    subprocess.run(cmd, cwd=cwd, check=True)


def build_bot():
    bot_out = OUT / "bot"
    run(["dotnet", "publish", str(ROOT / "SkillzBot.csproj"), "-c", "Release", "-r", "linux-musl-x64",
         "--self-contained", "true", "-p:PublishSingleFile=true", "-o", str(bot_out)])
    if not (bot_out / "SkillzBot").exists():
        sys.exit("publish produced no SkillzBot executable")
    return bot_out


def build_web(install):
    web = ROOT / "web"
    npm = "npm.cmd" if os.name == "nt" else "npm"
    if install or not (web / "node_modules").exists():
        run([npm, "install", "--no-audit", "--no-fund"], cwd=web)
    run([npm, "run", "build"], cwd=web)
    dist = web / "dist"
    if not (dist / "index.html").exists():
        sys.exit("web build produced no dist/index.html")
    return dist


def package(bot_dir, web_dir):
    OUT.mkdir(parents=True, exist_ok=True)
    pkg = OUT / f"skillzbot-{time.strftime('%Y%m%d-%H%M%S')}.tar.gz"
    with tarfile.open(pkg, "w:gz") as tar:
        if bot_dir:
            for f in bot_dir.iterdir():
                if f.suffix == ".pdb":
                    continue
                info = tar.gettarinfo(str(f), arcname=f.name)
                info.mode = 0o755 if f.name == "SkillzBot" else 0o644
                info.uid = info.gid = 0
                with open(f, "rb") as fh:
                    tar.addfile(info, fh)
        if web_dir:
            tar.add(str(web_dir), arcname="web", filter=_root_owned)
    print(f"package {pkg} ({pkg.stat().st_size // 1024 // 1024} MB)")
    return pkg


def _root_owned(info):
    info.uid = info.gid = 0
    return info


def package_data(data_dir: Path, include_logs: bool):
    """Tarball of an existing Channels_Data folder (logs skipped unless asked)."""
    if not (data_dir.is_dir()):
        sys.exit(f"--data: {data_dir} is not a directory")
    pkg = OUT / "channels-data.tar.gz"
    OUT.mkdir(parents=True, exist_ok=True)
    count = 0

    def flt(info):
        nonlocal count
        parts = Path(info.name).parts
        if not include_logs and "logs" in parts and info.isfile():
            return None
        info.uid = info.gid = 0
        if info.isfile():
            count += 1
        return info

    with tarfile.open(pkg, "w:gz") as tar:
        tar.add(str(data_dir), arcname=".", filter=flt)
    print(f"data package {pkg}: {count} files from {data_dir}")
    return pkg


# ----------------------------------------------------------------------------- remote side

class Host:
    def __init__(self, args, password):
        try:
            import paramiko
        except ImportError:
            sys.exit("paramiko is required:  pip install paramiko")
        self.paramiko = paramiko
        self.client = paramiko.SSHClient()
        self.client.set_missing_host_key_policy(paramiko.AutoAddPolicy())
        print(f"connecting to {args.user}@{args.host}:{args.port} ...")
        self.client.connect(args.host, port=args.port, username=args.user, password=password,
                            look_for_keys=False, allow_agent=False, timeout=25)
        self.sftp = self.client.open_sftp()

    def close(self):
        self.sftp.close()
        self.client.close()

    def sh(self, script, check=True):
        """Runs a shell script fragment on the host, streaming its output."""
        print(f"[remote] {script if len(script) < 160 else script[:157] + '...'}", flush=True)
        _, stdout, stderr = self.client.exec_command("sh -c " + _quote(script), get_pty=False)
        for line in iter(stdout.readline, ""):
            print("   ", line.rstrip())
        err = stderr.read().decode(errors="replace").strip()
        rc = stdout.channel.recv_exit_status()
        if err:
            print("   ", err.replace("\n", "\n    "))
        if check and rc != 0:
            sys.exit(f"remote command failed with exit code {rc}")
        return rc

    def put(self, local: Path, remote: str, mode=None):
        size = local.stat().st_size
        last = [0]

        def progress(done, total):
            pct = int(done * 100 / total) if total else 100
            if pct >= last[0] + 10 or done == total:
                last[0] = pct
                print(f"    upload {local.name}: {pct}%", flush=True)

        self.sftp.put(str(local), remote, callback=progress)
        if mode is not None:
            self.sftp.chmod(remote, mode)
        print(f"    uploaded {local.name} ({size // 1024} KB) -> {remote}")

    def put_dir(self, local: Path, remote: str):
        self.sh(f"mkdir -p '{remote}'")
        for f in sorted(local.iterdir()):
            if f.is_file():
                self.put(f, f"{remote}/{f.name}", 0o755 if f.suffix in ("", ".sh") else 0o644)

    def remote_dir_nonempty(self, path):
        return self.sh(f"[ -d '{path}' ] && [ \"$(ls -A '{path}' 2>/dev/null)\" ]", check=False) == 0


def _quote(s):
    return "'" + s.replace("'", "'\"'\"'") + "'"


# ----------------------------------------------------------------------------- steps

def step_init(host: Host, args):
    print("== host setup (packages, service, nginx)")
    host.put_dir(ALPINE, "/root/skillzbot-setup")
    host.sh(f"APP_DIR='{args.dir}' CHANNEL='{args.channel}' TZ='{args.tz}' API_PORT='{args.api_port}' sh /root/skillzbot-setup/install.sh")


def step_data(host: Host, args):
    target = f"{args.dir}/Channels_Data"
    if host.remote_dir_nonempty(target) and not args.data_overwrite:
        sys.exit(f"{target} on the host is not empty; add --data-overwrite to replace files with the local copy")
    pkg = package_data(Path(args.data), args.include_logs)
    host.put(pkg, "/tmp/skillzbot-data.tgz")
    host.sh(f"set -e; mkdir -p '{target}'; tar xzf /tmp/skillzbot-data.tgz -C '{target}'; rm -f /tmp/skillzbot-data.tgz; "
            f"echo 'channels on host:'; ls '{target}'")


def step_proxy_bin(host: Host, args):
    local = Path(args.proxy_bin)
    if not local.is_file():
        sys.exit(f"--proxy-bin: {local} is not a file")
    remote = f"{args.dir}/proxy/{local.name}"
    host.sh(f"mkdir -p '{args.dir}/proxy'")
    host.put(local, remote, 0o755)
    print(f"    set ProxyCorePath to {remote} in the channel config")


def step_deploy(host: Host, args, pkg, bot, web):
    host.put(pkg, "/tmp/skillzbot-upload.tgz")
    d = args.dir
    steps = ["set -e", f"mkdir -p '{d}'"]
    if not args.no_restart:
        steps.append("rc-service skillzbot status >/dev/null 2>&1 && rc-service skillzbot stop || true")
    if web:
        steps.append(f"rm -rf '{d}/web'")
    steps += [f"tar xzf /tmp/skillzbot-upload.tgz -C '{d}'", "rm -f /tmp/skillzbot-upload.tgz"]
    if bot:
        steps.append(f"chmod +x '{d}/SkillzBot'")
    if not args.no_restart:
        steps += ["rc-service skillzbot start", "sleep 4", "rc-service skillzbot status",
                  f"tail -n 15 '{d}/Channels_Data/{args.channel}/DATA/logs/bot-'$(date +%Y%m%d)'.log' 2>/dev/null || true"]
    else:
        steps.append("echo 'files staged; restart the bot from the panel or: rc-service skillzbot restart'")
    host.sh("; ".join(steps))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", required=True, help="Alpine host (IP or name)")
    ap.add_argument("--user", default="root")
    ap.add_argument("--port", type=int, default=22)
    ap.add_argument("--password", default=None, help="SSH password (otherwise SKILLZBOT_SSH_PASSWORD or a hidden prompt)")
    ap.add_argument("--dir", default="/opt/skillzbot", help="install directory on the host")
    ap.add_argument("--channel", default="general_hs_", help="ENV_CHANNEL_NAME")
    ap.add_argument("--tz", default="Europe/Moscow", help="time zone for the service (used with --init)")
    ap.add_argument("--api-port", type=int, default=8080, help="bot API port nginx proxies to (used with --init)")
    ap.add_argument("--init", action="store_true", help="first-time host setup: packages, service, nginx")
    ap.add_argument("--data", default=None, help="local Channels_Data folder to copy to the host (UNC paths work)")
    ap.add_argument("--data-overwrite", action="store_true", help="copy --data even if the host already has files")
    ap.add_argument("--include-logs", action="store_true", help="copy old log files along with --data")
    ap.add_argument("--proxy-bin", default=None, help="local xray/hysteria binary to place under <dir>/proxy/")
    ap.add_argument("--skip-bot", action="store_true", help="do not build/upload the bot")
    ap.add_argument("--skip-web", action="store_true", help="do not build/upload the web panel")
    ap.add_argument("--no-build", action="store_true", help="reuse deploy/out from the previous run")
    ap.add_argument("--no-restart", action="store_true", help="stage files only; the next restart picks them up")
    ap.add_argument("--npm-install", action="store_true", help="run npm install before building the panel")
    ap.add_argument("--dry-run", action="store_true", help="build and package, do not connect")
    args = ap.parse_args()

    if not args.no_build and OUT.exists():
        shutil.rmtree(OUT)
    bot_dir = None if args.skip_bot else (OUT / "bot" if args.no_build else build_bot())
    web_dir = None if args.skip_web else (ROOT / "web" / "dist" if args.no_build else build_web(args.npm_install))
    pkg = package(bot_dir, web_dir) if (bot_dir or web_dir) else None
    if args.data and args.dry_run:
        package_data(Path(args.data), args.include_logs)
    if args.dry_run:
        print("dry run: nothing uploaded")
        return

    password = args.password or os.environ.get("SKILLZBOT_SSH_PASSWORD") or getpass.getpass(f"password for {args.user}@{args.host}: ")
    host = Host(args, password)
    try:
        if args.init:
            step_init(host, args)
        if args.data:
            step_data(host, args)
        if args.proxy_bin:
            step_proxy_bin(host, args)
        if pkg:
            step_deploy(host, args, pkg, bot_dir, web_dir)
        print("deploy finished")
    finally:
        host.close()


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as e:
        sys.exit(f"command failed with exit code {e.returncode}")
    except KeyboardInterrupt:
        sys.exit("\ninterrupted")
