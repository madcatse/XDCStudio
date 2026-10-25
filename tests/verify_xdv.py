"""Independent structural + 8088-subset decoder for XDC output (no x86 execution).

Usage: python tests/verify_xdv.py INPUT.XDV [preview.ppm]
Rejects invalid instructions and out-of-bounds video/data accesses, reconstructs
every frame, hashes the resulting frames/audio, optionally exports a final frame.
"""
import hashlib
import json
import pathlib
import struct
import sys

COMPOSITE = [0x090909,0x009c3a,0x4018ff,0x00c5ff,0xe3054b,0x9a9d96,0xff1dff,0xddc5ff,0x4c7400,0x07ff00,0x9e9a9a,0x53ffd2,0xff7800,0xf5ff00,0xff98fa,0xffffff]
TANDY = [0x000000,0x0000aa,0x00aa00,0x00aaaa,0xaa0000,0xaa00aa,0xaa5500,0xaaaaaa,0x555555,0x5555ff,0x55ff55,0x55ffff,0xff5555,0xff55ff,0xffff55,0xffffff]


def decode_packet(packet, audio_size, vram):
    assert packet[:4] == bytes.fromhex('1e0e1fbe'), 'bad prologue'
    assert packet[6:14] == bytes.fromhex('b800b88ec0b500fc'), 'bad video segment'
    data_start = struct.unpack_from('<H', packet, 4)[0]
    assert 16 <= data_start <= len(packet) - audio_size
    assert packet[data_start-2:data_start] == bytes.fromhex('1fcb'), 'bad epilogue'
    ip, si, di, ax, cx = 14, data_start, 0, 0xb800, 0
    cycles = 118.0
    while ip < data_start - 2:
        op = packet[ip]
        ip += 1
        repeat = 1
        repeated = op == 0xf3
        if op == 0xf3:
            cycles += 4
            repeat, cx = cx, 0
            op = packet[ip]
            ip += 1
            assert op in (0xa4, 0xa5, 0xaa, 0xab)
        if op in (0xbf, 0xb9, 0xb8):
            cycles += 12
            value = struct.unpack_from('<H', packet, ip)[0]
            ip += 2
            if op == 0xbf: di = value
            elif op == 0xb9: cx = value
            else: ax = value
        elif op in (0xb0, 0xb1, 0xb5):
            cycles += 8
            value = packet[ip]
            ip += 1
            if op == 0xb0: ax = (ax & 0xff00) | value
            elif op == 0xb1: cx = (cx & 0xff00) | value
            else: cx = (cx & 255) | (value << 8)
        elif op in (0xa4, 0xa5, 0xaa, 0xab):
            width = 2 if op in (0xa5, 0xab) else 1
            length = width * repeat
            per_byte = 19.2 if op in (0xa4, 0xa5) else 13.7
            cycles += 4 + per_byte * width + (per_byte * length if repeated else 0)
            assert di + length <= len(vram), 'video write out of bounds'
            if op in (0xa4, 0xa5):
                assert si + length <= len(packet) - audio_size, 'data read into audio'
                vram[di:di+length] = packet[si:si+length]
                si += length
            else:
                value = ax.to_bytes(2, 'little')[:width]
                vram[di:di+length] = value * repeat
            di += length
        elif op == 0x26:
            kind, addressing = packet[ip:ip+2]
            ip += 2
            assert kind in (0xc6, 0xc7) and addressing == 6
            address = struct.unpack_from('<H', packet, ip)[0]
            ip += 2
            width = 1 if kind == 0xc6 else 2
            assert address + width <= len(vram)
            vram[address:address+width] = packet[ip:ip+width]
            ip += width
        else:
            raise AssertionError(f'unsupported opcode {op:02x} at {ip-1}')
    assert ip == data_start - 2
    return dict(cycles=cycles, video_bytes=si)


def verify(path, preview=None):
    data = pathlib.Path(path).read_bytes()
    magic, count, largest, audio, rate, mode, cols, rows, flags = struct.unpack_from('<4sHHHHBBBB', data)
    assert magic == b'XDCV' and 0 < count <= 65519
    assert mode in (1, 2, 3) and cols == 80 and rows == 200 and flags == 0
    index = data[-count:]
    assert len(data) == 512 + sum(index) * 512 + count
    assert largest == max(index) * 512
    vram = bytearray(16384)
    position = 512
    video_hash, audio_hash = hashlib.sha256(), hashlib.sha256()
    audio_min, audio_max = 255, 0
    previous = None
    max_cycles, max_video = 0, 0
    unique = 0
    for sectors in index:
        packet = data[position:position+sectors*512]
        metrics = decode_packet(packet, audio, vram)
        max_cycles = max(max_cycles, metrics['cycles'])
        max_video = max(max_video, metrics['video_bytes'])
        video_hash.update(vram)
        sound = packet[-audio:]
        audio_hash.update(sound)
        audio_min, audio_max = min(audio_min, min(sound)), max(audio_max, max(sound))
        if previous != vram:
            unique += 1
            previous = bytes(vram)
        position += len(packet)
    if preview:
        rgb = bytearray()
        for y in range(200):
            offset = (y % 2) * 8192 + (y // 2) * 80
            for x in range(640):
                if mode == 2:
                    value = (vram[offset+x//8] >> (7-x%8)) & 1
                    rgb.extend(bytes([value*255])*3)
                else:
                    pixel = x//4
                    value = (vram[offset+pixel//2] >> (4 if pixel%2 == 0 else 0)) & 15
                    color = (COMPOSITE if mode == 1 else TANDY)[value]
                    rgb.extend(color.to_bytes(3, 'big'))
        pathlib.Path(preview).write_bytes(b'P6\n640 200\n255\n'+rgb)
    return dict(file=str(path), mode=mode, packets=count, fps=rate/audio, duration=count*audio/rate,
                bytes=len(data), peak_kib_s=largest*rate/audio/1024, changed_frames=unique,
                audio_range=[audio_min,audio_max], video_sha256=video_hash.hexdigest(), audio_sha256=audio_hash.hexdigest(),
                max_cycles=max_cycles, max_video_bytes=max_video)


if __name__ == '__main__':
    print(json.dumps(verify(sys.argv[1], sys.argv[2] if len(sys.argv)>2 else None), indent=2))
