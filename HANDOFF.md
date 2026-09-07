# HANDOFF — 联机版《饥荒》(DST) 离线化 + mod 项目起步

日期：2026-09-07。对标 `../omniDontStarveMod`（单机版，已做完，v0.5.1）。

## 侦察结果

| 项 | 情况 |
|---|---|
| 游戏 | Don't Starve Together，**Windows x64 版**（`dontstarve_steam_x64.exe` PE32+），version **747465** |
| 真正的安装 | SD 卡 `standalone_games/steam_games/014 - Don't Starve Together/`，完整（bin/bin64/data/mods/DXRedist/VCRedist）。binary = `bin64/dontstarve_steam_x64.exe` |
| `steamapps/common/Don't Starve Together/` | 只剩 `data/ mods/ cached_mods/`，空壳，忽略 |
| 运行方式 | **Proton/Wine**（Windows exe）。omni-deck 的 `launch_standalone_game_process` 对 `.exe` 走 `get_wine_or_proton_runner(exe_path, game_id)` —— 已经能从 omni-deck「014 - 饥荒联机版」卡片启动（Proton + compat 前缀，默认 `~/.local/share/omni_deck_pfx`） |
| 存档 | `~/.klei/DoNotStarveTogether/` 还不存在（没跑过），Windows-via-Proton 会存到 Wine 前缀的 `Documents/Klei/DoNotStarveTogether/` |
| 创意工坊 mod | 16 个（`common/Don't Starve Together/mods/workshop-*` 和 standalone 的 `mods/`） |
| omni-deck | `DISPLAY_NAMES` 已有 `'014 - Don't Starve Together': '014 - 饥荒联机版'`；exe 识别列表含 `dontstarve_steam_x64.exe` |

## 已完成：离线化第一步（gbe_fork）

跟单机版一个路子，但是 **Windows x64 版 gbe_fork**：
- `bin64/steam_api64.dll` 换成 **gbe_fork `release-2026_08_23` regular/x64**（11 MB）。
  正版备份：`~/.klei/dst-omnideck-backup-<时间戳>/steam_api64.dll.valve-orig`
  和 `bin64/steam_api64.dll.valve-orig`。
- `bin64/steam_settings/`：
  - `steam_appid.txt` = 322330，`installed_app_ids.txt` = 322330
  - `configs.app.ini` `[app::dlcs] unlock_all=1`
  - `configs.main.ini` `disable_networking=1` + `offline=1`
  - `configs.user.ini` `language=schinese` + 固定假身份
  - `steam_interfaces.txt`（从正版 dll `strings` 提取的真实接口版本：SteamClient020 /
    SteamUser021 / STEAMUGC_INTERFACE_VERSION015 等，DST 747465 的）
  - `supported_languages.txt` = schinese/english
- `bin64/steam_appid.txt` + 根目录 `steam_appid.txt` = 322330。
- gbe dll + 配置也存了一份到 backup 目录 `gbe_fork-win-x64/`（含 SOURCE.txt）。

## 待做：离线化剩余

1. **确认 Proton 起得来**：从 omni-deck 点「014」看能不能进主菜单、Steam 初始化不卡。
   （gbe 让它不连真 Steam、不拉创意工坊、不同步云。）
2. **DST 语言/设置**：DST 用 `client_save/` 里的 `client.ini`（不是单机的 settings.ini），
   语言可能要在游戏内 Options 里选一次（跟单机的中文 `?` 问题类比 —— DST 中文字体是
   完整的，一般没问题）。
3. **本地房间的 mod 启用**：DST 走 `mods/dedicated_server_mods_setup.lua` +
   建房时的 `modoverrides.lua`（在存档 cluster 目录里）。我们的 mod 要 symlink 进
   `<game>/mods/omniDontStarveTogetherMod` 并在 `dedicated_server_mods_setup.lua` 里
   `ServerModSetup("omniDontStarveTogetherMod")` / 或前缀。
4. 第三方 16 个 workshop mod：用户单机版是"不用别人的"，联机版大概率同样 —— 移出 `mods/`。
5. 启动前存档备份 wrapper（Wine 前缀里的 Klei 目录）。

## 待做：mod 功能（对标单机版，但要按客户端/服务器改写）

