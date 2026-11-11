# Backlog

What is still to do, for Jentl (this repository) and the games built on it ([games-cs](../games-cs)). New work
comes first, then loose ends of what is already built, then things that work in tests but have never been seen or
heard. Where an idea is my reading of a wish rather than a decision, it says so, with the questions to settle first.

## New features

### A game soundtrack, as RuneScape has its music

Many songs, made for the games, collected so they can be played back.

- What there is: `Synth` renders tunes written as text (pulse, square, triangle, sine, saw and noise voices, with
  envelopes, vibrato and slides), and `SoundPlayer` loops and cross-fades music. Each game now has a song or two.
- To do:
  - Compose a library of songs per game and per mood (menus, play, tension, victory, defeat, calm), in the same
    chiptune style; far more than today, so each place in a game can have its own.
  - A music player, as RuneScape's: every song by name, those not yet unlocked greyed out, unlocked by playing (the
    place or moment the song belongs to), shuffle and loop, the unlocked ones remembered in the saves.
  - Longer songs than a loop of a few bars: sections (intro, A, B, bridge) and an order to play them in, which the
    text format doesn't have yet. The music maker below would write them.
- Questions: one soundtrack shared by all the games, or one per game? A player in each game, or an app of its own
  ("Jukebox")? How many songs to aim for at first?

### A music maker, like Bosca Ceoil

Writing songs on a grid, by mouse or touch, rather than as text.

- An app in games-cs (working title "Composer") on top of `Synth`:
  - Instruments: a wave, envelope, vibrato, slide and volume each, with presets (lead, bass, pad, pluck, and a drum
    kit of kick, snare and hats made of noise and slides), heard as they are changed.
  - Patterns: a piano roll per instrument, a bar or more long, notes placed and stretched on the grid, with a scale
    to keep to and an octave range to scroll.
  - The arrangement: patterns placed on a timeline, one row per instrument, to make the song; tempo and swing.
  - Play from anywhere, loop a section, undo and redo, save and open songs (JSON, in the saves), and export: to the
    `Tune` text a game plays, and to WAV.
- In Jentl: the song format (patterns and an arrangement) and its rendering belong in `Jentl.Core.Audio`, so games
  play what the Composer writes; the editor itself is a games-cs app.
- Question: should songs also play effects (filters, echo), which `Synth` doesn't have yet?

### Animation

What Jentl has: frame animations of sprites (`Animation`, `Animator`, `SpriteSheet`), tweens and easing (`Tween`,
`Easing`), coroutines and timers, particles, Tiled's tile animations, and in 3D node animations from glTF with
cross-fades. What is missing, as far as I can see:

- Sequences of tweens: one after another and side by side, with delays, loops and yoyo, on any value (position,
  scale, rotation, colour), and events at points in them; today each tween runs on its own.
- Animation states for a sprite: idle, run, jump, hurt, with the moves between them and when they happen (Fluffy's
  state machines could drive them).
- Events on frames: a footstep sound on frame 3, a hit box on frame 5.
- Reading animations made in tools: Aseprite's sprite sheets and tags (JSON), so artists draw and Jentl plays.
- Bones for 2D (a body of parts that bend and turn, as Spine or DragonBones make), and skins for 3D models (glTF
  skins; the fox now stands still): both larger pieces of work.
- Little touches: squash and stretch, shake, flash on hit, as ready-made effects.
- Question: which of these matter most to you? Sequences, states and Aseprite are small; bones and skins are not.

### Android

- Games-cs gets an Android head beside Desktop and Web: MonoGame's Android project (`net10.0-android`), touch as the
  pointer (gestures already work), saves in the app's own storage, pausing and resuming with the app, the back
  button as Back, and phone screen shapes (the shell already handles portrait).
- Building and signing an APK (and an AAB for the Play Store), in CI as well.
- Checking every game on a phone: layouts at small sizes, touch targets, performance (the software renderer and the
  physics), sound starting.
- Note: the browser build already runs on phones, as a fallback until then.
- Question: one APK holding all the games, or one app per game?

### Word games: Connect Word and Associate Words

My reading of the names; please correct:

- **Connect Word**: letters in a wheel, swiped across to make words that fill a small crossword (as Wordscapes),
  with levels of growing size, hints and bonus words.
