#!/usr/bin/env python3
"""
生成"快速建造"mod —— 把三个种族"造建筑"的技能（CAbilBuild: TerranBuild / ProtossBuild /
ZergBuild）里每栋建筑的建造时间按倍数缩短。

数据不是猜的：从游戏本体自己的 CASC 包里用 CascLib 挖出来的真实 mods/liberty.sc2mod/
base.sc2data/gamedata/abildata.xml（战网/单机版沿用的最底层 WoL 基础数据），确认了：
  <CAbilBuild id="TerranBuild">
      <InfoArray index="Build2" Unit="SupplyDepot" Time="30">...
  跟采集 mod 用的 <HarvestAmount value="X"/> 是同一套"XML 里孤零零一个数值"的简单模式，
  只是嵌在按 index 编号的数组里——按 id 覆盖已有 entry、只写要改的字段，其它字段照抄不动，
  这跟 5x/3xHarvest 已验证过的合并方式（同 id 就是覆盖）是一回事。

Time 单位不是真实秒数：SupplyDepot Time=30、CommandCenter Time=100，换算下来正好是玩家
熟悉的 21 秒 / 71 秒 乘以 1.4（星际 2 天梯默认"较快"游戏速度的时间缩放系数）——所以这里
按原始 Time 值本身等比例缩小，缩小完之后实际游戏里感受到的秒数也是同样倍数。

只覆盖三个种族"主建造"技能（SCV/探机直接建、异虫工蜂变形出的那些一级建筑），暂不含
虫族的 Lair/Hive/Greater Spire 之类"升级变形"（那些时间字段结构更复杂，嵌套在
SectionArray/DurationArray 里，还没验证覆盖安不安全，先不碰）以及人族 OrbitalCommand /
PlanetaryFortress 变形。

用法：
  uv run python make_fastbuild_mod.py 2 fastBuild "2 倍建造速度" "建筑建造耗时减半（人族/神族/异虫三族主建筑，不含指挥中心/主基地的升级变形）"
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
STORM.SFileOpenFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileRemoveFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint]
STORM.SFileAddFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                                 ctypes.c_uint, ctypes.c_uint, ctypes.c_uint]
STORM.SFileCompactArchive.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
STORM.SFileCloseArchive.argtypes = [ctypes.c_void_p]

# id -> [(原始 index 号, 建筑 unit id, 原版 Time), ...]。
# index 号是从原始数据里原样抄的、不是从 1 顺序编的——原始列表里 index 本来就有跳号
# （Terran 14 后面直接跳 16，没有 15；Protoss/Zerg 也各缺一个不带 Unit 的空位）。
# 头一版脚本图省事按列表顺序自动编号，结果把 FusionCore/CyberneticsCore/SporeCrawler 这些
# 跳号之后的建筑全写错了 index（改的是空槽位，没改到真正对应的建筑）——数据来源：
# mods/liberty.sc2mod/base.sc2data/gamedata/abildata.xml（CascLib 挖出来的原始数据），
# 这次原样保留跳号，只覆盖真正标了 Unit 的那些槽位。
BUILD_ABILITIES: dict[str, list[tuple[int, str, int]]] = {
    "TerranBuild": [
        (1, "CommandCenter", 100), (2, "SupplyDepot", 30), (3, "Refinery", 30), (4, "Barracks", 60),
        (5, "EngineeringBay", 35), (6, "MissileTurret", 25), (7, "Bunker", 30), (8, "RefineryRich", 30),
        (9, "SensorTower", 25), (10, "GhostAcademy", 40), (11, "Factory", 60), (12, "Starport", 50),
        (13, "MercCompound", 50), (14, "Armory", 65), (16, "FusionCore", 80),
    ],
    "ProtossBuild": [
        (1, "Nexus", 100), (2, "Pylon", 25), (3, "Assimilator", 30), (4, "Gateway", 65), (5, "Forge", 35),
        (6, "FleetBeacon", 60), (7, "TwilightCouncil", 50), (8, "PhotonCannon", 40), (10, "Stargate", 60),
        (11, "TemplarArchive", 50), (12, "DarkShrine", 100), (13, "RoboticsBay", 65),
        (14, "RoboticsFacility", 65), (15, "CyberneticsCore", 50),
    ],
    "ZergBuild": [
        (1, "Hatchery", 100), (2, "CreepTumor", 15), (3, "Extractor", 30), (4, "SpawningPool", 65),
        (5, "EvolutionChamber", 35), (6, "HydraliskDen", 40), (7, "Spire", 100), (8, "UltraliskCavern", 65),
        (9, "InfestationPit", 50), (10, "NydusNetwork", 50), (11, "BanelingNest", 60), (14, "RoachWarren", 55),
        (15, "SpineCrawler", 50), (16, "SporeCrawler", 30),
    ],
}


def read_mpq_file(h_mpq, name: str) -> bytes:
    h_file = ctypes.c_void_p()
    if not STORM.SFileOpenFileEx(h_mpq, name.encode(), 0, ctypes.byref(h_file)):
        raise RuntimeError(f"打不开 {name}")
    STORM.SFileGetFileSize.restype = ctypes.c_uint
    size = STORM.SFileGetFileSize(h_file, None)
    buf = ctypes.create_string_buffer(size)
    read = ctypes.c_uint(0)
    STORM.SFileReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                    ctypes.POINTER(ctypes.c_uint), ctypes.c_void_p]
    STORM.SFileReadFile(h_file, buf, size, ctypes.byref(read), None)
    STORM.SFileCloseFile.argtypes = [ctypes.c_void_p]
    STORM.SFileCloseFile(h_file)
    return buf.raw[:read.value]


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


def build_abildata_xml(multiplier: float) -> str:
    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]
    for abil_id, entries in BUILD_ABILITIES.items():
        lines.append(f'    <CAbilBuild id="{abil_id}">')
        for idx, unit, base_time in entries:
            new_time = max(1, round(base_time / multiplier))
            lines.append(f'        <InfoArray index="Build{idx}" Unit="{unit}" Time="{new_time}"/>')
        lines.append("    </CAbilBuild>")
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
    shutil.copyfile(TEMPLATE, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst}")

    # 模板带的采集数据删掉，换成建造时间数据
    STORM.SFileRemoveFile(h_mpq, b"Base.SC2Data\\GameData\\BehaviorData.xml", 0)
    write_mpq_file(h_mpq, "Base.SC2Data\\GameData\\AbilData.xml",
                   build_abildata_xml(multiplier).encode("utf-8"))

    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name=Fast Build ({multiplier}x)\n"
                   f"DocInfo/Desc=Building construction time divided by {multiplier} for all races!\n".encode())
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    print(f"OK: {dst}")
    print()
    print("把下面两条分别加进 bake.py 的 MOD_DEPS 和 MOD_INFO：")
    print(f'    "{out_name}": "bnet:{out_name}/0.0/999,file:Mods/{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
