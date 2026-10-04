#!/bin/sh
# Builds the bot and the web panel from $APP_DIR/src on this host and installs them into $APP_DIR.
# Called by deploy/deploy.py after the sources are uploaded; can also be run by hand:
#   APP_DIR=/opt/skillzbot RESTART=1 BUILD_BOT=1 BUILD_WEB=1 sh /opt/skillzbot/src/deploy/alpine/build.sh
set -eu
APP_DIR="${APP_DIR:-/opt/skillzbot}"
SRC="$APP_DIR/src"
RESTART="${RESTART:-1}"
BUILD_BOT="${BUILD_BOT:-1}"
BUILD_WEB="${BUILD_WEB:-1}"
export HOME="${HOME:-/root}" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-/root}"

[ -f "$SRC/SkillzBot.csproj" ] || { echo "no sources in $SRC (run deploy.py first)"; exit 1; }

if [ "$BUILD_BOT" = 1 ]; then
    echo "== dotnet publish (self-contained, linux-musl-x64)"
    cd "$SRC"
    dotnet publish SkillzBot.csproj -c Release -r linux-musl-x64 --self-contained true \
        -p:PublishSingleFile=true -o "$APP_DIR/.build/bot"
    [ -x "$APP_DIR/.build/bot/SkillzBot" ] || { echo "publish produced no executable"; exit 1; }
fi

if [ "$BUILD_WEB" = 1 ]; then
    echo "== web panel build"
    cd "$SRC/web"
    if [ -f package-lock.json ]; then npm ci --no-audit --no-fund; else npm install --no-audit --no-fund; fi
    rm -rf dist
    npm run build
    [ -f dist/index.html ] || { echo "web build produced no dist/index.html"; exit 1; }
fi

echo "== install into $APP_DIR"
if [ "$RESTART" = 1 ]; then
    rc-service skillzbot status >/dev/null 2>&1 && rc-service skillzbot stop || true
fi
if [ "$BUILD_BOT" = 1 ]; then
    install -m 0755 "$APP_DIR/.build/bot/SkillzBot" "$APP_DIR/SkillzBot.new"
    mv -f "$APP_DIR/SkillzBot.new" "$APP_DIR/SkillzBot"
fi
if [ "$BUILD_WEB" = 1 ]; then
    rm -rf "$APP_DIR/web.new"
    cp -r "$SRC/web/dist" "$APP_DIR/web.new"
    rm -rf "$APP_DIR/web"
    mv "$APP_DIR/web.new" "$APP_DIR/web"
fi
if [ "$RESTART" = 1 ]; then
    rc-service skillzbot start
    sleep 4
    rc-service skillzbot status
    for f in "$APP_DIR"/Channels_Data/*/DATA/logs/bot-"$(date +%Y%m%d)".log; do
        [ -f "$f" ] && { echo "-- $f"; tail -n 15 "$f"; }
    done
else
    echo "built and installed; restart from the panel or: rc-service skillzbot restart"
fi
