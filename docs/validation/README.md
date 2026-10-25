# Reference validation

These reports record earlier checks and comparisons on the privately supplied MIR.webm. Absolute workspace/user paths have been replaced with placeholders. The input video and generated comparison media are not redistributed here.

The versioned JSON files preserve the method and measurements; historical paths within them describe their original run layout. They are not instructions for the current repository layout. Use `tests/run.ps1` for the current self-contained checks and `tests/README.md` for optional MIR comparisons.

NTSC colour and temporal errors are model measurements, not physical CRT calibration or universal perceived-flicker scores. Reference 0.7 results compare all three dither masks and stabilization off/on; 0.7.1 covers offline help. Renaming old mode in 0.7.2 does not change the encoded bytes.
