# Dependency versions and source locations

## Original XDC

Jim Leonard, MIT. Original Pascal code, palettes and DOS binaries are kept in this fork. Base revision: [`56a5dc1951e1f0b8747fe8fa4614c607e2f91c8b`](https://github.com/MobyGamer/XDC/tree/56a5dc1951e1f0b8747fe8fa4614c607e2f91c8b). The bundled `XDC_PLAY.EXE` is unchanged. The original [LICENSE](../LICENSE) is retained.

## .NET

Build SDK 10.0.402 and Windows Desktop runtime 10.0.13, pinned by `global.json` and the dependency download hashes. Source: [runtime v10.0.13](https://github.com/dotnet/runtime/tree/v10.0.13), [Windows Forms v10.0.13](https://github.com/dotnet/winforms/tree/v10.0.13), [WPF v10.0.13](https://github.com/dotnet/wpf/tree/v10.0.13). MIT and third-party notices are included under `app/licenses` in the portable distribution.

## FFmpeg / FFprobe

Unmodified 9.0.2 essentials static Windows build by Gyan Doshi, launched as separate processes. [Exact binary release](https://github.com/GyanD/codexffmpeg/releases/tag/9.0.2), [build provider](https://www.gyan.dev/ffmpeg/builds/), [FFmpeg source revision 946fcce07b](https://github.com/FFmpeg/FFmpeg/tree/946fcce07b).

This is a **GPLv3 build**, including third-party codec libraries. The downloaded archive's LICENSE and README, including its build configuration, are retained in `app/licenses`. The binary and SDK download hashes are pinned in `fetch-dependencies.ps1`.

When distributing the portable binaries, provide the corresponding source for the exact FFmpeg build and its enabled libraries under their applicable licences. The Windows application's MIT licence does not replace those obligations. The source ZIP produced by this repository contains XDC/Studio code; it is not a complete source archive for every bundled FFmpeg library. Use the exact build provider's source/build information when assembling the public release's corresponding-source materials.

## Algorithms and artwork

Oklab, composite CGA measurements and blue-noise references are credited in [THIRD-PARTY.txt](../THIRD-PARTY.txt) and [CGA-NTSC.txt](CGA-NTSC.txt). No external emulator implementation is linked. See [artwork provenance](../assets/README.md) for the icon; third-party artwork is not relicensed by the code's MIT licence.
