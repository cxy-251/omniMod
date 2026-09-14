#!/usr/bin/env python3
"""
生成一个"N 倍采集"mod —— 晶体矿直接 ×N，气矿按"跟晶体矿同时挖空"反推倍率。

## 晶体矿和气矿走两条不同的路（都 CascLib 从游戏本体验证过，不是猜的）

- **晶体矿**：改资源节点行为 `CBehaviorResource.HarvestAmount`（`MineralFieldMinerals`
  / `...750` 两个真实 id 就覆盖了所有换皮矿脉——Lab/Purifier/BattleStation 这些
  CUnit 全 `parent="MineralFieldDefault"` 或显式 Link 回这两个）。富矿同理走
  `HighYieldMineralFieldMinerals` / `...750`。这条实测有效。

- **气矿**：同样的行为覆盖对气矿引擎就是不认（id、baked 地图里的依赖版本号、
  补全全部字段、GameStrings 的 UTF-8 BOM 全查过修过都没用；地图自带数据也确认
  没碰气矿）。改走 SC2Mapster 社区标准做法：改工人「采集能力」`CAbilHarvest`
  上的 `ResourceAmountMultiplier`——按资源类型索引的数组 [Minerals|Vespene|
  Terrazine|Custom]，原版全 1，机骡 `MULEGather` 就是把 Minerals 项设成 6 才
  单趟挖 25 的。我们只在 `SCVHarvest`/`ProbeHarvest`/`DroneHarvest` 上加 Vespene 项。

## 为什么气矿不是简单 ×N

晶体矿和气矿的总量(Contents)、单趟耗时(HarvestTime)、同时能站几个矿工
(IdealHarvesterCount) 都不一样（晶体矿 1800/2.786s/2人，气矿 2500/1.981s/3人），
乘同一个倍数挖空时间对不上。这里按"晶体矿 ×N 之后挖空要多久，气矿倍率往回反推到
挖空时间跟晶体矿对齐"来算：

  晶体矿新单趟量 = 5 × N
  气矿目标单趟量 = 气矿总量 × 气单趟耗时 × 晶体矿新单趟量 × 晶体矿矿工数
                   ÷ (晶体矿总量 × 晶体矿单趟耗时 × 气矿矿工数)
  气矿倍率 = 气矿目标单趟量 ÷ 4

  实测（3x）：晶体矿 5->15；气矿倍率 x2.5（4 -> ~10），挖空时间基本对齐。

用法：
  uv run python make_harvest_mod.py 3 3xHarvest "3倍采集" "晶体矿 ×3，气矿按跟晶体矿同时挖空换算（不是单纯也乘 3）"
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

# 晶体矿：改资源节点行为，直接 ×N。这两个真实 id 就覆盖所有换皮矿脉（CascLib 逐个
# 查过 CUnit：Lab/Purifier/BattleStation 全 parent="MineralFieldDefault" 或显式 Link
# 回这两个）。富矿走 RichMineralFieldDefault 明确 Link 的 HighYield* 两个。
MINERAL_IDS = ["MineralFieldMinerals", "MineralFieldMinerals750"]
MINERAL_IDS_RICH = ["HighYieldMineralFieldMinerals", "HighYieldMineralFieldMinerals750"]

# 气矿：改工人采集能力的 Vespene 倍率（行为覆盖对气矿引擎不认，见模块 docstring）。
# CascLib 确认这三个能力原版都没写任何 ResourceAmount* 字段，是干净的，直接加。
WORKER_HARVEST_IDS = ["SCVHarvest", "ProbeHarvest", "DroneHarvest"]


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
