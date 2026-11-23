#!/usr/bin/env python3
"""Explains why two .NET single-file bundles differ.

`cmp` only reports the first differing byte. In a single-file host that is usually the bundle header offset
inside the apphost, which moves whenever any embedded file changes size. This script reads both bundle
manifests and lists every embedded file whose stored bytes differ, plus any difference in the apphost itself.

    python3 compare_single_file.py <first bundle> <second bundle>

Exits 0 when the bundles are identical and 1 otherwise.
"""

import hashlib
import struct
import sys

SIGNATURE = bytes([
    0x8B, 0x12, 0x02, 0xB9, 0x6A, 0x61, 0x20, 0x38, 0x72, 0x7B, 0x93, 0x02, 0x14, 0xD7, 0xA0, 0x32,
    0x13, 0xF5, 0xB9, 0xE6, 0xEF, 0xAE, 0x33, 0x18, 0xEE, 0x3B, 0x2D, 0xCE, 0x24, 0xB3, 0x6A, 0xAE,
])


def read_string(data, position):
    length, shift = 0, 0
    while True:
        byte = data[position]
        position += 1
        length |= (byte & 0x7F) << shift
        shift += 7
        if byte < 0x80:
            break
    return data[position:position + length].decode("utf-8"), position + length


def read_bundle(path):
    data = open(path, "rb").read()
    signature = data.find(SIGNATURE)
    if signature < 8:
        raise SystemExit(f"{path}: bundle signature not found")
    header = struct.unpack_from("<q", data, signature - 8)[0]
    major, minor, count = struct.unpack_from("<IIi", data, header)
    position = header + 12
    bundle_id, position = read_string(data, position)
    if major >= 2:
        position += 8 * 5  # deps.json / runtimeconfig.json locations and flags
    files = {}
    first = len(data)
    for _ in range(count):
        offset, size = struct.unpack_from("<qq", data, position)
        position += 16
        compressed = 0
        if major >= 6:
            compressed = struct.unpack_from("<q", data, position)[0]
            position += 8
        position += 1  # file type
        name, position = read_string(data, position)
        stored = data[offset:offset + (compressed or size)]
        files[name] = (size, compressed, hashlib.sha256(stored).hexdigest()[:16])
        first = min(first, offset)
    # The apphost ends where the first file starts; mask the header offset so only real host changes show.
    host = bytearray(data[:first])
    host[signature - 8:signature] = b"\0" * 8
    return {
        "version": f"{major}.{minor}", "id": bundle_id, "header": header, "files": files,
        "host": hashlib.sha256(host).hexdigest()[:16], "host_size": first,
    }


def main():
    if len(sys.argv) != 3:
        raise SystemExit(__doc__)
    first, second = (read_bundle(path) for path in sys.argv[1:])
    differences = []
    for key in ("version", "id", "host_size", "host"):
        if first[key] != second[key]:
            differences.append(f"{key}: {first[key]} != {second[key]}")
    for name in sorted(set(first["files"]) | set(second["files"])):
        a, b = first["files"].get(name), second["files"].get(name)
        if a != b:
            describe = lambda entry: "missing" if entry is None else f"size={entry[0]} stored={entry[1] or entry[0]} sha256={entry[2]}"
            differences.append(f"{name}: {describe(a)} != {describe(b)}")
    if not differences:
        print("Single-file bundles are identical.")
        return 0
    print(f"Bundle header offset: {first['header']} != {second['header']}")
    print(f"{len(differences)} difference(s):")
    for line in differences:
        print("  " + line)
    return 1


if __name__ == "__main__":
    sys.exit(main())
