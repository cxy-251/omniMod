#!/usr/bin/env python3
r"""
把一份 .SC2Map 复制出来，往副本里注入 mod 内容，写回 MPQ。

  python3 _bake_inner.py <src.SC2Map> <dst.SC2Map> \
      [--dep <depstring>]... \
      [--merge <GameDataFile>=<fragment.xml>]...

  --dep <depstring>
      往 DocumentHeader 依赖表加一条扩展 mod 依赖，形如
      bnet:5xHarvest/0.0/123,file:Mods/5xHarvest.SC2Mod
      （3x/5x 采集走这条——它们改的 HarvestAmount 是每趟实时读目录的，扩展 mod 够用）

  --merge <GameDataFile>=<fragment.xml>
      把一段目录片段按 id 焊进地图自己的 Base.SC2Data\GameData\<GameDataFile>。
      片段里每个顶层 <C... id="X"> 条目：地图目录里已有同 id 的就整块换掉，没有就追加。
      （infiniteRes 焊进 BehaviorData.xml；instantBuild/macroSpeed 焊进 AbilData.xml。
       原因：AIE 这类"整套数据"地图自带 abildata/behaviordata，扩展 mod 依赖会被地图
       自己的目录压过去 / 同文件里同 id 是"先出现的赢"，只有把改动焊进地图目录、
       并且把地图原来的同 id 条目换掉，才真正生效。）

至少要一个 --dep 或一个 --merge。

StormLib 用随仓库带的 lib/libstorm.so（在 distrobox arch 容器里编好、宿主可直接加载）。
需要重编时进容器：见 RECON.md。
"""
import ctypes
import re
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

GAMEDATA_DIR = "Base.SC2Data\\GameData\\"

STORM.SFileOpenArchive.argtypes = [ctypes.c_char_p, ctypes.c_uint, ctypes.c_uint,
                                   ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileOpenFileEx.argtypes = [ctypes.c_void_p, ctypes.c_char_p, ctypes.c_uint,
                                  ctypes.POINTER(ctypes.c_void_p)]
STORM.SFileGetFileSize.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_uint)]
STORM.SFileGetFileSize.restype = ctypes.c_uint   # 返回值 = 低 32 位大小
STORM.SFileReadFile.argtypes = [ctypes.c_void_p, ctypes.c_void_p, ctypes.c_uint,
                                ctypes.POINTER(ctypes.c_uint), ctypes.c_void_p]
STORM.SFileCloseFile.argtypes = [ctypes.c_void_p]
STORM.SFileHasFile.argtypes = [ctypes.c_void_p, ctypes.c_char_p]
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


def try_read(h_mpq, name: str) -> bytes | None:
    if not STORM.SFileHasFile(h_mpq, name.encode()):
        return None
    try:
        return read_file_from_mpq(h_mpq, name)
    except RuntimeError:
        return None


def write_file_to_mpq(h_mpq, name: str, data: bytes) -> None:
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


def patch_document_header(hdr: bytes, new_deps: list[str]) -> bytes:
    assert hdr[:4] == b"H2CS", "不是 SC2 DocumentHeader"
    dep_start = hdr.find(b"bnet:", 0, 512)
    if dep_start < 0:
        dep_start = hdr.find(b"file:Mods/", 0, 512)
    if dep_start < 4:
        raise RuntimeError("DocumentHeader 里找不到依赖块")
    count_off = dep_start - 4
    (count,) = struct.unpack_from("<I", hdr, count_off)
    if not 0 < count < 64:
        raise RuntimeError(f"依赖数不合理: {count}")
    pos = dep_start
    for _ in range(count):
        end = hdr.index(b"\x00", pos)
        pos = end + 1
    dep_end = pos
    add = b"".join(d.encode() + b"\x00" for d in new_deps)
    return (hdr[:count_off]
            + struct.pack("<I", count + len(new_deps))
            + hdr[dep_start:dep_end]
            + add
            + hdr[dep_end:])


# --- 目录片段合并 ------------------------------------------------------------

_ENTRY_RE = re.compile(r'<(C[A-Za-z]\w*)\b[^>]*?\bid="([^"]+)"', re.S)


def _fragment_ids(fragment_text: str) -> list[str]:
    """片段里所有顶层 <C... id="X">，按出现顺序去重。"""
    seen, out = set(), []
    for m in _ENTRY_RE.finditer(fragment_text):
        i = m.group(2)
        if i not in seen:
            seen.add(i)
            out.append(i)
    return out


def _strip_entry(text: str, ent_id: str) -> tuple[str, int]:
    """把 text 里所有 id="ent_id" 的顶层 <C...>…</C...> / 自闭合块删掉。返回 (新文本, 删了几个)。"""
    n = 0
    # 成对标签
    pair = re.compile(
        r'[ \t]*<(C[A-Za-z]\w*)\b[^>]*?\bid="' + re.escape(ent_id) + r'"[^>]*?>.*?</\1>\s*\n?',
        re.S)
    text, k = pair.subn("", text)
    n += k
    # 自闭合
    selfc = re.compile(
        r'[ \t]*<C[A-Za-z]\w*\b[^>]*?\bid="' + re.escape(ent_id) + r'"[^>]*?/>\s*\n?',
        re.S)
    text, k = selfc.subn("", text)
    n += k
    return text, n


