#!/usr/bin/env python3
"""
sc2Mod —— 离线「真人 vs 内置AI」直连开局器

不登录、不开战网、不靠离线模式。用 SC2 自带的 AI-API（aiarena 打 bot 天梯那套接口）：
  1. 用 Proton 起 SC2_x64.exe -listen 127.0.0.1 -port N -displayMode 1
  2. 连本地 websocket，CreateGame（本地图 + 一个 Computer 对手）+ JoinGame（你的种族）
  3. 渲染窗口出来，你正常鼠标键盘打；本脚本只维持连接 + 按需喂 cheat

cheat（可选，靠 API 的 debug 指令，不改游戏文件）：
  - 开局在你主基地旁多塞 N 个农民
  - fast_build：建造/训练瞬间完成
  - 每隔几秒补满矿和气（“采不完” + 抹平残酷电脑的资源加成）

用法：
  uv run python play.py                      # 弹窗逐项选（地图/种族/对手/难度/cheat）
  uv run python play.py --map AbyssalReefAIE --race T --enemy-race Z --difficulty cheatinsane --cheat
  uv run python play.py --last               # 直接用上次的选择
"""
from __future__ import annotations

import argparse
import asyncio
import json
import os
import shutil
import signal
import socket
import subprocess
import sys
import time
from pathlib import Path

import websockets
from s2clientprotocol import sc2api_pb2 as sc_pb
from s2clientprotocol import common_pb2 as common_pb
from s2clientprotocol import debug_pb2 as debug_pb

# ----------------------------------------------------------------------------- #
# 路径 / 常量（按这台 Deck 的实际情况写死，换机改这里）
# ----------------------------------------------------------------------------- #
SC2_ROOT = Path("/home/deck/Games/StarCraft II")
SC2_CORE_EXE = SC2_ROOT / "Versions" / "Base97579" / "SC2_x64.exe"
SC2_SWITCHER = SC2_ROOT / "Support64" / "SC2Switcher_x64.exe"   # 会把参数转发给 SC2_x64.exe
MAPS_DIR = SC2_ROOT / "Maps"
STEAM_ROOT = Path.home() / ".local/share/Steam"
STEAM_COMMON = STEAM_ROOT / "steamapps/common"
COMPAT_DATA = Path.home() / ".local/share/omni_deck_pfx"

# 优先级：Experimental > 11 > 10 > 9 …（用第一个存在的）
_PROTON_CANDIDATES = ["Proton - Experimental", "Proton 11.0", "Proton 10.0",
                      "Proton 9.0 (Beta)", "Proton Hotfix", "Proton 8.0"]
PROTON = next((STEAM_COMMON / n / "proton" for n in _PROTON_CANDIDATES
               if (STEAM_COMMON / n / "proton").exists()), None)
# 新版 Proton 必须在 Steam Linux Runtime 容器里跑，否则直接退出 code 53
SLR_ENTRY = STEAM_COMMON / "SteamLinuxRuntime_sniper" / "_v2-entry-point"
PROTON_LOG = Path.home() / ".config/sc2mod/proton.log"

STATE_FILE = Path.home() / ".config/sc2mod/last.json"

RACE = {
    "T": common_pb.Terran, "P": common_pb.Protoss, "Z": common_pb.Zerg, "R": common_pb.Random,
    "TERRAN": common_pb.Terran, "PROTOSS": common_pb.Protoss, "ZERG": common_pb.Zerg, "RANDOM": common_pb.Random,
}
DIFFICULTY = {
    "veryeasy": sc_pb.VeryEasy, "easy": sc_pb.Easy, "medium": sc_pb.Medium,
    "mediumhard": sc_pb.MediumHard, "hard": sc_pb.Hard, "harder": sc_pb.Harder,
    "veryhard": sc_pb.VeryHard,
    "cheatvision": sc_pb.CheatVision, "cheatmoney": sc_pb.CheatMoney,
    "cheatinsane": sc_pb.CheatInsane,   # 残酷3
}
AI_BUILD = {
    "random": sc_pb.RandomBuild, "rush": sc_pb.Rush, "timing": sc_pb.Timing,
    "power": sc_pb.Power, "macro": sc_pb.Macro, "air": sc_pb.Air,
}

