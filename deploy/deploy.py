#!/usr/bin/env python3
"""Publish the bot on this workstation, ship it with the web panel sources to the Alpine host over SSH
(password login); the host builds the panel with npm and installs both. No .NET SDK on the host.

First-time population of an empty host (installs packages incl. Node, the service and nginx, copies the
bot data and the proxy binary, uploads bot + panel sources, builds the panel, starts the service):

    python deploy/deploy.py --host 192.168.254.154 --init ^
        --data "\\\\192.168.255.10\\skillzbot_data\\skillzbotdata\\Channels_Data" ^
        --channel general_hs_ --tz Europe/Moscow [--proxy-bin C:\\tools\\xray]

Every later update (publish the bot here, upload, build the panel on the host, restart):

    python deploy/deploy.py --host 192.168.254.154

Options: --skip-bot / --skip-web (ship only one part), --no-build (reuse the previous publish output),
--no-restart (install, let the next restart from the panel pick it up), --dry-run (build and package only).

The password is asked interactively (hidden); it can also come from the SKILLZBOT_SSH_PASSWORD
environment variable or --password. Nothing is written to disk by this script.

Needs on this machine: the .NET SDK (6 or newer), Python 3 and the paramiko package (pip install paramiko).
The host needs internet access for npm while the panel builds.
"""
import argparse
import getpass
import os
import subprocess
import sys
import tarfile
import tempfile
import time
from pathlib import Path

# absolute(), not resolve(): on Windows resolve() turns a mapped drive (Z:\...) into a UNC path.
ROOT = Path(__file__).absolute().parents[1]
ALPINE = ROOT / "deploy" / "alpine"
OUT = ROOT / "deploy" / "out"
WEB_FILES = ["package.json", "package-lock.json", "index.html", "vite.config.ts", "tsconfig.json"]
WEB_DIRS = ["src"]


# ----------------------------------------------------------------------------- build + packaging

