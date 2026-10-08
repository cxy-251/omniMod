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
    "MorphToTransportOverlord", "MorphToMothership", "MorphToBaneling",
    "UpgradeToOrbital", "UpgradeToPlanetaryFortress",
    "UpgradeToWarpGate", "MorphBackToGateway",
}

# merge_simple_time / merge_morph_sections 覆盖不到的几类"出兵"能力 —— 之前 2 倍时
# 差别不明显没发现，1000 倍（秒建造）时这些还按原速 = 用户看到的"部分出兵要等"。
# 数据是 CascLib 从 liberty+void+voidmulti 合并层挖的真实值，index 已核对。

# 折跃门 warp 兵：真正的等待是嵌套 <Charge TimeUse>（折跃门冷却），不是 <InfoArray Time>
# （那只是 warp 动画）。两个都得缩。index -> (InfoArray Time, Charge TimeStart|None, Charge TimeUse)
WARP_TRAIN: dict[str, tuple[float, float | None, float]] = {
    "Train1": (5.6, 32.8, 30.8),   # Zealot
    "Train2": (5.6, None, 30.8),   # Stalker
    "Train4": (5.6, None, 49.0),   # HighTemplar
    "Train5": (5.6, None, 49.0),   # DarkTemplar
    "Train6": (5.6, None, 30.8),   # Sentry
    "Train7": (5.6, None, 30.8),   # Adept
}

# 弹匣单位补充耗时：航母拦截机 / 巢虫领主子虫 / 核弹。ability id -> {InfoArray index: Time}
ARM_MAGAZINE: dict[str, dict[str, float]] = {
    "CarrierHangar":   {"Ammo1": 12.0},   # Interceptor（void 层把 8 改成了 12）
    "BroodLordHangar": {"Ammo1": 2.5},    # BroodlingEscort
    "ArmSiloWithNuke": {"Ammo1": 60.0},   # Nuke
}

