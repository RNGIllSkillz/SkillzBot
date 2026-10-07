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

WEB_OK=0
if [ "$BUILD_WEB" = 1 ]; then
    echo "== web panel build"
    # A panel build failure must not keep the bot from being installed and started.
    if (
        set -e
        [ -f "$SRC/web/package.json" ] || { echo "no web sources in $SRC/web"; exit 1; }
        cd "$SRC/web"
        if [ -f package-lock.json ]; then npm ci --no-audit --no-fund; else npm install --no-audit --no-fund; fi
        rm -rf dist
        npm run build
        [ -f dist/index.html ]
    ); then
        WEB_OK=1
    else
        echo "!! web panel build FAILED; the bot is installed anyway, the old panel (if any) stays"
    fi
fi

echo "== service files (OpenRC script, conf.d role, nginx hub routes)"
# install.sh writes these once (--init); an update must not leave an old single-channel service behind the new binary.
ALPINE="$SRC/deploy/alpine"
if [ -f "$ALPINE/skillzbot.initd" ] && ! cmp -s "$ALPINE/skillzbot.initd" /etc/init.d/skillzbot; then
    install -m 0755 "$ALPINE/skillzbot.initd" /etc/init.d/skillzbot && echo "OpenRC script updated"
fi
if [ -f /etc/conf.d/skillzbot ] && ! grep -q '^SKILLZBOT_ROLE=' /etc/conf.d/skillzbot; then
    printf '\n# hub: all channels of Channels_Data/channels.json (default); channel: only ENV_CHANNEL_NAME, the old single-channel mode\nSKILLZBOT_ROLE="hub"\n' >> /etc/conf.d/skillzbot
    echo "SKILLZBOT_ROLE=hub added to /etc/conf.d/skillzbot (the hub imports the existing channel on its first start)"
fi
NGX=/etc/nginx/http.d/skillzbot.conf
if [ -f "$NGX" ] && [ -f "$ALPINE/nginx-skillzbot.conf" ] && ! grep -q 'location ~ \^/c/' "$NGX"; then
    NGX_PORT=$(sed -n 's|.*proxy_pass http://127.0.0.1:\([0-9]*\)/api/;.*|\1|p' "$NGX" | head -n 1)
    NGX_DIR=$(sed -n 's|^[[:space:]]*root \(.*\)/web;.*|\1|p' "$NGX" | head -n 1)
    if [ -n "$NGX_PORT" ] && [ -n "$NGX_DIR" ]; then
        sed "s|@APP_DIR@|$NGX_DIR|g; s|@API_PORT@|$NGX_PORT|g" "$ALPINE/nginx-skillzbot.conf" > "$NGX.new"
        if nginx -t -c /etc/nginx/nginx.conf >/dev/null 2>&1 && mv -f "$NGX.new" "$NGX" && nginx -t >/dev/null 2>&1; then
            rc-service nginx reload >/dev/null 2>&1 || rc-service nginx restart
            echo "nginx site updated with the /c/<channel>/api/ route (port $NGX_PORT)"
        else
            rm -f "$NGX.new"; echo "!! could not update $NGX; copy deploy/alpine/nginx-skillzbot.conf by hand"
        fi
    else
        echo "!! $NGX has no hub route and could not be regenerated; copy deploy/alpine/nginx-skillzbot.conf by hand"
    fi
fi
mkdir -p "$APP_DIR/Channels_Data/hub/DATA/logs" "$APP_DIR/Channels_Data/_shared"

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
if [ "$WEB_OK" = 1 ]; then
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
if [ "$BUILD_WEB" = 1 ] && [ "$WEB_OK" != 1 ]; then
    echo "finished with a web panel build failure (see above)"
    exit 1
fi
