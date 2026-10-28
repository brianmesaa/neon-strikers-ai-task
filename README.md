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

3. Add the following scenes **IN THIS ORDER**:

   * Main Menu
   * Arena_01
   * Arena_02
   * Arena_03
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
* `Results` → Displays winner and final score

---

### Scripts

Located in: `Assets/Scripts/`

#### Core (`NeonStrikers.Core` assembly, `Assets/Scripts/Core/`)

* `GameMode` / `GameModeSettings` → Selected mode (Two Players / Vs Computer), kept across scenes
* `CpuOpponentBrain` → Engine-independent decision logic of the computer opponent (unit tested)

#### Gameplay

* `GameManager` → Controls match flow and scoring; applies the selected mode to Red when an arena starts
* `GoalZone` → Detects when a goal is scored
* `MatchData` → Stores score and winner between scenes
* `ResultsManager` → Handles Results screen (background swap + ghost animation)
* `SceneLoader` → Handles scene transitions (`StartTwoPlayers`, `StartVsComputer`, `LoadGame` = Play Again, `LoadMainMenu`)

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
* Multiple arenas
* Neon visual style
* Portal system and slow-motion zones
* Goal-triggered effects
* Results screen with dynamic background:

  * Blue wins → Blue victory background
  * Red wins → Red victory background
* Ghost animation appears above the losing player

---

## Game Modes

The Main Menu offers two buttons. Both start a fresh three-arena series in `Arena_01`.

| Mode | Blue | Red |
|------|------|-----|
| **Two Players** | Human: W A S D + Space | Human: Arrow Keys + Enter |
| **Vs Computer** | Human: W A S D + Space | Computer (Red's keyboard keys are ignored) |

* The selected mode is kept through `Arena_01` → `Arena_02` → `Arena_03` → `Results`.
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
2. Arena (01 / 02 / 03) → Players compete
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

## Tests

Open **Window → General → Test Runner**.

* **EditMode** (`Assets/Tests/EditMode`, assembly `NeonStrikers.Tests.EditMode`)
  * `CpuOpponentBrainTests`: approach side, defend/guard, portal and direction-change retargeting, smooth movement,
    shot choice (never into own goal), lift/hop, and the 0.4 s pulse limit (frame-rate independent, uses game time).
  * `GameModeSettingsTests`: default Two Players, selection persistence, reset, fresh-launch hook.
  * `SceneWiringTests`: Main Menu buttons → `StartTwoPlayers` / `StartVsComputer`, Results buttons, arena keys for
    both players, identical Blue/Red movement settings, `PlayerKick` still disabled, no scene-baked CPU, goals present,
    build settings.
* **PlayMode** (`Assets/Tests/PlayMode`, assembly `NeonStrikers.Tests.PlayMode`)
  * `GameModeFlowTests`: arena opened directly = Two Players, Vs Computer gives Red to the CPU, menu buttons start
    Arena_01 in the chosen mode, mode persists through all arenas and Play Again, Main Menu resets it, and the CPU
    moves and pulses through the real game loop within the pulse limit.

## Notes

* Make sure scenes are added to Build Settings **in the correct order**
* Results scene must be last
* Background image is used instead of winner text
* Ghost animation is handled separately from background
* Time scale is reset in the Results scene

---

