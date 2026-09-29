#!/usr/bin/env python3
"""
把 4 层游戏本体基础数据（liberty.sc2mod WoL 基础 -> swarm.sc2mod HotS 增量
-> void.sc2mod LotV 增量 -> voidmulti.sc2mod 联机平衡补丁增量，跟我们烤图时
写进 DocumentHeader 依赖表的 "Void (Mod)+VoidMulti (Mod)" 是同一条链）按顺序
合并，算出每个技能字段"当前真实生效值"——不能只看 liberty.sc2mod 一层，
有些技能（比如虚空之遗才有的 Ravager/Lurker 变形）liberty 里压根不存在，
得看后面的层；也有可能同一个字段被后面的平衡补丁层覆盖过。

用法（先用 casc_probe.py 把 4 个 AbilData.xml dump 到本地，这里直接读那些文件）：
  uv run python extract_gamedata.py
"""
from __future__ import annotations

import xml.etree.ElementTree as ET
from pathlib import Path

LAYERS = [
    "/tmp/AbilData_liberty.xml",
    "/tmp/AbilData_swarm.xml",
    "/tmp/AbilData_void.xml",
    "/tmp/AbilData_voidmulti.xml",
]


def merge_simple_time(tag: str) -> dict[tuple[str, str], dict]:
    """CAbilBuild/CAbilTrain/CAbilResearch: (ability_id, index) -> {"time": str, "ref": str|None}
    ref 是 Unit 或 Upgrade 属性（用来认出这个 index 是给哪个建筑/单位/科技用的）。"""
    result: dict[tuple[str, str], dict] = {}
    for f in LAYERS:
        if not Path(f).exists():
            continue
        root = ET.parse(f).getroot()
        for el in root.findall(tag):
            aid = el.get("id")
            for info in el.findall("InfoArray"):
                idx = info.get("index")
                t = info.get("Time")
                if idx is None or t is None:
                    continue
                ref = info.get("Unit") or info.get("Upgrade")
                key = (aid, idx)
                entry = result.setdefault(key, {})
                entry["time"] = t
                if ref:
                    entry["ref"] = ref
    return result


def merge_morph_sections(tag: str = "CAbilMorph") -> dict[tuple[str, int], dict]:
    """(ability_id, 数组下标) -> {"unit": str|None, "sections": {section_index: {duration_index: value}}}

    变形的 InfoArray 是个数组：大多数变形有两项——第 0 项是茧/卵阶段，第 1 项才是最终单位、
    带真正的耗时（DurationArray）。基础层（liberty/void）按出现顺序写、不带 index；后面的平衡
    补丁层用 <InfoArray index="1"> 按下标覆盖，而且常常不再写 Unit。所以必须按**数组下标**合并，
    不能按 Unit 名——以前按 Unit 合并，会把 voidmulti 的 index-only 覆盖整个丢掉。
    逐层按 (section, duration) 最细粒度覆盖，没提到的字段维持前面层的值（跟引擎合并规则一致）。"""
    result: dict[tuple[str, int], dict] = {}
    for f in LAYERS:
        if not Path(f).exists():
            continue
        root = ET.parse(f).getroot()
        for el in root.findall(tag):
            aid = el.get("id")
            pos_counter = 0
            for info in el.findall("InfoArray"):
                idx = info.get("index")
                pos = int(idx) if idx is not None and idx.isdigit() else pos_counter
                pos_counter = pos + 1
                entry = result.setdefault((aid, pos), {"unit": None, "sections": {}})
                if info.get("Unit"):
                    entry["unit"] = info.get("Unit")
                for sec in info.findall("SectionArray"):
                    sec_dict = entry["sections"].setdefault(sec.get("index"), {})
                    for dur in sec.findall("DurationArray"):
                        sec_dict[dur.get("index")] = dur.get("value")
    return result


if __name__ == "__main__":
    for tag in ("CAbilBuild", "CAbilTrain", "CAbilResearch"):
        merged = merge_simple_time(tag)
        print(f"=== {tag}: {len(merged)} 条 ===")
        by_ability: dict[str, list] = {}
        for (aid, idx), v in merged.items():
            by_ability.setdefault(aid, []).append((idx, v.get("ref"), v.get("time")))
        for aid, entries in by_ability.items():
            print(f"  {aid}: {len(entries)} 条")
    morphs = merge_morph_sections()
    print(f"=== CAbilMorph (SectionArray 结构): {len(morphs)} 条 ===")
    for (aid, pos), entry in morphs.items():
        print(f"  {aid}[{pos}] -> {entry['unit']}: {entry['sections']}")
