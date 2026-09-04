#!/usr/bin/env python3
"""
sc2Mod 路线 B 入口：装快捷键 → 弹选图器 → 打一局(真人 vs 残酷AI) → 打完问"再来/换/退"。

    uv run python play.py

（旧的"正常客户端启动器"因为国服要登录进不了大厅，见 RECON.md；这条路不需要登录。）
"""
from __future__ import annotations

import json
from pathlib import Path

import hotkeys
import runner
import ui

STATE = Path.home() / ".config/sc2mod/last.json"
MAPS_DIR = Path("/home/deck/Games/StarCraft II/Maps")


def _load() -> dict:
    try:
        return json.loads(STATE.read_text())
    except Exception:
        return {}


def _save(sel: dict) -> None:
    STATE.parent.mkdir(parents=True, exist_ok=True)
    STATE.write_text(json.dumps(sel, ensure_ascii=False, indent=2))


def main() -> None:
    hk = hotkeys.install()
    print(f"[sc2Mod] 快捷键已装：{hk}")

    sel = ui.choose(MAPS_DIR, _load())
    while sel:
        _save(sel)
        try:
            result = runner.play_one(sel)
        except KeyboardInterrupt:
            break
        except Exception as e:
            result = f"运行出错：{e}"
        print(f"[sc2Mod] 本局结果：{result}")

        choice = ui.after_game(str(result))
        if choice == "quit":
            break
        if choice == "change":
            sel = ui.choose(MAPS_DIR, sel)
        # "again" → sel 不变，直接下一局


if __name__ == "__main__":
    main()
