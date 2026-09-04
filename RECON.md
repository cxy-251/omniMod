# SC2 离线化 + 自制 mod —— 现状勘察 (2026-09-04)

## 安装

| 项 | 值 |
|---|---|
| 游戏目录 | `/home/deck/Games/StarCraft II/` |
| 客户端 | **国服 / 网易 (NetEase)**，非全球版。`.build.info`: Branch=`cn`，CDN=`blzdist-s2.necdn.leihuo.netease.com`，`acct-CHN geoip-CN zhCN` |
| 版本 | 5.0.16.97579，内核 `Versions/Base97579/SC2_x64.exe` (66 MB) |
| 启动器 | `Support64/SC2Switcher_x64.exe` (由战网调用的引导器) |
| 编辑器 | `Support64/SC2Editor_x64.exe` (87 MB，完整银河编辑器) + 根目录 `StarCraft II Editor_x64.exe` |
| 战网 | `/home/deck/Games/Battle.net/` —— **网易国服战网** v2.52.10.17731 (`acct-CHN`) |
| Wine 前缀 | `~/.local/share/omni_deck_pfx` (Proton 前缀，omni-deck 管理) |
| Proton | Steam 库里有 Experimental / 11 / 10 / 9 / 8 / 7 / 5.13 / Hotfix |
| Maps | `Maps/` 已有 AIE 天梯图 (AbyssalReefAIE 等)；`Old_Maps_Archive/` |
| Mods | `Mods/` 空 |
| omni-deck 入口 | 「独立游戏 / Windows 软件」区，`custom_id='StarCraft II'`，exe=`Support64/SC2Switcher_x64.exe`，Proton 运行，注释写「绕过战网」 |

## 关键事实

- 前缀内 **没有 SC2 用户数据** (无 Documents/StarCraft II、无 Variables.txt、无 Banks) —— 游戏很可能**从没在这个前缀里成功跑起来过**，或跑起来没进到会写用户数据的程度。
- 国服客户端认证走**网易服务器** (leihuo.netease.com)，不是暴雪全球 Battle.net。
- SC2 **没有运行时代码注入 / Harmony 那种 mod 方式**。mod = 用银河编辑器做的 `.SC2Mod`（数据表 + 触发器 + 脚本包）。

## "5x 采集 / 快速建造 / 采不完" 怎么做

都是 `.SC2Mod` 里的 Catalog 覆盖，编辑器里改几个字段：
- **采集倍率**: 工人采矿 `Effect - Resource Harvest` 的携带量，或矿点 `CUnit` 的 `Resource - Contents`。5→25。
- **采不完**: 矿点 `Contents` 设成天文数字；或周期性触发器补满；或行为(Behavior)重置。
- **快速建造**: 所有建筑 `Cost - Time` 缩放；或 `CAbilBuild` 建造时间。

用法：把这个 mod 作为**依赖**加到每张天梯图上（编辑器 → Dependencies），存副本；对 AI 对战就生效。可脚本批量给所有图挂依赖。

## 离线方案 (国服客户端是难点)

| 路线 | 做法 | 门槛 |
|---|---|---|
| 1. SC2 自带离线模式 | 前缀里用网易账号**成功登录一次**缓存许可 → 之后断网启动 → 「离线模式」→ 战役/打电脑/自定义图/编辑器可用 ~30 天，联网一次续期 | 需要能用的网易账号 + 国服战网在 Wine 里能登进去；国服离线宽容度未知 |
| 2. 国服 → 全球版转换 | 换 `.build.info`/`.product.db` 指向暴雪全球 `s2`，让它补丁到全球内核 → 用全球版离线模式（文档完善、可靠） | 需要暴雪全球账号登一次；补丁下载 ~GB |
| 3. 社区离线启动器 / 免认证引导 | 打补丁的 `SC2Switcher` 或直接 `SC2_x64.exe` 跳过认证进菜单；或 **AI-API 路线**：`SC2_x64.exe -listen 127.0.0.1 -port N -displayMode 1`（aiarena 打 bot 天梯用的接口），零战网、任意图打内置 AI，用 python 脚本(burnysc2)驱动 | 灰色地带（你全买了）；AI-API 路线没有战役、没有常规菜单，是脚本开局 |

## 待用户确认

1. 有没有能登录一次的账号？网易国服账号？还是有暴雪全球账号？
2. SC2 通过 omni-deck 启动过吗？到过什么画面（登录页 / 报错 / 黑屏）？
3. 战役也要离线打，还是主要就是「任意图 vs 残酷电脑 + cheat mod」？（决定走路线 1/2 还是路线 3 的 AI-API）