# 各族基础工人 / 主基地 unit type id（用于 cheat 开局塞农民、定位主基地）
WORKER_ID = {common_pb.Terran: 45, common_pb.Protoss: 84, common_pb.Zerg: 104}   # SCV / Probe / Drone
TOWNHALL_IDS = {18, 59, 86, 132, 130, 100}  # CC / Nexus / Hatchery / OrbitalCommand / PlanetaryFortress / Lair ...

# cheat（临时版，靠 API debug 指令）：真正的「5倍采集」要靠 cheat5x.SC2Mod（下一步做），
# 这里先用「周期补矿气」近似 —— 效果是经济不受限，够抹平残酷电脑的资源加成。
CHEAT_TOPUP_EVERY_S = 6      # 每隔多少秒补一次资源
CHEAT_TOPUP_ENABLE_FAST_BUILD = False   # 用户只要 5x 采集，不要瞬间建造


# ----------------------------------------------------------------------------- #
# 选择：命令行没给全就弹 kdialog / zenity
# ----------------------------------------------------------------------------- #
def _dialog_bin() -> str | None:
    for b in ("kdialog", "zenity"):
        if shutil.which(b):
            return b
    return None


def _kd_menu(title: str, options: list[tuple[str, str]], default: str | None = None) -> str | None:
    """options = [(key, label), ...]，返回选中的 key。"""
    b = _dialog_bin()
    if b == "kdialog":
        args = ["kdialog", "--title", "sc2Mod", "--menu", title]
        for k, label in options:
            args += [k, label]
        r = subprocess.run(args, capture_output=True, text=True)
        return r.stdout.strip() or None
    if b == "zenity":
        args = ["zenity", "--list", "--title", "sc2Mod", "--text", title,
                "--column", "key", "--column", "说明", "--hide-column", "1", "--print-column", "1"]
        for k, label in options:
            args += [k, label]
        r = subprocess.run(args, capture_output=True, text=True)
        return r.stdout.strip().split("|")[0] or None
    # 没有对话框工具：退回终端输入
    print(title)
    for k, label in options:
        print(f"  [{k}] {label}")
    return input("选择 (key): ").strip() or default


def _kd_yesno(text: str, default_yes: bool = True) -> bool:
    b = _dialog_bin()
    if b == "kdialog":
        r = subprocess.run(["kdialog", "--title", "sc2Mod", "--yesno", text])
        return r.returncode == 0
    if b == "zenity":
        r = subprocess.run(["zenity", "--question", "--title", "sc2Mod", "--text", text])
        return r.returncode == 0
    ans = input(f"{text} [Y/n]: ").strip().lower()
    return default_yes if ans == "" else ans.startswith("y")


def list_maps() -> list[str]:
    return sorted(p.stem for p in MAPS_DIR.glob("*.SC2Map"))


