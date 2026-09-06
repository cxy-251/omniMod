#!/usr/bin/env python3
"""
生成"资源采不完"mod —— 所有矿脉 / 气矿 / 泰伯林矿的总量(Contents)和上限(Capacity)
拉到 10 亿，工人照常采集，但采集量相对总量小到可以忽略，实际游戏时长内挖不空、
矿脉也不会缩小消失。

## 为什么这条路对气矿也有效（跟 3x/5x 采集 mod 的气矿坑不一样）

3x/5x 那边踩的坑是：改 `HarvestableVespeneGeyserGas*` 这个"精炼厂建好之后"的行为，
引擎不认（换 id / 版本号 / 补全字段全试过没用）。这里绕开它——真正决定气矿初始
总量的是"精炼厂建好之前"的 `RawVespeneGeyserGas` / `RawRichVespeneGeyserGas`，
这个行为引擎是认的（voidmulti.sc2mod 自己就把它的 Contents 从 2500 改成了 2000，
CascLib 验证过）。气矿单位从"生"状态带着当前总量过渡到"可采"状态，只要"生"状态
起始就是 10 亿，建了精炼厂之后也还是 10 亿上下，采集量那点消耗忽略不计。
`Harvestable*` 那几个也一起覆盖了（当双保险，认不认都无所谓）。

矿脉这边跟 3x/5x 的晶体矿一样，直接改 `CBehaviorResource` 就生效。

## 跟别的 mod 组合

只动 Contents / Capacity 两个字段，3x/5x 采集只动 HarvestAmount / 工人能力倍率，
macroSpeed 只动耗时——字段不重叠，同一个 `CBehaviorResource` id 被多个 mod 覆盖时
引擎按字段合并，几个 mod 叠着开互不干扰。

用法：
  uv run python make_infinite_res_mod.py infiniteRes "资源采不完" "所有矿脉/气矿总量拉到 10 亿，实际游戏时长内挖不空"
"""
from __future__ import annotations

import ctypes
import shutil
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
TEMPLATE = HERE / "mods" / "5xHarvest.SC2Mod"   # 只借壳（ComponentList/DocumentInfo 结构一样）
_LIB = HERE / "lib" / "libstorm.so.9.30.0"
STORM = ctypes.CDLL(str(_LIB) if _LIB.exists() else "libstorm.so")

MPQ_FILE_REPLACEEXISTING = 0x80000000
MPQ_FILE_COMPRESS = 0x00000200
MPQ_COMPRESSION_ZLIB = 0x02

STORM.SFileOpenArchive.argtypes = [ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileRemoveFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint]
STORM.SFileAddFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                                 ctypes.c_uint, ctypes.c_uint, ctypes.c_uint]
STORM.SFileCompactArchive.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
STORM.SFileCloseArchive.argtypes = [ctypes.c_void_p]

# 10 亿。int32 上限约 21.47 亿，留足余量；就算叠满采集加成，实际对局时长也挖不动分毫。
BIG = 1_000_000_000

# 全部 CBehaviorResource id（CascLib 从 liberty/swarm/void/voidmulti/balancemulti 合并
# 后去重挖出来的，就这些，没有遗漏）。
MINERAL_IDS = [
    "MineralFieldMinerals", "MineralFieldMinerals750", "MineralFieldMinerals450",
    "MineralFieldMineralsOpaque", "MineralFieldMineralsOpaque900",
    "MineralFieldMineralsNoRemove",
    "HighYieldMineralFieldMinerals", "HighYieldMineralFieldMinerals750",
]
GAS_IDS = [
    # "生"状态——真正决定气矿起始总量的，引擎认这个（voidmulti 自己改过它的 Contents）
    "RawVespeneGeyserGas", "RawRichVespeneGeyserGas", "RawTerrazineGeyserGas",
    # "可采"状态（精炼厂建好后）——当双保险一起覆盖
    "HarvestableVespeneGeyserGas", "HarvestableVespeneGeyserGasProtoss", "HarvestableVespeneGeyserGasZerg",
    "HarvestableRichVespeneGeyserGas", "HarvestableRichVespeneGeyserGasProtoss", "HarvestableRichVespeneGeyserGasZerg",
    "HarvestableTerrazineGeyserGas", "HarvestableTerrazineGeyserGasProtoss", "HarvestableTerrazineGeyserGasZerg",
]


def write_mpq_file(h_mpq, name: str, data: bytes) -> None:
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin") as tf:
        tf.write(data)
        tmp = tf.name
    STORM.SFileRemoveFile(h_mpq, name.encode(), 0)
    ok = STORM.SFileAddFileEx(h_mpq, tmp.encode(), name.encode(),
                              MPQ_FILE_REPLACEEXISTING | MPQ_FILE_COMPRESS,
                              MPQ_COMPRESSION_ZLIB, MPQ_COMPRESSION_ZLIB)
    Path(tmp).unlink(missing_ok=True)
    if not ok:
        raise RuntimeError(f"写 {name} 失败")


def build_behavior_xml() -> str:
    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]
    for rid in MINERAL_IDS + GAS_IDS:
        lines.append(
            f'    <CBehaviorResource id="{rid}">'
            f'<Capacity value="{BIG}"/><Contents value="{BIG}"/>'
            f'</CBehaviorResource>'
        )
    lines.append("</Catalog>")
    return "\n".join(lines) + "\n"


def main() -> None:
    if len(sys.argv) != 4:
        sys.exit(f"用法: {sys.argv[0]} <mod文件名(不带.SC2Mod)> <中文显示名> <中文描述>")
    out_name = sys.argv[1]
    display_name = sys.argv[2]
    desc = sys.argv[3]

    dst = HERE / "mods" / f"{out_name}.SC2Mod"
    if not TEMPLATE.exists():
        sys.exit(f"模板不存在: {TEMPLATE}")
    shutil.copyfile(TEMPLATE, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst}")

    # 文件名必须留着 BehaviorData.xml（改名字游戏 MPQ 加载器读不到，建局秒退 "Not in a game"）。
    write_mpq_file(h_mpq, "Base.SC2Data\\GameData\\BehaviorData.xml", build_behavior_xml().encode("utf-8"))

    BOM = b"\xef\xbb\xbf"
    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   BOM + b"DocInfo/Name=Infinite Resources\n"
                   b"DocInfo/Desc=Every mineral field / geyser holds 1e9 - never runs out.\n")
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   BOM + f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    print(f"OK: {dst}")
    print(f"  {len(MINERAL_IDS)} 个矿脉 id + {len(GAS_IDS)} 个气矿 id，Contents/Capacity -> {BIG:,}")
    print()
    print("把下面这条加进 bake.py 的 MOD_FILES 和 MOD_INFO：")
    print(f'    "{out_name}": "{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
