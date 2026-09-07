#!/bin/bash
# 把本项目 symlink 进游戏 mods/，并确保 modsettings.lua 里 ForceEnableMod。
# 改代码即时生效（symlink，不复制）。撤销用 ./undeploy.sh。
set -e
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
GAME="/home/deck/Games/omni-deck/steam_games/013 - Don't Starve"
DEST="$GAME/mods/omniDontStarveMod"
MS="$GAME/mods/modsettings.lua"

if [ ! -d "$GAME/mods" ]; then echo "找不到游戏 mods 目录: $GAME/mods" >&2; exit 1; fi

rm -rf "$DEST"
ln -s "$HERE" "$DEST"
echo "linked: $DEST -> $HERE"

# 注意：要匹配"未注释"的行（stock modsettings.lua 里有一行注释掉的示例会误命中）
if ! grep -qE '^[[:space:]]*ForceEnableMod\("omniDontStarveMod"\)' "$MS" 2>/dev/null; then
    echo 'ForceEnableMod("omniDontStarveMod")' >> "$MS"
    echo "added ForceEnableMod(\"omniDontStarveMod\") to modsettings.lua"
else
    echo "modsettings.lua 已有 ForceEnableMod（未注释），跳过"
fi
echo "done. 启动游戏即加载（游戏内 Mods 菜单也会看到）。"
