"""Read and write unencrypted Godot PCK archives (v2-v4)."""
import argparse
import hashlib
import json
import struct
from pathlib import Path


class Pack:
    def __init__(self, path):
        self.path = Path(path)
        self.entries = {}
        with self.path.open('rb') as f:
            magic, self.version, *self.engine = struct.unpack('<5I', f.read(20))
            if magic != 0x43504447 or self.version not in (2, 3, 4):
                raise ValueError('Unsupported PCK')
            flags, self.base = struct.unpack('<IQ', f.read(12))
            if flags & 1:
                raise ValueError('Encrypted directory is not supported')
            if self.version >= 3:
                directory, = struct.unpack('<Q', f.read(8))
                f.seek(directory)
            else:
                f.seek(96)
            count, = struct.unpack('<I', f.read(4))
            for _ in range(count):
                length, = struct.unpack('<I', f.read(4))
                name = f.read(length).rstrip(b'\0').decode('utf-8')
                offset, size = struct.unpack('<QQ', f.read(16))
                md5 = f.read(16).hex()
                file_flags, = struct.unpack('<I', f.read(4))
                self.entries[name] = dict(offset=offset + self.base, size=size,
                                          md5=md5, flags=file_flags)

    def read(self, name):
        entry = self.entries[name]
        if entry['flags']:
            raise ValueError(f'Unsupported entry flags: {name}')
        with self.path.open('rb') as f:
            f.seek(entry['offset'])
            data = f.read(entry['size'])
        if hashlib.md5(data).hexdigest() != entry['md5']:
            raise ValueError(f'Checksum mismatch: {name}')
        return data

    def extract(self, names, destination):
        root = Path(destination).resolve()
        for name in names:
            target = (root / name.removeprefix('res://')).resolve()
            if not target.is_relative_to(root):
                raise ValueError('Unsafe archive path')
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(self.read(name))


def write_pack(destination, entries, engine=(4, 7, 2)):
    """Create a v3 archive supported by Godot 4.7, with validated MD5 entries."""
    with Path(destination).open('wb') as f:
        f.write(struct.pack('<6IQQ', 0x43504447, 3, *engine, 2, 128, 0))
        f.write(bytes(128-f.tell()))
        index = []
        for name, data in sorted(entries.items()):
            offset = f.tell() - 128
            f.write(data)
            f.write(bytes((-f.tell()) % 32))
            index.append((name, offset, len(data), hashlib.md5(data).digest()))
        directory = f.tell()
        f.write(struct.pack('<I', len(index)))
        for name, offset, size, md5 in index:
            raw = name.removeprefix('res://').encode('utf-8')
            raw += bytes((-len(raw)) % 4)
            f.write(struct.pack('<I', len(raw)) + raw)
            f.write(struct.pack('<QQ', offset, size) + md5 + bytes(4))
        f.seek(32)
        f.write(struct.pack('<Q', directory))


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('pack')
    parser.add_argument('--index', default='analysis/pck-index.json')
    parser.add_argument('--extract', action='store_true')
    args = parser.parse_args()
    pack = Pack(args.pack)
    target = Path(args.index)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(pack.entries, ensure_ascii=False, indent=2), encoding='utf-8')
    from collections import Counter
    print('Engine:', pack.engine, 'PCK:', pack.version, 'Files:', len(pack.entries))
    print(Counter(Path(n).suffix for n in pack.entries))
    if args.extract:
        names = [n for n,e in pack.entries.items() if Path(n).suffix.lower() in
                 {'.json','.csv','.txt','.gdshader','.gd','.gdc','.cs','.tscn','.tres',
                  '.remap','.import','.godot','.binary','.res','.scn','.translation','.ink'}]
        pack.extract(names, 'analysis/extracted')
        print('Extracted:', len(names), 'files;', sum(pack.entries[n]['size'] for n in names), 'bytes')
