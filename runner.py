#!/usr/bin/env python3
"""
路线 B 的对局核心：用 burnysc2 的组局逻辑（久经考验，能正确加载地图、不秒胜），
把 SC2 通过 proton-wine 垫片在 Steam 容器里拉起来。真人 vs 内置残酷 AI，realtime。

单独跑一局：
  uv run python runner.py --map BlackburnAIE --race P --enemy-race P --difficulty cheatinsane
"""
from __future__ import annotations

import argparse
import os
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SC2_ROOT = Path("/home/deck/Games/StarCraft II")

# —— 让 burnysc2 走 Wine 分支，并用我们的 proton 垫片 ——
os.environ.setdefault("SC2PF", "WineLinux")
os.environ.setdefault("SC2PATH", str(SC2_ROOT))
os.environ.setdefault("WINE", str(HERE / "proton-wine"))
os.environ.setdefault("SC2MOD_COMPAT_DATA", str(Path.home() / ".local/share/omni_deck_pfx"))
os.environ.setdefault("SC2_TIMEOUT", "180")   # 起 SC2 给足时间

from sc2 import maps                       # noqa: E402
from sc2.data import AIBuild, Difficulty, Race, Result  # noqa: E402
from sc2.main import run_game              # noqa: E402
from sc2.player import Computer, Human     # noqa: E402

RACE = {"T": Race.Terran, "P": Race.Protoss, "Z": Race.Zerg, "R": Race.Random}
DIFF = {
    "veryeasy": Difficulty.VeryEasy, "easy": Difficulty.Easy, "medium": Difficulty.Medium,
    "mediumhard": Difficulty.MediumHard, "hard": Difficulty.Hard, "harder": Difficulty.Harder,
    "veryhard": Difficulty.VeryHard,
    "cheatvision": Difficulty.CheatVision, "cheatmoney": Difficulty.CheatMoney,
    "cheatinsane": Difficulty.CheatInsane,   # 残酷3
}
AIB = {
    "random": AIBuild.RandomBuild, "rush": AIBuild.Rush, "timing": AIBuild.Timing,
    "power": AIBuild.Power, "macro": AIBuild.Macro, "air": AIBuild.Air,
}


def play_one(sel: dict) -> Result | list | None:
    import bake
    mods = sel.get("mods") or (["5xHarvest"] if sel.get("cheat") else [])
    map_name = bake.bake(sel["map"], mods) if mods else sel["map"]
    game_map = maps.get(map_name)
    players = [
        Human(RACE[sel["race"].upper()]),
        Computer(RACE[sel["enemy_race"].upper()],
                 DIFF[sel["difficulty"]],
                 AIB.get(sel.get("ai_build", "random"), AIBuild.RandomBuild)),
    ]
    print(f"[sc2Mod] 开一局：{sel['map']}  你={sel['race']}  电脑={sel['enemy_race']}/{sel['difficulty']}")
    return run_game(game_map, players, realtime=True)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--map", required=True)
    ap.add_argument("--race", default="P")
    ap.add_argument("--enemy-race", default="P")
    ap.add_argument("--difficulty", default="cheatinsane", choices=list(DIFF))
    ap.add_argument("--ai-build", default="random", choices=list(AIB))
    ap.add_argument("--mods", default="", help="逗号分隔的 mod key，如 5xHarvest")
    ap.add_argument("--json", action="store_true", help="结束时额外打印一行机器可读结果")
    a = ap.parse_args()
    sel = {
        "map": a.map, "race": a.race, "enemy_race": a.enemy_race,
        "difficulty": a.difficulty, "ai_build": a.ai_build,
        "mods": [m for m in a.mods.split(",") if m],
    }
    err = None
    try:
        res = play_one(sel)
    except Exception as e:
        res = None
        err = str(e)
    print(f"[sc2Mod] 结果：{res if res is not None else err}")

    if a.json:
        import json
        payload = {
            "map": a.map,
            "result": (res.name if hasattr(res, "name") else
                       [r.name if hasattr(r, "name") else str(r) for r in res] if isinstance(res, list) else
                       str(res) if res is not None else None),
            "error": err,
        }
        print("SC2MOD_RESULT: " + json.dumps(payload, ensure_ascii=False))


if __name__ == "__main__":
    main()
