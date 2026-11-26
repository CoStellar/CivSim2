"""Cuts a General MIDI SoundFont (.sf2) down to the presets the game's music uses, so the game ships a small file.

Usage: python3 trim_sf2.py in.sf2 out.sf2 bank:program [bank:program ...]   (drums: 128:0)
Keeps the chosen presets, their instruments and their samples (with stereo partners); everything else is dropped.
"""
from __future__ import annotations

import struct
import sys


def chunks(data: bytes, start: int, end: int):
    pos = start
    while pos < end:
        cid = data[pos:pos + 4].decode("latin1")
        size = struct.unpack_from("<I", data, pos + 4)[0]
        yield cid, pos + 8, size
        pos += 8 + size + (size & 1)


def records(blob: bytes, size: int):
    return [blob[i:i + size] for i in range(0, len(blob), size)]


def read(path: str):
    data = open(path, "rb").read()
    assert data[:4] == b"RIFF" and data[8:12] == b"sfbk"
    lists = {}
    for cid, off, size in chunks(data, 12, len(data)):
        if cid == "LIST":
            kind = data[off:off + 4].decode("latin1")
            lists[kind] = {c: data[o:o + s] for c, o, s in chunks(data, off + 4, off + size)}
    return lists


def write_chunk(cid: str, payload: bytes) -> bytes:
    pad = b"\0" if len(payload) & 1 else b""
    return cid.encode("latin1") + struct.pack("<I", len(payload)) + payload + pad


def write_list(kind: str, parts: list[tuple[str, bytes]]) -> bytes:
    body = kind.encode("latin1") + b"".join(write_chunk(c, p) for c, p in parts)
    return write_chunk("LIST", body)


