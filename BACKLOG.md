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

- In (0.6.51): instruments edited (`Channel.Custom`, `InstrumentText`): the Composer's Sound panel (Sound chip, the
  instrument's name, or I) sets the wave, envelope, ring, vibrato, slide, filter, copies and detune, breath, and the
  pluck's and FM's own settings, heard at each change, and goes back to the preset. Not yet: harmonics drawn by hand.
- In (0.6.50): swing (`Tune.Swing`, `Composition.Swing`, a chip in the Composer), and a loop of one, two or four bars
  to work on (the Loop chip or L; a bar's number moves it).
- In (games-cs): notes resized from their end, moved by dragging, and selected together (Shift-drag or the Select
  chip), dragged, nudged with the arrows and deleted. Not yet tried by hand: a drag can't be scripted.
- In the browser: WAV export as a download, and the clipboard.
- Play it on a phone: the grid is small for fingers.

### Animation

In (0.6.34): tween sequences (`Motion`: tweens of floats, vectors and colours in sequence or together, waits, calls,
repeats and yoyo), more easings, animation states for sprites (`AnimationStates`), frame events, Aseprite's sheets
and tags (`AsepriteSheet`), and flash, squash and stretch, and shake. The Lab has an Animation demo. Still to do:

- Bones for 2D (a body of parts that bend and turn, as Spine or DragonBones make): a larger piece of work, left for
  when a game needs it. (3D skins are in since 0.6.38.)
- In the games: cards are dealt in, flying from the stock one after another (`TableView.DealIn`, 0.6.49); Hot Pan's
  cook squashes and flashes; Mossbrook's map flashes whoever is hit, floats the damage and shakes when it is you.

### Android

- Games-cs gets an Android head beside Desktop and Web: MonoGame's Android project (`net10.0-android`), touch as the
  pointer (gestures already work), saves in the app's own storage, pausing and resuming with the app, the back
  button as Back, and phone screen shapes (the shell already handles portrait).
- Building and signing an APK (and an AAB for the Play Store), in CI as well.
- Checking every game on a phone: layouts at small sizes, touch targets, performance (the software renderer and the
  physics), sound starting.
- Note: the browser build already runs on phones, as a fallback until then.
- In: every app has a phone project (`apps/<Game>/Android`, `suites/<Suite>/Android`, on `shell/Android.props`),
  and all eleven start on the emulator: Solitaire, Card Table, Poker, Mossbrook, Paddock, Roadworks, Composer, Cheese
  Farm, and the suites Words, Puzzles and Arcade (each choosing a game, "All games" to come back, saves kept apart).
  Full screen in either orientation, clear of the cut-out and gesture bar (Jentl's `Insets`); the back button is Back
  (the system's back gesture handed to the game), and on a title screen with nothing behind it the app goes to the
  background, its sound held until it is back (`SoundPlayer.Suspended`); hints speak of tapping. Uno is shown as Colour Cards / Kleurenkaart, its call as "Last card!".
- Still to do: play each game through on the phone (Solitaire's Klondike is checked by touch; the rest only to their
  title screens): layouts at phone sizes, touch targets, speed; the per-app music (only its games' songs); icons;
  the release key and AABs for the store; the brand check of
  the games' names; CI (the projects are outside `Games.slnx` until the build machine has the workload).
- Decided: an app per game, games of a kind together: Solitaire (with the solo ruleset deals), Card Table, Poker,
  Word games (Connect Word, Associate Words), Puzzles (Sokoban, CodeLock), Arcade (Bricks, Hot Pan), Mossbrook,
  Paddock, Roadworks and Composer; not the Lab. Each has the shell cut to its own games and songs, and its own saves.
  Ids `nl.jentl.<app>`, names in the phone's language. Debug-signed for now; a release key kept outside the
  repositories, out of CI until the store. Tools from brew (Android command-line tools, Kotlin) and the .NET
  workload, tested on a headless emulator. Uno is shown as Kleurenkaart / Colour Cards (a trademark), and every
  game's name is checked against brands before the first upload.

### Connect Word: more of it

The first version is playable (games-cs `apps/ConnectWord`): a 4×4 grid of word cards with a deck behind it; a card
dropped on one of its category merges with it, on any other it swaps; a category whose words are all in one row is
matched and leaves as a coloured row; cards fall and new ones come in; twenty levels, stars by moves, progress saved.
Since: twenty Dutch levels, undo, a merged card swelling, and daily and random puzzles mixed from the language's
categories, timed, with a streak of days. Still to do:

- Decided: a matched row leaves the grid, and a wrong drop costs only its move (no lives), unless the Android
  original turns out to do otherwise.
- Many more levels written by hand, and a level editor or a format others can write them in.
- Merges slide in and matched rows fly up to their banner (games-cs).

### Associate Words: more of it

The first version is playable (games-cs `apps/AssociateWords`), built to the rules settled with you: five columns
with their top card up, a draw pile turning one card and recycling, a few slots that a category card opens and a
complete category frees, words on their own kind in any order in the columns, a move limit, fifteen levels. Since:
deals made from a bank of categories with limits from the autoplayer, giving Dutch levels (the English shapes filled
with Dutch categories), a daily deal with a streak, and custom games of three sizes. Still to do:

- Jokers are in (games-cs): a wild ★ card that fills a slot an open category has; one a deal, two in a large one.
- Picture cards; hand-made Dutch levels; a level editor.
- Tuning the move limits by play (they are a quarter above a simple autoplayer's). A move back for each category
  finished is in (games-cs).

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
- Decided: the pan keeps heating without end; no choice of cooks until there is character art; burnt pieces and
  pieces on the cook wait until it has been played.

### Traffic: more simulation, and the road-building game

The first part is in: `Jentl.Traffic` (networks, lanes, junctions with priority from the right, priority roads,
lights and roundabouts, five vehicle kinds on the Intelligent Driver Model, routes, demand, measurements), Core's
`TrafficView`, and a Lab demo of a small town. Decided for now: from above in 2D, driving on the right, Dutch priority
from the right, no pedestrians or cyclists, roads drawn freely. Since (0.6.35): lights that follow the traffic, routes
round jams by measured travel times, and the game's first version, games-cs's Roadworks: lay roads on a grid between
gates, choose junctions (give way, lights, roundabout), within a budget; the traffic runs for ten minutes and the
delay against a target gives stars; five levels. Still to do:

- In (0.6.45): changing lanes (MOBIL, `TrafficSimulation.LaneChanges`): overtaking, leaving a held-up lane, keeping
  right, re-routing from the new lane, drawn gliding across. In (0.6.52): lane changes a route needs (to the lane a
  turn is made from, waiting at the lane's end for a gap), and starting in another lane when the best is full.
  Roadworks has two-lane roads (Widen) and lane changes on.
- More junctions: turning pockets, stop and give-way signs, two-lane roundabouts, slip roads and motorways; and in
  Roadworks: roads of two lanes, faster roads, diagonal roads.
- Green waves, turning arrows, timings the player sets; demand by place and time (the morning rush).
- Capacity: single-lane towns saturate around 1,200 vehicles an hour; left turns waiting in the lane hold up everyone
  behind them.
- Pedestrians and cyclists; buses on lines with stops.
- Roadworks: more levels and tuning by play (the five are passable with simple plans, checked by the tests), overlays
  of speed and jams, a sandbox. Seen on screen (offscreen screenshots, 0.6.40): the gates now keep clear of the bar.

### Cheese Farm

In (games-cs `apps/CheeseFarm`, `src/CheeseFarm`): a made-up farm in the east of the country. The herd gives milk each
morning; a wheel is made in four steps in the kitchen (warm the milk into the band, test the curd for a clean break,
cut on the marks as the harp crosses them, choose the days in the brine), which give its stars; the cellar's racks,
where wheels ripen from young to very old and want turning every other day; the farm shop's customers, each wanting a
ripeness and a flavour; and cows, racks, flavours, a better kettle and road signs to buy. Still to do:

- The weekly cheese fair is in: one wheel judged on stars and age against three farms, prizes and rosettes.
- The meadow is in: six fields of grass, herbs (richer milk) or left for meadow birds (chicks, and walkers at the
  shop); cows the meadow can't feed eat bought feed. Seasons are in too: summer mowing for hay, the
  herd in the barn on hay in winter, nesting in spring and summer.
- The cows have names, and calves are born and grow up in four weeks (games-cs). Milking by hand is a little game
  of its own each morning: squeeze in turn, steady, or she kicks the pail.
- Cheeses of three sizes are in (a baby cheese, the farm wheel, a big wheel). The market in town is in: midweek, a cart of
  four wheels sold by the piece.
- Songs of its own are in (Morning Milking, Cheese Fair Waltz, Clogs on the Dyke; not yet heard). Still: art beyond
  shapes.
- Tuning by play: prices, costs, how fast neglect bites; and on a phone.

### New games that do some good

Ideas for games that are fun first and leave the world a little better, the good built into the rules rather than
told between levels. In the order I expect them to be popular, the most played first. Each says what it builds on.

1. **A garden for everyone** (cosy). Make a garden in Mossbrook's way; bees, birds, butterflies and hedgehogs come
   when you plant for them (a small food web: flowers feed insects, insects feed birds), and paving it over sends
   them away. Seasons, a garden to visit each day, nothing to lose. Cosy games are played widely and long. Builds on
   the RPG kit's world and saves, tiles, and the soundtrack's calm songs.
2. **Leftover Chef** (cooking). A cousin of Hot Pan: cook from whatever is in the fridge before it goes off; good
   combinations score, anything thrown away costs. Quick rounds, a daily fridge, recipes unlocked. About food
   waste. Builds on Hot Pan, the daily-puzzle streaks, and Motion for the kitchen's bustle.
3. **Fake-News Factory** (cards). Play the misinformation maker: build posts from trick cards (a fake expert, fear,
   a false choice, a doctored photo), chase followers, and dodge fact-checker bots. Playing the manipulator for a
   while makes people better at spotting the tricks afterwards, as studies of the Cambridge game *Bad News* found
   (prebunking). Builds on the cards kit and rulesets; the content is cards and short texts.
4. **Bike Town** (city building). Roadworks grown up: people choose for themselves between car, bike, bus and
   walking by time, safety and comfort; you score on happy travellers, few accidents, clean air and room for parks
   rather than on car flow. Players find out by doing why Dutch towns separate cycle paths and filter cars. Builds
   on `Jentl.Traffic` and Roadworks; needs the cyclists, buses and choice of mode on the traffic list above.
5. **Only Together** (co-op). A puzzle on the GameServer that no one can win alone: each player sees part of it and
   holds part of the means, so they have to talk and share. For families and classrooms. Builds on the GameServer
   and the lobby the card games use.
6. **Full Grid** ("netcongestie"). Run a region's electricity: sun and wind come and go, homes and factories want
   power, cables have limits; batteries, heat pumps and moving demand ("charge the car at noon") beat waiting years
   for new cables. Plays like a flow puzzle or tower defence. Builds on Roadworks's grid and a network solver like
   the traffic's.
7. **Polder** (water). Keep a polder dry with pumps, dykes and room for the river while the sea rises and storms
   come; a flooded village can't be undone. Builds on the same grid; water flow is new.
8. **Ear School** (music). Ear training and music theory as games: hear the interval, finish the tune, name the
   chord, with Synth's instruments and the Composer's piano roll. Free music lessons for children who get none.
9. **Dutch for newcomers** (words). Connect Word and Associate Words with sets for learning Dutch, with pictures and
   spoken words, levels following the integration course's themes (shop, doctor, work, the town hall). A small
   audience, but it matters a great deal to them. Builds on both word games and their word banks; needs picture
   cards (on Associate Words's list) and recorded or generated speech.

## Loose ends of what is built

- Fixed in 0.6.41: the stars (★ ☆) were missing from the game font, so Connect Word's, Associate Words' and
  Roadworks' stars showed as gaps; Jentl now draws them (and ▲ ▼ ◀ ▶) itself, as it does the card suits.

### Cards and rulesets

- Pyramid is a ruleset (0.6.46): `pairs` let two free cards adding up to a sum leave together, and a rank alone with
  a tap; `"faceUp": "all"` deals covered cards face up; the `pyramid` example.
- Colour Cards (Uno as a ruleset) has the "Last card!" call (`turns.call`, 0.6.53: forgetting costs two). It leaves out
  challenging a +4, stacking draw cards and the starting card's effect; scripts could carry most of these (as
  Mau-Mau's sevens stack), and the hand-written Uno has them.
- Ruleset games for players are passed round (pass and play, a screen hiding the hand between turns) and played as
  matches to 100 points (games-cs); online play later.
- Uno's "rev" label: seen again on the table (0.6.53), it reads well enough at hand size.
- The ruleset bot plays well enough but simply: it doesn't count cards or think about the other players.

### Scripting

- In (0.6.54): setting a part of a struct any number of steps deep (`a.b.c.d = 1`, `list[0].x += 1`) writes each copy
  back.
- Never run in the browser (it should work: nothing is generated at run time).

### 3D

- Morph targets in glTF models; skins are bent on the CPU, which is fine for a few characters but not a crowd (GPU
  skinning needs shaders of our own).
- Shadows, point lights and spotlights: MonoGame's basic effect has three directional lights only; these need shaders
  of our own, compiled for desktop and the browser.
- Writing glTF files (for scenes built in code), and `KHR_texture_transform`.
- The Lab's 3D scene is seen on both renderers, the skinned stalk bending in each (0.6.40).

### Inspector

- Entities (`Inspectable.Entities`, offered by Bricks), undo (Z) and moving the panel (M) are in and seen on screen;
  the footer names Z and M (0.6.40).

### Games

- The flaky server test was a bot's hello racing a ten-second limit under load; the league's start limit is now a
  setting and its tests have room. Watch for others: three GameServer tests failed once more in a cold run straight
  after a fresh restore (0.6.40), and passed in five runs after; the names weren't caught.
- Caught one (0.6.54): `RunnerEndToEndTests.ABotThatPlaysLegally_PlaysLegallyOnline` failed after ten seconds in a
  full run under load, and passed three runs alone; likely a bot's answer counted late. Its tests now allow five
  seconds over the limit.

## Built but never seen or heard

These pass their tests but have not been checked by eye or ear. Screenshots are taken out of sight now: a desktop
game run with `--screenshot` keeps its window hidden (`JentlGame.Hidden`, `HiddenWindow`), so these can be checked
while someone works at the screen.

- The audio mixer's ducking, and sounds placed in the world.
- Gamepad rumble on a real pad.
- Larger text (`Fonts.TextScale`) across the screens.
- Replaying recorded input through a running game.
- Nine-slice panels and tooltips on screen.
- Naming a suit after a wild at the Card Table (tested in code, not on screen).
- The 3D, scripting and ruleset kits in the browser.

## Housekeeping

- Both repositories are pushed after every feature.