- **Associate Words**: words that belong together: either sorting a grid of sixteen into four groups of four by what
  links them (as the New York Times' Connections), or finding the word that links three others.
- Both need word lists (English and Dutch, as the games are in both) with a licence that allows it, and for
  Associate Words, groups made and checked by hand or checked by a person after they are made.
- A word kit in Jentl may be worth it: word lists, checking words, a crossword fitter for Connect Word.
- Questions: are these the games you mean? In which languages?

### Hot Pan: more for the frying pan

The first version is playable (games-cs `apps/HotPan`): a side-view platformer, the chicken heating from raw pink
through golden to black, the pan getting hotter without end, health burning away on anything too hot, ten points a
second, leaderboards, keys, gamepad and touch. Still to do:

- Power-ups: a cold drink (health back), oven gloves (no burning for a while), a lid (the pan stops heating), ice
  cubes to cool a piece.
- More happening: burnt pieces that stay as hazards instead of being flicked out, a spatula that flips pieces (a hot
  side goes down, a cooler side comes up), oil spitting, pieces landing on the cook.
- Tuning by play: how fast pieces heat and drop, how much burning hurts; play on a phone (touch controls, sizes).
- Character art and animation beyond shapes (walking, jumping, hopping on hot feet), and more sounds (a sizzle that
  grows with the heat, a sigh of relief on cool chicken).
- Questions: should the pan heat level off at some point? A choice of cooks? Daily seeds for a shared challenge?

### Traffic: a simulation, and a game of building roads

A full traffic simulation: cars, vans, trucks and more on roads of every kind, through traffic lights, priority
junctions and roundabouts. It should stand on its own as a kit, so that a game can be built on it: for instance one
where the player lays out the roads, junctions and lights of a town and is scored on how well the traffic flows.

- The simulation, as a framework-free kit (`Jentl.Traffic`), seeded and testable like the others:
  - **Vehicles**: types with their own length, top speed, acceleration, braking and how much room they keep (car,
    van, truck, bus, motorbike; perhaps bikes on their own paths). Following the vehicle ahead smoothly, as the
    Intelligent Driver Model does, and changing lanes when it helps and is safe (as MOBIL does), with drivers who
    differ a little (some cautious, some in a hurry).
  - **Roads**: a network of roads with lanes, each road a path of straights and curves (Jentl.Tracks already builds
    and measures such paths), with speed limits, one-way roads, turning lanes and lane markings that say where a
    lane may go; kinds from a residential street to a motorway with slip roads.
  - **Junctions**: priority from the right or by signs (give way, stop), traffic lights, and roundabouts (giving way
    to traffic already on them, one or two lanes). Vehicles slow for corners, queue, and leave room for a junction
    they can't cross yet so they don't block it.
  - **Traffic lights**: signal groups and phases with green, amber and red times, all-red clearance, fixed cycles,
    lights that react to waiting traffic (detectors), green waves along a road, and turning arrows.
  - **Demand**: trips from where they start to where they go (homes, shops, work), with rush hours; each vehicle
    picks a route through the network (the shortest or the quickest, and going round congestion).
  - **Measurements**: flow per road, travel and waiting times, queue lengths, stops, and where the jams are; enough
    for a game's score and for overlays on screen.
  - Fast enough for a town of thousands of vehicles, in a fixed time step that can run faster than real time.
- On screen (in `Jentl.Core`): drawing the network and the vehicles (from above), lights, overlays of speed and
  jams, and following a vehicle with the camera.
- The game (in games-cs): build roads on a grid or freely, place junctions, lights and roundabouts, and set the
  lights' timings; demand grows as the town does; a budget, goals per level (everyone home within a time, fewer than
  so many seconds waiting on average), and a sandbox; watch the traffic and fix the jams. Mini Motorways and the
  traffic side of Cities: Skylines are the nearest examples.
- Questions: seen from above in 2D, or in 3D (the 3D kit could draw it)? Which country's rules (driving on the right,
  priority from the right as in the Netherlands)? Pedestrians and cyclists too? A grid to build on, or free curves?

## Loose ends of what is built

### Cards and rulesets

- Layouts with cards covering each other (Pyramid, TriPeaks) can't be written as rulesets yet: a pile would need to
  know which piles cover it.
- Uno as a ruleset leaves out the "Uno!" call, challenging a +4, stacking draw cards and the starting card's effect.
  Scripts could now carry most of these; the hand-written Uno has them.
- Ruleset games for players are against bots only: no passing the device round, no online play, and one round at a
  time (no scores carried over to a match).
- Cards of a deck of its own are named by their code in messages ("clubs 7"), not by symbol ("7♣"); Uno's "rev"
  label is cramped on its card.
- After drawing a penalty in Mau-Mau, the player may still play the card drawn; some rules end the turn there. A
  ruleset field could say which.
- The ruleset bot plays well enough but simply: it doesn't count cards or think about the other players.

### Scripting

- Functions made in a loop share the loop's variable (they should each get their own).
- A struct read from one of the game's objects comes back as a copy, so its parts can't be set one at a time.
- The RPG kit runs scripts only through a custom effect; content files could take script conditions and effects
  directly.
- Never run in the browser (it should work: nothing is generated at run time).

### 3D

- Skins (characters bending as they move) and morph targets in glTF models.
- Shadows, point lights and spotlights: MonoGame's basic effect has three directional lights only; these need shaders
  of our own, compiled for desktop and the browser.
- Writing glTF files (for scenes built in code), and `KHR_texture_transform`.
- The Lab's 3D scene on the graphics card has never been looked at (the screen was locked); the software renderer
  path was checked offscreen.

### Inspector

- Scenes list what can be inspected themselves; an ECS world's entities aren't listed automatically.
- Its panel covers the right of the game, and there is no undo but changing a value back.

### Games

- One games-cs test failed once in 108 and didn't again; probably a timing test worth making steady.

## Built but never seen or heard

These pass their tests but have not been checked by eye or ear:

- The audio mixer's ducking, and sounds placed in the world.
- Gamepad rumble on a real pad.
- Larger text (`Fonts.TextScale`) across the screens.
- Replaying recorded input through a running game.
- Nine-slice panels and tooltips on screen.
- Naming a suit after a wild at the Card Table (tested in code, not on screen).
- The 3D, scripting and ruleset kits in the browser.

## Housekeeping

- Push: nothing since Jentl 0.6.11 has been pushed, in either repository (`git log origin/main..`).
