# NeonStrickers
# Neon Strikers

## Project Overview

Neon Strikers is a 2D arcade-style game built in Unity (6000.4). Blue and Red compete in neon-themed arenas using physics-based movement and interactions to score goals. Play it as **local multiplayer (Two Players)** or **single player against the computer (Vs Computer)**.

---

## How to Open the Project

1. Install **Unity Hub**
2. Install **Unity 6 (or compatible version used in this project)**
3. Open Unity Hub
4. Click **Open Project**
5. Select the project folder (`neon-kickers`)
6. Open the scene:

   ```
   Assets → Scenes → Main Menu
   ```
7. Press **Play** to start the game

---

## How to Build the Game

1. Open the project in Unity

2. Go to **File → Build Settings**

3. Add the following scenes **IN THIS ORDER** (already set up in `ProjectSettings/EditorBuildSettings.asset`):

   * Main Menu
   * Arena_01
   * Arena_02
   * Arena_03
   * CustomArena (plays custom arenas: playtests and the optional 4th arena)
   * ArenaEditor (the in-game arena editor)
   * Results

4. Select your platform (Windows or Mac)

5. Click **Build**

6. Choose a folder to export the game

---

## Project Structure

### Scenes

Located in: `Assets/Scenes/`

* `Main Menu` → Main menu UI and navigation
* `Arena_01`, `Arena_02`, `Arena_03` → Playable arenas
* `CustomArena` → A copy of `Arena_01` in which a custom arena is built at runtime (playtest and 4th arena)
* `ArenaEditor` → The in-game arena editor
* `Results` → Displays winner and final score

---

### Scripts

Located in: `Assets/Scripts/`

#### Core (`NeonStrikers.Core` assembly, `Assets/Scripts/Core/`)

* `GameMode` / `GameModeSettings` → Selected mode (Two Players / Vs Computer), kept across scenes
* `CpuOpponentBrain` → Engine-independent decision logic of the computer opponent (unit tested)
* `Arenas/` (namespace `NeonStrikers.Arenas`) → custom arena model, engine-light and unit tested:
  * `ArenaData` / `ArenaObjectData` / `ArenaFactory` → arena file format, starter and empty layouts
  * `ArenaRules` / `ArenaGeometry` → shared sizes (taken from the stock arenas) and shapes
  * `ArenaValidator` → finds broken arenas and invalid configurations and explains the fix
  * `ArenaEditorDocument` → editing operations, selection, undo / redo
  * `ArenaStorage` → JSON files on disk and the 4th-arena choice
  * `CustomArenaFlow` / `CustomArenaSession` → scene order with the optional 4th arena, playtest state

#### Arena editor (`Assets/Scripts/ArenaEditor/`)

* `ArenaEditorController` → the editor UI (IMGUI, works in builds with mouse or touchpad)
* `ArenaEditorNavigation` → editor / playtest / menu scene changes
* `CustomArenaBuilder` → builds a custom arena in the `CustomArena` scene from the stock Arena_01 objects
* `CustomArenaSlowMotion` / `CustomSlowZoneSensor` → stock goal slow motion for any number of slow zones
* `PlaytestOverlay` → "Back to Editor" / "Restart" bar during a playtest

#### Gameplay

* `GameManager` → Controls match flow and scoring; applies the selected mode to Red when an arena starts
* `GoalZone` → Detects when a goal is scored
* `MatchData` → Stores score and winner between scenes
* `ResultsManager` → Handles Results screen (background swap + ghost animation)
* `SceneLoader` → Handles scene transitions (`StartTwoPlayers`, `StartVsComputer`, `LoadGame` = Play Again, `LoadMainMenu`,
  `OpenArenaEditor`, `ReturnToArenaEditor`)

#### Player

* `PlayerMovement` → Player movement and controls (keyboard, or virtual keys from the CPU)
* `CpuPlayerController` → Runs the CPU brain for Red in Vs Computer mode (added automatically at runtime)
* `PlayerKick` → Ball interaction (kicking)
* `PlayerLoseEffects` → Visual effects when losing
* `PlayerLoseLaunch` → Launch/knockback effect on loss
* `PulseEffect` → Visual pulse interaction effect

#### Ball

* `BallController` → Handles ball physics and behavior

#### Camera

* `CameraShake` → Camera effects

#### UI

* `LogoPulse` → Logo animation in Main Menu
* `UIButtonHover` → Button hover effects

#### Utilities

* `GoalSlowMotion` → Slow-motion effect on goal
* `LocalSlowZone` → Area-based slow zones
* `PortalCooldown` → Portal cooldown logic
* `PortalTeleport` → Teleportation system
* `ZoneSlowable` → Controls slowdown zones

---

## Gameplay Features