单机版有的（`../omniDontStarveMod` v0.5.1，12 个 feature）：
`cheats`(map/speed/tech=freebuild/work/hp/dmg/light/hpbar/sanity) + `cheatmenu` +
`box_craft` + `janitor` + `healthinfo` + `foodinfo` + `cookstack` + `status` +
`boxpages` + `hovertip` + `omni_box` prefab + `modworldgenmain`(环形世界)。

DST 改写要点：
- **cheats**：全部在 `TheWorld.ismastersim` 里改（`AddPrefabPostInit`/`AddComponentPostInit`
  /`AddSimPostInit`）。`builder.freebuildmode`、`health:SetMinHealth`、`combat` 都在。
  移速、光照 = 服务器改玩家实体。
- **cheatmenu**：客户端 `Screen`（暂停菜单注入还是 `screens/pausescreen`），但开关要
  `SendModRPCToServer`（solo 房里 `ThePlayer` 是房主，也可以直接判断 ismastersim 就地改）。
- **omni_box**：DST 容器是**中心化的** `require("containers").params[prefab] = {...}` +
  `containers.widgetsetup(prefab)`，不是单机的 per-prefab `container.widgetslotpos`。
  隔箱合成、翻页要重写。
- **worldgen**：DST level id 是 `SURVIVAL_TOGETHER`（不是 `SURVIVAL_DEFAULT_PLUS`），
  task set 在 `map/tasksets/`。`branching`/`loop` override 应该还在；
  `SeperateStoryByBlanks` 可能改名，要查 DST 的 `map/storygen.lua`。
- **unlockchars**：DST 角色默认全解锁，不需要。
- **status / combined status**：DST 自带的状态显示比单机好，可能只做温度/天数一行。
- 悬停提示（healthinfo/foodinfo/hovertip）：DST 的 `widgets/hoverer` 还在，思路可复用。

## 项目脚手架

`~/Games/claude/omniDontStarveTogetherMod/`（与 omniMod / sc2Mod / omniDontStarveMod 同级），
git 已 init。`modinfo.lua`（api 10，all_clients_require_mod）+ `modmain.lua`（`FEATURES={}`）
+ `scripts/omnidst/`。deploy 脚本待写（要处理 DST 的 mod 启用机制）。

---

## 更新 (2026-09-07)：改用原生 Linux 版 + 只做单人

用户指出：DST 有原生 Linux 版，而且他 Steam 里就有这个游戏；他只单人玩，不需要联机功能。

- **SD 卡上的 `014 - Don't Starve Together` 是 Windows 版**（`.exe`），只能 Proton 跑。
  **弃用它**。已把它上面的 gbe 改动全部回退（`steam_api64.dll` 换回正版、删掉
  `steam_settings/` 和 `steam_appid.txt`）。Windows 版的 gbe 文件还留在
  `~/.klei/dst-omnideck-backup-*/gbe_fork-win-x64/` 备用。
- **让用户从 Steam 重新下载 DST** → 会装到 `~/.local/share/Steam/steamapps/common/
  Don't Starve Together/`（现在是空壳，Steam 会填满），是**原生 Linux 版**
  （`bin64/dontstarve_steam_x64` ELF，不是 .exe）。
- 下完之后：跟单机版一模一样的路子 —— **Linux .so 版 gbe_fork**（不是 Windows .dll）、
  中文、`dst-omnideck-backup` 存正版 `.so`。可选：像 SC2/单机DS 那样复制成 SD 卡冻结副本
  再脱离 Steam 管理（防自动更新破坏 mod/存档）。
- **只做单人**：solo 自建房里 `TheWorld.ismastersim` 在客户端就是 true，`ThePlayer`
  就是房主 —— **不用写 RPC**，作弊直接在 `ismastersim` 里改就行。功能移植大大简化，
  跟单机版差别没那么大了（主要是 `TheWorld`/`ThePlayer` 命名 + 容器中心化）。

**下一步**：等用户从 Steam 下完 DST（原生 Linux 版）。

---

## 更新 (2026-09-07 #2)：用户重下 DST，Steam 又给了 Windows 版

用户从 Steam 重新下载 DST 到 `FUCKDECK/steamapps/common/Don't Starve Together/`
（4.4G，`appmanifest_322330.acf` StateFlags 4）。**但装的还是 Windows 版**：
`InstalledDepots` = **322331**（DST Windows 内容库），带 `bin64/dontstarve_steam_x64.exe`
+ `DXRedist/` + `VCRedist/`。Linux 版应为 depot **322332**。