# 白球合体（executor merge -> Archon）：单个 <Info>，没有 index。ability id -> Time
MERGE_INFO: dict[str, float] = {
    "ArchonWarp": 16.6667,
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
    # 必须写 <InfoArray index="N">：不带 index 的 InfoArray 会被引擎当成"追加一个新阶段"，
    # 原来那段完整耗时照走、再加上我们这段——变形反而比原版更慢（以前就是这么错的）。
    morphs = eg.merge_morph_sections()
    by_ability_m: dict[str, list[tuple[int, str | None, dict]]] = {}
    for (aid, pos), entry in morphs.items():
        if aid in MORPH_WHITELIST:
            by_ability_m.setdefault(aid, []).append((pos, entry["unit"], entry["sections"]))
    for aid, entries in by_ability_m.items():
        lines.append('    <CAbilMorph id="' + aid + '">')
        for pos, unit, sections in sorted(entries):
            if sections:
                lines.append(f'        <InfoArray index="{pos}">')
                for sidx, durs in sections.items():
                    if not durs:
                        continue
                    lines.append(f'            <SectionArray index="{sidx}">')
                    for didx, val in durs.items():
                        lines.append(f'                <DurationArray index="{didx}" value="{scaled(val, multiplier)}"/>')
                    lines.append('            </SectionArray>')
                lines.append('        </InfoArray>')
            else:
                lines.append(f'        <InfoArray index="{pos}"/>')
        lines.append('    </CAbilMorph>')

    # ---- 折跃门 warp 出兵：InfoArray Time + 嵌套 <Charge>（折跃门冷却）一起缩 ----
    lines.append('    <CAbilWarpTrain id="WarpGateTrain">')
    for idx, (t, cs, cu) in WARP_TRAIN.items():
        lines.append(f'        <InfoArray index="{idx}" Time="{scaled(str(t), multiplier)}">')
        if cs is not None:
            lines.append(f'            <Charge TimeStart="{scaled(str(cs), multiplier)}" TimeUse="{scaled(str(cu), multiplier)}"/>')
        else:
            lines.append(f'            <Charge TimeUse="{scaled(str(cu), multiplier)}"/>')
        lines.append('        </InfoArray>')
    lines.append('    </CAbilWarpTrain>')

    # ---- 弹匣单位（拦截机 / 子虫 / 核弹）补充耗时 ----
    for aid, entries in ARM_MAGAZINE.items():
        lines.append(f'    <CAbilArmMagazine id="{aid}">')
        for idx, t in entries.items():
            lines.append(f'        <InfoArray index="{idx}" Time="{scaled(str(t), multiplier)}"/>')
        lines.append('    </CAbilArmMagazine>')

    # ---- 白球合体（executor merge -> Archon）----
    for aid, t in MERGE_INFO.items():
        lines.append(f'    <CAbilMerge id="{aid}">')
        lines.append(f'        <Info Time="{scaled(str(t), multiplier)}"/>')
        lines.append('    </CAbilMerge>')

    lines.append("</Catalog>")
    return "\n".join(lines) + "\n"


def build_behaviordata_xml(multiplier: float) -> str:
    lines = ['<?xml version="1.0" encoding="us-ascii"?>', "<Catalog>"]
    # 虫族基地自然产卵间隔（voidmulti 默认 13.86 秒）
    lines.append('    <CBehaviorSpawn id="SpawnLarva">')
    lines.append(f'        <InfoArray index="0" Delay="{scaled("13.86", multiplier)}"/>')
    lines.append('    </CBehaviorSpawn>')
    # 虫后注卵倒计时（原版默认 40 秒）
    lines.append('    <CBehaviorBuff id="QueenSpawnLarvaTimer">')
    lines.append(f'        <Duration value="{scaled("40", multiplier)}"/>')
    lines.append('    </CBehaviorBuff>')
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

    xml = build_abildata_xml(multiplier)
    behavior_xml = build_behaviordata_xml(multiplier)

    # 主用途：bake.py 把这份纯 XML 按 id 焊进地图自己的 AbilData.xml（AIE 这类自带整套
    # 数据的地图，扩展 mod 会被压过去 / 同文件同 id "先出现的赢"，只有焊进地图目录、
    # 换掉地图原来的同 id 条目才生效）。bake.py 只用 .abil.xml/.behavior.xml 焊接；
    # .SC2Mod 只留给编辑器里手动挂（别再挂成地图依赖，毒爆虫变异会坏，见 bake.py 注释）。
    abil_xml_path = HERE / "mods" / f"{out_name}.abil.xml"
    abil_xml_path.write_text(xml, encoding="utf-8")

    behavior_xml_path = HERE / "mods" / f"{out_name}.behavior.xml"
    behavior_xml_path.write_text(behavior_xml, encoding="utf-8")

    # 关键坑：文件名必须留着 BehaviorData.xml 不能改叫 AbilData.xml——即使里面装的是
    # CAbilBuild/CAbilTrain 数据。踩过：改名字（先 remove 旧名再 add 新名）会让 MPQ
    # 内部的东西对不上（大概率是 (listfile) 没跟着更新，游戏自己的 MPQ 加载器比
    # StormLib/mpyq 挑剔，读不到重命名后的文件），建出来的 mod 表面上没报错、
    # archive 本身也能正常打开，但游戏里一建局就秒退，报 "Not in a game"。
    # 内容是 CAbilBuild 还是 CBehaviorResource 都无所谓，游戏是按 <Catalog> 里每个
    # 元素自己的标签类型识别的，不看文件名——只要名字维持 BehaviorData.xml 就没事。
    # .SC2Mod 里技能和行为修改合在这一个文件里：
    mod_xml_lines = xml.rstrip().splitlines()[:-1]
    mod_behavior_lines = behavior_xml.strip().splitlines()[2:]
    combined_mod_xml = "\n".join(mod_xml_lines + mod_behavior_lines) + "\n"
    write_mpq_file(h_mpq, "Base.SC2Data\\GameData\\BehaviorData.xml", combined_mod_xml.encode("utf-8"))

    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name=Macro Speed ({multiplier}x)\n"
                   f"DocInfo/Desc=Build/train/research/morph time divided by {multiplier}!\n".encode())
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    print(f"OK: {dst}  ({len(combined_mod_xml.splitlines())} 行 XML)")
    print(f"OK: {abil_xml_path}  （bake 焊进地图 AbilData.xml 用的纯片段）")
    print(f"OK: {behavior_xml_path}  （bake 焊进地图 BehaviorData.xml 用的纯片段）")


if __name__ == "__main__":
    main()