def choose_interactively(args: argparse.Namespace, last: dict) -> dict:
    maps = list_maps()
    if not maps:
        sys.exit(f"没有找到地图：{MAPS_DIR}/*.SC2Map")

    sel: dict = {}

    # 地图
    if args.map:
        sel["map"] = args.map
    else:
        default_map = last.get("map") if last.get("map") in maps else maps[0]
        opts = [(m, m) for m in maps]
        # 把上次的图放最前面
        opts.sort(key=lambda kv: (kv[0] != default_map, kv[0]))
        sel["map"] = _kd_menu(f"地图（上次：{default_map}）", opts, default_map) or default_map

    def pick_race(flag, key, prompt):
        if flag:
            return flag.upper()
        d = last.get(key, "T")
        opts = [("T", "人族 Terran"), ("P", "神族 Protoss"), ("Z", "虫族 Zerg"), ("R", "随机 Random")]
        opts.sort(key=lambda kv: (kv[0] != d, kv[0]))
        return _kd_menu(prompt, opts, d) or d

    sel["race"] = pick_race(args.race, "race", "你的种族")
    sel["enemy_race"] = pick_race(args.enemy_race, "enemy_race", "电脑种族")

    # 难度
    if args.difficulty:
        sel["difficulty"] = args.difficulty.lower()
    else:
        d = last.get("difficulty", "cheatinsane")
        opts = [
            ("cheatinsane", "作弊-疯狂 / 残酷3（默认）"),
            ("cheatmoney", "作弊-资源"),
            ("cheatvision", "作弊-视野"),
            ("veryhard", "非常难 / 精英"),
            ("harder", "较难"),
            ("hard", "困难"),
            ("medium", "中等"),
        ]
        opts.sort(key=lambda kv: (kv[0] != d, kv[0]))
        sel["difficulty"] = _kd_menu("电脑难度", opts, d) or d

    # cheat
    if args.cheat is not None:
        sel["cheat"] = args.cheat
    else:
        sel["cheat"] = _kd_yesno(
            f"启用 cheat？\n\n开局 +{CHEAT_EXTRA_WORKERS} 农民、瞬间建造、每 {CHEAT_TOPUP_EVERY_S}s 补满矿气",
            default_yes=bool(last.get("cheat", True)),
        )

    sel["ai_build"] = (args.ai_build or last.get("ai_build", "random")).lower()
    return sel


# ----------------------------------------------------------------------------- #
# 启动 SC2（Proton）
# ----------------------------------------------------------------------------- #
def free_port() -> int:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


def launch_sc2(port: int | None, *, use_switcher: bool = True, winedebug: str = "-all") -> subprocess.Popen:
    if PROTON is None or not PROTON.exists():
        sys.exit("没找到 Proton（Steam 里装一个 Proton Experimental / 11）")

    exe = SC2_SWITCHER if (use_switcher and SC2_SWITCHER.exists()) else SC2_CORE_EXE

    env = os.environ.copy()
    env["STEAM_COMPAT_CLIENT_INSTALL_PATH"] = str(STEAM_ROOT)
    env["STEAM_COMPAT_DATA_PATH"] = str(COMPAT_DATA)
    env["STEAM_COMPAT_INSTALL_PATH"] = str(SC2_ROOT)
    env["STEAM_COMPAT_APP_ID"] = "0"
    env["SteamAppId"] = "0"
    env["SteamGameId"] = "0"
    env["PROTON_LOG"] = "1"
    env["PROTON_LOG_DIR"] = str(PROTON_LOG.parent)
    env["WINEDEBUG"] = winedebug
    env.setdefault("LANG", "zh_CN.UTF-8")
    env.setdefault("LC_ALL", "zh_CN.UTF-8")
    COMPAT_DATA.mkdir(parents=True, exist_ok=True)
    PROTON_LOG.parent.mkdir(parents=True, exist_ok=True)

    sc2_args: list[str] = []
    if port is not None:
        sc2_args = [
            "-listen", "127.0.0.1", "-port", str(port),
            "-displayMode", "1",
            "-windowwidth", "1600", "-windowheight", "900",
            "-windowx", "40", "-windowy", "40",
        ]

    # 关键：verb 用 waitforexitandrun（不是 run）——run 会立刻返回，wrapper 退出被误判成失败
    proton_cmd = [str(PROTON), "waitforexitandrun", str(exe), *sc2_args]
    if SLR_ENTRY.exists():
        cmd = [str(SLR_ENTRY), "--verb=waitforexitandrun", "--", *proton_cmd]
        how = f"SLR_sniper + Proton  ({exe.name})"
    else:
        cmd = proton_cmd
        how = f"Proton 裸跑  ({exe.name})"

    logf = open(str(PROTON_LOG.parent / "launch.stdouterr.log"), "w")
    print(f"[sc2Mod] 启动 SC2 … （{how}）")
    print(f"[sc2Mod] 输出: {logf.name}   Proton 日志: {PROTON_LOG.parent}/steam-*.log")
    return subprocess.Popen(cmd, env=env, cwd=str(SC2_ROOT), stdout=logf, stderr=subprocess.STDOUT)


