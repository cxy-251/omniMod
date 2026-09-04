#!/usr/bin/env python3
"""
一次性小工具：从游戏本体自带的 CASC 数据包里挖真实字段名/数值（而不是网上文档零星拼凑）。
用 lib/libcasc.so（CascLib，跟 lib/libstorm.so 同作者/同套路，在同一个 distrobox 里编的）。

用法：
  列出匹配某通配符的文件名：   uv run python casc_probe.py find "*UnitData*"
  导出某个文件到本地看内容：   uv run python casc_probe.py dump "<CascFindFirstFile 报出来的那个路径>" out.xml
"""
from __future__ import annotations

import ctypes
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
SC2_ROOT = Path("/home/deck/Games/StarCraft II")

CASC = ctypes.CDLL(str(HERE / "lib" / "libcasc.so"))

MAX_PATH = 1024
MD5_HASH_SIZE = 0x10


class CASC_FIND_DATA(ctypes.Structure):
    _fields_ = [
        ("szFileName", ctypes.c_char * MAX_PATH),
        ("CKey", ctypes.c_ubyte * MD5_HASH_SIZE),
        ("EKey", ctypes.c_ubyte * MD5_HASH_SIZE),
        ("TagBitMask", ctypes.c_uint64),
        ("FileSize", ctypes.c_uint64),
        ("szPlainName", ctypes.c_char_p),
        ("dwFileDataId", ctypes.c_uint32),
        ("dwLocaleFlags", ctypes.c_uint32),
        ("dwContentFlags", ctypes.c_uint32),
        ("dwSpanCount", ctypes.c_uint32),
        ("bFileAvailable", ctypes.c_uint32, 1),
        ("NameType", ctypes.c_uint32),
    ]


CASC.CascOpenStorage.argtypes = [ctypes.c_char_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
CASC.CascOpenStorage.restype = ctypes.c_bool
CASC.CascCloseStorage.argtypes = [ctypes.c_void_p]
CASC.CascFindFirstFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.POINTER(CASC_FIND_DATA), ctypes.c_char_p]
CASC.CascFindFirstFile.restype = ctypes.c_void_p
CASC.CascFindNextFile.argtypes = [ctypes.c_void_p, ctypes.POINTER(CASC_FIND_DATA)]
CASC.CascFindNextFile.restype = ctypes.c_bool
CASC.CascFindClose.argtypes = [ctypes.c_void_p]
CASC.CascOpenFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
CASC.CascOpenFile.restype = ctypes.c_bool
CASC.CascGetFileSize.argtypes = [ctypes.c_void_p, ctypes.c_void_p]
CASC.CascGetFileSize.restype = ctypes.c_uint32
CASC.CascReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_uint32)]
CASC.CascReadFile.restype = ctypes.c_bool
CASC.CascCloseFile.argtypes = [ctypes.c_void_p]


def open_storage() -> ctypes.c_void_p:
    h = ctypes.c_void_p()
    if not CASC.CascOpenStorage(str(SC2_ROOT).encode(), 0, ctypes.byref(h)):
        sys.exit(f"CascOpenStorage 失败（err={ctypes.get_errno()}）")
    return h


def find(mask: str) -> None:
    h = open_storage()
    fd = CASC_FIND_DATA()
    hfind = CASC.CascFindFirstFile(h, mask.encode(), ctypes.byref(fd), None)
    if not hfind:
        print("没找到匹配的文件")
        CASC.CascCloseStorage(h)
        return
    n = 0
    while True:
        print(fd.szFileName.decode(errors="replace"), f"({fd.FileSize} bytes)")
        n += 1
        if not CASC.CascFindNextFile(hfind, ctypes.byref(fd)):
            break
    CASC.CascFindClose(hfind)
    CASC.CascCloseStorage(h)
    print(f"共 {n} 个")


def dump(path: str, out: str) -> None:
    h = open_storage()
    hfile = ctypes.c_void_p()
    if not CASC.CascOpenFile(h, path.encode(), 0, 0, ctypes.byref(hfile)):
        sys.exit(f"CascOpenFile({path}) 失败")
    size = CASC.CascGetFileSize(hfile, None)
    buf = ctypes.create_string_buffer(size)
    read = ctypes.c_uint32(0)
    if not CASC.CascReadFile(hfile, buf, size, ctypes.byref(read)):
        sys.exit("CascReadFile 失败")
    Path(out).write_bytes(buf.raw[:read.value])
    CASC.CascCloseFile(hfile)
    CASC.CascCloseStorage(h)
    print(f"写到 {out}（{read.value} bytes）")


if __name__ == "__main__":
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    cmd = sys.argv[1]
    if cmd == "find":
        find(sys.argv[2])
    elif cmd == "dump":
        dump(sys.argv[2], sys.argv[3])
    else:
        sys.exit(f"未知命令: {cmd}")
