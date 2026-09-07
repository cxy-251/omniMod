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

### v0.0.5 (2026-09-06)

- **菜单文字截断**：标签全部缩短（"地图全开 开" 之类），`Menu:SetTextSize(30)`，
  面板 `small_dialog` 放大到 2.0×2.3，10 行重新排版。
- **秒砍伐/挖矿**（`cheats.lua`，默认开，`omni_work` / 菜单「秒砍伐挖矿」）：
  `AddComponentPostInit("workable")` 包 `WorkedBy`，玩家工作时把 `numworks` 顶到
  `self.workleft` → 一击完成砍树/挖矿/锤/挖树桩。只对玩家，不动其它生物。
- **随身箱子**（`omni_box()` / 菜单「给随身箱子」）：`SpawnPrefab("krampus_sack")`
  塞进背包 —— 克劳斯背包是游戏里最大的可携带容器（14 格，背部栏，跨三大世界携带）。
  没做全新 60 格箱子（要自定义 prefab + 容器 UI anim，工作量大）；14 格够先用。
- **懒人护符**（`lazyforager.lua`，新模块）：单机饥荒里没有 DST 的 lazyforager，
  对应物是**橙色护符 `orangeamulet`**（戴上自动捡拾）。`AddPrefabPostInit` 把
  `fueled.rate = 0` → 永不掉耐久。
- **锁血**：一直是做了的（`cheats.lua` 的 `omni_hp`，`health:SetMinHealth(10)`，
  默认开）—— 之前 mod 一直在崩所以没体现。现在能用了：掉血但不会低于 10 = 不会死。

### v0.0.6 — 自制随身箱子 (2026-09-06)

用户要 Portable Cellar 那样的箱子（不用他的，自己写）。读了 `_disabled_thirdparty_mods/
workshop-2972769037`（Portable Cellar）的源码。它很重（`trueportablecellar` 组件分页、
`sortcontainers` 整理、inventory 组件一堆改写让"隔箱取材料合成"生效）。我们只做核心：

- `scripts/prefabs/omni_box.lua`（新预制物 `omni_box`）：
  - 外观用游戏自带 `treasure_chest`（bank `chest` / build `treasure_chest`）
  - `inventoryitem` `cangoincontainer=true` → 能塞进默认物品格，多个箱子=全部家当随身
  - `container` 60 格（10×6，程序化 `widgetslotpos`，无 bg bank —— 每个槽自己画 `inv_slot.tex`）
  - `itemtestfn` 拦掉箱子套箱子
  - `AddTag("fridge")` + `itemget`→`perishable:StopPerishing()` / `itemlose`→`StartPerishing()`
    ＝ 里面食物**完全不腐**
  - `itemget` 把 `stackable.maxsize` 设 999（记 `_omni_maxsize`，拿出且没超上限才还原）
- `modmain.lua`：`PrefabFiles={"omni_box"}` + `Assets` + `STRINGS.NAMES.OMNI_BOX`。
- **关键**：加了 `GLOBAL.setmetatable(env,{__index=...→GLOBAL.rawget})` —— mod 环境回退到 `_G`，
  这样 prefab 文件里的 `CreateEntity`/`MakeInventoryPhysics`/`Vector3` 等裸名能用
  （PC 等大量 mod 的标准写法；我们之前没加所以 prefab 文件会全是 nil）。
- `omni_box()` / 菜单「给随身箱子」→ `SpawnPrefab("omni_box")`。

**没做**（PC 有、我们暂缺，按需再加）：隔箱合成取材料、多箱分页合并、一键整理、
自定义大容量 UI 背景框。

### 待做：穿戴装备格扩展（帽子/衣服/护符 多穿）

用户还要"扩展穿戴格子"的 mod。参考 `_disabled_thirdparty_mods/workshop-571751170`
（Extra Slots / Han's Extra Equip Slots）—— 会重改 HUD `inventorybar` + `inventory` 组件的
equipslots。是个大件，下一轮做。

### v0.0.7 (2026-09-06)

- **omni_box 崩溃修复**：`GiveItem` 时报 `Could not find region 'omni_box.tex'` —— 没有物品栏
  图标。`inventoryitem.imagename = "krampus_sack"` 借游戏自带图标。
- **箱子进建造栏**：`modmain.lua` 加 `Recipe("omni_box", {Ingredient("cutgrass",1)},
  RECIPETABS.SURVIVAL, TECH.NONE)`（免费建造默认开，材料只是占位），`recipe.atlas/image`
  用 krampus_sack 图标。作弊菜单里的「给随身箱子」删掉（`omni_box()` 控制台还留着）。
