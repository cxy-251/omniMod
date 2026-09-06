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
`modinfo.lua`（api 6，三 DLC 兼容）+ `modmain.lua`（`FEATURES` 列表登记模块）+
`scripts/omnidsm/*.lua`（一个功能一个模块，导出 `.init(GLOBAL)`）+ `deploy.sh`/`undeploy.sh`。

**已 deploy**（`deploy.sh`：symlink 到 `G/mods/omniDontStarveMod` + `modsettings.lua` 加**未注释**的
`ForceEnableMod("omniDontStarveMod")`）。踩过：`deploy.sh` 最初的 grep 命中了 stock modsettings.lua
里那行**注释掉的**示例，误判"已存在"没加真的 —— 已改成 `grep -qE '^[[:space:]]*ForceEnableMod...'`。

### 已实现功能

- **`unlockchars`** (2026-09-06)：解锁所有人物。`modmain` 里把 `GLOBAL.PlayerProfile.IsCharacterUnlocked`
  覆盖成恒 `true`（顺带覆盖当前 `Profile` 实例）。人物选择界面就是靠这个方法判定亮/黑剪影。
  DLC 人物出现在列表里靠 gbe `unlock_all=1`（三 DLC 认成已装）。

## 下一步（用户按需触发）

功能清单见 `README.md`：`cheatmenu`（屏幕按钮+菜单，无热键）/ `treeshake_bear` / `autopickup` / `janitor`。

---

## 中文 `?` — 第二轮修复 (2026-09-06)

彻底重开后仍 `?`。日志：boot #1 没加载 `chinese_s.po`（说明 profile 里语言没持久化，
`Profile:GetLanguageID()` 返回 ENGLISH），要到 boot #2/#3 才加载中文。而 `main.lua` 里
`require("languages/language")`(第169行) 在 `require("fonts")`(第211行) **之前**，所以只要
开机时 locale 已是中文，`SetUseUnicode(true)` 就会在字体加载前生效。

**做了两件事**：
1. `settings.ini` `use_small_textures` 改回 `false`（我之前设的 `true` 可能让引擎加载
   CJK 大图集 `fallback_full_(outline_)packed.zip`(2048² .tex) 出问题）。
2. **patch `data/scripts/languages/language.lua`** 的 `GetCurrentLocale()` →
   直接 `return LOC.GetLocale(LANGUAGE.CHINESE_S)`，强制开机即中文，绕开不持久化的 profile。
   原文件备份 `BK/language.lua.orig`；原逻辑在补丁里注释保留，删掉那一行 return 即恢复。
   （`data/scripts/` 是散文件、无 `scripts.zip` 遮蔽、单机版不校验签名，改了直接生效。）

若还 `?` → 说明是字体 fallback 本身坏了（不是加载顺序），plan C：重打包 / 替换
`fallback_full_(outline_)packed.zip`（可从 DST 或 Noto Sans CJK 重新生成 BMFont）。

### `cheats` 模块 (2026-09-06)

后台指令可控、**默认全开**的一组作弊。控制台全局函数（`GLOBAL.omni*`）：
`omni()` 看状态 / `omni_map(bool)` 地图全开 / `omni_speed(n)` 速度倍率(默认2) /
`omni_tech(bool)` 科技全解锁 / `omni_hp(bool)` 生命下限锁10 / `omni_dmg(n)` 伤害倍率(默认3) /
`omni_off()` / `omni_on()`。

实现（`scripts/omnidsm/cheats.lua`，全在 `AddSimPostInit`+`AddPlayerPostInit` 里重套，过场不丢）：
- 地图：`GetWorld().minimap.MiniMap:ShowArea(0,0,0,10000)` 一次全揭（关掉不会重新盖雾）
- 速度：`locomotor.runspeed = (首次记录的 base) * mult`（兼容非 Wilson）
- 科技：`builder.science/magic/ancient_bonus = 10` + `EvaluateTechTrees()`（材料仍需要）
- 锁血：`health:SetMinHealth(10)` —— 引擎 `Health:SetVal` 自带 minhealth 钳制，掉到 10 触发
  `minhealth` 事件而非 `death`
- 伤害：`combat.damagemultiplier = n`（`Combat:CalcDamage` 直接用）

### `cheatmenu` 模块 (2026-09-06)

`cheats` 的游戏内可视化菜单，Steam Deck 手柄可操作，不用键鼠。
- 入口：`AddClassPostConstruct("screens/pausescreen", ...)` 往暂停菜单加一项「作弊菜单」，
  加完按新项数重新水平居中。
- `CheatMenu`：`Class(Screen)` 竖排 `Menu`，8 行（地图/速度/科技/锁血/伤害/全恢复/全关/返回）。
  A = 行的 `act()` 然后 `Refresh()`（`menu:EditItem` 刷新每行文字）；B/Start = `Close()`。
  数值行按预设循环：速度 {1,1.5,2,3,5,8}，伤害 {1,2,3,5,10,25}。
- 读 `GLOBAL.OMNIDSM.state`（`cheats` 模块导出），改动走已有的 `GLOBAL.omni_*`。
- **本文件所有游戏全局走 `G.xxx`**（Class/require/常量/TheFrontEnd/SetPause…），
  因为 mod 脚本环境不保证能直接看到 `_G`。
- 待运行验证：暂停菜单能不能加进去、手柄导航、5 个按钮横排会不会太宽。

### mod 环境坑 (2026-09-06) —— 首个功能其实一直在报错

`打开饥荒报错`：`modmain.lua:25: attempt to call global 'pcall' (a nil value)`。
单机饥荒的 mod 环境（`mods.lua:CreateEnvironment`）只放了很少的裸名：
`pairs ipairs print math table type string tostring Class GLOBAL TUNING Prefab
Asset Ingredient modname MODROOT modimport` + `InsertPostInitFunctions` 加的
`Add*PostInit` / `AddClassPostConstruct` 等。**没有** `pcall / require / tonumber
/ assert / error`，也没有任何游戏运行时全局（除 `GLOBAL`）。
`FEATURES={}` 空的时候不触发，加了第一个功能才炸 —— 所以 unlockchars 之前从没生效过。

**重构**：
- `modmain.lua` 改用 `GLOBAL.pcall(modimport, "scripts/omnidsm/<name>.lua")` 逐个加载。
- 三个功能文件改成**扁平写法**：由 `modimport` 在 mod 环境里直接跑，不返回表，不再有
  `M.init(G)`。`require/pcall/tonumber` 及所有游戏全局走 `GLOBAL.xxx`；
  `Add*PostInit`/`Class`/`AddClassPostConstruct` 用裸名。
- `_template.lua` 同步成新写法。
- 菜单：`Menu` 的项是 `ImageButton`，**鼠标可点 + 手柄可导航**，本来就两者都支持；
  额外 `TheInputProxy:SetCursorVisible(true)`，提示文字也写了"上下/鼠标、A/左键、B/Esc"。
