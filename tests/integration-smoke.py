"""Portable smoke test with generated video/audio; no private MIR fixture required."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
from verify_xdv import verify

exe, output = (Path(a).resolve() for a in sys.argv[1:3])
output.mkdir(parents=True)
temp = output / 'empty-temp'
temp.mkdir()
env = dict(os.environ, TEMP=str(temp), TMP=str(temp), DOTNET_ROOT=str(output/'no-system-dotnet'))
si = subprocess.STARTUPINFO()
si.dwFlags |= subprocess.STARTF_USESHOWWINDOW
si.wShowWindow = 0

def run(args, **kwargs):
    return subprocess.run([str(a) for a in args], env=env, startupinfo=si, timeout=90, **kwargs)

def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))

ffmpeg = exe.parent / 'app/tools/ffmpeg/ffmpeg.exe'
source = output / 'generated.mkv'
p = run([ffmpeg, '-v', 'error', '-f', 'lavfi', '-i', 'testsrc2=size=320x240:rate=30',
         '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=22050', '-t', '2',
         '-c:v', 'ffv1', '-c:a', 'pcm_s16le', source], capture_output=True)
assert p.returncode == 0, p.stderr.decode(errors='replace')
results = []
for name, settings in [
    ('CGA', dict(Mode=1)), ('BW', dict(Mode=2, Dither='bayer4')),
    ('TANDY', dict(Mode=3, Dither='blue')), ('OLD', dict(Mode=1, CompositeAlgorithm='legacy')),
    ('LATE', dict(Mode=1, HalfHeight=True, CompositeNewCga=True, Dither='blue')),
]:
    job = dict(Input=str(source), Output=str(output/(name+'.XDV')), Duration=2, **settings)
    path = output / (name+'.json')
    path.write_text(json.dumps(job), encoding='utf-8')
    p = run([exe, '--convert', path])
    status = read(path.with_suffix('.json.result.json'))
    assert p.returncode == 0 and status['Success'], status
    decoded = verify(job['Output'])
    report = read(output/(name+'.report.json'))
    assert decoded['packets'] == 60 and decoded['mode'] == settings['Mode']
    assert decoded['max_cycles'] <= report['Encoder'][0]['CycleBudget'] + .001
    assert decoded['peak_kib_s'] <= 150 and report['IntermediateMediaFiles'] == 0
    reader_path = output/(name+'-reader.json')
    p = run([exe, '--reader-test', job['Output'], reader_path])
    reader = read(reader_path)
    assert p.returncode == 0 and reader['Success'] and reader['RandomSeeks'] == 7
    assert reader['VideoSha256'].lower() == decoded['video_sha256']
    assert reader['AudioSha256'].lower() == decoded['audio_sha256']
    results.append(dict(Name=name, Mode=decoded['mode'], Frames=decoded['packets'], Audio=decoded['audio_sha256']))
assert len({r['Audio'] for r in results}) == 1
audio = run([ffmpeg, '-v', 'error', '-ss', '0', '-i', source, '-map', '0:1', '-vn', '-sn',
             '-ac', '1', '-ar', '22050', '-af', 'aresample=async=1:first_pts=0,apad', '-t', '2',
             '-c:a', 'pcm_u8', '-f', 'u8', 'pipe:1'], capture_output=True)
assert audio.returncode == 0 and hashlib.sha256(audio.stdout).hexdigest() == results[0]['Audio']
old = (output/'CGA.XDV').read_bytes()
p = run([exe, '--convert', output/'CGA.json'])
assert p.returncode != 0 and not read(output/'CGA.json.result.json')['Success']
assert (output/'CGA.XDV').read_bytes() == old
assert not list(temp.iterdir()) and not list(output.glob('*.partial'))
for language in ('ru', 'en'):
    page = (exe.parent/f'docs/help.{language}.html').read_text(encoding='utf-8')
    assert 'old mode' in page and '0.5' not in page and 'https://github.com/MobyGamer/XDC' in page
report = dict(Success=True, Profiles=3, Cases=results, OldMode=True, IndependentAudio=True,
              ReaderSeeks=True, ExistingOutputPreserved=True, EmptyTemp=True, HelpPresent=True)
(output/'results.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print('PASS: all profiles, old mode, late/half/blue, audio, budgets, seeks, help and overwrite refusal.')
