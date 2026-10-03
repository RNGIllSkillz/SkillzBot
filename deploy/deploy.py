#!/usr/bin/env python3
"""Build SkillzBot and the web panel and push them to the Alpine host over SSH.

    python deploy/deploy.py --host 192.168.254.154 [--user root] [--dir /opt/skillzbot]
                            [--skip-bot] [--skip-web] [--no-build] [--no-restart]

Needs on this machine: dotnet SDK, node + npm (for the panel), and the OpenSSH client (ssh/scp on PATH;
Windows 10+ ships it). Authentication is whatever ssh does for you: an SSH key (recommended) or the
password prompt. Nothing is stored by this script.

What happens on the host: the service is stopped, the new single-file bot executable and the web
folder are unpacked over the old ones (Channels_Data is never touched), the service is started.
With --no-restart the files are staged while the bot keeps running; the next restart from the
panel (or `rc-service skillzbot restart`) picks them up.
"""
import argparse
import os
import shutil
import subprocess
import sys
import tarfile
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "deploy" / "out"


def run(cmd, cwd=None):
    print("$", " ".join(str(c) for c in cmd), flush=True)
    subprocess.run(cmd, cwd=cwd, check=True)


def build_bot():
    bot_out = OUT / "bot"
    run(["dotnet", "publish", str(ROOT / "SkillzBot.csproj"), "-c", "Release", "-r", "linux-musl-x64",
         "--self-contained", "true", "-p:PublishSingleFile=true", "-o", str(bot_out)])
    exe = bot_out / "SkillzBot"
    if not exe.exists():
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
                with open(f, "rb") as fh:
                    tar.addfile(info, fh)
        if web_dir:
            tar.add(str(web_dir), arcname="web")
    print(f"package {pkg} ({pkg.stat().st_size // 1024 // 1024} MB)")
    return pkg


def ssh_target(args):
    return f"{args.user}@{args.host}"


def remote(args, script):
    ssh_opts = ["-o", "StrictHostKeyChecking=accept-new"]
    if args.port:
        ssh_opts += ["-p", str(args.port)]
    run(["ssh", *ssh_opts, ssh_target(args), script])


def upload(args, pkg):
    scp_opts = ["-o", "StrictHostKeyChecking=accept-new"]
    if args.port:
        scp_opts += ["-P", str(args.port)]
    run(["scp", *scp_opts, str(pkg), f"{ssh_target(args)}:/tmp/skillzbot-upload.tgz"])


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", required=True, help="Alpine host (IP or name)")
    ap.add_argument("--user", default="root")
    ap.add_argument("--port", type=int, default=None, help="SSH port if not 22")
    ap.add_argument("--dir", default="/opt/skillzbot", help="install directory on the host")
    ap.add_argument("--skip-bot", action="store_true", help="do not build/upload the bot")
    ap.add_argument("--skip-web", action="store_true", help="do not build/upload the web panel")
    ap.add_argument("--no-build", action="store_true", help="reuse deploy/out from the previous run")
    ap.add_argument("--no-restart", action="store_true", help="stage files only; the next restart picks them up")
    ap.add_argument("--npm-install", action="store_true", help="run npm install before building the panel")
    args = ap.parse_args()
    if args.skip_bot and args.skip_web:
        sys.exit("nothing to deploy")

    if not args.no_build:
        if OUT.exists():
            shutil.rmtree(OUT)
    bot_dir = None if args.skip_bot else (OUT / "bot" if args.no_build else build_bot())
    web_dir = None if args.skip_web else (ROOT / "web" / "dist" if args.no_build else build_web(args.npm_install))

    pkg = package(bot_dir, web_dir)
    upload(args, pkg)

    d = args.dir
    steps = ["set -e", f"mkdir -p '{d}'"]
    if not args.no_restart:
        steps.append("rc-service skillzbot status >/dev/null 2>&1 && rc-service skillzbot stop || true")
    if web_dir:
        steps.append(f"rm -rf '{d}/web'")
    steps += [f"tar xzf /tmp/skillzbot-upload.tgz -C '{d}'", "rm -f /tmp/skillzbot-upload.tgz"]
    if bot_dir:
        steps.append(f"chmod +x '{d}/SkillzBot'")
    if not args.no_restart:
        steps += ["rc-service skillzbot start", "sleep 3", "rc-service skillzbot status"]
    else:
        steps.append("echo 'files staged; restart the bot from the panel or: rc-service skillzbot restart'")
    remote(args, "; ".join(steps))
    print("deploy finished")


if __name__ == "__main__":
    try:
        main()
    except subprocess.CalledProcessError as e:
        sys.exit(f"command failed with exit code {e.returncode}")
