"""Bind the TextMap root and language buckets for Windows 0.75.2."""
import struct


def read_text_map(image, registrations):
    type_slot = image.base + 0x0229B508
    keys = [key for key, (slot, _) in registrations.items() if slot == type_slot]
    if len(keys) != 1:
        raise ValueError(f"Expected one TextMap registration for type slot {type_slot:#x}")

    # The row reader dispatches the empty base message, text and unsigned key.
    dispatch = 0x008FA9D4
    targets = (0x04D9EE40, 0x04D9EF17, 0x04D9EF50)
    table = image.pe.get_data(dispatch, 33 * 4)
    if tuple(dispatch + struct.unpack_from("<i", table, tag * 4)[0] for tag in (10, 26, 32)) != targets:
        raise ValueError("TextMap entry reader tags could not be verified")

    address = image.base + 0x04BE0B82
    instruction = next(image.instructions(address, address + 6), None)
    if instruction is None or instruction.mnemonic != "imul" or instruction.operands[-1].imm != 100_000_000:
        raise ValueError("TextMap record bucket stride could not be verified")

    # Bucket order is verified against translations in the actual package, not the settings enum order.
    languages = ("CHS", "EN", "JP", "KR", "CHT", "VI", "DE", "RU", "FR", "TH", "ES", "PT", "ID")
    return {
        "recordType": f"0x{keys[0]:08X}",
        "recordStride": instruction.operands[-1].imm,
        "languages": dict(enumerate(languages, 1)),
    }
