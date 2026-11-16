# VR tutorial in `tutorial_scene` — design

Date: 2026-11-16

## Intent

New players put on the headset without knowing the controls. While they learn to look, walk and interact, the audience has time to join through the website. The tutorial replaces the fixed 60 s lobby: the game starts when the player presses **Begin**, not when a timer runs out. After Begin the player is taken into the house in `demo_city_night`, where the story starts.

## Goals

- The player learns, in order: look around, walk with the left stick, point at an object, interact with the grip button.
- Every step finishes because the player actually did the action. No "next" buttons, except the final Begin.
- The pairing code for the big screen (`/screen`) is shown on the first sign, so the player can read it out to the people at the laptop.
- The lobby on the site stays open until Begin. Then `StartGame()` runs and `demo_city_night` loads, with the session still connected.
- The player cannot leave the tutorial street.

## Non-goals

- No teleport (the user chose smooth movement only).
- No grabbing or throwing objects. The box only changes color.
- No changes to the story or the votes inside `demo_city_night`.
- No Dutch texts. All in-game text is in English, like the existing panel and the site.

## Anti-goals

- Text glued to the player's face. The signs stand still in the street.
- A tutorial that can get stuck: if the server is down, the "play without a big screen" option must still work.
- Breaking `ApplicationConnection.unity`, the existing test scene for the website link.

## Constraints

- Unity 6, URP 17.3, XR Interaction Toolkit 3.5.1, Input System, Meta Quest via OpenXR.
- The XRI Starter Assets rig exists: `Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab`. By default it has stick movement, snap turn, teleport, and near-far interactors, and the grip button selects.
- The flow lives in `ApplicationConnection.cs` (connect, show code, wait for `ScreenPaired`, `tutorial.Begin()`, wait for `IsDone` and at least `lobbySeconds`, `StartGame()`, phone test vote). `Tutorial.cs` is an empty hook that is done right away.
- The server keeps a lobby countdown: `Session.StartTutorial(seconds)` sets `_lobbyEndsAt`. `screen.html` shows a countdown bar and a "Skip countdown" button (`SkipLobby` / `LobbySkipped`). `host.html` (test page) and the webapp tests use these.
- `tutorial_scene.unity` and `demo_city_night.unity` contain no XR rig and no `ApplicationConnection`. `tutorial_scene` currently has night lighting.
- Team git workflow: work on a feature branch and open a PR to `main`. `demo_city_night` belongs to a teammate, so keep edits there small.

## Approaches (how the session moves to `demo_city_night`)

1. **Only the session survives the scene load (picked).** `SessionClient` and `PlayerViewStreamer` sit on one object with `DontDestroyOnLoad`. Each scene has its own XR rig and a `SpawnPoint`. Upside: `demo_city_night` can still be tested on its own. Downside: `demo_city_night` needs a rig added.
2. Session and rig both survive and get moved to a `SpawnPoint`. Fewer edits to the teammate's scene. Downside: that scene has no player when tested on its own, and two rigs collide.
3. Reconnect after the load with `ResumeSession`. Downside: the connection drops for a moment and the big screen stutters. Rejected.

## Decisions

### Flow

| Step | Sign | Text (English) | Done when |
|---|---|---|---|
| 0 Connecting | 1 | "Connecting..." / no-connection retry, as today | session code received (or offline chosen) |
| 1 Pair | 1 | "Pair the big screen", big code `123 456`, "On the laptop, go to {site}/screen and type this code." | `ScreenPaired` (or offline) |
| 2 Look around | 1 | "Look around" / "Turn your head to the left and to the right." | head yaw reached ≥ 70° left **and** ≥ 70° right of the start direction |
| 3 Walk | 1 | "Walk" / "Push the left stick to walk to the next sign." | player enters a trigger zone at sign 2 |
| 4 Point | 2 | "Point at the box" / "Aim your hand at the box." | box hovered (turns blue) |
| 5 Interact | 2 | "Press GRIP" / "Squeeze the button under your middle finger, on the inside of the controller. Look at your hand: it's the glowing yellow one." | box selected (turns green) |
| 6 Begin | 2 | "You're ready" + a **Begin** button | Begin button selected (point + grip) → `IsDone = true` |

After step 6, `ApplicationConnection` calls `StartGame()` and loads `demo_city_night`. There is no minimum wait.

When offline (no big screen), the flow skips the pairing step and the rest is the same. The existing "Press A to play without one" hint after 120 s stays, shown on sign 1.

### Signs

