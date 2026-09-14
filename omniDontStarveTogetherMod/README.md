# OmniDontStarveTogetherMod

联机版《饥荒》(DST) 的自制 mod，对标单机版 `../omniDontStarveMod`。

- **游戏**：Don't Starve Together，Windows x64 版，version 747465，Proton 运行
- **安装**：`/run/media/deck/FUCKDECK/standalone_games/steam_games/014 - Don't Starve Together/`
- **mod API**：10（联机版）。客户端/服务器架构 —— 改世界/作弊在服务器侧，UI 在客户端。

## 现状

离线化第一步（gbe_fork win-x64）已装。功能模块还没做。详见 `HANDOFF.md`。

## 关键区别（vs 单机版）

`GLOBAL.TheWorld` / `GLOBAL.ThePlayer` / `GLOBAL.AllPlayers`；改东西前 `if GLOBAL.TheWorld.ismastersim`；
客户端改服务器状态发 RPC；容器走中心化 `containers.params`；角色默认全解锁。
