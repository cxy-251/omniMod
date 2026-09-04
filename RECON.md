# sc2Mod —— 星际 2 离线打电脑 + cheat mod

## 目标（用户原话提炼）

- 主要玩法：**任意天梯图 vs 残酷电脑（CheatInsane，AI 有资源加成）**。战役买了(国服)但基本不玩。
- 痛点：新版 SC2 初始农民少 + 残酷电脑额外资源 → 前期怎么发展都比电脑慢（除非卡电脑开矿点触发龟缩 bug）。
- 需求 mod：**初始农民翻倍 / 采集 5 倍 / 资源采不完 / 快速建造**，用来抹平前期经济差。
- 要能**离线、免登录**打，配任意地图。

## 账号情况

| 账号 | 状态 |
|---|---|
| 国服(网易) | 满级，全战役 + 全指挥官已购 |
| 暴雪全球 | 有号，**什么都没买**（只有自由之翼基础） |

→ 国服值钱但离线难；全球版能离线但没内容。**都不理想**。

## 之前 Gemini 尝试的结果（关键教训）

- 通过 Wine 里的**网易战网**打开过游戏。
- **离线模式下无法新建地图开打**；网上下的地图**识别不了**。
- Gemini 做过 mod，但**没法测**：进游戏只有玩家一个人 → 直接判胜利（melee 图没有对手/AI）。

## 选定方案：s2client AI-API「真人 vs 内置AI」直连

**完全绕开战网 / 登录 / 离线模式**——这正是 aiarena.net 的 AI 天梯在 Linux 服务器上跑零暴雪账号 SC2 的方式。

一个 Linux 端 python 脚本：
1. 用 Proton 起 `Versions/Base97579/SC2_x64.exe -listen 127.0.0.1 -port <p> -displayMode 1`
2. 连本地 websocket
3. `RequestCreateGame`：本地地图路径 + 玩家 `[Participant, Computer(种族, Difficulty.CheatInsane, AIBuild)]`
4. `RequestJoinGame` 以 Participant 加入（realtime，不 step）
5. 渲染窗口出来，你正常鼠标键盘玩；脚本只维持连接

好处：
- 零登录、零离线模式依赖。
- 「地图识别不了」——脚本直接传文件路径，任何 `Maps/*.SC2Map` 都能开。
- 「只有一个玩家秒胜」——`CreateGame` 里显式加 `Computer` 对手，结构上就不会。
- 种族 / 难度 / 地图全是脚本参数。

缺点：没有常规主菜单、没有战役（战役要另想办法，但用户基本不玩）。

## Cheat mod 做法

SC2 没有运行时代码注入。做一个 `cheat.SC2Mod`（Catalog XML，手写即可，不用开编辑器 GUI）：
- 初始农民：地图 `CPlayer`/开局单位 或 melee 触发库覆盖 → 改 `MeleeInitialWorkers`
- 采集 5x：`CEffectResourceHarvest` 的 `Amount`（矿 5→25，气 4→20）
- 采不完：矿点 `CUnit`（MineralField 等）`Resource - Contents` 设 999999，或行为周期补满
- 快速建造：所有建筑 `Cost.TimeBuild` 全局缩放
把 mod 作为**依赖**批量挂到要玩的天梯图上（脚本遍历 `Maps/*.SC2Map` 改依赖表）。
或：`RequestCreateGame` 可以在 `local_map` 里带 mod 列表——先试这个，省得改图。

## 环境

| | |
|---|---|
| Python | 3.13.5，联网 OK |
| Proton | `~/.local/share/Steam/steamapps/common/Proton - Experimental/proton` |
| SC2 前缀 | `~/.local/share/omni_deck_pfx` |
| SC2 内核 | `/home/deck/Games/StarCraft II/Versions/Base97579/SC2_x64.exe` |
| 地图 | `/home/deck/Games/StarCraft II/Maps/*.SC2Map`（已有 AIE 天梯图） |
| 客户端 | 国服 5.0.16.97579（AI-API 自 3.16 起所有零售版都带，此版本必有） |

## 待办

- [ ] 装 `s2clientprotocol`（+ 视需要 `burnysc2`）到一个 venv
- [ ] 写 Proton 启动包装 + 最小 s2client 握手脚本 `play.py`
- [ ] 跑通「真人 vs CheatInsane，AbyssalReefAIE」一局
- [ ] 写 `cheat.SC2Mod`，先试 CreateGame 带 mod；不行再改图依赖
- [ ] omni-deck 里把「星际争霸2」入口换成调 `play.py`（带参数选图/种族/难度）
- [ ] 整理地图库
