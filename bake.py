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

# mod key -> 写进 DocumentHeader 的依赖串。file: 部分对应 Mods/ 下的 .SC2Mod。
MOD_DEPS: dict[str, str] = {
    "3xHarvest": "bnet:3xHarvest/0.0/999,file:Mods/3xHarvest.SC2Mod",
    "5xHarvest": "bnet:5xHarvest/0.0/999,file:Mods/5xHarvest.SC2Mod",
    "fastBuild": "bnet:fastBuild/0.0/999,file:Mods/fastBuild.SC2Mod",
}

# mod key -> 给 UI 用的展示信息。"group" 字段：同组互斥（前端渲染成单选，一次只能选一个），
# 没有 group 的照旧是独立勾选框。做新 mod 时 MOD_DEPS + MOD_INFO 两边都加一条。
MOD_INFO: dict[str, dict[str, str]] = {
    "3xHarvest": {
        "name": "3 倍采集",
        "desc": "矿 / 气全变体采集量 ×3（双方阵营对称生效，只改采集量，不改别的）",
        "group": "harvest",
    },
    "5xHarvest": {
        "name": "5 倍采集",
        "desc": "矿 / 气全变体采集量 ×5（双方阵营对称生效，只改采集量，不改别的）",
        "group": "harvest",
    },
    "fastBuild": {
        "name": "2 倍建造速度",
        "desc": "建筑建造耗时减半（人族/神族/异虫三族主建筑，不含指挥中心/主基地相关的升级变形）",
    },
}


def is_baked(stem: str) -> bool:
    return BAKED_SUFFIX in stem


def bake(map_stem: str, mod_keys: list[str]) -> str:
    """返回要传给 maps.get() 的地图名。没选 mod 就原样返回。"""
    mod_keys = [k for k in mod_keys if k in MOD_DEPS]
    if not mod_keys:
        return map_stem

    # 缓存键：图名 + mod 组合 + 各 .SC2Mod 文件的 mtime（mod 改了 → 键变 → 重烤）
    sig_parts = [map_stem, *sorted(mod_keys)]
    for k in sorted(mod_keys):
        mf = MODS_DIR / MOD_DEPS[k].split("file:Mods/")[-1]
        sig_parts.append(f"{k}:{int(mf.stat().st_mtime) if mf.exists() else 0}")
    key = hashlib.md5("|".join(sig_parts).encode()).hexdigest()[:8]
    out_stem = f"{map_stem}{BAKED_SUFFIX}{key}"
    out_file = MAPS_DIR / f"{out_stem}.SC2Map"
    src = MAPS_DIR / f"{map_stem}.SC2Map"
    if out_file.exists() and out_file.stat().st_mtime >= src.stat().st_mtime:
        return out_stem
    if not src.exists():
        raise FileNotFoundError(src)

    deps = [MOD_DEPS[k] for k in sorted(mod_keys)]
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
