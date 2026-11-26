"""Bind the verified fixed-layout Config readers for Windows 0.75.2."""


def read_raw_codecs(image, registrations):
    # Root constructor, key lookup and the Vector3 getters agree with DummyDll.
    root_type_slot = image.base + 0x0229A588
    keys = [key for key, (slot, _) in registrations.items() if slot == root_type_slot]
    if len(keys) != 1:
        raise ValueError(f"Expected one raw Config registration for type slot {root_type_slot:#x}")
    shapes = {
        0x04D7ED60: {("mov", "rax, qword ptr [rdi + 8]"), ("add", "rax, rdi"),
                     ("mov", "rbx, qword ptr [rdi + 0x10]"), ("add", "rbx, rdi"),
                     ("movsxd", "r14, dword ptr [rdi + 0x18]"), ("add", "rbx, 0x30"),
                     ("mov", "r8d, dword ptr [rbx + rax]"), ("add", "rcx, 4"),
                     ("call", hex(image.base + 0x08245F90))},
        0x04F28990: {("cmp", "dword ptr [r9 + rax*4], edx")},
        0x04FEDF80: {("mov", "rdx, qword ptr [rax + 8]")},
        0x04FEE020: {("mov", "rdx, qword ptr [rax + 0x14]")},
        0x04FEDE90: {("mov", "rdx, qword ptr [rax + 0x20]")},
    }
    for rva, expected in shapes.items():
        start, end = image.bounds(image.base + rva)
        actual = {(ins.mnemonic, ins.op_str) for ins in image.instructions(start, end)}
        if start != image.base + rva or not expected.issubset(actual):
            raise ValueError(f"Raw Config reader layout could not be verified at RVA {rva:#x}")
    return {keys[0]: "uint32-vector-table-v1"}
