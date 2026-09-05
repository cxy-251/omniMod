#!/usr/bin/env python3
"""
生成"运营加速"mod —— 建造 + 生产（训练单位）+ 研究 + 变形（虫族/人族/神族的关键
建筑变形与单位变形）四类耗时统一按倍数缩短。取代之前范围更窄的 fastBuild。

数据来源：CascLib 从游戏本体 CASC 包挖出来的 4 层基础数据按依赖顺序合并算出的
"当前真实生效值"（liberty.sc2mod WoL 基础 -> swarm.sc2mod HotS 增量 -> void.sc2mod
LotV 增量 -> voidmulti.sc2mod 联机平衡补丁增量——这条链跟我们烤图时写进
DocumentHeader 依赖表的 "Void (Mod)+VoidMulti (Mod)" 是同一条，只看第一层 liberty
会漏掉纯 LotV 才有的东西，比如异化石虫/潜地刺蛇变形在 liberty 里压根不存在）。
用 extract_gamedata.py 的合并逻辑，见那边注释。

建造/生产/研究都是同一种简单模式（<InfoArray index=".." Time="X"/>，改一个数字），
全量收进来了。变形（CAbilMorph）用的是更复杂的嵌套结构（同一个变形要同步改
Abils/Actor/Stats 几个 SectionArray 下的 DurationArray，数值还必须保持一致，否则
动画和实际完成时间对不上），只挑了"经济/科技类"的变形——虫后放包、Lair/Hive/
巨型脊柱虫/潜伏者穴/异化石虫/守护者/运输型跳虫、人族轨道司令部/行星要塞、
神族折跃门——不碰潜地/攻城模式/维京形态切换/折光棱镜这类"战斗中随手用"的
变形，那些不属于"运营"范畴，加速了反而是变相改战斗节奏。

用法：
  uv run python make_macro_speed_mod.py 2 macroSpeed "2 倍运营速度" "建造/生产/研究/变形耗时全部减半（只加速经济与科技类，不碰潜地/攻城等战斗变形）"
"""
from __future__ import annotations

import ctypes
import shutil
import sys
import tempfile
from pathlib import Path

import extract_gamedata as eg

HERE = Path(__file__).resolve().parent
TEMPLATE = HERE / "mods" / "5xHarvest.SC2Mod"
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

# 经济/科技类变形——不含潜地/攻城/维京形态/折光棱镜/伪装这类战斗中随手切的战术变形。
MORPH_WHITELIST = {
    "UpgradeToLair", "UpgradeToHive", "UpgradeToGreaterSpire", "UpgradeToLurkerDenMP",
    "MorphToBroodLord", "MorphToOverseer", "MorphToRavager", "MorphToLurker",
    "MorphToTransportOverlord", "MorphToMothership",
    "UpgradeToOrbital", "UpgradeToPlanetaryFortress",
    "UpgradeToWarpGate", "MorphBackToGateway",
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


def scaled(value: str, multiplier: float) -> str:
    v = max(0.05, float(value) / multiplier)
    # 保留原始精度风格：整数就出整数，小数就留 4 位
    return str(round(v)) if v == round(v) else f"{v:.4f}".rstrip("0").rstrip(".")


def build_abildata_xml(multiplier: float) -> str:
    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]

    # ---- 建造 + 生产 + 研究：都是同一种简单 index+Time 模式 ----
    for tag in ("CAbilBuild", "CAbilTrain", "CAbilResearch"):
        merged = eg.merge_simple_time(tag)
        by_ability: dict[str, list[tuple[str, str]]] = {}
        for (aid, idx), v in merged.items():
            by_ability.setdefault(aid, []).append((idx, v["time"]))
        for aid, entries in by_ability.items():
            lines.append(f'    <{tag} id="{aid}">')
            for idx, t in entries:
                lines.append(f'        <InfoArray index="{idx}" Time="{scaled(t, multiplier)}"/>')
            lines.append(f'    </{tag}>')

    # ---- 变形：白名单里的才改，嵌套的 SectionArray/DurationArray 全部字段同步缩放 ----
    morphs = eg.merge_morph_sections()
    by_ability_m: dict[str, list[tuple[str, dict]]] = {}
    for (aid, unit), sections in morphs.items():
        if aid in MORPH_WHITELIST:
            by_ability_m.setdefault(aid, []).append((unit, sections))
    for aid, entries in by_ability_m.items():
        lines.append('    <CAbilMorph id="' + aid + '">')
        for unit, sections in entries:
            lines.append(f'        <InfoArray Unit="{unit}">')
            for sidx, durs in sections.items():
                if not durs:
                    continue
                lines.append(f'            <SectionArray index="{sidx}">')
                for didx, val in durs.items():
                    lines.append(f'                <DurationArray index="{didx}" value="{scaled(val, multiplier)}"/>')
                lines.append('            </SectionArray>')
            lines.append('        </InfoArray>')
        lines.append('    </CAbilMorph>')

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

    STORM.SFileRemoveFile(h_mpq, b"Base.SC2Data\\GameData\\BehaviorData.xml", 0)
    xml = build_abildata_xml(multiplier)
    write_mpq_file(h_mpq, "Base.SC2Data\\GameData\\AbilData.xml", xml.encode("utf-8"))

    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name=Macro Speed ({multiplier}x)\n"
                   f"DocInfo/Desc=Build/train/research/morph time divided by {multiplier}!\n".encode())
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    print(f"OK: {dst}  ({len(xml.splitlines())} 行 XML)")
    print()
    print("把下面两条分别加进 bake.py 的 MOD_DEPS 和 MOD_INFO：")
    print(f'    "{out_name}": "bnet:{out_name}/0.0/999,file:Mods/{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