原因：Deck 的「为所有其他游戏启用 Steam Play」全局开关，对有原生 Linux 版的游戏
会让 Steam 抓 Windows 库、跳过 Linux 库。CompatToolMapping 里 322330 没有强制条目。

**用户已决定：走原生 Linux 版。** 让用户在桌面模式：
右键 DST → 属性 → 兼容性 → 勾「强制使用特定 Steam Play 兼容性工具」→ 选
**「Steam Linux Runtime 3.0 (sniper)」**（不是 Proton）→ Steam 重下 depot 322332
（~550M）。验证：`bin64/dontstarve_steam_x64` 是 ELF、无 DXRedist/VCRedist。

下完后再做离线化（Linux `.so` gbe_fork，不是 win dll）。Windows gbe 备份仍在
`~/.klei/dst-omnideck-backup-*/gbe_fork-win-x64/`。

## 单机版 013 顺手瘦身

删掉了 `013 - Don't Starve/_disabled_thirdparty_mods/`（39 个禁用的第三方 mod，
132M，游戏不加载，创意工坊可重下）。013 现 3.2G，全为必要文件（data 3.1G = 本体
+ RoG/SW/Hamlet 三 DLC，bin 37M）。

---

## 更新 (2026-09-07 #3)：原生 Linux 版离线化完成 ✅

用户在 Steam 里把 DST 兼容性设成「Steam Linux Runtime」后重下，depot 变 **322332**
（原生 Linux x64，`bin64/dontstarve_steam_x64` ELF）。已完成：

**冻结出 Steam 管理**（同 SC2 / 单机DS）：
- 旧 Windows `014` 改名 `014 - Don't Starve Together.WINDOWS-OLD`（4.2G，待用户确认后删）
- `steamapps/common/Don't Starve Together/` → `mv` 到
  `standalone_games/steam_games/014 - Don't Starve Together/`（同盘秒移）
- `steamapps/appmanifest_322330.acf` → `.acf.disabled`（防 Steam 自动更新覆盖补丁）

**gbe_fork（Linux .so，release-2026_08_23 regular）**：
- `bin64/lib64/libsteam_api.so` ← gbe x64（正版存 `.valve-orig` + backup 的 `gbe_fork-lin-x64/`）
  ＋ `steamclient.so`
- `bin/lib32/libsteam_api.so` ← gbe x86（同样备份；32 位 `bin/dontstarve` 已改名 `.disabled32`
  防 omni-deck 误选）
- `steam_settings/` 放在 bin64/ bin64/lib64/ bin/ bin/lib32/ 和根目录：appid 322330、
  `disable_networking=1`+`offline=1`+`disable_lobby_creation=1`、`unlock_all=1`、
  `language=schinese`、假身份 SteamID 76561197960287930、
  `steam_interfaces.txt`（`generate_interfaces_x64` 对正版 .so 生成，30 个接口，DST 747465）
- `steam_appid.txt` = 322330（根 + bin64 + bin64/lib64 + bin + bin/lib32）

**启动 wrapper**：`bin64/dontstarve` 重写 —— 写 steam_appid、启动前备份
`~/.klei/DoNotStarveTogether/Cluster_*`（留最近 15 份）、优先在 **Steam scout 运行时**
（`~/.local/share/Steam/ubuntu12_32/steam-runtime/run.sh`）里起 64 位引擎
（提供 `libcurl-gnutls.so.4`，这是引擎硬 NEEDED，SteamOS 系统没有）。
stock 脚本存 backup 的 `dontstarve.bin64.stock`。

**mod 启用**：`mods/omniDontStarveTogetherMod` → symlink 到项目；`mods/modsettings.lua`
重写为 `ForceEnableMod` + `DisableModDisabling()` + `DisableLocalModWarning()`
（stock 存 `modsettings.lua.stock`）。第三方 workshop mod：本次原生下载的 `mods/` 本来就干净。

