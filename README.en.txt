XDC Studio 0.7.2 — portable for Windows 10/11 x64

Help: use the bottom-right Help button or F1. Opens an offline HTML page
in the interface language, with all settings, quick start and XDC author
acknowledgements: docs/help.en.html.

Extract the WHOLE archive. Run XdcConverter.exe in the top-level folder.
Keep app/ beside it: all DLLs, FFmpeg and the DOS player live there.
No .NET installation, administrator access or runtime extraction is needed.

Browse/drop a video, select an output XDV and profile, adjust the image and
convert. The result player opens automatically. MP4/MKV/WebM/MOV/AVI and
other FFmpeg formats are supported. Profiles: Composite 160x200/160x100,
monochrome CGA 640x200, and Tandy 160x200.

Colour controls affect the exported XDV: gamma 0.30–3.00 (1 neutral,
above 1 brighter), saturation 0–200% (100% neutral, disabled in B&W),
dither strength 0–100% (0 nearest colour, 100 full mixing). No dithering
disables the strength slider. Reset restores 1 / 100% / 100%. Patterns remain screen-anchored.
Tandy retains linear-light palette pairs and Oklab colour matching.

Dithering and stability — new in 0.7
Bayer 8x8 remains the default. Bayer 4x4 offers a shorter repeating pattern;
Blue noise uses one fixed 8x8 void-and-cluster mask, never animated noise.
No dithering uses solid colours. NTSC candidates are measured separately
for each mask, not reused from a different pattern.

NTSC stability: 0–100%, default 50%. Retains nearly equivalent 4x4 regions
with low source motion, weighs replay work, and prioritizes visible colour
improvement per estimated 8088 cycle. History uses reconstructed VRAM.
Strong source changes release retention. Higher settings allow more holding
and can cause trails. Zero disables holding and the new update ordering;
Bayer 8x8 / zero is byte-identical to 0.6. This affects only Composite/NTSC,
not monochrome, Tandy or the legacy palette matcher.
The still preview has no temporal history; inspect stabilization in the
finished XDV player. History resets for every output part. Processing keeps
bounded frame buffers in RAM, not the whole video. Stabilization increases
conversion time considerably; zero keeps the previous fast path.

Source/result comparison: drag the vertical divider or focus the image
and press Left/Right. Home/End show all result/source. Uncheck comparison
for the full result. Both sides come from one decoded frame with identical
framing; the source side keeps full colour without adjustments.
The time slider chooses preview time, not the exported Start/Length.

NTSC colour selection
The CGA colour list selects NTSC / early CGA (default), NTSC / late CGA,
or old mode algorithm. This choice affects the exported XDV.
The new encoder measures complete dither patterns through the NTSC signal
model, including neighbours, filtering and display clipping, then matches
the resulting perceived colours. Dither strength limits minority coverage
and re-optimizes the pattern; zero selects solid colours only.
The first calculation takes about two seconds on the test PC. Tables stay
in RAM and subsequent frames use lookups. No on-disk table cache is created.

Encoding targets the selected card at hue 0 and chroma 100%. Selecting an
NTSC target also aligns preview with it. Later adjustments with the NTSC
button affect viewing only. XDV has no early/late card metadata: select the
matching monitor manually when opening the file elsewhere.

NTSC preview
The NTSC button in both windows selects early/late IBM CGA, monitor hue
(-45 to +45 degrees), colour intensity (0–200%), or the old XDC palette view.
Settings apply immediately to both windows and are saved in
app/monitor-settings.json. They affect ONLY preview, never the exported XDV.
The model decodes the 640 one-bit CGA samples per line, transition voltages,
colour-carrier phase, luminance/chroma filters and NTSC colour decoding.
Neighbours interact. Actual monitor colours/sharpness may differ; this is
not calibrated against your hardware. See docs/CGA-NTSC.txt for details.
Monochrome CGA and Tandy RGB rendering are unchanged.

The result player decodes actual XDV frames/audio: Play/Pause/Stop, seek,
volume and mute. Stop returns to the beginning. Space toggles pause;
arrows step frames (or change volume while its slider is focused).
Volume survives seeking. Select split parts at the top. Drop an XDV or run:
XdcConverter.exe --play FILE.XDV
It does not emulate slow disks, CGA snow or a complete PC. Test on your 8088.

Default conversion: 30 FPS, 22050 Hz audio, 150 KiB/s, integrity 75%.
Length 0 means to end. XDV names: 1–8 A–Z, digits or underscores. Paths can
contain spaces/Unicode. Existing output requires confirmation. Only checked
successful conversions replace it; cancellation leaves the old result intact.
XDC_PLAY.EXE and a BAT launcher are placed beside output. Long movies split
at 60000 frames. RGB/PCM stays in RAM: no intermediate BMP/WAV or DOSBox.
Final XDV is streamed to a .partial, checked and renamed; cancelled partials
are removed. No runtime files are unpacked to TEMP.

Choose RU/EN above preview; language is saved in app/ui-settings.json.
The top folder contains the EXE, two readmes and app/docs. The new PC 5150
icon displays the user-supplied Bad Apple frame on its CRT.

CLI: XdcConverter.exe --convert job.json
{"Input":"C:/video/MIR.webm","Output":"C:/video/MIR_CGA.XDV","Mode":1,
 "Gamma":1.15,"Saturation":1.2,"DitherStrength":65,
 "CompositeAlgorithm":"ntsc","CompositeNewCga":false,
 "Dither":"bayer4","TemporalStability":50}
Mode 1 Composite, 2 B&W, 3 Tandy. JSON Saturation is a 0–2 multiplier.
CompositeAlgorithm: ntsc (default) or legacy (old mode). CompositeNewCga: true
for late CGA; used only for Mode=1 with ntsc.
Other fields: HalfHeight, Fps, SampleRate, DiskRate, Integrity, Dither
(bayer/bayer4/blue/none), Fit (fit/crop/stretch), Deinterlace, Silent, AudioStream,
Start, Duration, SegmentFrames. Explicit unattended overwrite: Overwrite=true.
Result: job.json.result.json; exit code 0/1.
XdcConverter.exe --inspect FILE.XDV report.json

Licences: docs/THIRD-PARTY.txt and app/licenses/.
Test report: docs/TEST-RESULTS.txt. XDV format and DOS player are unchanged.