def run(cmd, cwd=None):
    print("$", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run(cmd, cwd=cwd, check=True)


def build_bot():
    bot_out = OUT / "bot"
    run(["dotnet", "publish", str(ROOT / "SkillzBot.csproj"), "-c", "Release", "-r", "linux-musl-x64",
         "--self-contained", "true", "-p:PublishSingleFile=true", "-o", str(bot_out)])
    if not (bot_out / "SkillzBot").exists():
        sys.exit("publish produced no SkillzBot executable")
    return bot_out


def _add(tar, path: Path, arcname: str, mode: int):
    info = tar.gettarinfo(str(path), arcname=arcname)
    info.uid = info.gid = 0
    info.mode = mode
    with open(path, "rb") as fh:
        tar.addfile(info, fh)


def package_upload(bot_dir, with_web):
    """One archive, laid out as it lands in <dir>: .build/bot/SkillzBot, src/web/..., src/deploy/alpine/..."""
    tmp = Path(tempfile.gettempdir()) / f"skillzbot-upload-{time.strftime('%Y%m%d-%H%M%S')}.tar.gz"
    count = 0
    with tarfile.open(tmp, "w:gz") as tar:
        if bot_dir:
            _add(tar, bot_dir / "SkillzBot", ".build/bot/SkillzBot", 0o755)
            count += 1
        if with_web:
            web = ROOT / "web"
            for name in WEB_FILES:
                if (web / name).is_file():
                    _add(tar, web / name, f"src/web/{name}", 0o644)
                    count += 1
            for d in WEB_DIRS:
                for f in sorted((web / d).rglob("*")):
                    if f.is_file():
                        _add(tar, f, f"src/web/{f.relative_to(web).as_posix()}", 0o644)
                        count += 1
        for f in sorted(ALPINE.iterdir()):
            if f.is_file():
                _add(tar, f, f"src/deploy/alpine/{f.name}", 0o755 if f.suffix == ".sh" else 0o644)
                count += 1
    print(f"package: {count} files -> {tmp} ({tmp.stat().st_size // 1024 // 1024} MB)")
    return tmp


def package_data(data_dir: Path, include_logs: bool):
    """Tarball of an existing Channels_Data folder (logs skipped unless asked)."""
    if not data_dir.is_dir():
        sys.exit(f"--data: {data_dir} is not a directory")
    tmp = Path(tempfile.gettempdir()) / "skillzbot-channels-data.tar.gz"
    count = 0

    def flt(info):
        nonlocal count
        if not include_logs and "logs" in Path(info.name).parts and info.isfile():
            return None
        info.uid = info.gid = 0
        if info.isfile():
            count += 1
        return info

    with tarfile.open(tmp, "w:gz") as tar:
        tar.add(str(data_dir), arcname=".", filter=flt)
    print(f"data: {count} files from {data_dir} -> {tmp}")
    return tmp


# ----------------------------------------------------------------------------- remote side

class Host:
    def __init__(self, args, password):
        try:
            import paramiko
        except ImportError:
            sys.exit("paramiko is required:  pip install paramiko")
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
        shown = script if len(script) < 160 else script[:157] + "..."
        print(f"[remote] {shown}", flush=True)
        _, stdout, stderr = self.client.exec_command("sh -c " + _quote(script), get_pty=False)
        for line in iter(stdout.readline, ""):
            print("   ", line.rstrip(), flush=True)
        err = stderr.read().decode(errors="replace").strip()
        rc = stdout.channel.recv_exit_status()
        if err:
            print("   ", err.replace("\n", "\n    "))
        if check and rc != 0:
            sys.exit(f"remote command failed with exit code {rc}")
        return rc

    def put(self, local: Path, remote: str, mode=None):
        last = [0]

        def progress(done, total):
            pct = int(done * 100 / total) if total else 100
            if pct >= last[0] + 20 or done == total:
                last[0] = pct
                print(f"    upload {local.name}: {pct}%", flush=True)

        self.sftp.put(str(local), remote, callback=progress)
        if mode is not None:
            self.sftp.chmod(remote, mode)

    def put_dir(self, local: Path, remote: str):
        self.sh(f"mkdir -p '{remote}'")
        for f in sorted(local.iterdir()):
            if f.is_file():
                self.put(f, f"{remote}/{f.name}", 0o755 if f.suffix in ("", ".sh") else 0o644)

    def dir_nonempty(self, path):
        return self.sh(f"[ -d '{path}' ] && [ \"$(ls -A '{path}' 2>/dev/null)\" ]", check=False) == 0


def _quote(s):
    return "'" + s.replace("'", "'\"'\"'") + "'"


# ----------------------------------------------------------------------------- steps

def step_init(host, args):
    print("== host setup (packages incl. .NET SDK and Node, service, nginx)")
    host.put_dir(ALPINE, "/root/skillzbot-setup")
    host.sh(f"APP_DIR='{args.dir}' CHANNEL='{args.channel}' TZ='{args.tz}' API_PORT='{args.api_port}' sh /root/skillzbot-setup/install.sh")


def step_data(host, args):
    target = f"{args.dir}/Channels_Data"
    if host.dir_nonempty(target) and not args.data_overwrite:
        sys.exit(f"{target} on the host is not empty; add --data-overwrite to replace files with the local copy")
    pkg = package_data(Path(args.data), args.include_logs)
    host.put(pkg, "/tmp/skillzbot-data.tgz")
    host.sh(f"set -e; mkdir -p '{target}'; tar xzf /tmp/skillzbot-data.tgz -C '{target}'; rm -f /tmp/skillzbot-data.tgz; "
            f"echo 'channels on host:'; ls '{target}'")
    pkg.unlink(missing_ok=True)


def step_proxy_bin(host, args):
    local = Path(args.proxy_bin)
    if not local.is_file():
        sys.exit(f"--proxy-bin: {local} is not a file")
    remote = f"{args.dir}/proxy/{local.name}"
    host.sh(f"mkdir -p '{args.dir}/proxy'")
    host.put(local, remote, 0o755)
    print(f"    set ProxyCorePath to {remote} in the channel config")


def step_upload(host, args, pkg, with_web):
    src = f"{args.dir}/src"
    host.put(pkg, "/tmp/skillzbot-upload.tgz")
    steps = ["set -e", f"mkdir -p '{args.dir}/.build/bot' '{src}/web'"]
    if with_web:
        # Replace the panel sources but keep node_modules so npm ci is fast on repeat runs.
        steps.append(f"find '{src}/web' -mindepth 1 -maxdepth 1 ! -name node_modules -exec rm -rf {{}} +")
    steps += [f"tar xzf /tmp/skillzbot-upload.tgz -C '{args.dir}'", "rm -f /tmp/skillzbot-upload.tgz",
              f"chmod +x '{src}'/deploy/alpine/*.sh", f"echo 'uploaded into {args.dir}'"]
    host.sh("; ".join(steps))


def step_install(host, args):
    env = (f"APP_DIR='{args.dir}' RESTART={0 if args.no_restart else 1} "
           f"INSTALL_BOT={0 if args.skip_bot else 1} BUILD_WEB={0 if args.skip_web else 1}")
    host.sh(f"{env} sh '{args.dir}/src/deploy/alpine/build.sh'")


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
    ap.add_argument("--skip-bot", action="store_true", help="do not publish/ship the bot")
    ap.add_argument("--skip-web", action="store_true", help="do not ship/build the web panel")
    ap.add_argument("--no-build", action="store_true", help="reuse deploy/out/bot from the previous publish")
    ap.add_argument("--no-restart", action="store_true", help="install, but do not restart the service")
    ap.add_argument("--dry-run", action="store_true", help="publish and package only, do not connect")
    args = ap.parse_args()

    bot_dir = None
    if not args.skip_bot:
        bot_dir = OUT / "bot" if args.no_build else build_bot()
        if not (bot_dir / "SkillzBot").exists():
            sys.exit(f"{bot_dir / 'SkillzBot'} not found; run without --no-build")
    pkg = package_upload(bot_dir, not args.skip_web)
    if args.data and args.dry_run:
        package_data(Path(args.data), args.include_logs)
    if args.dry_run:
        print("dry run: nothing uploaded")
        pkg.unlink(missing_ok=True)
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
        step_upload(host, args, pkg, not args.skip_web)
        step_install(host, args)
        print("deploy finished")
    finally:
        host.close()
        pkg.unlink(missing_ok=True)


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as e:
        sys.exit(f"command failed with exit code {e.returncode}")
    except KeyboardInterrupt:
        sys.exit("\ninterrupted")