**验证**（timeout 冒烟测试，读 `~/.klei/DoNotStarveTogether/omnideck-launch.log`）：
- `Don't Starve Together: 747465 LINUX_STEAM` `Mode: 64-bit`
- `Initializing distribution platform ... Steam AppBuildID: 10 ... Done` = **gbe 起来了**
- `Offline user ID: OU_76561197960287930` = 假身份生效
- `locale=CN&lang=schinese` = **中文生效**（DST 自带完整 CJK，无单机版的 `?` 问题）
- `[OmniDontStarveTogetherMod] v0.0.1 loaded (0 feature(s))` + `Registering prefabs` = 自制 mod 强制加载成功
- `Load FE: done` + `focus gained` = 进到主菜单
- omni-deck 打分：`bin64/dontstarve`（260 分）稳选，无 `.exe` → 原生直启不走 Proton

**已知无害项**：
- 主菜单 MOTD 图片下载每次 5s 超时后重试（离线，`klei-motd.klei.com` 连不上）——
  不致命，只是菜单有点烦。要彻底静音得改 /etc/hosts（需 sudo），暂不处理。
- `skilltree ... unrecoverable. Skill tree will be cleared.` —— 角色技能树需要联网拉，
  离线没有。单人沙盒+作弊玩法用不到。
- 首次运行 `Could not load modindex/morgue/...` —— 正常，文件还没生成。

## 待做：mod 功能移植（对标单机版 omniDontStarveMod v0.5.1）

脚手架 + 离线化都完成了。下一步开始往 `scripts/omnidst/` 写功能，`FEATURES` 里逐个开。
单人自建房 → `TheWorld.ismastersim` 客户端为 true，`ThePlayer` 即房主，**不用 RPC**。
顺序建议：cheats（服务器侧改组件）→ cheatmenu（客户端 Screen）→ box（containers.params）
→ janitor → worldgen（`SURVIVAL_TOGETHER`）。unlockchars 不需要（DST 默认全解锁）。

---

## 更新 (2026-09-07 #4)：功能移植 batch 1 + 两个大坑

