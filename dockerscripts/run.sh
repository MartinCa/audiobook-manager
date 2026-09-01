#!/bin/sh
set -e

umask 0000

PUID=${PUID:-911}
PGID=${PGID:-911}

CUR_GID=$(id -g appuser 2>/dev/null || echo "")
CUR_UID=$(id -u appuser 2>/dev/null || echo "")

if [ "$CUR_GID" != "$PGID" ]; then
    groupmod -o -g "$PGID" appgroup 2>/dev/null || true
fi

if [ "$CUR_UID" != "$PUID" ]; then
    usermod -o -u "$PUID" appuser 2>/dev/null || true
fi

echo "
-------------------------------------
Audiobook Manager Starting
User UID: $(id -u appuser)
User GID: $(id -g appuser)
-------------------------------------
"

if [ -d "/config" ]; then
    chown -R appuser:appgroup /config 2>/dev/null || true
    chmod -R 777 /config 2>/dev/null || true
fi

exec su-exec appuser:appgroup dotnet AudiobookManager.Api.dll