# ----------------------------------------------------------------------------- #
# s2client 握手 + cheat 保姆
# ----------------------------------------------------------------------------- #
class SC2Conn:
    def __init__(self, ws):
        self.ws = ws

    async def send(self, req: sc_pb.Request) -> sc_pb.Response:
        await self.ws.send(req.SerializeToString())
        data = await self.ws.recv()
        resp = sc_pb.Response()
        resp.ParseFromString(data)
        if resp.error:
            print(f"[sc2Mod] API error: {list(resp.error)}  {resp.error_details}")
        return resp

    async def ping(self) -> bool:
        try:
            r = await self.send(sc_pb.Request(ping=sc_pb.RequestPing()))
            return r.HasField("ping")
        except Exception:
            return False


def _dump_launch_logs() -> None:
    seen = set()
    for p in list(PROTON_LOG.parent.glob("*.log")):
        if p in seen or not p.is_file():
            continue
        seen.add(p)
        try:
            lines = p.read_text(errors="replace").splitlines()
        except Exception:
            continue
        if not lines:
            continue
        print(f"\n----- {p}  (末 50 行) -----")
        print("\n".join(lines[-50:]))


async def connect(port: int, sc2_proc: subprocess.Popen, timeout: float = 180.0) -> SC2Conn:
    url = f"ws://127.0.0.1:{port}/sc2api"
    deadline = time.time() + timeout
    print(f"[sc2Mod] 等待 SC2 API 就绪 {url} …")
    while time.time() < deadline:
        if sc2_proc.poll() is not None:
            _dump_launch_logs()
            raise RuntimeError(f"SC2 进程提前退出（code {sc2_proc.returncode}）—— 见上面日志尾部")
        try:
            ws = await websockets.connect(url, max_size=2 ** 26, open_timeout=5, ping_interval=None)
            print("[sc2Mod] 已连上 SC2 API")
            return SC2Conn(ws)
        except Exception:
            await asyncio.sleep(2)
    raise TimeoutError("等 SC2 API 超时")


async def create_and_join(conn: SC2Conn, sel: dict) -> int:
    my_race = RACE[sel["race"].upper()]
    enemy_race = RACE[sel["enemy_race"].upper()]
    diff = DIFFICULTY[sel["difficulty"]]
    ai_build = AI_BUILD.get(sel["ai_build"], sc_pb.RandomBuild)

    # RequestAvailableMaps 返回的就是 "<名字>.SC2Map" 这种相对名，CreateGame 要的也是它
    create = sc_pb.RequestCreateGame(
        local_map=sc_pb.LocalMap(map_path=f"{sel['map']}.SC2Map"),
        realtime=True,
        disable_fog=False,
    )
    create.player_setup.add(type=sc_pb.Participant, race=my_race)
    create.player_setup.add(type=sc_pb.Computer, race=enemy_race,
                            difficulty=diff, ai_build=ai_build, player_name="Cruel-AI")
    r = await conn.send(sc_pb.Request(create_game=create))
    if r.create_game.error:
        name = sc_pb.ResponseCreateGame.Error.Name(r.create_game.error)
        raise RuntimeError(f"CreateGame 失败：{name}  {r.create_game.error_details}")

    join = sc_pb.RequestJoinGame(
        race=my_race,
        options=sc_pb.InterfaceOptions(
            raw=True, score=True, show_cloaked=True, show_burrowed_shadows=True,
            show_placeholders=True, raw_affects_selection=False, raw_crop_to_playable_area=False,
        ),
    )
    r = await conn.send(sc_pb.Request(join_game=join))
    if r.join_game.error:
        name = sc_pb.ResponseJoinGame.Error.Name(r.join_game.error)
        raise RuntimeError(f"JoinGame 失败：{name}  {r.join_game.error_details}")
    pid = r.join_game.player_id
    print(f"[sc2Mod] 已加入对局，你是 player {pid}")
    return pid


