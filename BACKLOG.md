# Backlog

What is still to do, for Jentl (this repository) and the games built on it ([games-cs](../games-cs)). New work
comes first, then loose ends of what is already built, then things that work in tests but have never been seen or
heard. Where an idea is my reading of a wish rather than a decision, it says so, with the questions to settle first.

## New features

### A game soundtrack, as RuneScape has its music

In (0.6.30 and 0.6.31): `Song` and `Chords` in Jentl, real instruments in `Synth` (plucked strings, FM, additive
harmonics, a choir, ensembles, filters, reverb; `Instruments` has 35 presets), and in games-cs one soundtrack of 52
songs for harp, strings, flutes, lute, brass, choir and drums (chiptune kept for Bricks), shared by all the games,
in ten moods (title screens, puzzles, solitaire, card tables, village, cellar, races, kitchen, word
games, Bricks). A place asks for a mood and gets its songs in turn; a song is unlocked the first time it plays,
with a notice, and saved. The title screens' Music screen lists them all, plays the unlocked ones and says where the
locked ones are heard.

In 0.6.32: locked songs are greyed out; a place's songs follow one another; the Music screen has three modes (by
place, loop the chosen song, shuffle the unlocked ones), saved; and the moments (a win, a loss, a level gained) have
jingles for trumpet, horn, harp and timpani.

- Still to do:
  - More songs per game, so each has a sound of its own: Mossbrook's other places, each card game, each race track.
  - Hear them: every song renders in the tests and is levelled, but none has been checked by ear.

### A music maker, like Bosca Ceoil

