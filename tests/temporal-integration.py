"""0.7 portable: full MIR conversion, independent decoding/audio/seek checks, RAM-only media."""
import hashlib
import json
import os
import pathlib
import subprocess
import sys
import xml.etree.ElementTree as ET
from verify_xdv import verify

root = pathlib.Path(__file__).resolve().parent.parent
version = ET.parse(root/'src/XdcConverter.csproj').findtext('./PropertyGroup/Version')
exe = root / f'dist/XdcStudio-{version}-win-x64/XdcConverter.exe'
source = root/'local/media/MIR.webm'
assert source.exists(), 'Place your MIR.webm in local/media/'
historical = {}
def baseline(name, actual, reference):
    path = root/'local/reference'/reference
    if path.exists():
        assert actual.read_bytes() == path.read_bytes(), name
        historical[name] = True
    else:
        historical[name] = None  # baseline not supplied, not a passed comparison
out = pathlib.Path(sys.argv[1]) if len(sys.argv) > 1 else root / 'tests/output/mir-current'
out.mkdir(parents=True, exist_ok=True)
assert not list(out.iterdir()) or '--resume' in sys.argv, 'Use a fresh test output folder or --resume'
temp = out / 'empty-temp'
temp.mkdir(exist_ok=True)
env = dict(os.environ, TEMP=str(temp), TMP=str(temp))
startup = subprocess.STARTUPINFO()
startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
startup.wShowWindow = 0
results = []


def run(name, changes, success=True):
    job = dict(Input=str(source), Output=str(out/(name+'.XDV')), Mode=1)
    job.update(changes)
    path = out / (name+'.json')
    result_path = path.with_suffix('.json.result.json')
    if '--resume' in sys.argv and result_path.exists():
        assert json.loads(path.read_text('utf-8')) == job, 'Cannot resume a changed job'
        result = json.loads(result_path.read_text('utf-8-sig'))
        assert result['Success'] == success, result
    else:
        path.write_text(json.dumps(job), encoding='utf-8')
        proc = subprocess.run([str(exe), '--convert', str(path)], timeout=240, startupinfo=startup, env=env)
        result = json.loads(result_path.read_text('utf-8-sig'))
        assert result['Success'] == success and (proc.returncode == 0) == success, result
    if not success:
        assert not pathlib.Path(job['Output']).exists()
        return
    info = verify(job['Output'])
    report = json.loads((out/(name+'.report.json')).read_text('utf-8-sig'))
    assert info['max_cycles'] <= report['Encoder'][0]['CycleBudget'] + .001
    assert info['peak_kib_s'] <= job.get('DiskRate',150)
    assert report['IntermediateMediaFiles'] == 0 and report['Streaming']
    for key in ('Gamma','Saturation','DitherStrength','TemporalStability'):
        assert report['Job'][key] == job.get(key,100 if key == 'DitherStrength' else 50 if key == 'TemporalStability' else 1)
    reader_report = out / (name+'-reader.json')
    proc = subprocess.run([str(exe), '--reader-test', job['Output'], str(reader_report)], timeout=45, startupinfo=startup, env=env)
    reader = json.loads(reader_report.read_text('utf-8-sig'))
    assert proc.returncode == 0 and reader['Success'], reader
    assert reader['VideoSha256'].lower() == info['video_sha256']
    assert reader['AudioSha256'].lower() == info['audio_sha256']
    assert reader['RandomSeeks'] == 7 and reader['CheckpointCount'] <= 65
    assert not list(temp.iterdir())
    assert not list(out.glob('*.partial')) and not list(out.glob('*.rollback'))
    result = dict(name=name,passed=True,seconds=report['ElapsedSeconds'],peak_app_mib=report['ApplicationPeakWorkingSetBytes']/1048576,**info)
    results.append(result)
    print(json.dumps(dict(name=name,passed=True,frames=info['packets'],seconds=round(report['ElapsedSeconds'],3))),flush=True)
    return result



full = [run('MIR_CGA',dict(Mode=1)),run('MIR_BW',dict(Mode=2)),run('MIR_TND',dict(Mode=3))]
variants = [run('B4_STAB',dict(Dither='bayer4')),run('BLUE_STB',dict(Dither='blue')),
            run('B8_OFF',dict(TemporalStability=0)),run('B4_OFF',dict(Dither='bayer4',TemporalStability=0)),run('BLUE_OFF',dict(Dither='blue',TemporalStability=0))]
assert all(v['packets']==6042 for v in full+variants)
assert len({v['audio_sha256'] for v in full+variants})==1
baseline('DisabledMatches06', out/'B8_OFF.XDV', '0.6/MIR_CGA.XDV')
for name in ('MIR_BW','MIR_TND'):
    baseline(name+'Matches06', out/(name+'.XDV'), '0.6/'+name+'.XDV')
sound = subprocess.run([str(exe.parent/'app/tools/ffmpeg/ffmpeg.exe'),'-v','error','-ss','0','-i',str(source),'-map','0:1','-vn','-sn','-ac','1','-ar','22050',
    '-af','aresample=async=1:first_pts=0,apad','-t','201.4','-c:a','pcm_u8','-f','u8','pipe:1'],capture_output=True,timeout=30,startupinfo=startup,env=env)
assert sound.returncode==0 and hashlib.sha256(sound.stdout).hexdigest()==full[0]['audio_sha256']
run('MIR_ADJ',dict(Start=30,Duration=6,Gamma=1.15,Saturation=1.2,DitherStrength=65,Dither='blue',TemporalStability=80))
run('HALF',dict(HalfHeight=True,Start=30,Duration=6,Gamma=.8,Saturation=.5,DitherStrength=35,Dither='bayer4'))
run('BW_ADJ',dict(Mode=2,Start=30,Duration=6,Gamma=1.35,DitherStrength=50,Dither='blue'))
run('TND_ADJ',dict(Mode=3,Start=30,Duration=6,Gamma=1.2,Saturation=1.5,DitherStrength=75,Dither='bayer4'))
late=run('LATE',dict(Start=30,Duration=6,CompositeNewCga=True,Dither='blue'))
legacy=run('LEGACY',dict(CompositeAlgorithm='legacy'))
baseline('LegacyMatches05', pathlib.Path(legacy['file']), '0.5/MIR_CGA.XDV')
zero=run('ZERO',dict(Start=30,Duration=2,Dither='blue',DitherStrength=0))
none=run('NONE',dict(Start=30,Duration=2,Dither='none'))
assert pathlib.Path(zero['file']).read_bytes()==pathlib.Path(none['file']).read_bytes()
again=run('REPEAT',dict(Start=30,Duration=6,CompositeNewCga=True,Dither='blue'))
assert pathlib.Path(again['file']).read_bytes()==(out/'LATE.XDV').read_bytes()
for name,settings in [('BADG',dict(Gamma=0)),('BADMASK',dict(Dither='random')),('BADSTAB',dict(TemporalStability=101)),('BADALG',dict(CompositeAlgorithm='bogus'))]:
    run(name,settings,success=False)
assert not list(temp.iterdir())
(out/'results.json').write_text(json.dumps(dict(Success=True,FullMirProfiles=3,FullMirDitherVariants=6,AudioMatchesSource=True,InvalidSettingsRejected=True,EmptyTemp=True,HistoricalComparisons=historical,Results=results),indent=2),encoding='utf-8')
print('PASS: six full MIR dither variants, all profiles, audio, budgets, seeks, repeatability, empty TEMP.',flush=True)
