#!/usr/bin/env python3
"""
PyQt6 选图器：地形缩略图网格 + 顶部设置。点一张图 = 用当前设置开打。
被 play.py 调用。
"""
from __future__ import annotations

import sys
from pathlib import Path

from PyQt6 import QtCore, QtGui, QtWidgets

from picker import extract_thumb  # 复用：从 .SC2Map 抽 Minimap.tga

RACES = [("R", "随机"), ("T", "人族"), ("P", "神族"), ("Z", "虫族")]
DIFFS = [
    ("cheatinsane", "残酷3 (作弊-疯狂)"),
    ("cheatmoney", "作弊-资源"),
    ("cheatvision", "作弊-视野"),
    ("veryhard", "非常难"),
    ("harder", "较难"),
    ("hard", "困难"),
    ("medium", "中等"),
]

QSS = """
* { font-size: 14px; }
QWidget { background: #14171c; color: #e6e9ef; }
QScrollArea, QScrollArea > QWidget > QWidget { background: #14171c; }
QComboBox, QPushButton {
    background: #232833; border: 1px solid #333a47; border-radius: 6px; padding: 6px 12px;
}
QPushButton:hover, QComboBox:hover { border-color: #4c86ff; }
QPushButton:checked { background: #2b5cff; border-color: #2b5cff; color: white; }
QCheckBox { padding: 4px; }
#mapCard { background: #1b1f27; border: 2px solid transparent; border-radius: 10px; }
#mapCard:hover { border-color: #4c86ff; background: #20252f; }
#mapName { color: #b8c0cf; }
#hint { color: #7c8698; }
"""


class RaceRow(QtWidgets.QWidget):
    def __init__(self, label: str, default: str):
        super().__init__()
        lay = QtWidgets.QHBoxLayout(self)
        lay.setContentsMargins(0, 0, 0, 0)
        lay.addWidget(QtWidgets.QLabel(label))
        self.group = QtWidgets.QButtonGroup(self)
        for key, name in RACES:
            b = QtWidgets.QPushButton(name)
            b.setCheckable(True)
            b.setProperty("key", key)
            if key == default:
                b.setChecked(True)
            self.group.addButton(b)
            lay.addWidget(b)
        lay.addStretch(1)

    def value(self) -> str:
        b = self.group.checkedButton()
        return b.property("key") if b else "R"


class MapCard(QtWidgets.QFrame):
    clicked = QtCore.pyqtSignal(str)

    def __init__(self, stem: str, thumb: Path | None, is_last: bool):
        super().__init__()
        self.setObjectName("mapCard")
        self.stem = stem
        self.setCursor(QtCore.Qt.CursorShape.PointingHandCursor)
        v = QtWidgets.QVBoxLayout(self)
        v.setContentsMargins(8, 8, 8, 8)
        img = QtWidgets.QLabel()
        img.setAlignment(QtCore.Qt.AlignmentFlag.AlignCenter)
        if thumb and thumb.exists():
            pix = QtGui.QPixmap(str(thumb)).scaledToWidth(
                240, QtCore.Qt.TransformationMode.SmoothTransformation)
            img.setPixmap(pix)
        else:
            img.setText("(无缩略图)")
            img.setFixedHeight(120)
        v.addWidget(img)
        name = QtWidgets.QLabel(("★ " if is_last else "") + stem)
        name.setObjectName("mapName")
        name.setAlignment(QtCore.Qt.AlignmentFlag.AlignCenter)
        name.setWordWrap(True)
        v.addWidget(name)

    def mousePressEvent(self, e):  # noqa: N802
        self.clicked.emit(self.stem)


class Picker(QtWidgets.QWidget):
    def __init__(self, maps_dir: Path, last: dict):
        super().__init__()
        self.result: dict | None = None
        self.setWindowTitle("sc2Mod — 选图开打")
        self.resize(1120, 800)
        self.setStyleSheet(QSS)

        root = QtWidgets.QVBoxLayout(self)

        # 顶部设置
        top = QtWidgets.QGridLayout()
        self.my = RaceRow("你的种族", last.get("race", "P"))
        self.enemy = RaceRow("电脑种族", last.get("enemy_race", "P"))
        top.addWidget(self.my, 0, 0)
        top.addWidget(self.enemy, 1, 0)

        rc = QtWidgets.QHBoxLayout()
        rc.addWidget(QtWidgets.QLabel("难度"))
        self.diff = QtWidgets.QComboBox()
        for k, name in DIFFS:
            self.diff.addItem(name, k)
        i = self.diff.findData(last.get("difficulty", "cheatinsane"))
        self.diff.setCurrentIndex(max(0, i))
        rc.addWidget(self.diff)
        rc.addSpacing(20)
        self.cheat = QtWidgets.QCheckBox("5 倍采集 mod（需已烘焙到地图）")
        self.cheat.setChecked(bool(last.get("cheat", False)))
        rc.addWidget(self.cheat)
        rc.addStretch(1)
        w = QtWidgets.QWidget()
        w.setLayout(rc)
        top.addWidget(w, 2, 0)
        root.addLayout(top)

        hint = QtWidgets.QLabel("↓ 点地图缩略图直接开打（上次的图带 ★）")
        hint.setObjectName("hint")
        root.addWidget(hint)

        # 地图网格
        scroll = QtWidgets.QScrollArea()
        scroll.setWidgetResizable(True)
        grid_host = QtWidgets.QWidget()
        grid = QtWidgets.QGridLayout(grid_host)
        grid.setSpacing(10)
        maps = sorted(maps_dir.glob("*.SC2Map"))
        cols = 4
        for idx, mf in enumerate(maps):
            card = MapCard(mf.stem, extract_thumb(mf), mf.stem == last.get("map"))
            card.clicked.connect(self._pick)
            grid.addWidget(card, idx // cols, idx % cols)
        scroll.setWidget(grid_host)
        root.addWidget(scroll, 1)

    def _pick(self, stem: str):
        self.result = {
            "map": stem,
            "race": self.my.value(),
            "enemy_race": self.enemy.value(),
            "difficulty": self.diff.currentData(),
            "cheat": self.cheat.isChecked(),
            "ai_build": "random",
        }
        self.close()


def choose(maps_dir: Path, last: dict) -> dict | None:
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication(sys.argv)
    p = Picker(maps_dir, last)
    p.show()
    app.exec()
    return p.result


def after_game(result_text: str) -> str:
    """返回 'again' / 'change' / 'quit'。"""
    app = QtWidgets.QApplication.instance() or QtWidgets.QApplication(sys.argv)
    box = QtWidgets.QMessageBox()
    box.setStyleSheet(QSS)
    box.setWindowTitle("这局结束")
    box.setText(f"结果：{result_text}")
    again = box.addButton("再来一局（同设置）", QtWidgets.QMessageBox.ButtonRole.AcceptRole)
    change = box.addButton("换地图 / 设置", QtWidgets.QMessageBox.ButtonRole.ActionRole)
    box.addButton("退出", QtWidgets.QMessageBox.ButtonRole.RejectRole)
    box.exec()
    c = box.clickedButton()
    return "again" if c is again else "change" if c is change else "quit"


if __name__ == "__main__":
    import json
    st = Path.home() / ".config/sc2mod/last.json"
    last = json.loads(st.read_text()) if st.exists() else {}
    print(choose(Path("/home/deck/Games/StarCraft II/Maps"), last))
