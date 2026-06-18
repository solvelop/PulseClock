# Pulse Clock for Windows

A tiny system-tray clock that shows the current **@pulse** - Universal Internet
Time - right in your taskbar. One number, the same for everyone on Earth, no
time zones.

<img src="https://pulses.day/img/PulseClock_Screenshot.png" />

## What it does

- Shows the current **@pulse beat (0-999)** as the tray icon, updating as the
  day advances. `@500` is noon UTC; the day is split into 1000 beats.
- Hover the icon for the exact beat and your local time.
- **Left-click** opens a small flyout: the live beat, your local + UTC time, and
  a two-way converter (local time to @pulse, and @pulse to local time).
- **Right-click** for: copy your Pulse booking link, open Pulse, settings, start
  with Windows, and quit.

The clock and converter work entirely offline - no account, no network, no
tracking. The only thing that uses your details is the booking-link shortcut,
and that just stores a handle you type in once.

## Install

1. Download `PulseClock.zip` from the latest [release](../../releases).
2. Unzip it anywhere (for example, your Documents folder).
3. Run `PulseClock.exe`. The beat appears in your system tray.
4. Right-click the tray icon → **Settings** to set your booking handle (optional,
   only needed for "Copy booking link").
5. Right-click → **Start with Windows** if you want it to launch at sign-in.

Requires Windows 10 or 11 (.NET Framework 4.x, which is already on every modern
Windows).

## Heads up: the SmartScreen warning is expected

This app is **not code-signed**, so the first time you run it Windows may show a
blue **"Windows protected your PC"** SmartScreen prompt. That is normal for a
small open-source tool, not a sign that anything is wrong.

To run it: click **More info**, then **Run anyway**.

If you would rather not click through that, **build it yourself from the source
in this repo** (see below) - the exe you build locally won't trigger the prompt.

## Build it yourself

No Visual Studio, no NuGet, no SDK required - just Windows.

1. Clone or download this repo.
2. Double-click `build.cmd` (or run it from a terminal).
3. It compiles `PulseClock.exe` next to the source using the .NET Framework
   compiler that ships with Windows.

Prefer Visual Studio or the .NET CLI? Open `PulseClock.csproj` and build, or run
`dotnet build -c Release`.

The entire app is one readable file, `PulseClock.cs`. Read it before you run it -
that transparency is the point.

## Verify the download

This app is unsigned, so the way to be sure the file is genuine is to check its
**SHA-256 checksum** against the value published in the release notes.

After downloading, open PowerShell in the folder with the file and run:

```powershell
Get-FileHash .\PulseClock.zip -Algorithm SHA256
```

Compare the `Hash` it prints to the `SHA-256` listed on this release. If they
match, the file is exactly what was published. If they don't, do not run it -
re-download, and if it still differs, open an issue.

(For maintainers: generate the value to paste into the release notes the same
way - `Get-FileHash .\PulseClock.zip -Algorithm SHA256` - after building from the
committed source.)

## What it does NOT do

- It does not show your upcoming meetings. A desktop app can't borrow your
  browser's Pulse sign-in, and this tool deliberately avoids adding a separate
  login just to be a clock.
- It sends no data anywhere. Settings live in a plain text file at
  `%AppData%\PulseClock\settings.txt`.

## About @pulse

Universal Internet Time divides the day into 1000 beats, anchored to UTC, so a
time like `@437` means the same instant for everyone, everywhere. Learn more at
[pulses.day](https://pulses.day).

## License

See [LICENSE](LICENSE).

---

Made by [Solvelop](https://solvelop.com). Part of [Pulse](https://pulses.day).
