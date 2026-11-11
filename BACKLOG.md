# Backlog

What is still to do, for Jentl (this repository) and the games built on it ([games-cs](../games-cs)). New work
comes first, then loose ends of what is already built, then things that work in tests but have never been seen or
heard. Where an idea is my reading of a wish rather than a decision, it says so, with the questions to settle first.

## New features

### A game soundtrack, as RuneScape has its music

The first version is in (0.6.30): `Song` and `Chords` in Jentl, and in games-cs one soundtrack of 35 songs shared by
all the games, in ten moods (title screens, puzzles, solitaire, card tables, village, cellar, races, kitchen, word
games, Bricks). A place asks for a mood and gets its songs in turn; a song is unlocked the first time it plays,
with a notice, and saved. The title screens' Music screen lists them all, plays the unlocked ones and says where the
locked ones are heard.

- Still to do:
  - Locked songs drawn greyed out: `Menu` has no disabled item yet, so they show as "? ? ?" in the usual colour.
  - Shuffle and loop-one in the Music screen, and the mood's songs following each other while one place plays,
    rather than the first song looping until the place changes.
  - Moments as well as places: victory, defeat, tension (a timer running low) with songs of their own.
  - More songs per game, so each has a sound of its own: Mossbrook's other places, each card game, each race track.
  - Hear them: every song renders in the tests, but most have only been checked by numbers, not by ear.

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

### Connect Word: more of it

The first version is playable (games-cs `apps/ConnectWord`): a 4×4 grid of word cards with a deck behind it; a card
dropped on one of its category merges with it, on any other it swaps; a category whose words are all in one row is
matched and leaves as a coloured row; cards fall and new ones come in; twenty levels, stars by moves, progress saved.
Still to do:

- Check it against the Android original: whether a matched row really leaves the grid or stays frozen in place,
  whether wrong drops should cost something (lives, or moves), and how its levels grow.
- Dutch levels (the words are English only; the rest of the game is in both languages), and many more levels: a
  level editor or a format others can write levels in.
- Undo, a merge animation (cards sliding into each other), and the solved row flying up to its place.
- Daily puzzles, and scores for time as well as moves.

### Associate Words: more of it

The first version is playable (games-cs `apps/AssociateWords`), built to the rules settled with you: five columns
with their top card up, a draw pile turning one card and recycling, a few slots that a category card opens and a
complete category frees, words on their own kind in any order in the columns, a move limit, fifteen levels. Still to
do:

- Jokers, as the originals have (a card that goes with any category, or bridges two columns).
- More levels, Dutch ones, and picture cards; a level editor, or levels made and checked by the autoplayer.
- Tuning the move limits by play (they are a quarter above a simple autoplayer's), and extra moves as a reward.
- A daily deal, and a custom game: pick the size and play a fresh deal.

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

### Traffic: more simulation, and the road-building game

The first part is in: `Jentl.Traffic` (networks, lanes, junctions with priority from the right, priority roads,
lights and roundabouts, five vehicle kinds on the Intelligent Driver Model, routes, demand, measurements), Core's
`TrafficView`, and a Lab demo of a small town. Decided for now: from above in 2D, driving on the right, Dutch priority
from the right, no pedestrians or cyclists, roads drawn freely. Still to do:

- Changing lanes on the way (MOBIL), so routes needn't start in the right lane and fast traffic overtakes.
- More junctions: turning pockets, stop and give-way signs, two-lane roundabouts, slip roads and motorways.
- Lights that react to waiting traffic, green waves, turning arrows; timings the player sets.
- Routes that go round jams (by travel times measured as it runs), and demand by place and time (homes to work in the
  morning rush).
- Capacity: single-lane towns saturate around 1,200 vehicles an hour now; left turns waiting in the lane hold up
  everyone behind them.
- Pedestrians and cyclists; buses on lines with stops.
- The game: build roads on a grid or freely, place junctions and lights, a budget, goals per level, overlays of
  speed and jams, and a sandbox.

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
