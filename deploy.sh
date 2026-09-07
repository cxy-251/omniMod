#!/bin/bash
# 把本项目同步进 SD 卡上的 DST 游戏 mods 目录（DST 引擎不吃跨盘符号链接，只能用真实副本）
set -e
SRC="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DST_MOD="/run/media/deck/FUCKDECK/standalone_games/steam_games/014 - Don't Starve Together/mods/omniDontStarveTogetherMod"
mkdir -p "$DST_MOD"
rsync -a --delete \
  --exclude '.git' --exclude '.gitignore' --exclude '*.md' --exclude 'deploy.sh' --exclude '_disabled*' \
  "$SRC"/ "$DST_MOD"/
echo "已部署 -> $DST_MOD"
find "$DST_MOD" -type f | sed "s|$DST_MOD/||" | sort
