# HANDOFF — 单机饥荒离线化 + mod 项目起步

日期：2026-09-06

## 已完成：游戏离线化

游戏目录 `G = "/run/media/deck/FUCKDECK/standalone_games/steam_games/013 - Don't Starve"`
存档目录 `K = "~/.klei/DoNotStarve"`

| 改动 | 内容 |
|---|---|
| `G/bin/steam_appid.txt`、`G/steam_appid.txt` | 写 `219740` —— Steamworks 无客户端也能初始化 |
| `G/bin/dontstarve`（启动 wrapper，被 omni-deck 调用） | 重写：①保证 steam_appid ②启动前 `cp -a K/save K/save_backups/save-<时间戳>`，只留最近 15 份 ③`SteamAppId=219740` ④优先走 `~/.local/share/Steam/ubuntu12_32/steam-runtime/run.sh` 起 `dontstarve_steam`，否则 `LD_LIBRARY_PATH=./lib32` 兜底 ⑤日志 → `K/omnideck-launch.log` |
| `K/settings.ini` | `bloom=false` `distortion=false` `use_small_textures=true` `ENABLECONSOLE=true`；`[hamlet] renderjunglecanopy=false` `renderjunglevines=false`；`netbook_mode=true` 保留 |
| `G/mods/modsettings.lua` | 追加 `DisableModDisabling()`（崩溃后不自动禁用所有 mod） |
| `G/mods/` 第三方 mod | 39 个 `workshop-*` + `screecher` 全部 `mv` 到 `G/mods/_disabled_thirdparty/`（可逆）。用户要求"不用别人的"。 |

**备份**：动过的原文件在 `K/omnideck-offline-backup-20260906-172232/`
（`dontstarve.wrapper.orig` / `settings.ini.orig` / `modsettings.lua.orig` / `mods-listing-before.txt`）。
第三方 mod 没删，在 `_disabled_thirdparty/`。

## omni-deck 集成

**不用改 omni-deck 代码。** 它自动扫 `standalone_games/steam_games/*`，
`DISPLAY_NAMES` 里已有 `'013 - Don't Starve': '013 - 饥荒单机版 (Don't Starve)'`，
`_find_executable` 给 `dontstarve` 打 +100 分，会选中我们重写的 wrapper。
→ omni-deck 里点「013 - 饥荒单机版」就是离线启动 + 存档自动备份。

## 待验证（下次开游戏时）

1. omni-deck 点「饥荒单机版」能进主菜单（Steamworks 初始化不报错 / 报错也不影响进游戏）。
2. `K/save_backups/` 里出现了带时间戳的备份。
3. 主菜单 Mods 列表是空的（第三方全禁用了）。
4. **Steam 云存档**：以前的存档可能在 Steam Cloud（日志里 48 个云文件）。离线直起不走云同步。
   如果发现进游戏没有旧存档 —— 先用 Steam（离线模式也行）起一次把云存档拉到本地 `K/save/`，再走 omni-deck 直起。

## 已完成：mod 项目脚手架

`~/Games/claude/omniDontStarveMod/`（与 `omniMod` 同级），git 已 init。
`modinfo.lua` + `modmain.lua`（空壳，`FEATURES={}`）+ `scripts/omnidsm/_template.lua` + `deploy.sh`/`undeploy.sh`。
**还没 deploy，没功能** —— 用户要求 mod 先不做，之后一个个加。

## 下一步（用户按需触发）

功能清单见 `README.md`：`cheatmenu` / `treeshake_bear` / `autopickup` / `janitor`。
第一个大概率做 `cheatmenu`（屏幕按钮+菜单，无热键）。
