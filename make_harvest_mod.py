#!/usr/bin/env python3
"""
生成一个"N 倍采集"mod —— 矿用直接 ×N，气按"跟矿同时挖空"反推单趟采集量。

## 挖出来的两个真问题（CascLib 从游戏本体验证过，不是猜的）

1. **气矿的 id 从一开始就是错的**：旧版 5xHarvest 用 "VespeneGeyserGas" /
   "VespeneGeyserGasProtoss" / "VespeneGeyserGasZerg" / "RichVespeneGeyserGas..."，
   这些 id 在游戏真实数据（mods/liberty.sc2mod 一路到 voidmulti.sc2mod）里根本不存在，
   全是空改——从没生效过！真实 id 多了个 "Harvestable" 前缀：
   HarvestableVespeneGeyserGas(Terran) / HarvestableVespeneGeyserGasProtoss /
   HarvestableVespeneGeyserGasZerg，rich 版同理加 "Rich"。这就是为什么用户实测
   "3倍/5倍采集"之后晶体矿明显比气矿先挖空——矿真的被乘了，气从来没被乘过。
   （"RawRichMineralFieldMinerals"这个矿的 id 也没找到真实对应，目前不确定富矿这条
   有没有生效，先保留原样，没进一步验证。）

2. **单纯把气矿也乘 N 倍，挖空时间还是对不上**：矿和气矿的总量(Contents)、单趟
   耗时(HarvestTime)、同时能站几个矿工(IdealHarvesterCount)三个参数都不一样
   （矿 1800/2.786s/2人，气 2500/1.981s/3人——数据来自 CascLib 挖出来的
   mods/liberty.sc2mod/base.sc2data/gamedata/behaviordata.xml，晚期层没再改过
   这几个字段），乘同一个倍数不代表挖空时间也一样。这里按"矿 N 倍之后挖空要多久，
   气矿单趟采集量往回反推到挖空时间跟矿对齐"来算，而不是简单地气矿也乘 N。

## 算法

  矿新单趟量 = 矿基础单趟量(5) × N
  气新单趟量 = 气矿总量 × 气单趟耗时 × 矿新单趟量 × 矿同时矿工数
               ÷ (矿总量 × 矿单趟耗时 × 气同时矿工数)

  实测（3x）：矿 5->15 挖空约 167s；气反推出来 4/6(普通/超级气) -> 10，挖空约 165s，对齐了。
  （超级气矿总量/耗时跟普通气矿一样，只是原版单趟采集量更高，反推出来的目标挖空
  时间是同一个，所以普通/超级气矿新单趟量算出来一样——这是有意的，都对齐到"矿挖空
  要多久"这一个目标时间上，不再保留"超级气矿比普通气矿快"这层原版区别。）

用法：
  uv run python make_harvest_mod.py 3 3xHarvest "3 倍采集" "矿 ×3，气矿按跟矿同时挖空换算（不是单纯也乘 3）"
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

# 真实数据（CascLib 从 mods/liberty.sc2mod/base.sc2data/gamedata/behaviordata.xml 挖出来，
# 晚期层 swarm/void/voidmulti/balancemulti 都没再覆盖这几个字段，是当前生效值）：
CONTENTS_MIN, HARVESTTIME_MIN, IDEAL_MIN = 1800, 2.786, 2
CONTENTS_GAS, HARVESTTIME_GAS, IDEAL_GAS = 2500, 1.981, 3
BASE_MIN = 5          # 普通矿单趟基础采集量（超级矿基础是 7，矿这边就还是直接 ×N，不反推）
BASE_MIN_RICH = 7

# 矿：直接 ×N，用真实存在的 id（这几个是原版就有的，5xHarvest 一直生效正常）。
MINERAL_IDS = [
    "MineralFieldMinerals", "MineralFieldMinerals750",
    "PurifierMineralFieldMinerals", "PurifierMineralFieldMinerals750",
    "BattleStationMineralFieldMinerals", "BattleStationMineralFieldMinerals750",
]
MINERAL_IDS_RICH = [
    "RawRichMineralFieldMinerals",  # 没验证到真实对应 id，保留原样但不保证生效
    "PurifierRichMineralFieldMinerals", "PurifierRichMineralFieldMinerals750",
]

# 气：真实 id（带 Harvestable 前缀），单趟量用反推值，不是简单 ×N。
GAS_IDS_NORMAL = [
    "HarvestableVespeneGeyserGas",           # Terran
    "HarvestableVespeneGeyserGasProtoss",
    "HarvestableVespeneGeyserGasZerg",
]
GAS_IDS_RICH = [
    "HarvestableRichVespeneGeyserGas",
    "HarvestableRichVespeneGeyserGasProtoss",
    "HarvestableRichVespeneGeyserGasZerg",
]

# 气矿 id 真实原版每一个字段（CascLib 挖出来的），EditorCategories 的 Race 值就是
# 原版数据本身写的（三个 Rich 变体在原版里全标了 Race:Terran，连 Zerg/Protoss 那两个也是——
# 这是暴雪自己数据里的笔误，不是我们抄错，照抄不改）。踩过的坑：只写 HarvestAmount 一个
# 字段的"最小覆盖"对矿有效、对气矿没生效，怀疑是气矿这几个字段（尤其 RequiredAlliance /
# Flags HideHarvesters）缺了会导致覆盖不完整被引擎忽略，所以气矿这边把原版全部字段都
# 照抄一遍，只改 HarvestAmount 一个数值。
GAS_EXTRA_FIELDS = {
    "HarvestableVespeneGeyserGas": (
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<Capacity value="32000"/><HarvestTime value="1.981"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<InfoFlags index="Hidden" value="1"/><Flags index="HideHarvesters" value="1"/>'
        '<RequiredAlliance value="Control"/>'
        '<EditorCategories value="Race:Terran,AbilityorEffectType:Units"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
    "HarvestableVespeneGeyserGasProtoss": (
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<Capacity value="2500"/><HarvestTime value="1.981"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<InfoFlags index="Hidden" value="1"/><Flags index="HideHarvesters" value="1"/>'
        '<RequiredAlliance value="Control"/>'
        '<EditorCategories value="Race:Protoss,AbilityorEffectType:Units"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
    "HarvestableVespeneGeyserGasZerg": (
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<Capacity value="2500"/><HarvestTime value="1.981"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<InfoFlags index="Hidden" value="1"/><Flags index="HideHarvesters" value="1"/>'
        '<RequiredAlliance value="Control"/>'
        '<EditorCategories value="Race:Zerg,AbilityorEffectType:Units"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
    "HarvestableRichVespeneGeyserGas": (
        '<InfoFlags index="Hidden" value="1"/>'
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<EditorCategories value="Race:Terran,AbilityorEffectType:Units"/>'
        '<Capacity value="32000"/><HarvestTime value="1.981"/>'
        '<Flags index="HideHarvesters" value="1"/><RequiredAlliance value="Control"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
    "HarvestableRichVespeneGeyserGasZerg": (
        '<InfoFlags index="Hidden" value="1"/>'
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<EditorCategories value="Race:Terran,AbilityorEffectType:Units"/>'  # 原版数据自己就是这么写的
        '<Capacity value="32000"/><HarvestTime value="1.981"/>'
        '<Flags index="HideHarvesters" value="1"/><RequiredAlliance value="Control"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
    "HarvestableRichVespeneGeyserGasProtoss": (
        '<InfoFlags index="Hidden" value="1"/>'
        '<InfoIcon value="Assets\\Textures\\icon-gas.dds"/>'
        '<EditorCategories value="Race:Terran,AbilityorEffectType:Units"/>'  # 原版数据自己就是这么写的
        '<Capacity value="32000"/><HarvestTime value="1.981"/>'
        '<Flags index="HideHarvesters" value="1"/><RequiredAlliance value="Control"/>'
        '<ExhaustedAlert value="ResourceExhausted_Vespene"/>'
        '<IdealHarvesterCount value="3"/>'
    ),
}


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


def gas_amount_for(mineral_new_amount: float) -> int:
    """按"矿新单趟量挖空要多久，气矿单趟量往回反推到同一个挖空时间"算。"""
    k = (CONTENTS_GAS * HARVESTTIME_GAS * IDEAL_MIN) / (CONTENTS_MIN * HARVESTTIME_MIN * IDEAL_GAS)
    return max(1, round(k * mineral_new_amount))


def build_behavior_xml(multiplier: float) -> str:
    min_new = round(BASE_MIN * multiplier)
    min_new_rich = round(BASE_MIN_RICH * multiplier)
    gas_new = gas_amount_for(min_new)   # 普通/超级气矿反推目标是同一个挖空时间，算出来共用一个值

    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]
    for mid in MINERAL_IDS:
        lines.append(f'    <CBehaviorResource id="{mid}"><HarvestAmount value="{min_new}"/></CBehaviorResource>')
    for mid in MINERAL_IDS_RICH:
        lines.append(f'    <CBehaviorResource id="{mid}"><HarvestAmount value="{min_new_rich}"/></CBehaviorResource>')
    for gid in GAS_IDS_NORMAL + GAS_IDS_RICH:
        extra = GAS_EXTRA_FIELDS[gid]
        lines.append(f'    <CBehaviorResource id="{gid}">{extra}<HarvestAmount value="{gas_new}"/></CBehaviorResource>')
    lines.append("</Catalog>")
    return "\n".join(lines) + "\n"


def main() -> None:
    if len(sys.argv) != 5:
        sys.exit(f"用法: {sys.argv[0]} <倍数> <mod文件名(不带.SC2Mod)> <中文显示名> <中文描述>")
    multiplier = float(sys.argv[1])
    out_name = sys.argv[2]
    display_name = sys.argv[3]
    desc = sys.argv[4]

    dst = HERE / "mods" / f"{out_name}.SC2Mod"
    if not TEMPLATE.exists():
        sys.exit(f"模板不存在: {TEMPLATE}")
    shutil.copyfile(TEMPLATE, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst}")

    # 文件名必须留着 BehaviorData.xml——踩过坑：改名字会让游戏自己的 MPQ 加载器读不到，
    # 建局直接秒退报 "Not in a game"，具体见 make_macro_speed_mod.py 里的记录。
    behavior_path = "Base.SC2Data\\GameData\\BehaviorData.xml"
    write_mpq_file(h_mpq, behavior_path, build_behavior_xml(multiplier).encode("utf-8"))

    # 真实游戏本体的 GameStrings.txt 都是带 UTF-8 BOM（EF BB BF）开头的——用 CascLib 挖
    # mods/liberty.sc2mod 自己的 zhCN GameStrings.txt 对比确认过。我们生成的文件之前没加
    # BOM，编辑器日志里报过 "Unable to load 'GameText <zhCN>'"，虽然不确定这跟气矿数据
    # 不生效是不是同一个根因，但这本身就是个真实的、该修的编码问题，顺手一起修了。
    BOM = b"\xef\xbb\xbf"
    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   BOM + f"DocInfo/Name={multiplier}x Harvest Rate\n"
                   f"DocInfo/Desc=Minerals x{multiplier}; gas rate recalculated to deplete "
                   f"at the same time as minerals (not simply x{multiplier})!\n".encode())
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   BOM + f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    min_new = round(BASE_MIN * multiplier)
    gas_new = gas_amount_for(min_new)
    print(f"OK: {dst}")
    print(f"  矿单趟量 {BASE_MIN} -> {min_new}；气矿单趟量反推 -> {gas_new}（原来是没生效的错 id，这次修正）")
    print()
    print("把下面两条分别加进 bake.py 的 MOD_DEPS 和 MOD_INFO：")
    print(f'    "{out_name}": "bnet:{out_name}/0.0/999,file:Mods/{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
