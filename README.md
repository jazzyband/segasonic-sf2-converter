# SegaSonic MIDI & SoundFont Converter

A C#/.NET 8 command-line converter for the extracted sound assets of **SegaSonic the Hedgehog**. It outputs a single **`SegaSonic.sf2`** with FM and PCM instruments and 14 MIDI files with FL Studio compatibility.

## What the converter produces

```text
Output/
  SegaSonic.sf2
  instrument_mapping.csv
  MIDI/
    14 .mid files
```

All original notes and tempos are retained. The MIDI tracks are split by channel, named and assigned the correct SoundFont program numbers. Drum note-offs (48 ticks) are inserted so FL Studio shows the percussion notes.

The SF2 contains the original extracted PCM waveforms, emulator-rendered YM3438 FM instruments, the corrected `-12`/`-24` FM sample-selection zones, individually optimized FM sample loops, oneshot envelopes for naturally fading FM sounds, and 5 dB quieter drum presets.

## Requirements

- Windows 10/11, **.NET 8 SDK** ([download](https://dotnet.microsoft.com/download/dotnet/8.0))
- Visual Studio C++ Build Tools or MinGW-w64 GCC
- [cheviman's SegaSonic Decomp](https://github.com/cheviman/segasonic-decomp)
- A copy of SegaSonic Japan Rev. C ROM
- `sound.zip`, a ZIP of the contents of SegaSonic Decomp's `assets/sound/` folder

## 1. Extract the sound assets

Clone cheviman's decompile locally:
```cmd
git clone --recursive https://github.com/cheviman/segasonic-decomp.git
cd segasonic-decomp
mkdir rom
```

Place your `sonic.zip` inside `segasonic-decomp\rom\` (create said folder). Then run:
```cmd
python tools/verify_rom.py
python extract_assets.py rom\sonic.zip
```

Then package the contents of the extracted sound directory. Inside `sound.zip`, paths should begin with `data/` and `samples/`, not an extra `sound/` folder.

## 2. Build from Windows

From **x64 Native Tools Command Prompt**, inside this converter's root folder:
```cmd
build_windows.bat "[LOCATION]\segasonic-decomp" "[LOCATION]\segasonic-decomp\sound.zip"
```

The script builds the small C bridge against the upstream native YM3438 implementation and launches the converter. Output is written to `Output/` and the SoundFont is called **`SegaSonic.sf2`**. Optionally supply a third argument to choose another output folder.

### Run the program directly

After the native library has been built, the converter can be invoked without the batch file:
```cmd
dotnet run --configuration Release --project src\SegaSonicConverter\SegaSonicConverter.csproj -- --sound "[LOCATION]\sound.zip" --fm-library "[LOCATION]\segasonic_fm.dll" --output "Output" --drums-db 5
```

On Linux, `./build_linux.sh /[LOCATION]/segasonic-decomp /[LOCATION]/sound.zip` performs the equivalent build using GCC

## 3. Load into FL Studio

1. Import a `.mid` from `Output/MIDI` (keep tracks separate).
2. Put a SF2-capable plugin on the imported tracks.
3. Load `Output/SegaSonic.sf2` into each instance.
4. Select the appropriate preset listed in `instrument_mapping.csv` or in the track names. Bank is **0**.

Additionally, VLC/FluidSynth can also play the MIDI with the generated `SegaSonic.sf2` configured as its SoundFont.

## Notes

- FM patches are rendered using the original SegaSonic Decomp YM3438 C emulation code through `native/segasonic_fm_bridge.c`.
- The C# resampler implements the same Kaiser-windowed polyphase resampling approach as the original Python tool for 55,930 Hz to 44,100 Hz.
- FM looping uses per-sample zero-crossing and waveform matching, and approximately 5 dB of drum attenuation is applied using SoundFont preset attenuation generators.
- The sample-based result still has the inherent limitations of SF2 compared with a live YM3438 synthesizer. The very low/high key ranges use neighboring multisamples.

This project is, of course, unofficial, and is not affiliated with SEGA.
