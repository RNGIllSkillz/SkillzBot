#!/bin/sh
# One-time setup of an Alpine host for SkillzBot + web panel. Run as root from this directory:
#   APP_DIR=/opt/skillzbot CHANNEL=general_hs_ TZ=Europe/Moscow API_PORT=8080 sh install.sh
# Afterwards copy Channels_Data into $APP_DIR and run deploy/deploy.py from your PC.
set -eu
APP_DIR="${APP_DIR:-/opt/skillzbot}"
CHANNEL="${CHANNEL:-general_hs_}"
TZ="${TZ:-Europe/Moscow}"
API_PORT="${API_PORT:-8080}"
HERE="$(cd "$(dirname "$0")" && pwd)"

echo "== packages (.NET runtime deps, .NET SDK and Node for building here, nginx, timezone data)"
apk add --no-cache icu-libs icu-data-full krb5-libs libgcc libintl libssl3 libstdc++ zlib tzdata nginx curl
apk add --no-cache dotnet8-sdk nodejs npm || {
    echo "dotnet8-sdk / nodejs / npm are not available from apk on this Alpine release (need 3.19+)."
    echo "Enable the community repository in /etc/apk/repositories or upgrade Alpine, then rerun."
    exit 1
}
dotnet --version && node --version && npm --version

echo "== timezone $TZ"
if [ -f "/usr/share/zoneinfo/$TZ" ]; then
    ln -sf "/usr/share/zoneinfo/$TZ" /etc/localtime
    echo "$TZ" > /etc/timezone
fi

echo "== layout under $APP_DIR"
mkdir -p "$APP_DIR/web" "$APP_DIR/src" "$APP_DIR/Channels_Data/$CHANNEL/DATA/logs" "$APP_DIR/Channels_Data/_shared" "$APP_DIR/proxy"

echo "== OpenRC service"
install -m 0755 "$HERE/skillzbot.initd" /etc/init.d/skillzbot
sed "s|@APP_DIR@|$APP_DIR|g; s|@CHANNEL@|$CHANNEL|g; s|@TZ@|$TZ|g" "$HERE/skillzbot.confd" > /etc/conf.d/skillzbot
rc-update add skillzbot default >/dev/null 2>&1 || true

echo "== nginx site"
sed "s|@APP_DIR@|$APP_DIR|g; s|@API_PORT@|$API_PORT|g" "$HERE/nginx-skillzbot.conf" > /etc/nginx/http.d/skillzbot.conf
rm -f /etc/nginx/http.d/default.conf
rc-update add nginx default >/dev/null 2>&1 || true
nginx -t && rc-service nginx restart

echo "== daily trim of the supervisor's stdout capture (the bot keeps its own rolling logs)"
cat > /etc/periodic/daily/skillzbot-logs <<'TRIM'
#!/bin/sh
for f in /var/log/skillzbot.out /var/log/skillzbot.err; do
    [ -f "$f" ] && [ "$(stat -c %s "$f")" -gt 52428800 ] && : > "$f"
done
exit 0
TRIM
chmod +x /etc/periodic/daily/skillzbot-logs

cat <<MSG

Done. Next steps:
  1. Copy the bot data:   Channels_Data/  ->  $APP_DIR/Channels_Data/   (deploy.py --data does this)
  2. If the proxy is used, put the xray/hysteria binary under $APP_DIR/proxy/ and point ProxyCorePath at it.
  3. On your PC:  python deploy/deploy.py --host <this host>   (uploads sources, builds here, starts the service)
  4. Check:       rc-service skillzbot status;  tail -f $APP_DIR/Channels_Data/$CHANNEL/DATA/logs/bot-*.log
MSG
