#!/bin/sh
# Installs what deploy.py uploaded: the bot executable staged at $APP_DIR/.build/bot/SkillzBot (compiled on
# the workstation) and the web panel, which is built here from $APP_DIR/src/web with npm.
# Can also be run by hand:  APP_DIR=/opt/skillzbot RESTART=1 INSTALL_BOT=1 BUILD_WEB=1 sh build.sh
set -eu
APP_DIR="${APP_DIR:-/opt/skillzbot}"
SRC="$APP_DIR/src"
RESTART="${RESTART:-1}"
INSTALL_BOT="${INSTALL_BOT:-1}"
BUILD_WEB="${BUILD_WEB:-1}"
STAGED="$APP_DIR/.build/bot/SkillzBot"
export HOME="${HOME:-/root}"

if [ "$INSTALL_BOT" = 1 ] && [ ! -f "$STAGED" ]; then
    echo "no staged bot executable at $STAGED; skipping the bot"
    INSTALL_BOT=0
fi

if [ "$BUILD_WEB" = 1 ]; then
    [ -f "$SRC/web/package.json" ] || { echo "no web sources in $SRC/web (run deploy.py first)"; exit 1; }
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
if [ "$INSTALL_BOT" = 1 ]; then
    install -m 0755 "$STAGED" "$APP_DIR/SkillzBot.new"
    mv -f "$APP_DIR/SkillzBot.new" "$APP_DIR/SkillzBot"
    rm -f "$STAGED"
    echo "bot executable installed"
fi
if [ "$BUILD_WEB" = 1 ]; then
    rm -rf "$APP_DIR/web.new"
    cp -r "$SRC/web/dist" "$APP_DIR/web.new"
    rm -rf "$APP_DIR/web"
    mv "$APP_DIR/web.new" "$APP_DIR/web"
    echo "web panel installed"
fi
if [ "$RESTART" = 1 ]; then
    rc-service skillzbot start
    sleep 4
    rc-service skillzbot status
    for f in "$APP_DIR"/Channels_Data/*/DATA/logs/bot-"$(date +%Y%m%d)".log; do
        [ -f "$f" ] && { echo "-- $f"; tail -n 15 "$f"; }
    done
else
    echo "installed; restart from the panel or: rc-service skillzbot restart"
fi
