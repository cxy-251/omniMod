#!/usr/bin/env python3
"""
离线启动星际2 —— 正常客户端（有主菜单/大厅/选项/快捷键设置，打完回大厅继续下一场）。

不用 AI-API，不组局。就是把 SC2 用 Proton 在容器里拉起来。
之后的一切（vs AI、选图、挂 cheat mod、改快捷键）都在游戏里自己点。

用法：
  uv run python play.py          # 起游戏
  uv run python play.py --editor # 起银河编辑器（做 mod 用）
"""
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

SC2_ROOT = Path("/home/deck/Games/StarCraft II")
SC2_SWITCHER = SC2_ROOT / "Support64" / "SC2Switcher_x64.exe"
SC2_EDITOR = SC2_ROOT / "Support64" / "SC2Editor_x64.exe"
STEAM_ROOT = Path.home() / ".local/share/Steam"
STEAM_COMMON = STEAM_ROOT / "steamapps/common"
COMPAT_DATA = Path.home() / ".local/share/omni_deck_pfx"
PREFIX_DOCS = COMPAT_DATA / "pfx/drive_c/users/steamuser/Documents/StarCraft II"
LOG_DIR = Path.home() / ".config/sc2mod"

_PROTON_CANDIDATES = ["Proton - Experimental", "Proton 11.0", "Proton 10.0",
                      "Proton 9.0 (Beta)", "Proton Hotfix", "Proton 8.0"]
PROTON = next((STEAM_COMMON / n / "proton" for n in _PROTON_CANDIDATES
               if (STEAM_COMMON / n / "proton").exists()), None)
SLR_ENTRY = STEAM_COMMON / "SteamLinuxRuntime_sniper" / "_v2-entry-point"


def ensure_prefix_links() -> None:
    """SC2 从用户文档目录找 Maps/Mods；把游戏目录软链过去（幂等）。"""
    PREFIX_DOCS.mkdir(parents=True, exist_ok=True)
    for name in ("Maps", "Mods"):
        link = PREFIX_DOCS / name
        target = SC2_ROOT / name
        if link.is_symlink() and link.resolve() == target.resolve():
            continue
        if link.exists() or link.is_symlink():
            if link.is_dir() and not link.is_symlink():
                continue  # 真目录，别动
            link.unlink()
        link.symlink_to(target)


def launch(editor: bool) -> int:
    if PROTON is None or not PROTON.exists():
        sys.exit("没找到 Proton（Steam 里装个 Proton Experimental / 11）")
    exe = SC2_EDITOR if editor else SC2_SWITCHER
    if not exe.exists():
        sys.exit(f"找不到 {exe}")

    ensure_prefix_links()
    LOG_DIR.mkdir(parents=True, exist_ok=True)

    env = os.environ.copy()
    env["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = str(STEAM_ROOT)
    env["STEAM_COMPAT_DATA_PATH"] = str(COMPAT_DATA)
    env["STEAM_COMPAT_INSTALL_PATH"] = str(SC2_ROOT)
    env["STEAM_COMPAT_APP_ID"] = "0"
    env["SteamAppId"] = "0"
    env["SteamGameId"] = "0"
    env["WINEDEBUG"] = "-all"
    env.setdefault("LANG", "zh_CN.UTF-8")
    env.setdefault("LC_ALL", "zh_CN.UTF-8")
    COMPAT_DATA.mkdir(parents=True, exist_ok=True)

    proton_cmd = [str(PROTON), "waitforexitandrun", str(exe)]
    if SLR_ENTRY.exists():
        cmd = [str(SLR_ENTRY), "--verb=waitforexitandrun", "--", *proton_cmd]
    else:
        cmd = proton_cmd

    what = "银河编辑器" if editor else "星际争霸2（离线客户端）"
    print(f"[sc2Mod] 启动 {what} … 关掉游戏窗口即结束本进程。")
    logf = open(LOG_DIR / "launch.log", "w")
    proc = subprocess.Popen(cmd, env=env, cwd=str(SC2_ROOT),
                            stdout=logf, stderr=subprocess.STDOUT)
    try:
        return proc.wait()
    except KeyboardInterrupt:
        proc.terminate()
        return 130


def main() -> None:
    ap = argparse.ArgumentParser(description="离线启动星际2（正常客户端）")
    ap.add_argument("--editor", action="store_true", help="启动银河编辑器（做 mod）")
    args = ap.parse_args()
    sys.exit(launch(editor=args.editor))


if __name__ == "__main__":
    main()
