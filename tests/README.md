# Tests

Run `./tests/run.ps1` after building the portable app. Requires the pinned local .NET SDK and Python 3. No external video is downloaded or distributed.

- `NativeCoreTests`: exact convergence and 8088/disk budgets for synthetic frames.
- `OutputTransactionTests`: cancellation, atomic replacement and locked-file rollback.
- `TemporalContracts`: NTSC masks, colour endpoints, stable noise, scene changes, moving edges and deterministic stabilization.
- `integration-smoke.py`: generates a two-second FFmpeg test pattern and tone, converts all profiles plus old mode and late/half-height Composite, then independently decodes video/audio and exercises seeks.
- `verify_xdv.py`: independent Python XDV/8088 decoder; `python tests/verify_xdv.py FILE.XDV`.

Each run gets a fresh folder under ignored `tests/output/`. `-SkipIntegration` omits only the generated-media portion. No GUI automation or physical 8088 is required for CI. Full physical playback remains a separate check.

## Optional MIR comparisons

Place your own source at `local/media/MIR.webm`. It is deliberately excluded from Git and the source archive. `temporal-integration.py OUTPUT` tests the current release on full MIR and runs additional short conversions. Historical byte-for-byte assertions are optional: they run when the corresponding `local/reference/0.5/` and `local/reference/0.6/` XDV files are available, otherwise the report explicitly records their absence.

`TemporalTests`, `TemporalComparison`, `NtscEncodingTests`, `NtscTests`, `ColorTests` and `DitherAnalysis` are research/diagnostic tools. Supply repository root and a fresh output directory as arguments. Some comparisons need the retained historical XDV references; they are not included in public CI. Reference results are under `docs/validation/`.

Obsolete one-off scripts and generated prototype outputs were removed from the active tree during repository preparation. Their source snapshots remain in ignored `local/archives/`. The ongoing suites above remain in the repository.
