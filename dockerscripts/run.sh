#!/bin/sh

umask 0000

PUID=${PUID:-911}
PGID=${PGID:-911}

groupmod -o -g "$PGID" xyzgroup
usermod -o -u "$PUID" appuser

echo "
User uid: $(id -u appuser)
User gid: $(id -g appuser)
"

chmod 777 /config

su -c "dotnet AudiobookManager.Api.dll" -m appuser
