"""Acceptance gate for real Linux Release dump symbolication; no network URLs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import struct
import tempfile
import uuid


class GateError(Exception):
    pass


def require(condition, message):
    if not condition:
        raise GateError(message)


def native_run(arguments, timeout):
    return subprocess.run(arguments, check=True, capture_output=True, timeout=timeout,
                          env={"PATH": os.environ["PATH"], "HOME": "/tmp", "LANG": "C.UTF-8"})


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def u32(data, offset):
    return struct.unpack_from("<I", data, offset)[0]


def debug_id(codeview):
    require(codeview[:4] == b"RSDS", "missing RSDS CodeView identity")
    return uuid.UUID(bytes_le=codeview[4:20]).hex.upper() + format(u32(codeview, 20), "X")


def dump_identity(path):
    data = path.read_bytes()
    require(data[:4] == b"MDMP", "not a minidump")
    for i in range(u32(data, 8)):
        kind, _, rva = struct.unpack_from("<III", data, u32(data, 12) + 12 * i)
        if kind != 4:
            continue
        for j in range(u32(data, rva)):
            offset = rva + 4 + j * 108
            name = u32(data, offset + 20)
            name = data[name + 4:name + 4 + u32(data, name)].decode("utf-16le")
            if name.replace("\\", "/").split("/")[-1].lower() == "runtime.exe":
                size, address = struct.unpack_from("<II", data, offset + 76)
                return {"debugId": debug_id(data[address:address + size]),
                        "timestamp": format(u32(data, offset + 16), "08X"),
                        "imageSize": format(u32(data, offset + 8), "x")}
    raise GateError("runtime module missing from dump")


def pair_identity(pair):
    data = (pair / "runtime.exe").read_bytes()
    require(data[:2] == b"MZ", "invalid PE")
    pe = u32(data, 60)
    require(data[pe:pe + 4] == b"PE\0\0", "invalid PE header")
    require(struct.unpack_from("<H", data, pe + 4)[0] == 0x8664, "PE is not x64")
    optional = pe + 24
    require(struct.unpack_from("<H", data, optional)[0] == 0x20b, "PE is not PE32+")
    sections = optional + struct.unpack_from("<H", data, pe + 20)[0]

    def file_offset(rva):
        for i in range(struct.unpack_from("<H", data, pe + 6)[0]):
            size, address, raw, pointer = struct.unpack_from("<IIII", data, sections + i * 40 + 8)
            if address <= rva < address + max(size, raw):
                return pointer + rva - address
        raise GateError("invalid PE RVA")

    debug_rva, debug_size = struct.unpack_from("<II", data, optional + 112 + 6 * 8)
    identity = None
    for offset in range(file_offset(debug_rva), file_offset(debug_rva) + debug_size, 28):
        if u32(data, offset + 12) == 2:
            pointer = u32(data, offset + 24)
            identity = debug_id(data[pointer:pointer + u32(data, offset + 16)])
    require(identity, "PE has no debug identity")
    pdb = (pair / "runtime.pdb").read_bytes()
    require(pdb.startswith(b"Microsoft C/C++ MSF 7.00"), "PDB is not MSF 7")
    block_size, _, _, directory_size, _, block_map = struct.unpack_from("<IIIIII", pdb, 32)
    count = (directory_size + block_size - 1) // block_size
    blocks = struct.unpack_from("<" + "I" * count, pdb, block_map * block_size)
    directory = b"".join(pdb[b * block_size:(b + 1) * block_size] for b in blocks)[:directory_size]
    stream_count = u32(directory, 0)
    cursor = 4 + 4 * stream_count
    info = None
    for i in range(stream_count):
        size = u32(directory, 4 + 4 * i)
        count = 0 if size == 0xffffffff else (size + block_size - 1) // block_size
        blocks = struct.unpack_from("<" + "I" * count, directory, cursor)
        if i == 1:
            info = b"".join(pdb[b * block_size:(b + 1) * block_size] for b in blocks)[:size]
        cursor += count * 4
    pdb_id = uuid.UUID(bytes_le=info[12:28]).hex.upper() + format(u32(info, 8), "X")
    require(pdb_id == identity, "PE/PDB debug identity mismatch")
    return {"debugId": identity, "timestamp": format(u32(data, pe + 8), "08X"),
            "imageSize": format(u32(data, optional + 56), "x")}


def source_gate(report, expected_crash):
    crash = report.get("crash_info", {})
    require(crash.get("type") == expected_crash["reason"], "wrong fault reason")
    index = crash.get("crashing_thread")
    require(index == expected_crash["threadIndex"], "wrong crashing thread index")
    require(report["threads"][index]["thread_id"] == expected_crash["threadId"], "wrong fault thread ID")
    frames = report["threads"][index]["frames"]
    for name in ("Application.cpp", "main.cpp"):
        require(any(Path((f.get("file") or "").replace("\\", "/")).name == name
                    and (f.get("line") or 0) > 0 for f in frames), "source-resolution gate failed: " + name)
    require(any(f.get("trust") == "cfi" for f in frames), "no CFI-recovered crashing-thread frame")
    require(any(f.get("inlines") for f in frames), "inline source data was lost")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", required=True, type=Path)
    parser.add_argument("--private-pair", type=Path)
    args = parser.parse_args()
    require(os.name == "posix", "Linux tools required (run the native Docker image)")
    for tool in ("dump_syms", "minidump-stackwalk"):
        require(shutil.which(tool), "missing prerequisite: " + tool)
    lock = json.loads((Path(__file__).resolve().parents[2] /
                       "src/Server/Avalon.Crashes.Worker/native/tools.lock.json").read_text())
    for tool in lock["tools"]:
        require(digest(Path(shutil.which(tool["name"]))) == tool["binarySha256"], "tool digest mismatch")
        version = native_run([tool["name"], "--version"], 10)
        require(version.stdout.decode().strip() == tool["name"] + " " + tool["version"], "tool version mismatch")
    for name in ("dump.dmp", "runtime.sym", "provenance.json"):
        require((args.fixture / name).is_file(), "missing fixture: " + name)
    provenance = json.loads((args.fixture / "provenance.json").read_text())
    for name in ("dump.dmp", "runtime.sym"):
        require(digest(args.fixture / name) == provenance["sha256"][name], "fixture digest mismatch: " + name)
    require(dump_identity(args.fixture / "dump.dmp") == provenance["identity"], "dump identity mismatch")
    text = (args.fixture / "runtime.sym").read_text()
    expected = provenance["module"]
    require(expected == "MODULE windows x86_64 " + provenance["identity"]["debugId"] + " runtime.pdb",
            "MODULE does not match dump debug identity")
    require(text.splitlines()[0] == expected, "MODULE identity mismatch")
    require("\nSTACK CFI INIT " in text, "missing unwind records")
    require("\nFUNC " in text, "missing functions")
    require("\nINLINE " in text, "missing inline records")
    with tempfile.TemporaryDirectory(prefix="avalon-native-") as temp:
        symbols = Path(temp) / "symbols"
        _, _, _, debug_id, debug_file = expected.split()
        target = symbols / debug_file / debug_id / "runtime.sym"
        target.parent.mkdir(parents=True)
        target.write_text(text)
        if args.private_pair:
            require(pair_identity(args.private_pair) == provenance["identity"], "PE/PDB/dump identity mismatch")
            for name in ("runtime.exe", "runtime.pdb"):
                require(digest(args.private_pair / name) == provenance["sha256"][name], "private pair digest mismatch")
            converted = Path(temp) / "converted.sym"
            native_run(["dump_syms", "--inlines", "--check-cfi", "-o", str(converted),
                        str(args.private_pair / "runtime.pdb")], 120)
            generated = converted.read_text()
            require(generated.splitlines()[0] == expected, "converted MODULE identity mismatch")
            require("\nSTACK CFI INIT " in generated, "converted symbols lack unwind records")
            require("\nINLINE " in generated, "converted symbols lack inline records")
            target.write_text(generated)
        result = native_run(["minidump-stackwalk", "--json", str(args.fixture / "dump.dmp"), str(symbols)], 60)
        source_gate(json.loads(result.stdout), provenance["crash"])
        target.write_text(text)
        cached = native_run(["minidump-stackwalk", "--json", str(args.fixture / "dump.dmp"), str(symbols)], 60)
        source_gate(json.loads(cached.stdout), provenance["crash"])
        # Real wrong symbols: keep the correct MODULE identity, remove all source records.
        target.write_text(expected + "\n")
        wrong = native_run(["minidump-stackwalk", "--json", str(args.fixture / "dump.dmp"), str(symbols)], 60)
        try:
            source_gate(json.loads(wrong.stdout), provenance["crash"])
        except GateError:
            pass
        else:
            raise GateError("wrong-symbol source-resolution gate unexpectedly passed")
    print("PASS: MODULE, unwind, fault/thread, Application.cpp/main.cpp lines, CFI and wrong-symbol rejection")


if __name__ == "__main__":
    try:
        main()
    except (GateError, ValueError, OSError, subprocess.SubprocessError, KeyError, IndexError, struct.error) as error:
        # Do not echo native diagnostics, paths, registers, memory or dump strings.
        print("FAIL: " + (str(error) if isinstance(error, GateError) else type(error).__name__))
        raise SystemExit(1)
