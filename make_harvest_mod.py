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
BASE_GAS = 4          # 普通气矿原版单趟采集量（工人 Harvest 能力的 Vespene 倍率就以这个为基准换算）

# 气矿走"另一条路"：不再改资源节点行为（CBehaviorResource.HarvestAmount）——实测这条对
# 晶体矿有效、对气矿从来不生效（id、依赖版本号、字段补全、BOM 全查过修过都没用，
# 见下面 GAS_EXTRA_FIELDS 注释）。改成 SC2Mapster 社区标准做法：直接改工人「采集能力」
# CAbilHarvest 上的 ResourceAmountMultiplier —— 这是个按资源类型索引的数组
# [Minerals|Vespene|Terrazine|Custom]，原版全是 1，MULEGather 就是靠把 Minerals 项设成 6
# 让机骡单趟挖 25 的。我们只动 Vespene 项，晶体矿那条继续用行为覆盖（它是好的，别碰）。
# CascLib 从 liberty/void/voidmulti 的 abildata.xml 确认：SCV/Probe/Drone 三个采集能力
# 原版都没写任何 ResourceAmount* 字段，是干净的，直接加就行。
WORKER_HARVEST_IDS = ["SCVHarvest", "ProbeHarvest", "DroneHarvest"]

# 矿：直接 ×N。之前这里有 6 个"听起来像"真实 id 的条目（PurifierMineralFieldMinerals、
# BattleStationMineralFieldMinerals 等），实测在游戏本体任何一层数据里都不存在——全是
# 从上一版 mod 照抄下来、从没验证过的假 id，纯粹占地方，不影响功能但也没用。
# 真相是：Lab/Purifier/BattleStation 这些"换皮"矿脉单位（CUnit）全都 parent="MineralFieldDefault"
# 直接继承，自己不声明 BehaviorArray，或者显式 Link 回 "MineralFieldMinerals"/"MineralFieldMinerals750"
# ——用 CascLib 逐个查过 CUnit 定义确认的。所以普通矿只需要这两个 id 就能覆盖所有换皮版本。
MINERAL_IDS = ["MineralFieldMinerals", "MineralFieldMinerals750"]

# 富矿同理：RichMineralField（BlackburnAIE 这张图实际放置的富矿类型）的父类
# RichMineralFieldDefault 明确 Link 到 "HighYieldMineralFieldMinerals"（750 版本同理）——
# 之前用的 "RawRichMineralFieldMinerals"/"PurifierRichMineralFieldMinerals" 这些也是假 id，
# 从没生效过，这是本轮排查气矿问题时顺手挖出来的另一个真 bug。
MINERAL_IDS_RICH = ["HighYieldMineralFieldMinerals", "HighYieldMineralFieldMinerals750"]

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


def gas_multiplier_for(mineral_new_amount: float) -> float:
    """气矿目标单趟量 ÷ 原版单趟量(4) = 挂在工人采集能力 Vespene 项上的倍率。"""
    return round(gas_amount_for(mineral_new_amount) / BASE_GAS, 3)


def build_behavior_xml(multiplier: float) -> str:
    min_new = round(BASE_MIN * multiplier)
    min_new_rich = round(BASE_MIN_RICH * multiplier)
    gas_mult = gas_multiplier_for(min_new)

    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]
    # 晶体矿：继续走资源节点行为覆盖（实测有效）。
    for mid in MINERAL_IDS:
        lines.append(f'    <CBehaviorResource id="{mid}"><HarvestAmount value="{min_new}"/></CBehaviorResource>')
    for mid in MINERAL_IDS_RICH:
        lines.append(f'    <CBehaviorResource id="{mid}"><HarvestAmount value="{min_new_rich}"/></CBehaviorResource>')
    # 气矿：改工人采集能力的 Vespene 倍率（另一条路，绕开对气矿不生效的行为覆盖）。
    # 同一个 BehaviorData.xml 里混写 <CAbilHarvest>——SC2 数据系统按元素标签名归类到对应
    # catalog，跟文件名无关（macroSpeed mod 已经这么干过，能进游戏）。超级气矿原版单趟是 6，
    # 乘同一个倍率会比普通气矿高一点，但总量(Contents)一样、目标是"大致对齐"，可接受。
    for aid in WORKER_HARVEST_IDS:
        lines.append(
            f'    <CAbilHarvest id="{aid}">'
            f'<ResourceAmountMultiplier index="Vespene" value="{gas_mult}"/>'
            f'</CAbilHarvest>'
        )
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
    gas_mult = gas_multiplier_for(min_new)
    print(f"OK: {dst}")
    print(f"  晶体矿单趟量 {BASE_MIN} -> {min_new}（资源节点行为覆盖，实测有效）")
    print(f"  气矿：工人采集能力 Vespene 倍率 x{gas_mult}（4 -> ~{gas_new}），绕开对气矿不生效的行为覆盖")
    print()
    print("把下面两条分别加进 bake.py 的 MOD_DEPS 和 MOD_INFO：")
    print(f'    "{out_name}": "bnet:{out_name}/0.0/999,file:Mods/{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
