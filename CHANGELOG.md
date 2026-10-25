# Changelog

## 0.7.2

- Rename the flat-palette encoding choice to **old mode** in both interface languages and help. The JSON value `legacy` is unchanged.
- Prepare the source tree as a fork retaining original XDC Git history, Pascal sources and licence.
- Add bilingual project documentation, pinned Windows CI and tests that generate their own input video.
- Keep private media, historic source snapshots and generated results outside the tracked source tree.

## 0.7.1

- Offline Russian/English HTML help, bottom-right Help button and F1.
- Settings reference, quick start, troubleshooting and original XDC credits.

## 0.7.0

- Temporal NTSC stabilization and update priorities based on estimated 8088 cost.
- Bayer 4×4 and static blue noise, with separate NTSC measurements for each mask.

## Earlier development

- NTSC-aware encoding and composite preview, early/late CGA models.
- Gamma, saturation, dither strength and source/result comparison.
- Native Windows x64 encoder, streaming RGB/PCM and XDV player with audio.
- Initial DOSBox-based prototype, superseded by the native encoder.

Reference validation details are retained in `TEST-RESULTS.txt` and `docs/validation/`.
