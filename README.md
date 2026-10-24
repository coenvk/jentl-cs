# Jentl

Jentl is a small C# game engine on [MonoGame](https://monogame.net) (desktop) and [KNI](https://github.com/kniEngine/kni)
(the browser, through Blazor WebAssembly): one code base, both platforms. It grew out of an old Java engine of the same
name, and powers the games in [games-cs](https://github.com/coenvk/games-cs).

The game logic kits are framework-free (plain C#, easy to unit test, usable in tools and servers); the screen layer
draws them.

| Package | What it is |
|---|---|
| `Jentl.Core` | The screen layer for MonoGame: game loop and scenes, input (keyboard, pointer, gamepad), UI widgets, audio and a synthesizer, particles and sprite animation, camera and virtual resolution, render layers, lighting and post effects, timers and coroutines, a debug overlay, localization, and an asset store with hot reload |
| `Jentl.Core.Kni` | The same, built against KNI for the browser |
| `Jentl.Puzzle` | Grid puzzles: boards, rules through recorded turns, exact undo and redo, level files |
| `Jentl.Cards` | Card games: cards, piles, tables, rules, undo and redo, hints |
| `Jentl.Rpg` | Turn-based RPGs: Tiled maps, field of view, actors and combat, items, quests, dialogue, saves |
| `Jentl.Multiplayer` | Turn-based games for humans and bots at one table |
| `Jentl.Net` | Online play: a JSON protocol, an authoritative room server and a WebSocket client |
| `Jentl.Ecs` | An entity component system |
| `Jentl.Physics` | 2D rigid-body physics |
| `Jentl.Collect` | Collecting games: weighted draws (packs, loot), counters that recharge on the wall clock (tickets, energy), calendar rotations |
| `Jentl.Tracks` | Tracks and roads: paths built from straights and arcs, with position, heading and curvature along them |
| `Jentl.Assets` | PNG reading and writing, a pixel canvas for art drawn in code, texture atlases, asset checks |
| `Jentl.Fluffy` | Fluffy, a finite state machine engine |
| `Jentl.Assets.Tool` | The `jentl-assets` command line: pack atlases, check assets |

The guide is in [docs/kits.md](docs/kits.md), and Fluffy has its own in [docs/fluffy.md](docs/fluffy.md).

## Using it

Releases are published to GitHub Packages. GitHub asks for a token (with `read:packages`) even for public packages:

```bash
dotnet nuget add source https://nuget.pkg.github.com/coenvk/index.json --name jentl --username <you> --password <token>
dotnet add package Jentl.Core            # or Jentl.Core.Kni for a browser build
```

Without a token, build them from a checkout instead: `dotnet pack Jentl.slnx -c Release` puts every package in
`artifacts/packages`, which works as a local package source. (The games repository does exactly that in CI.)

## Building

```bash
dotnet test Jentl.slnx
```

Requires the .NET 10 SDK. The build treats warnings as errors and runs the recommended analyzers.

## Releasing

Tag a version (`git tag v0.2.0 && git push --tags`); CI tests, packs everything with that version and publishes it.