- **秒砍伐没生效？**：`WorkedBy` 包装看起来是对的，怀疑上次是被 omni_box 的报错屏污染了会话。
  这版：① 包装条件从 `worker==GetPlayer()` 放宽成 `worker.components.inventory ~= nil`
  ② 加了 5 次上限的调试打印「秒砍伐生效 -> <prefab>」，下次看 log 能确认
  ③ `apply_player` 里把玩家 `worker` 组件的 CHOP/MINE/HAMMER/DIG 效率设 999（空手也算）。
- **一键采集**（`omni_harvest()` / 菜单「一键采集周围」）：`TheSim:FindEntities` 半径 30，
  对每个：`pickable:Pick` / `crop:Harvest` / `harvestable:Harvest` / `workable:Destroy`
  （workleft<=40，排除墙和建筑）/ 捡起地上 `inventoryitem`。

### 下一轮：随身箱子完整版（用户点名要）

参考 `_disabled_thirdparty_mods/workshop-2972769037` 的 `modmain.lua` +
`scripts/components/trueportablecellar.lua`：
- **隔箱合成取材料**：改写玩家 `inventory` 的 `GetItems / FindItem / FindItems /
  GetNextAvailableSlot / GetItemSlot`，把箱子里的物品也算进去（PC 的 modmain 56-260 行）。
- **多箱分页合并**：`trueportablecellar` 组件，多个箱子当一个虚拟大容器翻页。
- **一键整理**：PC 的 `sortcontainers()`（prefab 77-446 行），按类别+耐久排序回填。
- **大网格背景框**：PC 自带 `images/iai_pc_8x15` / `iai_pc_12x20` 贴图 + `widgetbgatlas/
  widgetbgimage`。要么复制那两个资源，要么不做框（现在就是无框裸槽）。

### v0.0.8 (2026-09-06)

- **删掉**：`omni_box()` / `omni_harvest()` 控制台指令、作弊菜单「一键采集周围」行。
  （箱子改成只在建造栏造。）
- **采集不出动作**（`cheats.lua`，并入 `omni_work` 开关）：`AddStategraphPostInit("wilson")`
  把 `dolongaction`（采草/摘果/挖花/收割等）压到 4 帧完成，关掉 `秒砍伐` 时走原版慢动作。
  砍树/挖矿走各自的 chop/mine state，靠 `WorkedBy` 一击到 0 提前退出。
- **随身箱子**：60 → **120 格**（12×10，槽距 60）。`widgetpos` 上移到 60。
- **反鲜（不是保鲜）**：`omni_box.lua` `on_itemget` 从 `StopPerishing()` 改成
  `SetPercent(1)` + `StopPerishing()` —— 放进去立刻恢复到最新鲜、之后也不腐。
- **隔箱合成**（`box_craft.lua`，新模块）：`AddComponentPostInit("inventory")` 包
  `Count` / `GetCraftingIngredient` / `RemoveItem` —— 合成/建造时把物品栏（含背包）里
  所有 `omni_box` 的内容也算进去，不用先打开箱子。已打开的箱子（在 `opencontainers`）
  跳过以免重复计数。用了 `Container:Count / GetCraftingIngredient / RemoveItem` 现成方法。

### 随身箱子 —— 还没做的部分

- 多箱分页合并（`trueportablecellar` 组件）
- 一键整理按钮（PC 的 `sortcontainers`）
- 大网格背景框（现在是无框裸槽；PC 自带 `iai_pc_8x15/12x20` 贴图）

### v0.0.9 (2026-09-07)

- **懒人护符还在掉耐久**：查了 `amulet.lua` —— 这个 build 里 `orangeamulet`
  ("The Lazy Forager") 的耐久是 **finiteuses**（不是 fueled，我上一版改错组件了）。
  每自动捡一件 `finiteuses:Use(1)`，而 `FiniteUses:Use` 判断 `if not self.unlimited_uses`。
  修法：`AddPrefabPostInit("orangeamulet")` 设 `finiteuses.unlimited_uses = true` +
  `SetPercent(1)` —— 永不掉、耐久条一直满。（不是新护符，是原来那个。）
- **采集自动堆叠进箱子**（`box_craft.lua`）：改写 `Inventory:GiveItem` —— 拿到可堆叠物品且
  没指定 slot 时，先扫所有箱子（含已打开的），有同名未满的格子就 `stackable:Put` 堆进去，
  堆满了继续找 / 走原逻辑。perishable 的顺带 `SetPercent(1)`。
  `boxes_of` 加了 `include_open` 参数（合成那三个方法仍跳过已打开的箱子避免重复计数，
  GiveItem 这里连打开的也算）。

### v0.2.0 (2026-09-07) — 防崩管家 + 永久光照 + 生物血量

- **回复理智**：`omni_full` 改回 `omni_sanity`（只回理智），菜单「回复理智」。
- **身上永久光照**（`cheats.lua` `state.light`，默认开，`omni_light`）：`apply_player` 里
  `p.entity:AddLight()`（若无）+ SetRadius 6 / Intensity .75 / Enable。跨世界重套。
