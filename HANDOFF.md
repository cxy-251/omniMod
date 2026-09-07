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
