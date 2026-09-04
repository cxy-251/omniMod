#!/usr/bin/env python3
"""
生成一个"N 倍采集"mod —— 复制 mods/5xHarvest.SC2Mod 当模板（已验证能用、结构最简单），
把 BehaviorData.xml 里所有 HarvestAmount 按新倍数重算，改掉中/英文显示名，另存一份。

倍数换算用原版基础值（5xHarvest 里的值 ÷ 5 就是原版基础值，5xHarvest 已经验证过
这套换算是对的——25/5=5, 35/5=7, 20/5=4, 30/5=6，跟 SC2 原版矿/气单趟采集量一致）。

用法：
  uv run python make_harvest_mod.py 3 3xHarvest "3 倍采集" "矿 / 气全变体采集量 ×3（双方阵营对称生效，只改采集量，不改别的）"

产物：mods/<name>.SC2Mod，同时打印一行 bake.py 里 MOD_DEPS/MOD_INFO 要加的内容，手动填过去。
"""
from __future__ import annotations

import ctypes
import re
import shutil
import sys
import tempfile
from pathlib import Path

HERE = Path(__file__).resolve().parent
TEMPLATE = HERE / "mods" / "5xHarvest.SC2Mod"
_LIB = HERE / "lib" / "libstorm.so.9.30.0"
STORM = ctypes.CDLL(str(_LIB) if _LIB.exists() else "libstorm.so")

MPQ_FILE_REPLACEEXISTING = 0x80000000
MPQ_FILE_COMPRESS = 0x00000200
MPQ_COMPRESSION_ZLIB = 0x02

STORM.SFileOpenArchive.argtypes = [ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileOpenFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint, ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileGetFileSize.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint)]
STORM.SFileGetFileSize.restype = ctypes.c_uint
STORM.SFileReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                ctypes.POINTER(ctypes.c_uint), ctypes.c_void_p]
STORM.SFileCloseFile.argtypes = [ctypes.c_void_p]
STORM.SFileRemoveFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint]
STORM.SFileAddFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                                 ctypes.c_uint, ctypes.c_uint, ctypes.c_uint]
STORM.SFileCompactArchive.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
STORM.SFileCloseArchive.argtypes = [ctypes.c_void_p]


def read_mpq_file(h_mpq, name: str) -> bytes:
    h_file = ctypes.c_void_p()
    if not STORM.SFileOpenFileEx(h_mpq, name.encode(), 0, ctypes.byref(h_file)):
        raise RuntimeError(f"打不开 {name}")
    size = STORM.SFileGetFileSize(h_file, None)
    buf = ctypes.create_string_buffer(size)
    read = ctypes.c_uint(0)
    STORM.SFileReadFile(h_file, buf, size, ctypes.byref(read), None)
    STORM.SFileCloseFile(h_file)
    return buf.raw[:read.value]


def write_mpq_file(h_mpq, name: str, data: bytes) -> None:
    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin") as tf:
        tf.write(data)
        tmp = tf.name
    STORM.SFileRemoveFile(h_mpq, name.encode(), 0)
    ok = STORM.SFileAddFileEx(h_mpq, tmp.encode(), name.encode(),
                              MPQ_FILE_REPLACEEXISTING | MPQ_FILE_COMPRESS,
                              MPQ_COMPRESSION_ZLIB, MPQ_COMPRESSION_ZLIB)
    Path(tmp).unlink(missing_ok=True)
    if not ok:
        raise RuntimeError(f"写 {name} 失败")


def rescale_behavior_xml(xml: str, multiplier: int) -> str:
    """HarvestAmount value="25" -> 原版基础值(25/5) * multiplier，四舍五入取整。"""
    def repl(m: re.Match) -> str:
        base5x = int(m.group(1))
        base = base5x / 5
        new = round(base * multiplier)
        return f'<HarvestAmount value="{new}"/>'
    return re.sub(r'<HarvestAmount value="(\d+)"/>', repl, xml)


def main() -> None:
    if len(sys.argv) != 5:
        sys.exit(f"用法: {sys.argv[0]} <倍数> <mod文件名(不带.SC2Mod)> <中文显示名> <中文描述>")
    multiplier = int(sys.argv[1])
    out_name = sys.argv[2]
    display_name = sys.argv[3]
    desc = sys.argv[4]

    dst = HERE / "mods" / f"{out_name}.SC2Mod"
    if not TEMPLATE.exists():
        sys.exit(f"模板不存在: {TEMPLATE}")
    shutil.copyfile(TEMPLATE, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst}")

    behavior_path = "Base.SC2Data\\GameData\\BehaviorData.xml"
    xml = read_mpq_file(h_mpq, behavior_path).decode("utf-8")
    new_xml = rescale_behavior_xml(xml, multiplier)
    write_mpq_file(h_mpq, behavior_path, new_xml.encode("utf-8"))

    write_mpq_file(h_mpq, "enUS.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name={multiplier}x Harvest Rate\n"
                   f"DocInfo/Desc={multiplier}x mineral and gas harvesting rate for all players and AI!\n".encode())
    write_mpq_file(h_mpq, "zhCN.SC2Data\\LocalizedData\\GameStrings.txt",
                   f"DocInfo/Name={display_name}\n"
                   f"DocInfo/Desc={desc}\n".encode())

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)

    print(f"OK: {dst}")
    print()
    print("把下面两条分别加进 bake.py 的 MOD_DEPS 和 MOD_INFO：")
    print(f'    "{out_name}": "bnet:{out_name}/0.0/999,file:Mods/{out_name}.SC2Mod",')
    print(f'    "{out_name}": {{"name": "{display_name}", "desc": "{desc}"}},')


if __name__ == "__main__":
    main()
