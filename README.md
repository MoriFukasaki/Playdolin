# Playdolin

**Version 1.0.2** — a free-play Needolin mod for Hollow Knight: Silksong.

Play individual notes and chords across three octaves using your keyboard, with optional reverb. The game's original Needolin actions remain available when free-play mode is off.

### What you need

- BepInEx installed and working.
- Needolin unlocked in your save.

Tested with BepInEx 5.4.23.2 and the installed Unity 6000.0.50 game build. Compatibility with other game or loader versions has not been verified.

### Installation
To install it, simply copy `Silksong Playdolin.dll` into the game's `BepInEx/plugins` folder. Replace the previous Playdolin DLL if updating. Keep only one installed copy.

The audio samples are included in the mod. No separate WAV files are required. If it is already working for your other mods, you can use the existing installation.

### Playing the instrument

Press **F9** to enter or exit free-play mode. Start while standing on the ground, with normal control available, outside a bench or the inventory.

While active, Hornet holds the Needolin playing pose. Movement and combat input are blocked, and the inventory cannot be opened. Press multiple note keys to play chords. Press F9 again to restore normal control.

Pausing, leaving gameplay, changing scenes, death, hazards, recoil, or another system taking control ends free play and stops the sound. Press F9 again when normal gameplay resumes. Free play does not make Hornet invincible.

### Keyboard layout

Each pitch uses its own embedded WAV sample, played at its original pitch and duration.

| Row | Keys, left to right | Notes, left to right |
|---|---|---|
| Upper — Test 1 | Q W E R T Y U I | C5 D5 E5 F5 G5 A5 B5 C6 |
| Middle — Test 2 | A S D F G H J | C4 D4 E4 F4 G4 A4 B4 |
| Lower — Test 3 | Z X C V B N M | C3 D3 E3 F3 G3 A3 B3 |

#### Flats with Shift

Hold **either Shift key** while pressing a note key to access the five additional pitches in each octave:

| Normal note | With Shift |
|---|---|
| C | D♭ |
| D | E♭ |
| E | E |
| F | G♭ |
| G | A♭ |
| A | B♭ |
| B | B |

This applies to all three rows. I remains C6 with or without Shift. Shift affects newly triggered notes only; it does not change notes already ringing. For example, A plays C4 and Shift+A plays D♭4.

### Optional settings

The defaults work without editing anything. Use this section only if you want
to change the toggle key, volume, playing pose, or reverb.

After the first launch, close the game and edit:

```text
BepInEx/config/com.example.playdolin.cfg
```

Default settings:

```ini
[Instrument]
ToggleKey = F9
Volume = 0.7
PlayingAnimation = true

[Reverb]
Enabled = true
DecaySeconds = 2.2
Level = -1200

```

| Setting | Effect |
|---|---|
| `ToggleKey` | Key used to enter or exit free play. Choose a key outside the note layout. |
| `Volume` | Instrument volume, from 0 to 1. |
| `PlayingAnimation` | Enable the Needolin pose. Audio remains usable if the animation is unavailable. |
| Reverb `Enabled` | Enable or disable the reverb tail. |
| `DecaySeconds` | Reverb decay setting, from 0.1 to 8 seconds. Higher values produce a longer tail. |
| `Level` | Reverb level in millibels, from -10000 to 0. Try -2000 for subtle, -1200 for medium, or -600 for stronger reverb. |

Existing configuration values are preserved when updating the mod, except the old default volume of 0.35 is upgraded once to 0.7. Custom volume values are retained; you can adjust Volume again after the upgrade.

### Current limitations

- Keyboard controls only; no instrument overlay or individual note rebinding.
- Notes play as plucked samples. Holding a key does not sustain or repeat a note.
- Shift is shared across newly played notes, so mixed natural/flat chords may need staggered presses.
- Up to 16 voices can ring at once; additional notes replace an existing voice.
- Free play cannot be started while sitting at a bench.

### Troubleshooting

If F9 does nothing, check that Needolin is unlocked, Hornet is standing on the ground, and no pause menu, inventory, bench interaction, or cutscene is active. Check `ToggleKey` if you changed it.

If sound is missing, check `Volume`, the game's audio settings, and `BepInEx/LogOutput.log`. Look for entries from `Playdolin Plugin` and `Needolin Audio Test`. Confirm that only one Playdolin DLL is installed.

For animation problems, set `PlayingAnimation = false` to continue using the instrument without the pose. For audio comparison, set reverb `Enabled = false`.

For a normal installation, this is all you need. The section below is for people
who want to edit or build the mod themselves.

---

## For Developers

### Development requirements

- A local Hollow Knight: Silksong installation with working BepInEx 5.
- Visual Studio with the **.NET desktop development** workload.
- The **.NET Framework 4.7.2 targeting/developer pack**, matching the project target.

The project references DLLs supplied by the game and BepInEx. These include
`Assembly-CSharp.dll`, `PlayMaker.dll`, Unity modules, and `BepInEx.dll`.
They are development references, not extra files that players must download.

### Build from source

1. Download or clone the source project.
2. Open `Silksong Playdolin.slnx` in Visual Studio. If your Visual Studio cannot
   open `.slnx` files, open `Silksong Playdolin/Silksong Playdolin.csproj` directly.
3. Check the project's references. The saved paths point to the original local
   Steam installation. If your game is installed elsewhere, update the
   `HintPath` entries in the `.csproj`, or remove and re-add unresolved references
   from your game's `Hollow Knight Silksong_Data/Managed` and `BepInEx/core` folders.
4. Select **Release** and build the project.
5. Close the game and copy **only** the resulting mod DLL from
   `Silksong Playdolin/bin/Release/Silksong Playdolin.dll` into `BepInEx/plugins`.

Do not distribute or install the game, Unity, or BepInEx dependency DLLs that
may appear in the build output. The 37 individual note samples (C3 through C6) are embedded resources
and are included automatically in the mod DLL.

### FSM diagnostics

To inspect Needolin FSMs, enable the following section in the mod configuration:

```ini
[Diagnostics]
Enabled = true
```

Press F8 in a loaded game to write the FSM dump to `BepInEx/LogOutput.log`.
The dump can be large. Diagnostics are disabled by default; turn them off
after troubleshooting.

### Validation

The Release build completed successfully. The project owner confirmed the listed in-game checks passed, including free-play entry/exit, notes and chords, the playing pose, inventory/control restoration, pause, damage/death, scene changes, and the original Needolin actions outside free play.
