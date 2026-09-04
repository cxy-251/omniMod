#!/usr/bin/env python3
"""
把一份 .SC2Map 复制出来，往它的 DocumentHeader 里加若干 mod 依赖，写回 MPQ。

  python3 _bake_inner.py <src.SC2Map> <dst.SC2Map> <dep1> [dep2 ...]

<depN> 形如： bnet:5xHarvest/0.0/999,file:Mods/5xHarvest.SC2Mod

StormLib 用随仓库带的 lib/libstorm.so（在 distrobox arch 容器里编好、宿主可直接加载）。
需要重编时进容器：见 RECON.md。
"""
import ctypes
import shutil
import struct
import sys
import tempfile
from pathlib import Path

_LIB = Path(__file__).resolve().parent / "lib" / "libstorm.so.9.30.0"
STORM = ctypes.CDLL(str(_LIB) if _LIB.exists() else "libstorm.so")

MPQ_FILE_REPLACEEXISTING = 0x80000000
MPQ_FILE_COMPRESS = 0x00000200
MPQ_COMPRESSION_ZLIB = 0x02
SFILE_INVALID_SIZE = 0xFFFFFFFF

STORM.SFileOpenArchive.argtypes = [ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                                   ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileOpenFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint,
                                  ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileGetFileSize.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint)]
STORM.SFileGetFileSize.restype = ctypes.c_uint   # 返回值 = 低 32 位大小
STORM.SFileReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                ctypes.POINTER(ctypes.c_uint), ctypes.c_void_p]
STORM.SFileCloseFile.argtypes = [ctypes.c_void_p]
STORM.SFileRemoveFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint]
STORM.SFileAddFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_char_p,
                                 ctypes.c_uint, ctypes.c_uint, ctypes.c_uint]
STORM.SFileCompactArchive.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_int]
STORM.SFileCloseArchive.argtypes = [ctypes.c_void_p]


def read_file_from_mpq(h_mpq, name: str) -> bytes:
    h_file = ctypes.c_void_p()
    if not STORM.SFileOpenFileEx(h_mpq, name.encode(), 0, ctypes.byref(h_file)):
        raise RuntimeError(f"打不开 {name}")
    high = ctypes.c_uint(0)
    size = STORM.SFileGetFileSize(h_file, ctypes.byref(high))
    if size in (0, SFILE_INVALID_SIZE):
        STORM.SFileCloseFile(h_file)
        raise RuntimeError(f"{name} 大小异常: {size}")
    buf = ctypes.create_string_buffer(size)
    read = ctypes.c_uint(0)
    STORM.SFileReadFile(h_file, buf, size, ctypes.byref(read), None)
    STORM.SFileCloseFile(h_file)
    return buf.raw[:read.value]


def patch_document_header(hdr: bytes, new_deps: list[str]) -> bytes:
    assert hdr[:4] == b"H2CS", "不是 SC2 DocumentHeader"
    dep_start = hdr.find(b"bnet:", 0, 512)
    if dep_start < 0:
        # 没有 bnet: 前缀的依赖？找 file:Mods/
        dep_start = hdr.find(b"file:Mods/", 0, 512)
    if dep_start < 4:
        raise RuntimeError("DocumentHeader 里找不到依赖块")
    count_off = dep_start - 4
    (count,) = struct.unpack_from("<I", hdr, count_off)
    if not 0 < count < 64:
        raise RuntimeError(f"依赖数不合理: {count}")

    # 走过 count 个以 \0 结尾的字符串
    pos = dep_start
    for _ in range(count):
        end = hdr.index(b"\x00", pos)
        pos = end + 1
    dep_end = pos  # 最后一个依赖的 \0 之后

    add = b"".join(d.encode() + b"\x00" for d in new_deps)
    out = (hdr[:count_off]
           + struct.pack("<I", count + len(new_deps))
           + hdr[dep_start:dep_end]
           + add
           + hdr[dep_end:])
    return out


def main() -> None:
    src, dst, *deps = sys.argv[1:]
    if not deps:
        sys.exit("要至少一个依赖串")
    src, dst = Path(src), Path(dst)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst} (err {ctypes.get_errno()})")

    hdr = read_file_from_mpq(h_mpq, "DocumentHeader")
    new_hdr = patch_document_header(hdr, deps)

    with tempfile.NamedTemporaryFile(delete=False, suffix=".bin") as tf:
        tf.write(new_hdr)
        tmp = tf.name

    STORM.SFileRemoveFile(h_mpq, b"DocumentHeader", 0)
    ok = STORM.SFileAddFileEx(h_mpq, tmp.encode(), b"DocumentHeader",
                              MPQ_FILE_REPLACEEXISTING | MPQ_FILE_COMPRESS,
                              MPQ_COMPRESSION_ZLIB, MPQ_COMPRESSION_ZLIB)
    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)
    Path(tmp).unlink(missing_ok=True)

    if not ok:
        sys.exit("SFileAddFileEx 失败")
    print(f"OK: {dst}  (+{len(deps)} 依赖, DocumentHeader {len(hdr)}->{len(new_hdr)})")


if __name__ == "__main__":
    main()
