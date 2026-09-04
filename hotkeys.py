#!/usr/bin/env python3
"""
装自定义快捷键到 Wine 前缀里的 SC2 用户文档。

用户要的三个改动（用游戏真实命令名）：
  `  (Grave)   ArmySelect  —— 全选部队      （默认 F2；反引号默认是语音键 PTT，一并让出）
  空格         TownCamera  —— 回基地/循环   （默认 Backspace）
  Backspace    AlertRecall —— 跳到上个警报  （默认空格）  ← 空格/Backspace 互换
"""
from __future__ import annotations

from pathlib import Path

PFX_DOCS = (Path.home() / ".local/share/omni_deck_pfx"
            / "pfx/drive_c/users/steamuser/Documents/StarCraft II")
PROFILE_NAME = "sc2Mod"

HOTKEYS_INI = f"""\
[Settings]

[Hotkeys]
ArmySelect=Grave
TownCamera=Space
AlertRecall=Backspace
PTT=
"""


def install() -> Path:
    hk_dir = PFX_DOCS / "Hotkeys"
    hk_dir.mkdir(parents=True, exist_ok=True)
    hk_file = hk_dir / f"{PROFILE_NAME}.SC2Hotkeys"
    hk_file.write_text(HOTKEYS_INI, encoding="utf-8")

    # 让 SC2 默认用这个 profile：改 Variables.txt 里的 hotkeyProfile
    var = PFX_DOCS / "Variables.txt"
    lines = var.read_text(encoding="utf-8", errors="replace").splitlines() if var.exists() else []
    lines = [ln for ln in lines if not ln.lower().startswith("hotkeyprofile=")]
    lines.append(f"hotkeyProfile={PROFILE_NAME}")
    PFX_DOCS.mkdir(parents=True, exist_ok=True)
    var.write_text("\n".join(lines) + "\n", encoding="utf-8")

    return hk_file


if __name__ == "__main__":
    p = install()
    print(f"已装快捷键：{p}")
    print(f"Variables.txt hotkeyProfile -> {PROFILE_NAME}")
    print("进游戏后如果没生效，去 选项→热键 里手动选一次 'sc2Mod' 这个方案。")
