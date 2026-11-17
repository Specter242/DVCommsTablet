# Comms Tablet for Derail Valley

A Unity Mod Manager mod: a held tablet that does the comms radio's work on
a screen. A home screen of apps (Vehicles, Cargo, Workshop, Switches,
Tools, Light, Settings) replaces stepping through the radio's lists one
press at a time: choose on the tablet, with a picture of each vehicle or
paint, then aim and use the tablet as you would the radio. It mounts on a
cab gadget mount. Realistic Firing (1.24.0 and newer) and AI Engineer
(0.15.1 and newer) become apps on it (Firing, Engineers) and retire their
own tablets.

- Players: get it from Nexus Mods,
  https://www.nexusmods.com/derailvalley/mods/1845, or through Unity Mod
  Manager. `PLAYER-README.md` is the readme that ships in the zip.
- Changes: `CHANGELOG.md`.
- License: MIT (`LICENSE`). The game's pictures the tablet shows (icons,
  maps) are drawn from the game at run time; none are in this repository.

## Building

Requires the .NET SDK (see `global.json`), Mono for the contract check, and
an installed Derail Valley with Unity Mod Manager; set `DV_INSTALL_DIR` if
the game is not in the default Steam folder on Linux.

- `scripts/build.sh` builds the mod (Release) against the installed game.
- `scripts/check.sh` checks the mod's contract with the game's assemblies
  under Mono, without Unity: the private members it uses, its Harmony
  targets, and its links to Multiplayer, Custom Car Loader, and Realistic
  Firing when those are installed. `PUBLIC_RELEASE=1 scripts/check.sh`
  checks the public build. `scripts/fetch-check-mods.sh` puts DV Car
  Changer 1.5.4's assemblies (its GitHub release, SHA-256 pinned) in
  `.deps/`, and the check then holds the Change page to that version;
  `scripts/package.sh` always fetches them.
- `scripts/package.sh` builds the public release zip in `dist/`.

The in-game self-test the author runs is not part of this repository; the
public release (`-p:PublicRelease=true`) leaves out its hooks.

## Guest apps

Other mods can put an app on the tablet's home screen: publish a static
class named `<YourAssembly>.CommsTabletApp`, and the tablet finds it and
binds its members by reflection, with no reference to `CommsTablet.dll`.
The full contract (version 1), with its members, when each is called, the
screen, the buttons, versioning, and Realistic Firing's and AI Engineer's
working examples, is in [`GUEST-APPS.md`](GUEST-APPS.md).
`src/CommsTablet/Guests/` is the host side.

## Releases

Each release's zip is attached to its GitHub release here, the same bytes
as on Nexus Mods, and listed in `repository.json` once that release exists.

**0.1.2**: the Workshop's Change page for DV Car Changer 1.5.4: switch a
car's modifications on and off.

**0.1.1**: Spawn and Paint pictures fit every engine to its tile, and show
the doors and handles.

**0.1.0**: the first release.