In (0.6.33): `Composition` in Jentl (channels of one-bar patterns and an arrangement, rendered through `Synth` with
the instrument presets and reverb, saved as JSON, written out as a tune's code or a WAV), and games-cs's Composer
app: a piano roll with a key and scale to shade, patterns made, copied, cleared and transposed, the arrangement a row
per channel and a column per bar, instruments chosen from the presets, tempo, bars, 3/4 or 4/4, echo, undo and
redo, playing with a playhead (edits heard from the bar playing), songs saved and opened, copied as code, and saved
as WAV on the desktop. Still to do:

- Editing instruments themselves (wave, envelope, filter), rather than choosing a preset and a volume.
- Swing, a loop of a few bars to work on, selecting and moving several notes, and changing a note's length from its
  end rather than drawing it again.
- In the browser: WAV export as a download, and the clipboard.
- Play it on a phone: the grid is small for fingers.

### Animation

In (0.6.34): tween sequences (`Motion`: tweens of floats, vectors and colours in sequence or together, waits, calls,
repeats and yoyo), more easings, animation states for sprites (`AnimationStates`), frame events, Aseprite's sheets
and tags (`AsepriteSheet`), and flash, squash and stretch, and shake. The Lab has an Animation demo. Still to do:

- See the Lab's Animation demo on screen (the screen was locked when it was built; its pieces are tested).
- Bones for 2D (a body of parts that bend and turn, as Spine or DragonBones make): a larger piece of work, left for
  when a game needs it. (3D skins are in since 0.6.38.)
- Use them in the games: cards dealt with sequences, Hot Pan's cook squashing on landing, hits flashing in Mossbrook.

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
Since: twenty Dutch levels, undo, a merged card swelling, and daily and random puzzles mixed from the language's
categories, timed, with a streak of days. Still to do:

- Check it against the Android original: whether a matched row really leaves the grid or stays frozen in place,
  whether wrong drops should cost something (lives, or moves), and how its levels grow. (Needs you, or the original.)
- Many more levels written by hand, and a level editor or a format others can write them in.
- Cards sliding into each other as they merge, and the solved row flying up to its place.

### Associate Words: more of it

The first version is playable (games-cs `apps/AssociateWords`), built to the rules settled with you: five columns
with their top card up, a draw pile turning one card and recycling, a few slots that a category card opens and a
complete category frees, words on their own kind in any order in the columns, a move limit, fifteen levels. Since:
deals made from a bank of categories with limits from the autoplayer, giving Dutch levels (the English shapes filled
with Dutch categories), a daily deal with a streak, and custom games of three sizes. Still to do:

- Jokers, as the originals have (a card that goes with any category, or bridges two columns): how they complete a
  category is a rules question for you.
- Picture cards; hand-made Dutch levels; a level editor.
- Tuning the move limits by play (they are a quarter above a simple autoplayer's), and extra moves as a reward.

### Hot Pan: more for the frying pan

The first version is playable (games-cs `apps/HotPan`): a side-view platformer, the chicken heating from raw pink
through golden to black, the pan getting hotter without end, health burning away on anything too hot, ten points a
second, leaderboards, keys, gamepad and touch. Since: power-ups (a cold drink, oven gloves, a lid, ice), oil that
bubbles and spits, the spatula flipping the hottest piece, a daily pan, the pan's sizzle growing with the heat, a sigh
of relief on cool chicken, and the cook squashing, stretching and flashing. Still to do:

- Burnt pieces staying as hazards instead of being flicked out, and pieces landing on the cook: both change how the
  game plays, so worth deciding by playing.
- Tuning by play: how fast pieces heat and drop, how much burning and oil hurt, how often power-ups come; on a phone.
- Character art and animation beyond shapes.
- Questions: should the pan heat level off at some point? A choice of cooks?

### Traffic: more simulation, and the road-building game

The first part is in: `Jentl.Traffic` (networks, lanes, junctions with priority from the right, priority roads,
lights and roundabouts, five vehicle kinds on the Intelligent Driver Model, routes, demand, measurements), Core's
`TrafficView`, and a Lab demo of a small town. Decided for now: from above in 2D, driving on the right, Dutch priority
from the right, no pedestrians or cyclists, roads drawn freely. Since (0.6.35): lights that follow the traffic, routes
round jams by measured travel times, and the game's first version, games-cs's Roadworks: lay roads on a grid between
gates, choose junctions (give way, lights, roundabout), within a budget; the traffic runs for ten minutes and the
delay against a target gives stars; five levels. Still to do:

- Changing lanes on the way (MOBIL), so routes needn't start in the right lane and fast traffic overtakes.
- More junctions: turning pockets, stop and give-way signs, two-lane roundabouts, slip roads and motorways; and in
  Roadworks: roads of two lanes, faster roads, diagonal roads.
- Green waves, turning arrows, timings the player sets; demand by place and time (the morning rush).
- Capacity: single-lane towns saturate around 1,200 vehicles an hour; left turns waiting in the lane hold up everyone
  behind them.
- Pedestrians and cyclists; buses on lines with stops.
- Roadworks: more levels and tuning by play (the five are passable with simple plans, checked by the tests), overlays
  of speed and jams, a sandbox; and seeing it on screen (built while the screen was locked).

## Loose ends of what is built

### Cards and rulesets

- Pyramid as a ruleset: covered piles are in (`coveredBy`, `places`; the `tripeaks` example), but Pyramid's pairs
  adding up to thirteen need a pair move rulesets don't have (two cards leaving together).
- Uno as a ruleset leaves out the "Uno!" call, challenging a +4, stacking draw cards and the starting card's effect.
  Scripts could now carry most of these (as Mau-Mau's sevens stack); the hand-written Uno has them.
- Ruleset games for players are against bots only: no passing the device round, no online play, and one round at a
  time (no scores carried over to a match).
- Uno's "rev" label is cramped on its card.
- The ruleset bot plays well enough but simply: it doesn't count cards or think about the other players.

### Scripting

- Setting a part of a struct deeper than one step (`a.b.c.d = 1`), or of a struct in a list (`list[0].x = 1`), still
  sets a copy; one step (`thing.position.x = 5`) is written back.
- Never run in the browser (it should work: nothing is generated at run time).

### 3D

- Morph targets in glTF models; skins are bent on the CPU, which is fine for a few characters but not a crowd (GPU
  skinning needs shaders of our own).
- Shadows, point lights and spotlights: MonoGame's basic effect has three directional lights only; these need shaders
  of our own, compiled for desktop and the browser.
- Writing glTF files (for scenes built in code), and `KHR_texture_transform`.
- The Lab's 3D scene on the graphics card has never been looked at (the screen was locked), nor its skinned stalk; the
  software renderer path was checked offscreen.

### Inspector

- An ECS world's entities can be listed (`Inspectable.Entities`), but a scene still has to offer them; no game uses it
  yet. Undo (Z) and moving the panel (M) are in.

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