### 坑 1：DST 引擎不加载「跨盘符号链接」的 mod 目录
`mods/omniDontStarveTogetherMod` 原来是 symlink → `/home/deck/Games/claude/...`（SD 卡
指向内置盘）。DST 引擎扫 `mods/` 时不解析这种跨文件系统的软链，`LoadModInfo` 拿不到
modinfo → `known_mods[mod]` 为空 → `GetModsToLoad` 里 force-enable 的 mod「isn't known」
→ 撞上 DST 自己的 bug（`mainfunctions.lua:1638 known_error_key is not declared`，
gamelogic 还没加载）→ `Error during game initialization` 整个起不来。
**解法**：`mods/omniDontStarveTogetherMod` 改成**真实目录**，项目里加 `deploy.sh`
（rsync 排除 .git/*.md），每次改完 mod 跑一下 `./deploy.sh`。
（附带教训：不要手删 `client_save/modindex` —— DST 的 `ModIndex:Load` 只在成功读到旧
index 时才 `UpdateModInfo()` 重扫，没有 index + 有 force-enable mod = 同样的崩。）

### 坑 2：退服/返回主菜单卡在加载页（+ 洞穴分片连不上）
DST「建游戏」带洞穴 = Master + Caves 两个 server 子进程，走 127.0.0.1:10888 互连。
离线环境下第一次能连上、能玩，但**客户端退回主菜单时 `DoRestart` 卡死**
（`[IPC] Sending signal` 后无响应），留下僵尸 server 进程 + `/dev/shm/sem.DST_*` 信号量，
导致**下一次启动 Caves 分片连不上 Master**（`Connection to master failed` 死循环）。
**已处理**：
- 现有世界 `cluster.ini` 改 `shard_enabled = false`、`Caves/` 目录改名挪走 →
  **单分片无洞穴**，彻底没有分片联网/退出等待。
- `bin64/dontstarve` wrapper 开头加：`pkill -9` 残留 dontstarve 进程 +
  `rm -f /dev/shm/sem.DST_* /dev/shm/DST_*`，防上次卡死污染下次启动。
- gbe `configs.main.ini` 精简为只留 `offline=1`（去掉 disable_networking /
  disable_lobby_creation / disable_source_query —— 会让退服等一个永不返回的回调）。
- **给用户的建议**：离开游戏用「退出到桌面」，别用「返回主菜单/断开连接」；
  omni-deck 按钮重新进。洞穴以后单独再搞（多分片 + 离线 = 麻烦）。

### batch 1 已移植（v0.1.0，`FEATURES` 里已开 4 个）
- `nonet.lua` —— 禁用 MotdManager（主菜单公告板不再联网 5s 超时重试）+ 关更新提示。
- `unlockall.lua` —— 角色本就全解锁（DST playerprofile 没有 IsCharacterUnlocked）；
  技能树：`skilltreeupdater` 上 `skip_validation=true` + 灌满 SKILL_THRESHOLDS 经验 +
  遍历 `SKILLTREE_DEFS[prefab]` 激活所有有 rpc_id 的技能。玩家生成后 +2s/+6s 跑，
  过图/复活重跑。控制台 `omni_skills()` 手动补。
- `cheats.lua` —— 服务器侧（ismastersim）：地图全开（遍历 TheWorld.Map 的 tile 调
  `player_classified.MapExplorer:RevealArea`）、移速（`locomotor:SetExternalSpeedMultiplier`）、
  免费建造（`builder.freebuildmode`）、秒砍伐（包 `Workable:WorkedBy`）、worker 效率、
  锁血下限 10（`health:SetMinHealth`）、伤害倍率（包 `Combat:CalcDamage`）、身上光照、
  生物血量悬停（包 `EntityScript:GetDisplayName` 简版）。控制台 `omni` / `omni_*`。
  `G.OMNIDST = {state, ...}`。
- `cheatmenu.lua` —— `AddClassPostConstruct("screens/redux/pausescreen")` 往暂停菜单
  加「作弊菜单」项；`CheatMenu` = 两列 TextButton 的 Screen，手柄焦点连线。

**冒烟测试**：4 个 feature 全部 `loaded`，无报错，进到主菜单。
**还没验证**：进世界后各作弊是否真生效、技能树是否真点满、作弊菜单 UI 是否正常 ——
需要用户实际游玩确认。

### 待移植 batch 2/3
box（DST 容器 `containers.params`）、janitor、healthinfo/foodinfo/hovertip、cookstack、
status、worldgen（`SURVIVAL_TOGETHER`）。

---

## 更新 (2026-09-07 #5)：磁盘狂读的真凶 = KDE baloo 文件索引器

用户发现进出世界时 **`baloo_file` 也在疯狂读**。`baloo_file` = KDE Plasma 桌面搜索的
文件索引器（SteamOS 桌面模式默认开着，已索引 379 万文件 / 2 GiB 索引库）。它的
`includeFolders` 是 `/home/deck/`，涵盖 `~/.klei/DoNotStarveTogether/`。

**反馈循环**：DST 每次自动存档 / 进出世界 → 重写 6MB 存档 + session 快照 + 日志
→ baloo 立刻侦测到变化 → 把这些文件全部读回去重新索引 → 再叠加 DST 自己反序列化
整张地图(425×425)+ 实体的读盘 → SD 卡上就是 40MB/s 持续读 + 卡顿。

**已处理（宿主机侧，不在 mod 里）**：
- `~/.config/baloofilerc` 加 `exclude folders[$e]=$HOME/.klei/`
- （`balooctl6 purge` 会全量重建索引、本身狂读盘，已中止；配置对新增写入即时生效）
- 用户可选：桌面模式里彻底 `balooctl6 disable`（游戏机不需要桌面文件搜索）

**仍然存在**：DST 冷启动黑屏 + 「回到世界」一次性大量读盘 —— 这部分是 DST 引擎
本身：反序列化整个存档 + 首次把资源从 SD 卡上的 `databundles/*.zip` 读进来。属于
DST 在慢速存储上的固有表现，mod 层面能做的有限（地图全开已改成一次性，不再是元凶）。

### mod v0.3.2
- `cheats`: 生物血量悬停改成钩 `widgets/hoverer:OnUpdate`（联机版 hoverer 只在有
  左键动作时才拼名字，指到被动生物没动作 -> 啥也不显示）。现在指到任何有 health 的
  实体都强制补 `[当前/最大] 攻X`。
- v0.3.1: 修 `FAST_TAGS` 定义顺序（之前 `ipairs(nil)` 每几秒刷一屏报错写爆日志 =
  用户看到的"疯狂读写"的一大来源）；`SkillTreeData:GetPointsForSkillXP -> 999`
  让全技能过 ValidateCharacterData、能存档能过图。

用户已确认：花能采了 ✅ 技能没问题 ✅。待确认：生物血量(v0.3.2 新)、baloo 排除后
磁盘是否正常。
