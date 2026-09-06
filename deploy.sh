#!/bin/bash
# 把本项目 symlink 进游戏 mods/，并确保 modsettings.lua 里 ForceEnableMod。
# 改代码即时生效（symlink，不复制）。撤销用 ./undeploy.sh。
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="/run/media/deck/FUCKDECK/standalone_games/steam_games/013 - Don't Starve"
DEST="$GAME/mods/omniDontStarveMod"
MS="$GAME/mods/modsettings.lua"

if [ ! -d "$GAME/mods" ]; then echo "找不到游戏 mods 目录: $GAME/mods" >&2; exit 1; fi

rm -rf "$DEST"
ln -s "$HERE" "$DEST"
echo "linked: $DEST -> $HERE"

if ! grep -q 'ForceEnableMod("omniDontStarveMod")' "$MS" 2>/dev/null; then
    echo 'ForceEnableMod("omniDontStarveMod")' >> "$MS"
    echo "added ForceEnableMod(\"omniDontStarveMod\") to modsettings.lua"
else
    echo "modsettings.lua 已有 ForceEnableMod，跳过"
fi
echo "done. 启动游戏即加载（游戏内 Mods 菜单也会看到）。"
