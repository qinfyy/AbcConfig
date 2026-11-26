"""Build a Config contract package from the ABC Windows 0.75.2 SDK dump."""
import argparse
import base64
import json
import re
import sys
from collections import deque
from pathlib import Path

from raw_config import read_raw_codecs
from text_map import read_text_map

APP_VERSION = "0.75.2"
REGISTER_FUNCTIONS = ((0x4DCFD00, 603), (0x4DDADA0, 1))
DICTIONARY_INSERT = 0x7C3DDF0
SINGLETON_REGISTER = 0x49E8B40
# The Config decrypt wrapper references this literal from RVA 0x04C16AB2.
MANIFEST_SEED_LITERAL = 0x18231F980


def read_types(image, index):
    from capstone import x86

    pointers = {
        int(item["virtualAddress"], 16): item["type"]
        for item in index["addressMap"]["typeInfoPointers"]
    }
    result = {}
    for rva, expected in REGISTER_FUNCTIONS:
        start, end = image.bounds(image.base + rva)
        if start != image.base + rva:
            raise ValueError(f"Registration function not found at RVA {rva:#x}")
        count = 0
        pointer = type_id = None
        preceding = deque(maxlen=3)
        for ins in image.instructions(start, end):
            operands = ins.operands
            if ins.mnemonic == "mov" and len(operands) == 2 and operands[0].type == x86.X86_OP_REG:
                dest, src = operands
                if dest.reg == x86.X86_REG_RSI:
                    pointer = None
                    if src.type == x86.X86_OP_MEM and src.mem.base == x86.X86_REG_RIP:
                        pointer = ins.address + ins.size + src.mem.disp
                elif dest.reg == x86.X86_REG_R8D and src.type == x86.X86_OP_IMM:
                    type_id = src.imm & 0xffffffff
            if ins.mnemonic == "call" and operands[0].type == x86.X86_OP_IMM:
                target = operands[0].imm - image.base
                if target == DICTIONARY_INSERT:
                    add_registration(result, pointers, pointer, type_id, ins.address)
                    count += 1
                    pointer = type_id = None
                elif target == SINGLETON_REGISTER:
                    load, key, flag = preceding
                    if not (load.mnemonic == key.mnemonic == flag.mnemonic == "mov"
                            and load.operands[0].reg == x86.X86_REG_RCX
                            and load.operands[1].type == x86.X86_OP_MEM
                            and load.operands[1].mem.base == x86.X86_REG_RIP
                            and key.operands[0].reg == x86.X86_REG_EDX
                            and key.operands[1].type == x86.X86_OP_IMM
                            and flag.operands[0].reg == x86.X86_REG_R8B):
                        raise ValueError(f"Unexpected singleton registration at {ins.address:#x}")
                    address = load.address + load.size + load.operands[1].mem.disp
                    add_registration(result, pointers, address, key.operands[1].imm & 0xffffffff, ins.address)
                    count += 1
            preceding.append(ins)
        if count != expected:
            raise ValueError(f"RVA {rva:#x}: found {count} registrations, expected {expected}")
    return dict(sorted(result.items()))


def add_registration(result, pointers, pointer, type_id, address):
    match = re.fullmatch(r"struct (.+)__Class \*", pointers.get(pointer, ""))
    if match is None or type_id is None or type_id in result:
        raise ValueError(f"Unrecognized or duplicate Config registration at {address:#x}")
    result[type_id] = (pointer, match[1])


def read_manifest_seed(index):
    for item in index["addressMap"]["stringLiterals"]:
        if int(item["virtualAddress"], 16) == MANIFEST_SEED_LITERAL:
            seed = base64.b64decode(item["string"], validate=True)
            if len(seed) != 32:
                raise ValueError("Unexpected Config manifest seed length")
            return base64.b64encode(seed).decode("ascii")
    raise ValueError(f"Config manifest seed was not found in the {APP_VERSION} SDK dump")


def resolve_index(dump):
    if dump.is_file():
        return dump
    indices = [path for path in (dump / "il2cpp.json", dump / "Script_IDA" / "il2cpp.json") if path.is_file()]
    if len(indices) != 1:
        raise ValueError(f"Expected one SDK index under {dump}; specify --dump with the il2cpp.json path")
    return indices[0]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--game", type=Path, required=True)
    parser.add_argument("--build", required=True, help="Config ChangeList")
    parser.add_argument("--dump", type=Path, help="SDK dump directory or il2cpp.json (default: GAME/DUMP2)")
    parser.add_argument("--tools", type=Path, required=True, help="CyreneDumper tools directory")
    parser.add_argument("--out", type=Path, required=True, help="Empty contract output directory")
    args = parser.parse_args()
    if re.fullmatch(r"[0-9]+", args.build) is None:
        parser.error("--build must be a numeric ChangeList")
    game = args.game.resolve()
    output = args.out.resolve()
    if output == game or output.is_relative_to(game):
        parser.error("--out cannot be inside the game directory")
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        parser.error("--out must be an empty directory")
    dump = args.dump or game / "DUMP2"
    if not dump.is_file() and (output == dump.resolve() or output.is_relative_to(dump.resolve())):
        parser.error("--out cannot be inside the SDK dump directory")
    update = game / "NexusAnima_Data" / "PersistentAssets" / "Bin" / args.build
    if not (update / "ConfigLoadManifest.bin").is_file():
        parser.error(f"Config update package was not found: {update}")

    sys.path.insert(0, str(args.tools.resolve()))
    from native_probe import Image

    with resolve_index(dump).open(encoding="utf-8-sig") as source:
        index = json.load(source)
    image = Image(game / "GameAssembly.dll")
    try:
        registrations = read_types(image, index)
        raw_codecs = read_raw_codecs(image, registrations)
        text_map = read_text_map(image, registrations)
    finally:
        image.pe.close()
    contract = {
        "format": "abc-config-contracts-v1",
        "appVersion": APP_VERSION,
        "changeList": args.build,
        "config": {
            "manifestSeed": read_manifest_seed(index),
            "types": {f"0x{key:08X}": name for key, (_, name) in registrations.items()},
            "rawCodecs": {f"0x{key:08X}": codec for key, codec in raw_codecs.items()},
        },
        "textMap": text_map,
    }
    output.mkdir(parents=True, exist_ok=True)
    path = output / "contracts.json"
    with path.open("x", encoding="utf-8", newline="\n") as target:
        json.dump(contract, target, indent=2)
        target.write("\n")
    print(f"{len(registrations)} Config registrations, {len(raw_codecs)} raw layouts -> {path}")


if __name__ == "__main__":
    main()