- **生物血量显示**（`cheats.lua` `state.hpbar`，默认开，`omni_hpbar`）：抄 Health Info Plus
  的思路 —— 一次性包 `GLOBAL.EntityScript.GetDisplayName`，鼠标指到有 health 的生物时在
  名字后面拼 `[cur/max] 攻X`。跳过玩家自己。
- **防崩管家**（`janitor.lua` 新模块，`state.janitor` 默认开，`omni_janitor`）：
  `AddSimPostInit` 里挂世界的周期任务 —— 每 30s `collectgarbage("collect")`；
  每 180s `sweep()` 只删远处(>50)纯垃圾（`persists==false` 的 FX / `ash` /
  远处彻底腐烂没主的东西，跳过 irreplaceable）；左上角 `Text` 显示 `Lua NN MB · M 分`，
  >200MB 变红加「建议存盘重进」。HUD 重建后 `ensure_hud` 会重新挂。
- 菜单加到 13 行，面板放大到 2.0×2.9、行距 40、标题 y=275。

### v0.2.1 (2026-09-07) — 血量显示修复 + 菜单两列

- **生物血量没显示**：hoverer.lua 只有在 `lmb.invobject == nil`（空手）时才用
  `GetDisplayName()` 拼名字；手里拿工具时走的是动作字符串，不会再拼名字。所以只改
  `GetDisplayName` 不够。新模块 `healthinfo.lua`（从 cheats.lua 挪出来）抄 Health
  Info Plus 的双改：① `EntityScript:GetDisplayName`（空手）② `playercontroller:
  GetLeftMouseAction` 打标记 + `BufferedAction:GetActionString` 补血量（手持工具）。
  `state.hpbar` 开关不变。
- **作弊菜单两列**：13 行拆成 左 7（地图/速度/免建造/秒采伐/锁血/伤害/光照）+
  右 6（血量/防崩/回理智/全默认/全关闭/返回），两个竖排 `Menu`，用
  `SetFocusChangeDir(MOVE_LEFT/RIGHT)` 按行号配对连焦点。标签全缩到 ≤3 字避免截断，
  字号 28，面板缩回 2.2×1.95。

### v0.2.3 (2026-09-07)

- **菜单文字截断**：`Menu` 用的 `ImageButton` 底图宽度固定会裁字。改成自己用
  **`TextButton`**（纯文字、无底图、不裁）排两列：`self.root:AddChild(TextButton(""))`，
  `SetPosition(col.x, 150-(i-1)*44)`，`SetOnClick`，手动 `SetFocusChangeDir`
  MOVE_UP/DOWN（列内）+ MOVE_LEFT/RIGHT（列间）。`default_focus = L[1]`。标签放回全名。
- **伤害倍率没生效**：`apply_player` 里直接设 `combat.damagemultiplier` 会被角色（沃尔夫冈）
  和 buff 每帧覆盖。改成 `AddComponentPostInit("combat")` 包 `CalcDamage`，玩家出手时把
  最终伤害 `* state.dmg`。`omni_dmg` 开关不变。
- **箱子空间**：120 → **192 格**（16×12，槽距 52）。

### v0.3.0 (2026-09-07) — 4 个新功能（都 always-on，无菜单开关）

- **`foodinfo`**：鼠标指到食物在名字后拼 `饥+N 血+N 智+N 鲜N%`。跟 healthinfo 一样两处
  改（GetDisplayName + GetActionString）。`edible:GetHunger/GetHealth/GetSanity(player)`。
- **`cookstack`**：抄 Cook Stack Food 精简版。`AddComponentPostInit("stewer")` 包
  StartCooking（算最小堆叠数、多的返还、记 foodstack）+ Harvest（补 (stack-1)*配方产量 份，
  按 40 上限分次给）+ OnSave/OnLoad 存 foodstack。`cooking.recipes[pot][product].stacksize`。
- **`status`**：① `AddClassPostConstruct("widgets/statusdisplays")` 把 heart/stomach/brain
  的 `.num` `:Show()`（引擎一直在 SetString，只是 Hide 了）② HUD 顶部加一行 Text，
  周期任务显示 `第 N 天 · 季节 · 温度`（`GetClock().numcycles+1` / `GetSeasonManager().
  current_season` / `temperature:GetCurrent()`）。
- **`boxpages`**：`AddClassPostConstruct("widgets/containerwidget")` 包 `Open`，打开的是
  omni_box 且身上 ≥2 个箱子时，加一排 `◀ i/n ▶`（`spin_arrow.tex`）。翻页 = 关当前箱子 +
  `Open` 列表里上/下一个，UI 自然重建成新一页。`_omni_pager` 存在 widget 上，重开时先 Kill。
  **待验证**：翻页闪一下正常；箭头贴图/位置可能要调。
