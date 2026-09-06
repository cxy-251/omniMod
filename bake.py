#!/usr/bin/env python3
"""
把选中的 mod 烘焙进地图副本（改 DocumentHeader 依赖表），产物放进 Maps/，
文件名带 __<hash> 后缀。缓存：同 图+mod 组合第二次直接返回。

MPQ 写靠 _bake_inner.py（宿主直接跑，用仓库自带 lib/libstorm.so）。
"""
from __future__ import annotations

import hashlib
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SC2_ROOT = Path("/home/deck/Games/StarCraft II")
MAPS_DIR = SC2_ROOT / "Maps"
MODS_DIR = SC2_ROOT / "Mods"
BAKED_SUFFIX = "__"          # 烤出来的图名里含这个，picker 会过滤掉

# mod key -> Mods/ 下对应的 .SC2Mod 文件名。
MOD_FILES: dict[str, str] = {
    "3xHarvest": "3xHarvest.SC2Mod",
    "5xHarvest": "5xHarvest.SC2Mod",
    "macroSpeed": "macroSpeed.SC2Mod",
    "infiniteRes": "infiniteRes.SC2Mod",
}


def _mod_dep_string(key: str) -> str:
    """写进 DocumentHeader 的依赖串，形如 "bnet:X/0.0/<版本号>,file:Mods/X.SC2Mod"。

    版本号不能写死（以前是死的 "999"）——本地 Battle.net 有一层内容缓存
    （ProgramData/Blizzard Entertainment/Battle.net/Cache 下一堆按哈希命名的
    .s2ma），踩过坑：mod 文件内容改了、三份拷贝（sc2Mod 源头/游戏安装目录/Wine
    前缀 Documents）也都同步过了，但版本号没变，游戏那边可能还是把它当"以前解析过
    的那个版本"，读的是缓存结果而不是重新解析新内容——改了跟没改一样。这里让
    版本号跟着 mod 文件内容的哈希走，内容一变版本号必然跟着变，不给缓存偷懒的机会。"""
    fname = MOD_FILES[key]
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
    },
    "infiniteRes": {
        "name": "资源采不完",
        "desc": "所有矿脉/气矿/泰伯林矿总量拉到 10 亿，实际对局时长内挖不空、矿脉不缩小消失"
                "（只改总量，可与倍率采集、运营速度叠加）",
    },
}


def is_baked(stem: str) -> bool:
    return BAKED_SUFFIX in stem


def bake(map_stem: str, mod_keys: list[str]) -> str:
    """返回要传给 maps.get() 的地图名。没选 mod 就原样返回。"""
    mod_keys = [k for k in mod_keys if k in MOD_FILES]
    if not mod_keys:
        return map_stem

    # 缓存键：图名 + mod 组合 + 各 .SC2Mod 文件的 mtime（mod 改了 → 键变 → 重烤）
    sig_parts = [map_stem, *sorted(mod_keys)]
    for k in sorted(mod_keys):
        mf = MODS_DIR / MOD_FILES[k]
        sig_parts.append(f"{k}:{int(mf.stat().st_mtime) if mf.exists() else 0}")
    key = hashlib.md5("|".join(sig_parts).encode()).hexdigest()[:8]
    out_stem = f"{map_stem}{BAKED_SUFFIX}{key}"
    out_file = MAPS_DIR / f"{out_stem}.SC2Map"
    src = MAPS_DIR / f"{map_stem}.SC2Map"
    if out_file.exists() and out_file.stat().st_mtime >= src.stat().st_mtime:
        return out_stem
    if not src.exists():
        raise FileNotFoundError(src)

    deps = [_mod_dep_string(k) for k in sorted(mod_keys)]
    cmd = [sys.executable, str(HERE / "_bake_inner.py"), str(src), str(out_file), *deps]
    print(f"[sc2Mod] 烘焙 {map_stem} + {mod_keys} …")
    r = subprocess.run(cmd, capture_output=True, text=True)
    if r.returncode != 0 or not out_file.exists():
        raise RuntimeError(f"烘焙失败：\n{r.stdout}\n{r.stderr}")
    print(f"[sc2Mod] {r.stdout.strip()}")
    return out_stem


if __name__ == "__main__":
    import sys
    print(bake(sys.argv[1], sys.argv[2:] or ["5xHarvest"]))
