#!/usr/bin/env python3
"""
把选中的 mod 烘焙进地图副本，产物放进 Maps/，文件名带 __<hash> 后缀。
缓存：同 图+mod 组合第二次直接返回。

两类 mod：
  - 依赖类（DEP_MODS）：往地图 DocumentHeader 依赖表加一条扩展 mod 依赖。
    3x/5x 采集、macroSpeed、instantBuild 走这条。
  - 目录类（CATALOG_MODS）：把一段 <CBehaviorResource> 目录片段焊进地图自己的
    Base.SC2Data\\GameData\\BehaviorData.xml。infiniteRes 走这条——扩展 mod 依赖
    改 Contents 到不了已摆放的矿点，实测无效；焊进地图才行（同"无限矿产"作弊图）。

MPQ 写靠 _bake_inner.py（宿主直接跑，用仓库自带 lib/libstorm.so）。
"""
from __future__ import annotations

import hashlib
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
REPO_MODS = HERE / "mods"                       # 仓库里的 mod 源头（生成器写这里）
SC2_ROOT = Path("/home/deck/Games/StarCraft II")
MAPS_DIR = SC2_ROOT / "Maps"
MODS_DIR = SC2_ROOT / "Mods"                    # 游戏装的 Mods 目录（bake 读这里）
BAKED_SUFFIX = "__"          # 烤出来的图名里含这个，picker 会过滤掉

# 依赖类：key -> Mods/ 下的 .SC2Mod。改 HarvestAmount（每趟实时读目录）够用，
# 且 AIE 地图不碰这个字段，所以走扩展 mod 依赖没问题。
DEP_MODS: dict[str, str] = {
    "3xHarvest": "3xHarvest.SC2Mod",
    "5xHarvest": "5xHarvest.SC2Mod",
}
# 兼容旧名
MOD_FILES = DEP_MODS

# 焊接类：key -> [(地图 GameData 里的目标文件, 仓库 mods/ 下的片段文件), ...]。
# 片段按 id 焊进地图自己的那个目录文件，地图原有同 id 条目整块换掉。
# instantBuild/macroSpeed 只走焊接，不要再把它们的 .SC2Mod 挂成地图依赖"兜底"：
# 那份 .SC2Mod 没有 DocumentHeader、不声明依赖，按地图依赖表顺序加载在 Void 之后、
# 对战模式由游戏自动追加的 VoidMulti 之前。MorphToBaneling 这类只在 VoidMulti 里才定义
# 的技能，会先被它"新建"成一个残缺条目——实测毒爆虫变异按钮直接失效。焊进地图自己的
# GameData 加载在 VoidMulti 之后，对所有地图都生效（地图原本没有这个文件也会新建）。
# 2026-10-08 在 16BitLE 上实测：只焊接时毒爆/破坏者变异秒建造 0.18 秒、两倍速正好减半。
CATALOG_MERGES: dict[str, list[tuple[str, str]]] = {
    "infiniteRes":  [("BehaviorData.xml", "infiniteRes.behavior.xml")],
    "instantBuild": [("AbilData.xml", "instantBuild.abil.xml"), ("BehaviorData.xml", "instantBuild.behavior.xml")],
    "macroSpeed":   [("AbilData.xml", "macroSpeed.abil.xml"),   ("BehaviorData.xml", "macroSpeed.behavior.xml")],
}

ALL_MOD_KEYS = set(DEP_MODS) | set(CATALOG_MERGES)


def _catalog_paths(key: str) -> list[Path]:
    return [HERE / "mods" / frag for _, frag in CATALOG_MERGES.get(key, [])]


def _mod_dep_string(key: str) -> str:
    """写进 DocumentHeader 的依赖串，形如 "bnet:X/0.0/<版本号>,file:Mods/X.SC2Mod"。

    版本号不能写死（以前是死的 "999"）——本地 Battle.net 有一层内容缓存
    （ProgramData/Blizzard Entertainment/Battle.net/Cache 下一堆按哈希命名的
    .s2ma），踩过坑：mod 文件内容改了、三份拷贝（sc2Mod 源头/游戏安装目录/Wine
    前缀 Documents）也都同步过了，但版本号没变，游戏那边可能还是把它当"以前解析过
    的那个版本"，读的是缓存结果而不是重新解析新内容——改了跟没改一样。这里让
    版本号跟着 mod 文件内容的哈希走，内容一变版本号必然跟着变，不给缓存偷懒的机会。"""
    fname = DEP_MODS[key]
    mf = MODS_DIR / fname
    content = mf.read_bytes() if mf.exists() else b""
    build = int(hashlib.md5(content).hexdigest()[:6], 16) % 900000 + 1
    return f"bnet:{key}/0.0/{build},file:Mods/{fname}"

