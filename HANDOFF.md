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
| `G/mods/modsettings.lua` | stock + 注释。**踩过的坑**：`DisableModDisabling()` 是**联机版(DST)专属**，单机版没有 —— 加了它 `main.lua` 加载报错、进不去菜单直接闪退。别加。用 `ForceEnableMod("omniDontStarveMod")` 就够（强制加载，不受"崩溃后禁用 mod"影响）。 |
| 第三方 mod | 39 个 `workshop-*` + `screecher` 全部 `mv` 到 **`G/_disabled_thirdparty_mods/`**（在游戏根目录，不在 `mods/` 里，DS 完全不会扫到）。可逆。用户要求"只借鉴别人的，不用别人的 mod"。 |

**备份**：动过的原文件在 `K/omnideck-offline-backup-20260906-172232/`
（`dontstarve.wrapper.orig` / `settings.ini.orig` / `modsettings.lua.orig` / `mods-listing-before.txt`）。
第三方 mod 没删，在 `G/_disabled_thirdparty_mods/`。

**首次启动结果 (2026-09-06)**：
- 第一次崩了（`DisableModDisabling` 那个坑），修掉后 `modsettings.lua` 恢复 stock。
- 第二次进去了，但**连上了真 Steam**：`libsteam_api.so` 是正版 Valve 的，Deck 上 Steam 客户端常开 →
  游戏 `EnumerateUserSubscribedFiles` 把用户订阅的 38 个创意工坊 mod **全部重新下载回 `mods/`**，
  还从 Steam 云同步回了存档。用户要的是全新干净无 mod 无存档 + 中文。

**已换成 Steam 模拟库 (gbe_fork)** —— 跟缺氧(012)一个路子（缺氧那个是 64 位 gbe_fork）：
- `bin/lib32/libsteam_api.so` 换成 **gbe_fork `release-2026_08_23` regular/x86**（32 位，10 MB）。
  正版备份在 `BK/libsteam_api.so.valve-orig` 和 `bin/lib32/libsteam_api.so.valve-orig`。
  也放了 `bin/lib32/steamclient.so`（gbe 自带）。
- `steam_interfaces.txt` 用 gbe 的 `generate_interfaces_x86` 从正版 lib 生成（只出 5 条，
  已手工补全到 25 条常用接口）。
- `bin/steam_settings/`（也复制一份到 `bin/lib32/steam_settings/`）：
  - `steam_appid.txt`=219740；`installed_app_ids.txt`=219740+282470+393010+712640（DLC 认成已装）
  - `configs.app.ini` → `[app::dlcs] unlock_all=1`（RoG/SW/Hamlet 全解锁）
  - `configs.main.ini` → `[main::connectivity] disable_networking=1 / offline=1`（彻底断网隔离）
  - `configs.user.ini` → `language=schinese`（**中文界面**，DS 首次运行按这个定语言）+ 固定假身份
  - `supported_languages.txt` = `schinese/english`
- 清理：`mods/` 里 38 个重下的 `workshop-*` 全删；`~/.klei/DoNotStarve/save/` 清空（全新无存档）；
  `settings.ini` `DISABLECLOUD=true`。
- gbe 二进制 + 配置也存了一份到 `BK/gbe_fork-x86/`（含 `SOURCE.txt` 下载地址），SD 卡副本重置时不用重下。

**验证结果 (2026-09-06)**：mods 列表空、存档空 —— ✅ 干净了。

**中文字体 `?` 问题**：
- 字体**不缺**。`data/fonts/fallback_full_packed.zip` / `fallback_full_outline_packed.zip` 就是
  `Noto Sans CJK SC`，44201 个字形，中文全覆盖。
- 根因是 DS 的老毛病：**游戏内 Options 切语言**走 `loc.lua:SwapLanguage()`，它只重载字符串、
  **不重新调 `TheSim:SetUseUnicode()`**。`SetUseUnicode` 只在开机时 `language.lua` 里跑一次，
  读的是 `Profile:GetLanguageID()`。所以当场切 → `?`；但选择**已存进 `save/profile`**。
- **修法：彻底退出再重开**。下次开机 `language.lua` 读到 profile 里的中文 → 跑 `SetUseUnicode(true)`
  → 中文正常。
- gbe 的 `language=` 对 DS 无效（`PlayerProfile:GetLanguageID` 非主机平台硬默认 ENGLISH，
  从不查 Steam 语言），已改回 `english` 免得误导。真正的语言开关是游戏内 Options，存到 profile。
- 若彻底重开还是 `?` → plan B：给 `loc.lua:SwapLanguage()` 补一行
  `TheSim:SetUseUnicode(LOCALE.GetUseUnicode())`（改游戏脚本，或以后做进 mod）。

**仍待验证**：① 三个 DLC 世界可选 ② `save_backups/` 有时间戳备份 ③ 彻底重开后中文正常。

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
