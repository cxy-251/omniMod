#!/bin/bash
# 撤销 deploy.sh：移除 symlink + modsettings.lua 里的 ForceEnableMod 行。
set -e
GAME="/home/deck/Games/omni-deck/steam_games/013 - Don't Starve"
DEST="$GAME/mods/omniDontStarveMod"
MS="$GAME/mods/modsettings.lua"

[ -L "$DEST" ] && rm -f "$DEST" && echo "removed symlink: $DEST"
[ -d "$DEST" ] && echo "注意：$DEST 是真目录不是 symlink，没动它" >&2

if [ -f "$MS" ]; then
    sed -i '/^[[:space:]]*ForceEnableMod("omniDontStarveMod")/d' "$MS"
    echo "removed ForceEnableMod line from modsettings.lua"
fi
