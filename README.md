# OmniDontStarveMod

自制的单机《饥荒》mod，对标 ONI 的 `../omniMod`。不用别人的 mod，功能按需一个个加。

- **游戏**：单机版 Don't Starve（**不是**联机版），原生 Linux 32 位，build 578406，DLC 全（巨人国 / 海难 / 哈姆雷特）
- **安装**：`/run/media/deck/FUCKDECK/standalone_games/steam_games/013 - Don't Starve/`（脱离 Steam 管理的独立副本，跟 SC2 那套一样）
- **存档**：`~/.klei/DoNotStarve/`
- **mod API**：6（单机版）。Lua，无编译。

## 开发循环

```sh
./deploy.sh      # symlink 本目录 -> 游戏 mods/omniDontStarveMod，并加 ForceEnableMod
# 改 modmain.lua / scripts/omnidsm/*.lua
# 通过 omni-deck 的「013 - 饥荒单机版」启动，或直接跑 <game>/bin/dontstarve
./undeploy.sh    # 撤销
```

symlink 方式下，改代码重启游戏即生效，不用复制。项目里的 `.git` / `*.sh` / `*.md` 游戏会忽略。

## 结构

| 文件 | 作用 |
|---|---|
| `modinfo.lua` | mod 清单 + `configuration_options`（功能开关会出现在 主菜单>Mods>配置） |
| `modmain.lua` | 入口。`FEATURES` 列表登记要加载的功能模块 |
| `scripts/omnidsm/*.lua` | 一个功能一个模块，导出 `.init(GLOBAL)` |
| `scripts/omnidsm/_template.lua` | 新功能模板 |
| `deploy.sh` / `undeploy.sh` | 装/卸到游戏 mods/ |

## 关键约定

**所有"运行时改角色"的东西都在 `AddSimPostInit` 里做。** 每次世界加载（含下洞穴、三大世界互跳）都会触发 → 避免"后台指令过场就重置"的老毛病。

## 已实现功能

- `unlockchars` — 解锁所有人物（覆盖 `PlayerProfile:IsCharacterUnlocked` 恒 true）。
- `cheats` — 一组**后台指令可控**的作弊，**默认全开**，控制台（` 键）可关/调：
  | 指令 | 作用 | 默认 |
  |---|---|---|
  | `omni()` | 查看所有状态 | — |
  | `omni_map(true/false)` | 地图全开 | 开 |
  | `omni_speed(n)` | 行走速度倍率 | 2 |
  | `omni_tech(true/false)` | 科技全解锁（材料仍需要） | 开 |
  | `omni_hp(true/false)` | 生命下限锁 10（掉血不死） | 开 |
  | `omni_dmg(n)` | 伤害倍率 | 3 |
  | `omni_off()` / `omni_on()` | 全关 / 全恢复默认 | — |
  改动在 `AddSimPostInit`/`AddPlayerPostInit` 里重套，过场（洞穴/三世界互跳）不重置。

## 计划中的功能（做一个开一个）

- `treeshake_bear` — 召唤熊獾，利用其践踏（Ground Pound）震树掉落物，默认不砸自家建筑。
- `autopickup` — 身边一圈掉落物自动进背包，**无耐久损耗**；过滤档：全部 / 只宝石+矿物。
- `janitor` — 定时 `collectgarbage` + 清远处掉落物/尸体/蛛网残渣 + 屏幕角内存 MB，超阈值提示"该存盘重进"。
- `cheatmenu`（可选）— 把上面这些做成屏幕按钮+菜单，免开控制台。

## 离线化现状

见 `HANDOFF.md`。