- World-space canvas, fixed in the street and facing the spawn point. About 1.6 × 0.9 m, bottom edge at about 1.2 m.
- White background at about 60 % opacity, dark text, the same title/big/small layout as the current panel. Reuse the existing legacy `Text` + built-in font helpers.
- Sign 1 stands about 3 m in front of the spawn point. Sign 2 stands about 10 m further down the street. Next to sign 2 is the box (a cube on a small stand, about 1 m high), and the Begin button sits under the sign text.
- In `tutorial_scene` the text goes to the signs instead of the head-following panel. `ApplicationConnection.unity` keeps the head-following panel.

### Interaction

- The box and the Begin button use `XRSimpleInteractable`. On hover enter the box turns blue (and back to its normal color on hover exit, until it has been selected). On select it turns green and stays green.
- Select is the grip button, which is the XRI default. The trigger is not used.
- The grip button must be obvious to everyone (user request). During the grip and Begin steps, each controller shows a pulsing yellow marker on the grip button with a "GRIP" label. The sign says "Press GRIP" and points to the glowing button.

### Rig and movement

- Put the XRI Starter `XR Origin (XR Rig)` prefab at a `SpawnPoint` on the street in `tutorial_scene`, and another one in the house in `demo_city_night`.
- Left stick: smooth move. Right stick: snap turn. Teleport is turned off.
- `XRRigSetup` already does nothing when an `XROrigin` exists, so no change is needed there.

### Staying in the street

- Invisible `BoxCollider` walls (not triggers) close off both ends and both sides of the tutorial street. The rig's `CharacterController` collides with them.
- Check that the building fronts along the street have colliders. Add wall colliders where they don't.

### Lighting (day)

- `tutorial_scene` only: a day skybox (the default procedural one is fine), a brighter, warmer directional light as the sun, ambient lighting from the skybox, and the night lamp lights turned off.

### Session across scenes

- At startup, `SessionClient` and `PlayerViewStreamer` are moved to a root object `Session` with `DontDestroyOnLoad`. If one already exists, reuse it instead of making a second.
- `ApplicationConnection` gets a `nextScene` field. Empty means: run the phone test vote as today (keeps `ApplicationConnection.unity` working). Set to `demo_city_night` means: after `StartGame()`, load that scene.
- `PlayerViewStreamer` already follows `Camera.main` every frame. After a scene load it must copy the camera settings from the new main camera again (clear flags and skybox differ per scene).
- Add `demo_city_night` and `tutorial_scene` to the build settings, with `tutorial_scene` first.

### Website: lobby without a timer

- Server: `StartTutorial` takes no seconds and sets no lobby end time. Remove `SkipLobby`, `LobbySkippedEvent` and `_lobbySkipped`. Remove or null out `lobbyRemaining` in the state.
- `screen.html`: the lobby shows "The player is learning the controls…" with the QR code, the code and the viewer count. No countdown bar and no Skip button.
- `host.html`: drop the 60 s timer and the Skip handling. "Auto" calls `StartTutorial()` after pairing.
- Unity: remove `lobbySeconds`, the `WaitingForAudience` phase, the A/K skip, `SkipLobby` and `LobbySkipped`.
- Update `HubTests`, `SessionTests` and `UnityClientTests` to match.

### Calls made after the builder check

- `ApplicationConnection` lives in `tutorial_scene` (on a `Game` object next to the rig) and is destroyed on scene load. That is intended: only `Session` survives. When `nextScene` is set, `ApplicationConnection` makes `Session` survive before it loads the scene.
- Offline mode: like today, no `StartTutorial` or `StartGame` calls. After Begin, `nextScene` still loads.
- Removing the lobby timer does not affect votes. `StartVote` already moves the phase to `Playing` itself, and `StartGame` sets `Playing`.
- After the load, nothing runs in `demo_city_night` yet (user decision). The player stands in the house and the session stays connected. The team adds the story and votes later.
- The street in `tutorial_scene` is final (user decision). Signs, walls and the spawn point go straight into the scene, and the scene goes on the feature branch.
- Controllers only, no hand tracking.
- The lamp lights to turn off are the `LIGHTING` objects inside `tutorial_scene`. Don't edit shared prefabs or `demo_city_night` lighting.
- Build settings: keep the existing scenes and put `tutorial_scene` first, then `demo_city_night`.

## Left to the builder

- Exact sign positions, sizes, colors and font sizes, as long as they are readable in the headset at the stated distances.
- How the step logic is structured, for example a `StreetTutorial : Tutorial` subclass with a step enum. The look-around check should be a small pure function with one EditMode test.
- Where the spawn point in the house in `demo_city_night` goes. Pick a spot inside and let the user confirm it in the editor.
- Editor keyboard fallbacks, only if they cost a few lines (for example skip the current step with a key).
- The user tests in the editor and in the headset. Give them a short checklist of what to check after each step.