* 2-player local competitive gameplay
* Single player Vs Computer mode with a reactive CPU opponent
* Physics-based movement and ball interaction
* Multiple arenas, plus an in-game **Arena Editor** to build your own and play it as a 4th arena
* Neon visual style
* Portal system and slow-motion zones
* Goal-triggered effects
* Results screen with dynamic background:

  * Blue wins → Blue victory background
  * Red wins → Red victory background
* Ghost animation appears above the losing player

---

## Game Modes

The Main Menu offers two game buttons. Both start a fresh series in `Arena_01` (three arenas, or four when a
custom 4th arena is chosen in the Arena Editor). A third button opens the **Arena Editor**.

| Mode | Blue | Red |
|------|------|-----|
| **Two Players** | Human: W A S D + Space | Human: Arrow Keys + Enter |
| **Vs Computer** | Human: W A S D + Space | Computer (Red's keyboard keys are ignored) |

* The selected mode is kept through `Arena_01` → `Arena_02` → `Arena_03` → (custom 4th arena) → `Results`.
* **Play Again** restarts at `Arena_01` with the same mode.
* **Main Menu** (button or **Esc**) resets the mode so you choose again.
* A fresh launch, or pressing Play with an arena scene open in the editor, always starts in **Two Players**.
  The project has *Enter Play Mode Options* enabled with domain reload disabled, so the mode (and `MatchData`)
  are reset by `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` hooks rather than relying on static initialisers.

Nothing has to be set up in the Inspector: `GameManager.Start` adds `CpuPlayerController` to `player2` (Red)
at runtime only in Vs Computer mode.

## Computer Opponent

The CPU plays by the same rules as a human:

* It only produces **virtual key presses** (left/right/up/down + action) that `PlayerMovement` processes with
  exactly the same code as the keyboard. It never moves itself or the ball directly, never sets the ball's velocity
  and never adds forces of its own. Movement speed, lift, pulse radius/force, soft possession, ball speed cap,
  animation, collisions, slow zones, portals and round resets therefore apply to it exactly as to Blue.
* The disabled `PlayerKick` component is left alone; the pulse (`DoLift`/`PulseBall`) remains the only way to hit
  the ball, and each CPU pulse spawns the normal pulse effect once.
* Pulse attempts are limited to **one every 0.4 seconds of game time** (`CpuPulseGate`; tuning can't go below this).

Every frame, `CpuOpponentBrain`:

1. Predicts the ball from its **current** position and velocity (gravity, damping, speed cap, arena bounds). Nothing
   is cached, so bounces, hits, portals and kick-off resets are picked up the next frame. A sudden jump or sharp
   change of direction also clears any committed movement.
2. Chooses an intent:
   * **Defend** when the prediction ends in Red's goal or the ball is close to it.
   * **Guard** (cover the line between ball and Red's goal) when Blue has the ball or contests it while the CPU's pulse recharges.
   * **Attack** otherwise.
3. Moves to the earliest reachable point on the ball's path. When attacking it approaches from the side away from
   Blue's goal; when defending it gets goal-side. Left/right hysteresis keeps movement smooth instead of jittery.
4. When the ball is within pulse reach, tries each direction a human could hold, simulates the result and fires only
   if it beats leaving the ball alone (it never picks a pulse predicted to score an own goal). Otherwise it may pulse
   to climb towards a high ball or hop when blocked.

Goals are read from each arena's `GoalZone` triggers (`scoringTeam` "Blue" = the goal Red defends, "Red" = the goal
Red attacks), so the side goals of Arena_01, the floor goals of Arena_02 and the rotated goals of Arena_03 all work.
Difficulty knobs live on `CpuPlayerController.tuning` (`CpuTuning`).

## Controls

### Player 1 (Blue) — both modes

* Move: **W A S D**
* Action: **Space**

### Player 2 (Red) — Two Players mode only

* Move: **Arrow Keys**
* Action: **Enter**

In Vs Computer mode Red is controlled by the computer.

### General

* **Esc**: back to the Main Menu

---

## Game Flow

1. Main Menu → **Two Players** or **Vs Computer**
2. Arena (01 / 02 / 03, then the custom 4th arena if one is chosen) → Players compete
3. Goals are detected using `GoalZone`
4. Match ends → `MatchData` stores results
5. Results Scene:

   * Background changes based on winner
   * Final score is displayed
   * Ghost animation plays on losing side
6. Player chooses:

   * Play Again
   * Return to Main Menu

---

## Arena Editor

Main Menu → **ARENA EDITOR**. It is part of the running game (not a Unity Editor window).

### Screen

* **Top bar**: arena name, `New`, `Open` (all saved arenas), `Save`, `Undo` / `Redo`, `Playtest`,
  `Use as 4th Arena` (or `Remove from Game` when the open arena is the 4th arena) and `Help`.
* **Left panel – Add to arena**: Wall, Platform, Portal Pair, Single Portal, Blue Goal, Red Goal, Slow Zone, Ball,
  Blue Spawn, Red Spawn. Pick one, then click in the arena (Shift+click places several). Snap-to-grid / grid toggles.
* **Centre**: the arena in game units (19 × 10, the stock arenas' size; dashed lines show the edge of a 16:9 screen).
* **Right panel**: the selection's exact values (position, size, rotation, portal link, goal team, Duplicate /
  Delete) and the live **Arena check**.
* **Status bar**: what the mouse / current tool does, mouse coordinates and the current 4th arena.

### Mouse / touchpad and keys

| Action | How |
|---|---|
| Select | click an object |
| Multi-select | Shift/Ctrl/Cmd + click, or drag a box on empty space (Ctrl+A selects all) |
| Move | drag a selected object (the whole selection moves); arrow keys nudge |
| Resize | drag the white square handles (circles keep round) |
| Rotate | drag the round handle (15° steps with snapping, Shift = free); Q / E; ±90 buttons |
| Duplicate / Delete | Ctrl+D / Del or Backspace, or the panel buttons |
| Undo / Redo | Ctrl+Z / Ctrl+Y or Ctrl+Shift+Z, or the toolbar |
| Cancel tool / deselect | Esc or right-click |
| Save / Help | Ctrl+S / F1 |

### Objects

All objects behave exactly like the corresponding objects of the stock arenas; `CustomArenaBuilder` clones the real
Arena_01 objects (kept, hidden, in the `CustomArena` scene) so colliders, layers, physics materials, particles and
every serialized gameplay value come from the base game. Only position, size and rotation change.

| Editor object | Cloned from | Notes |
|---|---|---|
| Wall / Platform | `Walls/BottomLeftWall` / `ArenaPlatforms/BottomPlatform` | Solid bar + outline collider; outline and end caps keep their stock thickness at any size |
| Portal | `Portals/LeftPortal` | `PortalTeleporter` 0.25 s cooldown, ×1.1 ball / ×0.9 player exit speed, min exit speed 5, exit offset 0.5. Local *up* (the arrow) is the exit direction; things entering a portal come out of its partner 0.6 + 0.5 units in front of it. A coloured, translucent visual is added (stock portals are invisible) |
| Blue Goal / Red Goal | `Goals/LeftGoal` (scoringTeam `Red`) / `Goals/RightGoal` (scoringTeam `Blue`) | Includes the goal particles; GameManager's goal bursts use the first goal of each team |
| Slow Zone | `Goals/LeftGoalSlowZone` | Circle trigger with `LocalSlowZone` 0.97 / 0.97 and the stock goal slow motion (see below). A faint visual is added |
| Ball, Blue / Red Spawn | the scene's ball, players and spawn points | Moved to the chosen positions |

**Slow motion parity.** Every stock arena has exactly two slow zones whose `GoalSlowMotionZone` (slowTimeScale 0.01,
transitionSpeed 10) each lerp `Time.timeScale` every frame; while the ball is in one zone the other pulls time back
towards 1, so the stock game settles around half speed. Reusing the script on any number of custom zones would change
that (one zone would freeze the game at 0.01). `CustomArenaSlowMotion` therefore reproduces the stock two-zone update
exactly (`lerp(towards slow if the ball is in any zone) then lerp(towards normal)`, same `k = unscaledDeltaTime * 10`),
with the values read from the stock zone.

Ball, players, GameManager, camera, UI and SceneLoader are the untouched Arena_01 objects, so physics and player /
ball movement are identical to the stock arenas.

### Arena check (validation)

The check runs after every change. Clicking a problem selects the objects involved; problem objects pulse red.

* **MUST FIX** (blocks Playtest and the game): no ball / spawn, more than one ball or spawn of a team, spawn or ball
  outside the arena, inside a wall/platform or a portal, ball spawning inside a goal, Blue and Red spawning on top of
  each other, invalid numbers, damaged ids.
* **FIX** (Playtest asks for confirmation; can't be added to the game): missing Blue or Red goal, unpaired portal,
  broken / one-way portal link, portal exit leading outside the arena or into a wall, the ball or a player able to
  leave the arena (flood fill over the layout, following linked portals), a team's goal unreachable for the ball,
  objects outside the arena, sizes out of range, unknown object types, too many objects.
* **TIP**: no name, a goal that can't be reached while another goal of the same team can.

### Playtest

`Playtest` launches the current layout – even unsaved or unfinished (only MUST FIX problems block it) – in
**Two Players** mode in the `CustomArena` scene. `Back to Editor` / **Esc** (or the end of the match) returns to the
editor with the arena and its full undo history exactly as they were
(`CustomArenaSession.EditorDocument` survives the scene changes). `Restart` replays the match.

### Undo / redo, multi-selection and relationships

Every change is an undo step (a drag, resize or rotation is one step; typing a number is merged into one step).
History entries are snapshots of the whole arena, so relationships are restored exactly:

* duplicating both portals of a pair creates a new pair linked to each other; the originals are unchanged;
* duplicating one portal gives an unpaired copy, reported by the Arena check;
* deleting a portal unpairs its partner; undo restores the portal **and** the partner's link;
* linking a portal that already had a partner releases the old partner (undo restores it).

### Saving, opening and the 4th arena

* Arenas are saved as readable JSON in `Application.persistentDataPath/CustomArenas/<id>.arena.json`
  (written through a temporary file). They survive restarts. `Open` lists every file, newest first, with its status;
  unreadable files (corrupted JSON, not an arena, newer format, duplicated ids) are listed as **Broken file** with the
  reason and can be deleted.
* `Use as 4th Arena` saves the open arena (only when it has no MUST FIX / FIX problems) and records its id in
  `CustomArenas/gameflow.json`. It is then played after Arena_03 in **Two Players and Vs Computer** (the CPU reads the
  custom goals), before Results. Choosing another arena (toolbar or `Open` list) replaces the 4th arena; the previous
  one stays saved and listed.
* `Remove from Game` / `Use original 3 arenas`, deleting the 4th arena's file, or a 4th arena that has become invalid
  or unreadable → the game uses the original Arena_01 → Arena_02 → Arena_03 → Results flow.

## Tests

Open **Window → General → Test Runner**.

* **EditMode** (`Assets/Tests/EditMode`, assembly `NeonStrikers.Tests.EditMode`)
  * `CpuOpponentBrainTests`: approach side, defend/guard, portal and direction-change retargeting, smooth movement,
    shot choice (never into own goal), lift/hop, and the 0.4 s pulse limit (frame-rate independent, uses game time).
  * `GameModeSettingsTests`: default Two Players, selection persistence, reset, fresh-launch hook.
  * `SceneWiringTests`: Main Menu buttons → `StartTwoPlayers` / `StartVsComputer`, Results buttons, arena keys for
    both players, identical Blue/Red movement settings, `PlayerKick` still disabled, no scene-baked CPU, goals present,
    build settings.
  * `ArenaEditorDocumentTests`: add / undo / redo, multi-selection duplicate and delete, duplicating a portal pair
    (new connected pair, originals intact) vs. one portal (unpaired + validation), undoing a deletion restores the
    links, drags as single undo steps, merged typing, dirty state, linking, goal team swap.
  * `ArenaValidatorTests`: starter arena valid, empty arena needs goals, missing ball / spawns, invalid spawn points,
    duplicates, unpaired / broken portals, portal exits, open arena, walled-off goal, damaged values, ordering.
  * `ArenaStorageTests`: save / load round trip, persistence for a new storage instance (restart), broken files,
    unknown types, replacing the 4th arena keeps the old one, deleting it restores the original flow, scene order with
    and without a 4th arena, invalid 4th arena skipped, playtest snapshot precedence.
  * `ArenaEditorSceneWiringTests`: Arena Editor menu button, build settings, editor scene, the CustomArena templates
    and its players / ball / GameManager have exactly Arena_01's serialized values.
* **PlayMode** (`Assets/Tests/PlayMode`, assembly `NeonStrikers.Tests.PlayMode`)
  * `GameModeFlowTests`: arena opened directly = Two Players, Vs Computer gives Red to the CPU, menu buttons start
    Arena_01 in the chosen mode, mode persists through all arenas and Play Again, Main Menu resets it, and the CPU
    moves and pulses through the real game loop within the pulse limit.
  * `CustomArenaPlayModeTests`: custom arenas are built from the stock templates with the stock values, a custom goal
    scores for the right team, linked portals teleport with the stock exit rules, a custom slow zone slows time like
    the stock Arena_01 zone, the 4th arena is played after Arena_03 in Vs Computer (CPU uses the custom goals) and then
    Results, no 4th arena keeps the original flow, the menu button opens the editor, playtest and back keeps the arena
    and its undo history, and the editor refuses broken arenas for the game flow and replaces the 4th arena.

Tests that touch saved arenas use a temporary folder (`ArenaStorage.OverrideDefaultRoot`), never your saved arenas.

## Notes

* Make sure scenes are added to Build Settings **in the correct order**
* Results scene must be last
* `CustomArena` must stay a copy of `Arena_01`: its Walls / ArenaPlatforms / Portals / Goals objects are the templates
  of the editor objects (they are hidden at runtime). Changing a stock object there changes the custom arenas too.
* Background image is used instead of winner text
* Ghost animation is handled separately from background
* Time scale is reset in the Results scene

---

