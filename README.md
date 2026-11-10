# zMuzy WeaponPhysics

[![Build](https://github.com/zMuzy/zMuzy-WeaponsPsychics/actions/workflows/build.yml/badge.svg)](https://github.com/zMuzy/zMuzy-WeaponsPsychics/actions/workflows/build.yml) [![Release](https://img.shields.io/github/v/release/zMuzy/zMuzy-WeaponsPsychics)](https://github.com/zMuzy/zMuzy-WeaponsPsychics/releases/latest) [![License](https://img.shields.io/github/license/zMuzy/zMuzy-WeaponsPsychics)](LICENSE)

A [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) plugin for CS2 that makes guns lying on the ground react to hits. Shoot near a dropped weapon and it gets knocked away; slash it with a knife and it flies off. Knives, grenades, C4 and other utility items are left alone.

It works well next to zMuzy JailBreak (Gun Toss gets a lot more fun), but it doesn't need it.

## Requirements

- CS2 dedicated server with Metamod:Source 2.0.
- CounterStrikeSharp 1.0.379 or newer (with-runtime package, .NET 10).

## Install

1. Download `MuzyWeaponsPhysics-vX.Y.Z.zip` from the [latest release](https://github.com/zMuzy/zMuzy-WeaponsPsychics/releases/latest).
2. Unzip it into `game/csgo/` on the server. You should end up with `game/csgo/addons/counterstrikesharp/plugins/MuzyWeaponsPhysics/MuzyWeaponsPhysics.dll`.
3. Restart the server, or run `css_plugins load MuzyWeaponsPhysics` in the server console.

The first start writes `game/csgo/addons/counterstrikesharp/configs/plugins/MuzyWeaponsPhysics/MuzyWeaponsPhysics.json`.

## Configuration

| Setting | Default | Range | What it does |
|---|---|---|---|
| `Enabled` | `true` | | Master switch. |
| `EnableBulletBoost` | `true` | | Bullets push nearby weapons. |
| `EnableKnifeBoost` | `true` | | Knife swings push weapons in front of the player. |
| `BulletImpactRadius` | `52` | 8 to 256 | How close a bullet has to land to a weapon, in units. |
| `BulletBoostSpeed` | `1150` | 50 to 4000 | Speed given by a bullet hit. |
| `KnifeReach` | `125` | 16 to 256 | How far a knife swing reaches. |
| `KnifeLateralTolerance` | `48` | 8 to 160 | How far off-center a knife swing can be and still hit. |
| `KnifeBoostSpeed` | `1450` | 50 to 4000 | Speed given by a knife hit. |
| `VerticalBoost` | `65` | 0 to 1000 | Extra upward push so weapons hop instead of sliding. |
| `MaxWeaponSpeed` | `1700` | 100 to 5000 | Speed cap for a pushed weapon. |
| `ImpulseCooldownSeconds` | `0.05` | 0 to 1 | Minimum time between pushes on the same weapon. |

Values outside the range are clamped when the config loads.

## Build

You need the .NET 10 SDK.

```bash
dotnet build MuzyWeaponsPhysics.csproj -c Release
```

Pushing a `v*` tag builds the plugin on GitHub Actions and attaches the zip to a release.

## License

GPL-3.0. See [LICENSE](LICENSE).

## Credits

- [zMuzy](https://muzy.io), with AI assistance.