async def get_obs(conn: SC2Conn) -> sc_pb.ResponseObservation:
    r = await conn.send(sc_pb.Request(observation=sc_pb.RequestObservation()))
    return r.observation


async def do_cheats_once(conn: SC2Conn, pid: int) -> None:
    cmds = []
    if CHEAT_TOPUP_ENABLE_FAST_BUILD:
        cmds.append(debug_pb.DebugCommand(game_state=debug_pb.fast_build))
    if cmds:
        await conn.send(sc_pb.Request(debug=sc_pb.RequestDebug(debug=cmds)))
    print("[sc2Mod] cheat（临时·补矿气）已启动；真·5倍采集见 cheat5x.SC2Mod")


async def cheat_topup_loop(conn: SC2Conn) -> None:
    while True:
        await asyncio.sleep(CHEAT_TOPUP_EVERY_S)
        try:
            await conn.send(sc_pb.Request(debug=sc_pb.RequestDebug(debug=[
                debug_pb.DebugCommand(game_state=debug_pb.minerals),
                debug_pb.DebugCommand(game_state=debug_pb.gas),
            ])))
        except Exception:
            return


async def show_maps() -> None:
    """连上 SC2，问它 ping（版本）+ 能看到哪些地图。"""
    port = free_port()
    proc = launch_sc2(port, use_switcher=True)
    try:
        conn = await connect(port, proc)
        r = await conn.send(sc_pb.Request(ping=sc_pb.RequestPing()))
        print(f"\n[ping] game_version={r.ping.game_version!r}  data_version={r.ping.data_version!r} "
              f"base_build={r.ping.base_build}  data_build={r.ping.data_build}")
        print(f"[LocalMap 字段] {[f.name for f in sc_pb.LocalMap.DESCRIPTOR.fields]}")
        r = await conn.send(sc_pb.Request(available_maps=sc_pb.RequestAvailableMaps()))
        am = r.available_maps
        print(f"\n[SC2 能看到的本地图] 共 {len(am.local_map_paths)} 个：")
        for p in list(am.local_map_paths)[:60]:
            print("   ", p)
        print(f"\n[SC2 能看到的 Bnet 图] 共 {len(am.battlenet_map_names)} 个：")
        for p in list(am.battlenet_map_names)[:30]:
            print("   ", p)
        await conn.send(sc_pb.Request(quit=sc_pb.RequestQuit()))
    finally:
        if proc.poll() is None:
            proc.terminate()


async def probe(use_switcher: bool) -> None:
    """只把 SC2 拉起来（不带 -listen / 不连 API），看窗口能不能出来、进程能不能活着。"""
    proc = launch_sc2(None, use_switcher=use_switcher, winedebug="")
    print("[sc2Mod] 已启动，无 API。观察 60 秒 …（Ctrl+C 结束）")
    for i in range(60):
        await asyncio.sleep(1)
        if proc.poll() is not None:
            print(f"[sc2Mod] SC2 在第 {i+1} 秒退出，code {proc.returncode}")
            _dump_launch_logs()
            return
    print("[sc2Mod] 60 秒内没退出 —— 说明 SC2 起来了。手动关掉游戏窗口即可。")
    proc.terminate()