def _entries_of(catalog_xml: str) -> str:
    m = re.search(r"<Catalog>(.*)</Catalog>", catalog_xml, re.S)
    return (m.group(1) if m else catalog_xml).strip("\n")


def _resolve_gamedata_name(h_mpq, filename: str) -> tuple[str, bytes | None]:
    """在地图里按大小写不敏感找 GameData 目录下的文件，返回 (实际用的名字, 内容或 None)。"""
    for cand in (GAMEDATA_DIR + filename,
                 GAMEDATA_DIR + filename.lower(),
                 GAMEDATA_DIR + filename[0].lower() + filename[1:]):
        d = try_read(h_mpq, cand)
        if d is not None:
            return cand, d
    return GAMEDATA_DIR + filename, None


def merge_catalog(h_mpq, filename: str, fragment_path: Path) -> tuple[int, int, int]:
    """把 fragment_path 的目录条目按 id 焊进地图的 GameData/<filename>。
    返回 (旧字节, 新字节, 换掉的旧条目数)。"""
    frag_raw = fragment_path.read_text(encoding="utf-8")
    frag_body = _entries_of(frag_raw)
    ids = _fragment_ids(frag_body)

    name, existing = _resolve_gamedata_name(h_mpq, filename)
    old_len = len(existing) if existing else 0
    replaced = 0

    if existing:
        text = existing.decode("utf-8", "replace")
        for i in ids:
            text, k = _strip_entry(text, i)
            replaced += k
        block = f"    <!-- baked: {fragment_path.name} -->\n{frag_body}\n"
        if "</Catalog>" in text:
            new_text = text.replace("</Catalog>", block + "</Catalog>", 1)
        else:
            new_text = ('<?xml version="1.0" encoding="utf-8"?>\n<Catalog>\n'
                        + text.strip() + "\n" + block + "</Catalog>\n")
    else:
        new_text = ('<?xml version="1.0" encoding="utf-8"?>\n<Catalog>\n'
                    f"    <!-- baked: {fragment_path.name} -->\n{frag_body}\n</Catalog>\n")

    data = new_text.encode("utf-8")
    write_file_to_mpq(h_mpq, name, data)
    _ensure_in_listfile(h_mpq, name)
    return old_len, len(data), replaced


def _ensure_in_listfile(h_mpq, name: str) -> None:
    lf = try_read(h_mpq, "(listfile)")
    if lf is None:
        return
    lines = lf.decode("utf-8", "replace").replace("\r\n", "\n").split("\n")
    if name.lower() in {ln.strip().lower() for ln in lines}:
        return
    lines = [ln for ln in lines if ln.strip()]
    lines.append(name)
    write_file_to_mpq(h_mpq, "(listfile)", ("\r\n".join(lines) + "\r\n").encode("utf-8"))


# --- CLI -------------------------------------------------------------------

def parse_args(argv: list[str]):
    if len(argv) < 2:
        sys.exit(__doc__)
    src, dst = argv[0], argv[1]
    deps: list[str] = []
    merges: list[tuple[str, Path]] = []
    i = 2
    while i < len(argv):
        a = argv[i]
        if a == "--dep":
            deps.append(argv[i + 1]); i += 2
        elif a == "--merge":
            spec = argv[i + 1]; i += 2
            fn, _, frag = spec.partition("=")
            merges.append((fn, Path(frag)))
        else:  # 向后兼容：裸参数当依赖串
            deps.append(a); i += 1
    if not deps and not merges:
        sys.exit("要至少一个 --dep 或 --merge")
    return src, dst, deps, merges


def main() -> None:
    src_s, dst_s, deps, merges = parse_args(sys.argv[1:])
    src, dst = Path(src_s), Path(dst_s)
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(src, dst)

    h_mpq = ctypes.c_void_p()
    if not STORM.SFileOpenArchive(str(dst).encode(), 0, 0, ctypes.byref(h_mpq)):
        sys.exit(f"打不开 MPQ {dst} (err {ctypes.get_errno()})")

    msg = []
    if deps:
        hdr = read_file_from_mpq(h_mpq, "DocumentHeader")
        new_hdr = patch_document_header(hdr, deps)
        write_file_to_mpq(h_mpq, "DocumentHeader", new_hdr)
        msg.append(f"+{len(deps)} 依赖 ({len(hdr)}->{len(new_hdr)}B)")

    for fn, frag in merges:
        o, n, rep = merge_catalog(h_mpq, fn, frag)
        msg.append(f"{fn}: {o}->{n}B, 换掉 {rep} 条")

    STORM.SFileCompactArchive(h_mpq, None, 0)
    STORM.SFileCloseArchive(h_mpq)
    print(f"OK: {dst}  ({'; '.join(msg)})")


if __name__ == "__main__":
    main()
