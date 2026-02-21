# Third-Party Notices / Attribution

This repository contains original code plus third-party components. The license texts for third-party components are
included in this repository in their respective directories.

## Project licenses

- `NukedOPL3Sharp/` (chip/core library): **GNU LGPL v2.1** (see `LICENSE`).
- `NukedOPL3Sharp.MidiPlayer/` (application code): **BSD 3-Clause** (see `NukedOPL3Sharp.MidiPlayer/LICENSE.txt`).
    - Note: the application links against the LGPL-licensed `NukedOPL3Sharp` library, so binary redistributions must
      comply with both.

## What gets shipped

- The `NukedOPL3Sharp` **NuGet package** ships only the `NukedOPL3Sharp/` library (plus `LICENSE`, `README.md`, and this
  notices file).
- The `NukedOPL3Sharp.MidiPlayer` application does **not** ship any upstream/reference repositories that may be present
  in the source tree for development/verification.

## Nuked-OPL3 (upstream)

- Project: Nuked-OPL3 by **nukeykt**
- Used in: `NukedOPL3Sharp/` (C# port)
- License: **GNU LGPL v2.1**
- Upstream: https://github.com/nukeykt/Nuked-OPL3
- License text: `LICENSE` (LGPL v2.1)

## ymfmidi + ymfm (reference implementation / assets)

- Project: **ymfmidi** by **Devin Acker** and **ymfm** by **Aaron Giles**
- Used in:
    - Portions of the `NukedOPL3Sharp.MidiPlayer` codebase are ported from `ymfmidi` and remain under the BSD 3-Clause
      terms below.
- License: **BSD 3-Clause**
- Upstream:
    - ymfmidi: https://github.com/doomtech/ymfmidi (or your fork)
    - ymfm: https://github.com/aaronsgiles/ymfm
- License texts:
    - ymfmidi: `LICENSES/BSD-3-Clause-ymfmidi.txt`
    - ymfm: `LICENSES/BSD-3-Clause-ymfm.txt`

Ported files (BSD 3-Clause; see SPDX headers in-file):

- `NukedOPL3Sharp.MidiPlayer/Core/Patches/OplPatchNames.cs`
- `NukedOPL3Sharp.MidiPlayer/Core/Patches/Op2BankLoader.cs`
- `NukedOPL3Sharp.MidiPlayer/Core/Patches/WoplBankLoader.cs`
- `NukedOPL3Sharp.MidiPlayer/Core/Synth/OplMidiSynth.cs`

## NuGet dependencies (not vendored)

The `NukedOPL3Sharp.MidiPlayer` application depends on third-party NuGet packages (e.g. Avalonia, Silk.NET). These are
not vendored in the repository source tree; consult the packages’ own license metadata when redistributing binaries.