async def run(sel: dict) -> None:
    port = free_port()
    proc = launch_sc2(port, use_switcher=sel.get("use_switcher", True))
    conn = None
    try:
        conn = await connect(port, proc)
        pid = await create_and_join(conn, sel)

        if sel["cheat"]:
            await asyncio.sleep(2)          # 等对局实体生成
            await do_cheats_once(conn, pid)
            topup = asyncio.create_task(cheat_topup_loop(conn))
        else:
            topup = None

        # 主循环：定期看对局是否结束 / SC2 是否关掉
        while True:
            if proc.poll() is not None:
                print("[sc2Mod] SC2 已退出")
                break
            try:
                obs = await get_obs(conn)
            except Exception:
                print("[sc2Mod] 连接断开（多半是你关了游戏）")
                break
            if obs.player_result:
                res = {r.player_id: sc_pb.Result.Name(r.result) for r in obs.player_result}
                print(f"[sc2Mod] 对局结束：{res}")
                break
            await asyncio.sleep(3)

        if topup:
            topup.cancel()
    finally:
        try:
            if conn:
                await conn.send(sc_pb.Request(leave_game=sc_pb.RequestLeaveGame()))
                await conn.send(sc_pb.Request(quit=sc_pb.RequestQuit()))
        except Exception:
            pass
        if proc.poll() is None:
            proc.send_signal(signal.SIGTERM)
            try:
                proc.wait(timeout=10)
            except subprocess.TimeoutExpired:
                proc.kill()


# ----------------------------------------------------------------------------- #
def main() -> None:
    ap = argparse.ArgumentParser(description="离线 SC2 真人 vs 内置AI 开局器")
    ap.add_argument("--map", help="地图名（不含 .SC2Map），不给则弹窗选")
    ap.add_argument("--race", choices=list("TPZR"), help="你的种族")
    ap.add_argument("--enemy-race", choices=list("TPZR"), help="电脑种族")
    ap.add_argument("--difficulty", choices=list(DIFFICULTY), help="电脑难度（默认 cheatinsane=残酷3）")
    ap.add_argument("--ai-build", choices=list(AI_BUILD), help="电脑开局风格")
    ap.add_argument("--cheat", dest="cheat", action="store_true", default=None, help="强制开 cheat")
    ap.add_argument("--no-cheat", dest="cheat", action="store_false", help="强制关 cheat")
    ap.add_argument("--last", action="store_true", help="直接沿用上次选择，不弹窗")
    ap.add_argument("--probe", action="store_true",
                    help="只把 SC2 拉起来看能不能跑（不选图/不连 API），排障用")
    ap.add_argument("--maps", action="store_true",
                    help="连上 SC2，打印版本 + 它能看到的地图清单，排障用")
    ap.add_argument("--core", action="store_true",
                    help="直接用 SC2_x64.exe，不经 SC2Switcher（默认经 Switcher 转发参数）")
    args = ap.parse_args()

    if args.probe:
        try:
            asyncio.run(probe(use_switcher=not args.core))
        except KeyboardInterrupt:
            pass
        return

    if args.maps:
        try:
            asyncio.run(show_maps())
        except KeyboardInterrupt:
            pass
        return

    STATE_FILE.parent.mkdir(parents=True, exist_ok=True)
    last = {}
    if STATE_FILE.exists():
        try:
            last = json.loads(STATE_FILE.read_text())
        except Exception:
            last = {}

    if args.last and last:
        sel = {**last}
        # 命令行仍可覆盖
        for k, v in (("map", args.map), ("race", args.race), ("enemy_race", args.enemy_race),
                     ("difficulty", args.difficulty and args.difficulty.lower()),
                     ("ai_build", args.ai_build), ("cheat", args.cheat)):
            if v is not None:
                sel[k] = v
    else:
        sel = choose_interactively(args, last)

    sel["use_switcher"] = not args.core
    STATE_FILE.write_text(json.dumps(sel, ensure_ascii=False, indent=2))
    print(f"[sc2Mod] 本局：{sel}")

    try:
        asyncio.run(run(sel))
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
