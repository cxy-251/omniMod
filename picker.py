#!/usr/bin/env python3
"""
可视化开局选择器：一个 Tkinter 窗口
  上方：你的种族 / 电脑种族 / 难度 / 是否 cheat
  下方：所有地图的「地形全貌」缩略图网格（从每张 .SC2Map 里的 Minimap.tga 抽出来）
点某张图的缩略图 = 用当前设置开这局。

被 play.py 调用；也能单独 `uv run python picker.py` 预览。
"""
from __future__ import annotations

import io
import tkinter as tk
from pathlib import Path
from tkinter import ttk

import mpyq
from PIL import Image, ImageTk

CACHE = Path.home() / ".cache/sc2mod/thumbs"
# 统一画布：所有缩略图输出成同尺寸（地图按比例缩放后居中，两侧/上下留深色边），
# 这样 UI 里每张卡片高度一致。
THUMB_W, THUMB_H = 256, 144
CANVAS_BG = (18, 20, 24)

RACES = [("T", "人族"), ("P", "神族"), ("Z", "虫族"), ("R", "随机")]
DIFFS = [
    ("cheatinsane", "残酷3 / 作弊-疯狂"),
    ("cheatmoney", "作弊-资源"),
    ("cheatvision", "作弊-视野"),
    ("veryhard", "非常难"),
    ("harder", "较难"),
    ("hard", "困难"),
    ("medium", "中等"),
]


def _thumb_path(map_file: Path) -> Path:
    return CACHE / f"{map_file.stem}.png"


