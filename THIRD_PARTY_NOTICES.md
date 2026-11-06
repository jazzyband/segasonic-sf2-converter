# Third-party code and tools

This repository does not include SegaSonic's ROM, music sequences, instrument definitions, or samples.

- **SegaSonic Decomp** (https://github.com/cheviman/segasonic-decomp), by its respective contributors, provides the extraction tools and YM3438 emulation library. At the time this package was prepared, the repository advertised a BSD-3-Clause license. Respect its license and the licenses of its dependencies when redistributing any of its code.
- **`runtime/src/ym3438.c` and `runtime/include/s32/ym3438.h`** are built from a user-supplied checkout of SegaSonic Decomp. The emulator source is **not copied or ported** into this repository.
- **`native/segasonic_fm_bridge.c`** is this project's thin C interface to the upstream YM3438 implementation. It uses the upstream `s32_fm_*` API.
- The C# MIDI, WAV, SoundFont, rendering orchestration, pitch correction, looping, and drum balance code in `src/` is this project's code, replacing its original Python implementation. No third-party Python packages or conversions of the upstream decompiler are bundled.