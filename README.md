# NukedOPL3Sharp

A C# port of **nukeykt**’s Nuked-OPL3 FM synth core, plus a small MIDI player application that demonstrates how to drive
the chip from a Standard MIDI (.mid) file.

The output hash from Nuked-OPL3 was generated for several scenarios and embedded in tests to guarantee parity - it is
bit by bit identical.

## Projects

- `NukedOPL3Sharp` (chip/core library)
    - Implements the OPL3 chip as a register-driven synthesizer.
    - Main entry point: `NukedOPL3Sharp.Opl3Chip` (`Reset`, `WriteRegister`, `GenerateStream`, etc.).
- `NukedOPL3Sharp.MidiPlayer` (demo application)
    - Loads `.mid` / `.midi` files.
    - Loads instrument banks in `.op2` and `.wopl` formats (user-selected; not bundled/autoloaded).
    - Renders audio by translating MIDI events into OPL3 register writes and then pulling PCM from `Opl3Chip`.
- `NukedOPL3Sharp.Benchmarks` (BenchmarkDotNet suite)
    - Separately measures dormant, 2-op, 4-op, rhythm, and dynamic buffered/resampled paths in nanoseconds per frame.

## Benchmarks

Build before measuring so BenchmarkDotNet uses current Release binaries:

```powershell
dotnet build NukedOPL3Sharp.Benchmarks\NukedOPL3Sharp.Benchmarks.csproj -c Release
dotnet run --project NukedOPL3Sharp.Benchmarks\NukedOPL3Sharp.Benchmarks.csproj -c Release --no-build -- --filter '*' --artifacts BenchmarkDotNet.Artifacts\current
```

## How the MIDI demo wires up the chip

At a high level, the data flow is:

1. `MidiFile` parses the `.mid` file into events.
2. `MidiSequence` schedules events over time (ticks → samples) at a fixed sample rate.
3. `OplMidiSynth` receives MIDI events (note on/off, CC, program change, pitch bend), looks up patches from the loaded
   `.op2`/`.wopl` bank, and writes the corresponding OPL register values into an `Opl3Chip`.
4. `MidiPlaybackEngine.Render(...)` asks the chip for audio via `Opl3Chip.GenerateStream(...)` into an interleaved
   stereo buffer, then applies master gain and a light DC high-pass filter.
5. `PlaybackService` streams the generated audio to the sound device via OpenAL.

This is intentionally structured so the demo shows the minimum “glue” required to:

- convert MIDI timing to sample timing,
- map MIDI programs/drums to OPL patches,
- and drive `Opl3Chip` purely through register writes.

## License

- `NukedOPL3Sharp` (chip/core library): **GNU LGPL v2.1** — see `LICENSE`.
- `NukedOPL3Sharp.MidiPlayer` (application code): **BSD 3-Clause** — see `NukedOPL3Sharp.MidiPlayer/LICENSE.txt`.

Note: `NukedOPL3Sharp.MidiPlayer` depends on the LGPL-licensed `NukedOPL3Sharp` library, so redistributions must comply
with both.

## Third-party notices

See `THIRD_PARTY_NOTICES.md`.