def extract_thumb(map_file: Path) -> Path | None:
    """从 .SC2Map 抽 Minimap.tga → 存 PNG 缓存。已存在就直接返回。"""
    out = _thumb_path(map_file)
    if out.exists() and out.stat().st_mtime >= map_file.stat().st_mtime:
        return out
    try:
        arch = mpyq.MPQArchive(str(map_file))
        raw = arch.read_file("Minimap.tga")
        if not raw:
            return None
        im = Image.open(io.BytesIO(raw)).convert("RGB")
        im.thumbnail((THUMB_W, THUMB_H), Image.LANCZOS)  # 按比例缩到画布内
        canvas = Image.new("RGB", (THUMB_W, THUMB_H), CANVAS_BG)
        canvas.paste(im, ((THUMB_W - im.width) // 2, (THUMB_H - im.height) // 2))
        CACHE.mkdir(parents=True, exist_ok=True)
        canvas.save(out)
        return out
    except Exception:
        return None


def minimap_hash(map_file: Path) -> str:
    """Minimap.tga 内容哈希，用于识别"同一张图的不同赛季版本"。"""
    import hashlib
    try:
        raw = mpyq.MPQArchive(str(map_file)).read_file("Minimap.tga")
        return hashlib.md5(raw).hexdigest()[:12] if raw else map_file.stem
    except Exception:
        return map_file.stem


def dedupe_maps(map_files: list[Path]) -> list[Path]:
    """同地形只留一张，代表用最短名（一般是不带 512/513 后缀的那张）。"""
    by_hash: dict[str, list[Path]] = {}
    for mf in map_files:
        by_hash.setdefault(minimap_hash(mf), []).append(mf)
    reps = [min(v, key=lambda p: (len(p.stem), p.stem)) for v in by_hash.values()]
    return sorted(reps, key=lambda p: p.stem.lower())


def choose(maps_dir: Path, last: dict) -> dict | None:
    """弹窗。返回 {map,race,enemy_race,difficulty,cheat,ai_build} 或 None（取消）。"""
    map_files = sorted(maps_dir.glob("*.SC2Map"))
    if not map_files:
        raise SystemExit(f"没有地图：{maps_dir}")

    root = tk.Tk()
    root.title("sc2Mod — 选图开打")
    root.geometry("1180x820")
    try:
        root.tk.call("tk", "scaling", 1.3)
    except Exception:
        pass

    result: dict = {}

    v_myrace = tk.StringVar(value=last.get("race", "P"))
    v_enemy = tk.StringVar(value=last.get("enemy_race", "P"))
    v_diff = tk.StringVar(value=last.get("difficulty", "cheatinsane"))
    v_cheat = tk.BooleanVar(value=bool(last.get("cheat", True)))

    # ---------- 顶部设置条 ----------
    bar = ttk.Frame(root, padding=10)
    bar.pack(side="top", fill="x")

    def race_row(parent, label, var):
        f = ttk.Frame(parent)
        ttk.Label(f, text=label).pack(side="left", padx=(0, 6))
        for key, name in RACES:
            ttk.Radiobutton(f, text=name, value=key, variable=var).pack(side="left")
        return f

    race_row(bar, "你的种族", v_myrace).grid(row=0, column=0, sticky="w", padx=6, pady=4)
    race_row(bar, "电脑种族", v_enemy).grid(row=0, column=1, sticky="w", padx=6, pady=4)

    df = ttk.Frame(bar)
    ttk.Label(df, text="难度").pack(side="left", padx=(0, 6))
    ttk.Combobox(df, textvariable=v_diff, state="readonly", width=18,
                 values=[f"{k}  ·  {name}" for k, name in DIFFS]).pack(side="left")
    df.grid(row=1, column=0, sticky="w", padx=6, pady=4)
    # Combobox 显示 "key · 名字"，取值时切回 key
    combo = df.winfo_children()[1]
    combo.set(next((f"{k}  ·  {n}" for k, n in DIFFS if k == v_diff.get()), DIFFS[0][0]))

    ttk.Checkbutton(bar, text="启用 cheat（临时：周期补矿气）", variable=v_cheat)\
        .grid(row=1, column=1, sticky="w", padx=6, pady=4)

    ttk.Label(bar, text="↓ 点地图缩略图开打", foreground="#888")\
        .grid(row=2, column=0, columnspan=2, sticky="w", padx=6, pady=(8, 0))

    # ---------- 可滚动缩略图网格 ----------
    mid = ttk.Frame(root)
    mid.pack(side="top", fill="both", expand=True)
    canvas = tk.Canvas(mid, highlightthickness=0)
    sb = ttk.Scrollbar(mid, orient="vertical", command=canvas.yview)
    grid = ttk.Frame(canvas)
    grid.bind("<Configure>", lambda e: canvas.configure(scrollregion=canvas.bbox("all")))
    canvas.create_window((0, 0), window=grid, anchor="nw")
    canvas.configure(yscrollcommand=sb.set)
    canvas.pack(side="left", fill="both", expand=True)
    sb.pack(side="right", fill="y")
    canvas.bind_all("<Button-4>", lambda e: canvas.yview_scroll(-3, "units"))
    canvas.bind_all("<Button-5>", lambda e: canvas.yview_scroll(3, "units"))
    canvas.bind_all("<MouseWheel>", lambda e: canvas.yview_scroll(-1 * (e.delta // 120), "units"))

    def pick(map_stem: str):
        key = combo.get().split(" ")[0].strip() or "cheatinsane"
        result.update(
            map=map_stem, race=v_myrace.get(), enemy_race=v_enemy.get(),
            difficulty=key, cheat=bool(v_cheat.get()),
            ai_build=last.get("ai_build", "random"),
        )
        root.destroy()

    status = ttk.Label(root, text="正在生成缩略图…", padding=6)
    status.pack(side="bottom", fill="x")

    COLS = 4
    photos: list[ImageTk.PhotoImage] = []  # 防 GC

    def build():
        for i, mf in enumerate(map_files):
            tp = extract_thumb(mf)
            cell = ttk.Frame(grid, padding=6)
            cell.grid(row=i // COLS, column=i % COLS, padx=4, pady=4)
            if tp and tp.exists():
                ph = ImageTk.PhotoImage(Image.open(tp))
                photos.append(ph)
                b = tk.Button(cell, image=ph, relief="flat", bd=1, cursor="hand2",
                              command=lambda s=mf.stem: pick(s))
                b.pack()
            else:
                tk.Button(cell, text="(无缩略图)", width=28, height=6,
                          command=lambda s=mf.stem: pick(s)).pack()
            name = mf.stem
            if name == last.get("map"):
                name = "★ " + name
            ttk.Label(cell, text=name, wraplength=THUMB_W).pack()
            if i % 8 == 0:
                status.config(text=f"生成缩略图 {i+1}/{len(map_files)} …")
                root.update_idletasks()
        status.config(text=f"{len(map_files)} 张图。点缩略图开打。")

    root.after(50, build)
    root.mainloop()
    return result or None


if __name__ == "__main__":
    import json
    st = Path.home() / ".config/sc2mod/last.json"
    last = json.loads(st.read_text()) if st.exists() else {}
    print(choose(Path("/home/deck/Games/StarCraft II/Maps"), last))