# mod key -> 给 UI 用的展示信息。"group" 字段：同组互斥（前端渲染成单选，一次只能选一个），
# 没有 group 的照旧是独立勾选框。做新 mod 时 MOD_DEPS + MOD_INFO 两边都加一条。
MOD_INFO: dict[str, dict[str, str]] = {
    "3xHarvest": {
        "name": "3 倍采集",
        "desc": "矿 ×3；气矿按跟矿同时挖空换算，不是单纯也乘 3（双方阵营对称生效，只改采集量）",
        "group": "harvest",
    },
    "5xHarvest": {
        "name": "5 倍采集",
        "desc": "矿 ×5；气矿按跟矿同时挖空换算，不是单纯也乘 5（双方阵营对称生效，只改采集量）",
        "group": "harvest",
    },
    "macroSpeed": {
        "name": "2 倍运营速度",
        "desc": "建造/生产/研究/变形耗时全部减半（三族建筑、训练单位、科技研究、"
                "Lair/Hive/轨道司令部/行星要塞/折跃门等经济科技类变形；不碰潜地/攻城/维京"
                "形态切换这类战斗中随手用的战术变形）",
        "group": "macro",
    },
    "instantBuild": {
        "name": "秒建造",
        "desc": "建造/生产/研究/变形耗时全部压到 0.05 秒——覆盖范围跟「2 倍运营速度」完全一样，"
                "只是直接拉满。与「2 倍运营速度」互斥。",
        "group": "macro",
    },
    "infiniteRes": {
        "name": "资源采不完",
        "desc": "所有矿脉/气矿总量拉到 100 万，实际对局挖不空、矿脉不缩小消失，"
                "数字仍正常显示（焊进地图目录，只改总量，可与倍率采集/运营速度叠加）",
    },
}


def is_baked(stem: str) -> bool:
    return BAKED_SUFFIX in stem


def _sync_dep_mods() -> None:
    """把仓库 mods/*.SC2Mod 同步到游戏 Mods/ 目录（bake 和 sc2_panel 都从那里取）。
    生成器只写仓库，这一步保证 bake 永远用的是最新版——否则改了数值悄悄不生效。
    （前缀 Documents/Mods 那一层由 omni-deck 的 sync_mods_to_pfx 再往下推。）"""
    try:
        MODS_DIR.mkdir(parents=True, exist_ok=True)
        for f in REPO_MODS.glob("*.SC2Mod"):
            dst = MODS_DIR / f.name
            if not dst.exists() or dst.stat().st_mtime < f.stat().st_mtime:
                shutil.copy2(f, dst)
    except Exception as e:
        print(f"[sc2Mod] 同步 mods 到 {MODS_DIR} 出错（继续）：{e}")


def _sig_sources(key: str) -> list[Path]:
    """缓存签名要看的文件：依赖类看 Mods/ 里的 .SC2Mod，焊接类看仓库里的片段 .xml。"""
    if key in DEP_MODS:
        return [MODS_DIR / DEP_MODS[key]]
    return _catalog_paths(key)


def bake(map_stem: str, mod_keys: list[str]) -> str:
    """返回要传给 maps.get() 的地图名。没选 mod 就原样返回。"""
    mod_keys = [k for k in mod_keys if k in ALL_MOD_KEYS]
    if not mod_keys:
        return map_stem
    mod_keys = sorted(mod_keys)
    _sync_dep_mods()

    # 缓存键：图名 + mod 组合 + 各来源文件的 mtime（mod 改了 → 键变 → 重烤）
    sig_parts = [map_stem, *mod_keys]
    for k in mod_keys:
        for f in _sig_sources(k):
            sig_parts.append(f"{k}:{f.name}:{int(f.stat().st_mtime) if f.exists() else 0}")
    key = hashlib.md5("|".join(sig_parts).encode()).hexdigest()[:8]
    out_stem = f"{map_stem}{BAKED_SUFFIX}{key}"
    out_file = MAPS_DIR / f"{out_stem}.SC2Map"
    src = MAPS_DIR / f"{map_stem}.SC2Map"
    if out_file.exists() and out_file.stat().st_mtime >= src.stat().st_mtime:
        return out_stem
    if not src.exists():
        raise FileNotFoundError(src)

    cmd = [sys.executable, str(HERE / "_bake_inner.py"), str(src), str(out_file)]
    for k in mod_keys:
        if k in CATALOG_MERGES:
            for target_file, frag_name in CATALOG_MERGES[k]:
                cmd += ["--merge", f"{target_file}={HERE / 'mods' / frag_name}"]
        else:                                        # 纯依赖类（3x/5x 采集）
            cmd += ["--dep", _mod_dep_string(k)]
    print(f"[sc2Mod] 烘焙 {map_stem} + {mod_keys} …")
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0 or not out_file.exists():
        raise RuntimeError(f"烘焙失败：\n{r.stdout}\n{r.stderr}")
    print(f"[sc2Mod] {r.stdout.strip()}")
    return out_stem


if __name__ == "__main__":
    import sys
    print(bake(sys.argv[1], sys.argv[2:] or ["5xHarvest"]))
