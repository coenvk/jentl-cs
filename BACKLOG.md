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

### Hot Pan: a game in a frying pan

Working title. You stand in a frying pan. Pieces of chicken are dropped in, and they heat up, and so does the pan.
Stand on the pan or on a piece that has got too hot and your feet burn: you lose health. Jump from piece to piece to
those still cool enough. At no health the game is over; the longer you last, the more points.

- Seen from the side, a little platformer: you run and jump across the pan, the pieces are platforms. Jentl's physics
  (as in Bricks) can carry the jumping and landing.
- Heat: each piece warms from the pan it lies on, from raw pink through golden to burnt, so you can see how hot it is;
  the pan heats over the game, so pieces warm faster as time goes on. Burning takes health while you stand on
  something too hot, with a moment of sizzle and smoke.
- New pieces fall in now and then (where, and how often, set the difficulty); burnt ones might be flipped out, or
  stay as hazards. Possibly power-ups: a cold drink, oven gloves, a lid.
- Score by time survived, with a best score and an online leaderboard (the shell has them), and sounds and music to
  match.
- Questions: seen from the side, or from above (moving round the pan rather than jumping)? Phone controls (tap to
  jump, tilt or drag to move)? Is the pan heat what ends a long game, or does it level out?

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

- Push: Jentl is 17 commits ahead of `origin/main` (0.6.12 to 0.6.28), games-cs 6.
