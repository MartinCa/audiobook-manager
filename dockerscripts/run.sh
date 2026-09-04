#!/bin/sh
set -e

# Everything the application writes - relocated m4b files, desc.txt, reader.txt, metadata.opf,
# cover images - lands on volumes shared with the host and often with other containers, so the
# umask decides who else can write to a user's library.
#
# The default is 022: owner writes, group and others read. That is a change from the 000 this
# used to be hardcoded to, which made every file 0666 and every directory 0777. The intent behind
# 000 was to survive PUID/PGID mismatches on NAS setups, which is a real problem - but the chown
# below is what actually solves that, and 000 solved it by granting write to everyone on the host.
#
# If another container genuinely needs to write into these directories, the supported way is to
# give it the same PGID and set UMASK=002 (group-writable). UMASK=000 restores the old behaviour
# for anyone who was relying on it.
UMASK=${UMASK:-022}

# /config holds the SQLite database and its journal. 0750 keeps it to the application's own user
# and group, which is all that ever opens it - unlike the library, nothing else is expected to
# read this. It used to be 777, which made the database writable by every user on the host.
#
# Applied with chmod -R, so it lands on the directory as well as the files in it and must keep the
# owner's execute bit: 0640 would look like a sensible file mode and would make /config itself
# untraversable, so the application could not open the database at all.
#
# And applied on every start, not only to new files - so an existing database is re-moded the first
# time the container restarts after an upgrade, not gradually as UMASK's effect would be. The
# hardcoded chmod -R 777 this replaces ran on every start too; what changes is the mode, not when
# it is applied. README says so under "File permissions".
CONFIG_CHMOD=${CONFIG_CHMOD:-0750}

# Both are fed to umask/chmod, so reject anything that is not a mode before doing that. An
# unnoticed typo here would otherwise either fail silently (leaving the previous, wrong
# permissions) or be interpreted as some other mode entirely.
if ! echo "$UMASK" | grep -Eq '^[0-7]{3,4}$'; then
    echo "[FATAL] UMASK must be a 3- or 4-digit octal mode (got '$UMASK')." >&2
    exit 1
fi

if ! echo "$CONFIG_CHMOD" | grep -Eq '^[0-7]{3,4}$'; then
    echo "[FATAL] CONFIG_CHMOD must be a 3- or 4-digit octal mode (got '$CONFIG_CHMOD')." >&2
    exit 1
fi

# The owner's execute bit, without which /config cannot be traversed and the database cannot be
# opened. Caught here rather than as an unexplained failure to start.
# Not expr: it exits non-zero when its result is "0", which under `set -e` would abort the whole
# script with no message for a mode like 0077.
CONFIG_CHMOD_OWNER=$(echo "$CONFIG_CHMOD" | tail -c 4 | cut -c1)
if [ $(( CONFIG_CHMOD_OWNER & 1 )) -eq 0 ]; then
    echo "[FATAL] CONFIG_CHMOD must give the owner execute permission so /config stays traversable (got '$CONFIG_CHMOD')." >&2
    exit 1
fi

umask "$UMASK"

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
umask:    $UMASK
-------------------------------------
"

if [ -d "/config" ]; then
    # chown, not chmod 777: making the application's own user the owner is what lets it write here
    # regardless of which PUID/PGID the host asked for. Granting write to every user on the host
    # was never what that needed.
    chown -R appuser:appgroup /config 2>/dev/null || true
    chmod -R "$CONFIG_CHMOD" /config 2>/dev/null || true
fi

exec su-exec appuser:appgroup dotnet AudiobookManager.Api.dll
