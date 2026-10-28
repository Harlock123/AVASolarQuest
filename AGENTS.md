# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers playing the game, its controls and
what the tests check; this file covers how the code is put together and the rules that keep it working.

## What this is

A colour vector shooter in the style of Cinematronics' 1981 arcade game Solar Quest, in C# on .NET 10 and
Avalonia 12, drawn through Avalonia's `DrawingContext`. One self-contained executable per platform (Windows, macOS and Linux, x64 and
ARM). Everything in it is original: the ships are drawn in code, and the sounds and tunes are synthesised by
`tools/make_sounds.py`.

## Commands

```bash
dotnet build AVASolarQuest.slnx                   # the SDK version is in global.json (10.0.400 or later)
dotnet test AVASolarQuest.slnx                    # every test runs headless: no window opens
dotnet run --project src/AVASolarQuest            # play it; add -- --no-audio for silence
SOLARQUEST_SHOTS=$PWD/Images dotnet test tests/AVASolarQuest.Tests --filter ScreenshotTests   # refresh the README's pictures
SOLARQUEST_PROBE=1 dotnet test tests/AVASolarQuest.Tests --filter PilotProbe --logger "console;verbosity=detailed"
python3 tools/make_sounds.py                      # rebuild src/AVASolarQuest/Sounds (standard-library Python only)
```

The probe reports how the computer player gets on, wave by wave, for tuning; `SOLARQUEST_PROBE=lazy` runs one
that just spins and fires. Without the variable it does nothing.

`Directory.Build.props` sets `TreatWarningsAsErrors`, nullable reference types and implicit usings for every
project: a warning fails the build, so keep it at zero.

## Layout

- `src/AVASolarQuest/Engine/`: the game, with no Avalonia drawing in it.
  - `GameEngine.cs` runs the states (title, attract, play, pause, game over, initials, high scores) and the
    fixed-step loop; `GameEngine.Play.cs` has the play itself: the waves, the enemy, the escape pods and the
    rescue chain.
  - `Entities.cs` has `Controls`, the wrapping screen and the sun's pull (`Field`, including `Field.Step`), the
    ship, bolts, enemies and pods.
  - `ShipPilot.cs` is the computer player, used by the attract mode and the tests.
  - `InputHandler.cs` (keys) and `ControllerManager.cs` (SDL2 gamepad) produce `Controls`.
  - `HighScoreTable.cs`, `InitialsEntry.cs` and `SoundManager.cs` do what their names say.
- `src/AVASolarQuest/Views/`: `GameCanvas` (split over partial files) draws a fixed 1024x768 picture scaled to
  the window and runs the 60 Hz timer.
- `src/AVASolarQuest/Audio/`: the OpenAL Soft audio engine, shared with the author's other AVA games.
- `src/AVASolarQuest/Sounds/`: the generated WAV files.
- `tests/AVASolarQuest.Tests/`: xUnit v3 with `Avalonia.Headless.XUnit`: `GameplayTests`, `ScreenshotTests`,
  `ShiftReleaseTests` and `PilotProbe`.

## Rules that keep it working

- **Fixed steps.** `GameEngine` advances in fixed 1/60 s steps (`Field.Step`), independent of the frame rate, so
  tests can play whole waves deterministically from a seed. Draw random numbers from the engine's seeded
  `Random` (`new GameEngine(seed, table)`), never from `Random.Shared`.
- **Engine first, drawing second.** Gameplay belongs in `Engine/`, where tests can reach it without a window.
  `Views/` only reads the engine and draws it.
- **Input goes through `InputHandler`.** `Controls` has no up/down axis: it is `Turn` (negative is left), `Thrust`
  (Up, W, stick up), `Fire` and `Hyper` (Down, S, Shift, X). Test new controls through `InputHandler`, not by
  building `Controls` directly.
  - Keys are tracked by `PhysicalKey`, because on some Linux setups Avalonia reports the release of Shift as
    CapsLock. `ShiftReleaseTests` covers this.
- **Test hooks.** `GameEngine.Calm` (internal, for tests) makes the enemy hold still and hold its fire, and stops
  more coming in.
- **SDL under one lock.** `ControllerManager` starts, polls and stops SDL only inside `lock (Sdl)`. The tests
  create many handlers in parallel, and unlocked SDL calls corrupt its heap.
- **Audio.** OpenAL Soft through Silk.NET. `Directory.Build.targets` swaps in the official Windows `soft_oal.dll`
  from `Audio/native/`, which needs no Visual C++ runtime. In a virtual machine `OpenAlConfig` writes a larger
  mixing period to `ALSOFT_CONF` so the sound doesn't break up.
- **Tests never touch the player's files.** High scores live in the app-data folder (`AVASolarQuest`). A
  `GameEngine` built without a table loads that file, so tests load the table from a temporary folder and set
  `Silent = true`.

## Making a change

- Add or update tests with every gameplay change. `dotnet test` must pass, with no warnings.
- If the change shows on screen, refresh the screenshots with the command above, and look at them.
- Update `README.md` (features, controls, the test list) to match.
- New sounds go into `tools/make_sounds.py`, not as hand-edited WAV files.

## CI and releases

`.github/workflows/build.yml` builds and tests every push and pull request. It then publishes self-contained
single-file builds for win-x64/x86/arm64, linux-x64/arm64/arm and osx-x64/arm64. Every run keeps the builds as
workflow artifacts. Only pushes to `main` replace the assets of the `latest` pre-release, and a `v*` tag makes a
proper release. After a release it asks the portfolio site to rebuild, if the `SITE_REFRESH_TOKEN` secret is set.
