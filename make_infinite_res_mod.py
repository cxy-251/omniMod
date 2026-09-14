#!/usr/bin/env python3
"""
生成"资源采不完"目录片段 —— 所有矿脉 / 气矿 / 泰伯林矿的总量(Contents)和上限
(Capacity)拉到一个大到实际对局挖不空的值，工人照常采集，矿脉也不会缩小消失。

## 为什么不是扩展 mod、而是一段"焊进地图"的目录片段

踩过的坑（用户实测）：把这些 <CBehaviorResource> 改动作为**扩展 mod 依赖**挂在地图
DocumentHeader 上——`5xHarvest` 改 `HarvestAmount`（每趟采集实时读目录）生效，
但这里改 `Contents`（建单位时读一次）**到不了已经摆在地图上的矿点**，矿照常采空。

参照物：仓库里那张 `单位升级+...无限矿产...` 作弊图，它就是把一模一样的
`<CBehaviorResource><Capacity/><Contents/></CBehaviorResource>` 焊进**地图自己的**
`Base.SC2Data\\GameData\\BehaviorData.xml`（不是扩展 mod），没有任何触发器碰矿点，
就能生效。所以这里输出一段纯 XML 片段，由 bake.py / _bake_inner.py 在烤图时合并
进地图副本的 BehaviorData.xml。

## 数量取值

默认 100 万：一块矿 100 万 ÷ 每趟 5 = 20 万趟，单个工人挖到天荒地老也挖不空，
但数字还能在矿点 / 单位信息栏正常显示（10 亿会把数字 UI 撑爆，就是用户说的
"看不到剩余晶矿数量"）。要更夸张改 BIG 即可，int32 上限约 21.47 亿。

## 跟别的 mod 组合

只动 Contents / Capacity 两个字段，3x/5x 采集只动 HarvestAmount / 工人能力倍率，
macroSpeed / instantBuild 只动耗时——字段不重叠，叠着开互不干扰。

用法：
  uv run python make_infinite_res_mod.py            # 用默认 100 万
  uv run python make_infinite_res_mod.py 2000000000 # 自定义数量
产物：mods/infiniteRes.behavior.xml
"""
from __future__ import annotations

import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
OUT = HERE / "mods" / "infiniteRes.behavior.xml"

# 默认 100 万（见上面"数量取值"）。命令行第一个参数可覆盖。
BIG = 1_000_000

# 全部矿脉 / 气矿的 CBehaviorResource id。CascLib 从 core/liberty/void/voidmulti 合并
# 去重挖出来的真实集合（Lab/Purifier/BattleStation/Rich 各种摆放单位复用的都是这些
# 行为 id，没有单独的 LabMineralFieldMinerals 之类）。带 * 的在当前版本里查不到定义，
# 留着当双保险（覆盖不存在的 id = 空操作，不报错）。
MINERAL_IDS = [
    "MineralFieldMinerals",
    "MineralFieldMinerals750",
    "MineralFieldMinerals450",
    "MineralFieldMineralsOpaque",
    "MineralFieldMineralsOpaque900",
    "HighYieldMineralFieldMinerals",
    "HighYieldMineralFieldMinerals750",
    "MineralFieldMineralsNoRemove",   # *
]
GAS_IDS = [
    # "生"状态——真正决定气矿起始总量的，引擎认这个（voidmulti 自己把它的 Contents
    # 从 2500 改成 2000）
    "RawVespeneGeyserGas",
    "RawRichVespeneGeyserGas",
    "RawTerrazineGeyserGas",          # *
    # "可采"状态（精炼厂建好后）——当双保险一起覆盖
    "HarvestableVespeneGeyserGas",
    "HarvestableVespeneGeyserGasProtoss",
    "HarvestableVespeneGeyserGasZerg",
    "HarvestableRichVespeneGeyserGas",
    "HarvestableRichVespeneGeyserGasProtoss",
    "HarvestableRichVespeneGeyserGasZerg",
    "HarvestableTerrazineGeyserGas",          # *
    "HarvestableTerrazineGeyserGasProtoss",   # *
    "HarvestableTerrazineGeyserGasZerg",      # *
]


def build_fragment(big: int) -> str:
    lines = ['<?xml version="1.0" encoding="utf-8"?>', "<Catalog>"]
    for rid in MINERAL_IDS + GAS_IDS:
        lines.append(
            f'    <CBehaviorResource id="{rid}">'
            f'<Capacity value="{big}"/><Contents value="{big}"/>'
            f'</CBehaviorResource>'
        )
    lines.append("</Catalog>")
    return "\n".join(lines) + "\n"


def main() -> None:
    big = int(sys.argv[1]) if len(sys.argv) > 1 else BIG
    if not 0 < big < 2_147_483_647:
        sys.exit(f"数量要在 1 .. 2147483646 之间：{big}")
    OUT.write_text(build_fragment(big), encoding="utf-8")
    print(f"OK: {OUT}")
    print(f"  {len(MINERAL_IDS)} 个矿脉 id + {len(GAS_IDS)} 个气矿 id，Contents/Capacity -> {big:,}")
    print()
    print("bake.py 的 CATALOG_MERGES 已指向这个文件，烤图时按 id 焊进地图 BehaviorData.xml")
    print("（地图原有同 id 条目整块换掉）。")


if __name__ == "__main__":
    main()