def trim(src: str, dst: str, wanted: set[tuple[int, int]], drum_keys: set[int] | None = None):
    """Keeps the presets `wanted`; of a drum kit (bank 128) only the zones of `drum_keys`, when given."""
    lists = read(src)
    info, sdta, pdta = lists["INFO"], lists["sdta"], lists["pdta"]
    smpl = sdta["smpl"]
    phdr = records(pdta["phdr"], 38)
    pbag = records(pdta["pbag"], 4)
    pmod = records(pdta["pmod"], 10)
    pgen = records(pdta["pgen"], 4)
    inst = records(pdta["inst"], 22)
    ibag = records(pdta["ibag"], 4)
    imod = records(pdta["imod"], 10)
    igen = records(pdta["igen"], 4)
    shdr = records(pdta["shdr"], 46)

    def bag_range(headers, i, fmt_off):
        a = struct.unpack_from("<H", headers[i], fmt_off)[0]
        b = struct.unpack_from("<H", headers[i + 1], fmt_off)[0]
        return a, b

    def bag_lists(bags, i):
        g0, m0 = struct.unpack("<HH", bags[i])
        g1, m1 = struct.unpack("<HH", bags[i + 1])
        return (g0, g1), (m0, m1)

    keep_presets = []
    for i in range(len(phdr) - 1):
        program, bank = struct.unpack_from("<HH", phdr[i], 20)
        if (bank, program) in wanted:
            keep_presets.append(i)
    found = {struct.unpack_from("<HH", phdr[i], 20)[::-1] for i in keep_presets}
    missing = wanted - found
    if missing:
        print("missing presets:", sorted(missing), file=sys.stderr)

    def zone_plays(gens, g0, g1, keys):
        """A zone without a key range (or a global one) plays every key; otherwise it must cover one of `keys`."""
        for g in range(g0, g1):
            oper = struct.unpack_from("<H", gens[g])[0]
            if oper == 43:
                lo, hi = gens[g][2], gens[g][3]
                return any(lo <= k <= hi for k in keys)
        return True

    drum_inst = set()
    for p in keep_presets:
        if struct.unpack_from("<H", phdr[p], 22)[0] == 128:
            b0, b1 = bag_range(phdr, p, 24)
            for b in range(b0, b1):
                (g0, g1), _ = bag_lists(pbag, b)
                for g in range(g0, g1):
                    oper, amount = struct.unpack("<HH", pgen[g])
                    if oper == 41:
                        drum_inst.add(amount)

    def inst_keys(i):
        return drum_keys if drum_keys is not None and i in drum_inst else None

    # Instruments used by the kept presets.
    used_inst = set()
    for p in keep_presets:
        b0, b1 = bag_range(phdr, p, 24)
        for b in range(b0, b1):
            (g0, g1), _ = bag_lists(pbag, b)
            for g in range(g0, g1):
                oper, amount = struct.unpack("<HH", pgen[g])
                if oper == 41:
                    used_inst.add(amount)
    used_inst = sorted(used_inst)

    used_smp = set()
    for i in used_inst:
        b0, b1 = bag_range(inst, i, 20)
        for b in range(b0, b1):
            (g0, g1), _ = bag_lists(ibag, b)
            if inst_keys(i) is not None and not zone_plays(igen, g0, g1, inst_keys(i)):
                continue
            for g in range(g0, g1):
                oper, amount = struct.unpack("<HH", igen[g])
                if oper == 53:
                    used_smp.add(amount)
    # Stereo partners.
    changed = True
    while changed:
        changed = False
        for s in list(used_smp):
            link, stype = struct.unpack_from("<HH", shdr[s], 42)
            if stype & 0x0E and link < len(shdr) - 1 and link not in used_smp:
                used_smp.add(link)
                changed = True
    used_smp = sorted(used_smp)

    inst_map = {old: new for new, old in enumerate(used_inst)}
    smp_map = {old: new for new, old in enumerate(used_smp)}

    # Samples: copy each one with 46 zero points after it (as the spec asks), shifting its offsets.
    new_smpl = bytearray()
    new_shdr = []
    for old in used_smp:
        rec = shdr[old]
        name = rec[:20]
        start, end, ls, le, rate = struct.unpack_from("<IIIII", rec, 20)
        pitch, corr = struct.unpack_from("<Bb", rec, 40)
        link, stype = struct.unpack_from("<HH", rec, 42)
        base = len(new_smpl) // 2
        new_smpl += smpl[start * 2:end * 2] + b"\0" * 92
        shift = base - start
        new_link = smp_map.get(link, 0) if stype & 0x0E else 0
        new_type = stype if (stype & 0x0E) == 0 or link in smp_map else 1
        new_shdr.append(name + struct.pack("<IIIII", start + shift, end + shift, ls + shift, le + shift, rate)
                        + struct.pack("<Bb", pitch, corr) + struct.pack("<HH", new_link, new_type))
    new_shdr.append(b"EOS".ljust(20, b"\0") + b"\0" * 26)

    def copy_zones(headers_idx, headers, bags, mods, gens, name_len, ref_oper, ref_map, keys_of=None):
        out_h, out_b, out_m, out_g = [], [], [], []
        for idx in headers_idx:
            b0, b1 = bag_range(headers, idx, name_len)
            out_h.append((headers[idx], len(out_b)))
            keys = keys_of(idx) if keys_of else None
            for b in range(b0, b1):
                (g0, g1), (m0, m1) = bag_lists(bags, b)
                if keys is not None and not zone_plays(gens, g0, g1, keys):
                    continue
                out_b.append(struct.pack("<HH", len(out_g), len(out_m)))
                out_m.extend(mods[m0:m1])
                for g in range(g0, g1):
                    oper, amount = struct.unpack("<HH", gens[g])
                    if oper == ref_oper:
                        amount = ref_map[amount]
                    out_g.append(struct.pack("<HH", oper, amount))
        return out_h, out_b, out_m, out_g

    ih, ib, im, ig = copy_zones(used_inst, inst, ibag, imod, igen, 20, 53, smp_map, inst_keys)
    new_inst = [rec[:20] + struct.pack("<H", bag) for rec, bag in ih]
    new_inst.append(b"EOI".ljust(20, b"\0") + struct.pack("<H", len(ib)))
    ib.append(struct.pack("<HH", len(ig), len(im)))
    im.append(b"\0" * 10)
    ig.append(b"\0" * 4)

    ph, pb, pm, pg = copy_zones(keep_presets, phdr, pbag, pmod, pgen, 24, 41, inst_map)
    new_phdr = [rec[:24] + struct.pack("<H", bag) + rec[26:] for rec, bag in ph]
    new_phdr.append(b"EOP".ljust(20, b"\0") + b"\0" * 4 + struct.pack("<H", len(pb)) + b"\0" * 12)
    pb.append(struct.pack("<HH", len(pg), len(pm)))
    pm.append(b"\0" * 10)
    pg.append(b"\0" * 4)

    info_parts = [(k, v) for k, v in info.items()]
    body = b"sfbk" + write_list("INFO", info_parts) + write_list("sdta", [("smpl", bytes(new_smpl))]) + write_list("pdta", [
        ("phdr", b"".join(new_phdr)), ("pbag", b"".join(pb)), ("pmod", b"".join(pm)), ("pgen", b"".join(pg)),
        ("inst", b"".join(new_inst)), ("ibag", b"".join(ib)), ("imod", b"".join(im)), ("igen", b"".join(ig)),
        ("shdr", b"".join(new_shdr)),
    ])
    with open(dst, "wb") as f:
        f.write(b"RIFF" + struct.pack("<I", len(body)) + body)
    print(f"{len(keep_presets)} presets, {len(used_inst)} instruments, {len(used_smp)} samples, {len(new_smpl) / 1e6:.1f} MB of sound")


if __name__ == "__main__":
    pairs = {tuple(int(x) for x in a.split(":")) for a in sys.argv[3:]}
    trim(sys.argv[1], sys.argv[2], pairs)
