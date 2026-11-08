# MFGUnlocker GUI

An **unofficial** Windows helper for [MFG Unlocker](https://github.com/dashdogy/MFG-Unlocker)
by **dashdogy** made 100% with Claude with me providing direction.
Pick a game's install folder and it works out where the mod goes,
installs it, and helps if it doesn't work. It can also upgrade and remove it again.

> **Not affiliated with or endorsed by dashdogy or the MFG Unlocker project.**
> All credit for MFG Unlocker itself, and for its game compatibility list, goes to
> [dashdogy/MFG-Unlocker](https://github.com/dashdogy/MFG-Unlocker). Please read
> their [install guide](https://github.com/dashdogy/MFG-Unlocker/blob/main/docs/install.md)
> and [troubleshooting](https://github.com/dashdogy/MFG-Unlocker/blob/main/docs/troubleshooting.md),
> and report problems with the mod itself to them, not here.

## ⚠️ Limited testing: use at your own risk

This tool has been tested on **one PC** (Windows 11, NVIDIA RTX 3090) with **two
games** (The Outer Worlds 2 from Battle.net and The Witcher 3 from GOG). It may get
things wrong on other setups. Modding a game can cause crashes or other problems.
**Never use it on games with anti-cheat**: that can get your account banned. The
tool tries to detect anti-cheat, but it can't promise to find every kind.

It comes with no warranty and no promise of support (see [LICENSE](LICENSE)).

## What it does

- **Finds the real game program**, not the launcher, and the folder the mod must go in.
- **Recognises the game engine** (Unreal, Unity, RE Engine, REDengine) to choose which filename to try first.
- **Checks for anti-cheat** (EasyAntiCheat, BattlEye and others) and refuses to install if it finds any.
- **Checks the game has DLSS Frame Generation**, which the mod needs.
- **Picks a filename the game will load**, using dashdogy's compatibility list (community reports) when the game is listed. It never uses a name another file or mod already uses.
- **Downloads MFG Unlocker from dashdogy's official GitHub releases**, only after you agree. It checks the download against dashdogy's published SHA-256 checksums.
- **Troubleshooting:** if the mod's menu doesn't appear, it checks whether Windows blocked it, then offers the next filename to try. It also has the advice from dashdogy's troubleshooting guide.
- **Upgrade and uninstall**, plus a game list so you can upgrade each game when a new version comes out.

## What it never does

- It never overwrites or deletes a file it didn't put there. Old mod files are moved to a backup folder (it keeps the 3 most recent per game).
- It never asks for administrator rights. If a game folder is locked, it tells you, and you decide whether to run it as administrator.
- It never turns off or works around Windows security (Smart App Control, SmartScreen, Defender).
- It never sends anything anywhere. It only downloads from dashdogy's GitHub pages.
- **It doesn't include MFG Unlocker.** The mod is always downloaded from dashdogy's own releases.

## Requirements

- Windows 10 or 11, 64-bit. Nothing else to install: it uses the .NET Framework 4.8 that comes with Windows.
- An NVIDIA RTX 20, 30, 40 or 50 series card, and a game with DLSS Frame Generation. AMD and Intel cards need the OptiScaler/XeMFG route, which this tool doesn't do yet (see dashdogy's [OptiScaler guide](https://github.com/dashdogy/MFG-Unlocker/blob/main/docs/optiscaler.md)).

## How to use it

1. Download the `MFGUnlockerGUI_v….zip` file from [Releases](../../releases). Right-click it, choose **Extract All…**, then put `MFGUnlockerGUI.exe` anywhere you like. There's no installer. The release notes list the zip's SHA-256 fingerprint if you want to check your download.
2. Run it. The first time, it offers to download the latest MFG Unlocker and shows the release notes. Click OK.
3. Click **Add a game…** and pick the game's install folder.
4. Check what it found, then click **Install**.
5. Start the game and press **Backspace** for the mod's menu. Come back and click **It worked** or **It didn't work…**.

**RTX 30 and 20 cards:** if the game's frame generation option is missing or stays
off, tick **Game FG option: Unlock** in the mod's menu, then turn frame generation on
in the game's settings.

**Windows may warn you** the first time you run a downloaded copy, because the
program isn't code-signed. If you choose to run it anyway, that's your decision.
This project will never ask you to turn Windows security off.

Settings and backups are kept in `%LOCALAPPDATA%\MFGUnlockerGUI\`.

## Coming soon

Plans, not promises, and no dates:

- **AMD and Intel cards:** the OptiScaler / XeMFG route, with automatic graphics card detection.
- **More games tested**, with results shared back to dashdogy's compatibility list.

## Building it yourself

Needs the .NET SDK (any recent version). Then:

```
dotnet build src/MFGUnlockerGUI -c Release
dotnet test tests/MFGUnlockerGUI.Tests
```

## Licence

MIT. See [LICENSE](LICENSE). This project includes a copy of dashdogy's game
compatibility list, which is under its own MIT licence. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
