# VR Tutorial Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A tutorial in `tutorial_scene` that teaches looking, walking and pointing/grip on fixed street signs. It keeps the website lobby open until the player presses Begin, then loads `demo_city_night` with the session still connected.

**Architecture:** `StreetTutorial : Tutorial` runs the steps and drives two `TutorialSign` world-space canvases. `ApplicationConnection` sends its connect/pair text to sign 1 when it has one. After Begin it calls `StartGame()`, moves `SessionClient` and `PlayerViewStreamer` onto a `DontDestroyOnLoad` object, and loads `nextScene`. The server drops the lobby countdown and the skip button.

**Tech Stack:** Unity 6, URP 17.3, XRI 3.5.1 (Starter Assets rig), Input System, legacy UI `Text`. ASP.NET Core + SignalR, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-16-vr-tutorial-design.md`

## Global Constraints

- All in-game and site text is in English. Code comments follow the existing files (Dutch in these scripts).
- Branch `feature/vr-tutorial`. Ask the user before every commit (user rule). The commit steps below are proposals.
- `ApplicationConnection.unity` must keep working: no sign means the head-following panel, and an empty `nextScene` means the phone test vote.
- Movement: left stick smooth move, right stick snap turn, teleport off. Select = grip.
- Look-around threshold: 70° left and 70° right of the start yaw.
- The sign background is white at about 60 % alpha with dark text.
- Don't touch lighting or shared prefabs outside `tutorial_scene`.
- The Unity project has no test framework. Unity checks are manual: the user tests in the editor and in the headset. Webapp checks use `dotnet test`.

## Review Focus

1. Pairing happens **after** the player has already walked away from sign 1. Expected: not possible, because pairing comes before look-around. Code is only shown on sign 1 at the start.
2. The session drops mid-tutorial (`SessionLost`). Expected: the existing restart with a new code. `StreetTutorial` must reset to the pairing state, not keep running. Covered in Task 3 (`ResetTutorial()`).
3. The player hovers the box and stops hovering before pressing grip. Expected: the box goes back to its normal color, and step 5 needs the box to be selected, not "was hovered once". Covered in Task 2.
4. Grip is pressed on the Begin button before it is visible. Expected: nothing happens, because the button object is inactive until step 6. Covered in Task 2.
5. In `demo_city_night` there is a second `SessionClient` or `XRRigSetup` from the scene. Expected: the scene has none of these, so the persistent one is the only one. Verified in Task 5.

---

### Task 1: Server and site — lobby without a timer

**Files:**
- Modify: `webapp/src/CollectiveNarratives.Web/Sessions/Session.cs:45-46,218-262,578-583`
- Modify: `webapp/src/CollectiveNarratives.Web/Sessions/Models.cs:47-52,86`
- Modify: `webapp/src/CollectiveNarratives.Web/Hubs/SessionHub.cs:96,135-138`
- Modify: `webapp/src/CollectiveNarratives.Web/wwwroot/screen.html` (lobby section, skip button, countdown code around lines 43-44, 133-134, 247, 272-273)
- Modify: `webapp/src/CollectiveNarratives.Web/wwwroot/host.html` (lines 74, 92, 138-142, 185-188, 219-236)
- Test: `webapp/tests/CollectiveNarratives.Web.Tests/SessionTests.cs`, `HubTests.cs`, `UnityClientTests.cs`

**Interfaces:**
- Produces: hub method `StartTutorial()` with no arguments. `SkipLobby` and the `LobbySkipped` event are gone. `SessionState` has no `LobbyRemaining`.

- [ ] **Step 1: Update the tests first.**
  - In `SessionTests`, delete `Lobby_counts_down_from_StartTutorial` and the three `SkipLobby_*` tests. Change `StartTutorial(60)` to `StartTutorial()`. Keep `StartTutorial_needs_a_paired_screen`. Add `StartTutorial_keeps_the_lobby_open_until_StartGame`: after pairing and `StartTutorial()`, `GetState().Phase` is still the lobby phase. After `StartGame()` it is `Playing`.
  - In `HubTests`, change the lobby test to call `StartTutorial` with no args and assert the screen's state phase is the lobby phase. Delete the skip test.
  - In `UnityClientTests:41`, call `InvokeAsync("StartTutorial")`.
- [ ] **Step 2:** Run `dotnet test webapp` from the repo root. Expected: compile errors or failures on `StartTutorial()`.
- [ ] **Step 3: Implement.** `Session.StartTutorial()` keeps the pairing check and `AddState`, but no `_lobbyEndsAt`. Remove `_lobbyEndsAt`, `_lobbySkipped`, `SkipLobby`, `LobbySkippedEvent` and `LobbyRemaining` (record field and `GetState`). Change the hub method to `public Task StartTutorial() => GameSession().StartTutorial();` and remove the hub's `SkipLobby`.
- [ ] **Step 4: Update the pages.**
  - `screen.html`: the lobby text becomes "The player is learning the controls…". Remove the bar, the skip button and the `lobbyEndsAt` / `lobbyTotal` code.
  - `host.html`: the button reads `StartTutorial()`, the Auto text reads "StartTutorial() after pairing, StartGame() when the player presses Begin", and the timer and `LobbySkipped` handler are removed.
- [ ] **Step 5:** Run `dotnet test webapp`. Expected: all pass. Then grep `rg -n "SkipLobby|LobbySkipped|lobbyRemaining|LobbyRemaining" webapp/src webapp/tests`. Expected: no hits.
- [ ] **Step 6:** Propose a commit: `feat(web): keep lobby open until player starts`.

### Task 2: Signs and interactables (Unity scripts)

**Files:**
- Create: `CN_VR/Assets/ApplicationConnection/Scripts/TutorialSign.cs`
- Create: `CN_VR/Assets/ApplicationConnection/Scripts/ColorOnInteract.cs`

**Interfaces:**
- Produces:
  - `public class TutorialSign : MonoBehaviour` with `public void Show(string title, string big, string small)`. On `Awake` it builds a world-space canvas child of 1600×900 at scale `0.001`, a white `(1,1,1,0.6)` background and dark text `(0.1,0.1,0.12)`. The title, big and small layout is copied from `ApplicationConnection.BuildVrPanel`. It is fixed in the world and does not follow the head.
  - `public class ColorOnInteract : MonoBehaviour` that requires `XRSimpleInteractable` and a `Renderer`. It has `public Color hoverColor = Color.blue, selectColor = Color.green;`, `public bool WasSelected { get; private set; }` and `public bool IsHovered { get; private set; }`. Hover enter sets blue and hover exit restores the original color, unless `WasSelected`. Select entered sets green and `WasSelected = true`. Use `renderer.material.color`.
  - `public class GripHint : MonoBehaviour` is placed on a child of each controller, at the grip button (the inside of the handle, where the middle finger rests).
    - It shows a pulsing bright-yellow sphere of about 3 cm and a small world-space label "GRIP". The label faces the camera and sits about 6 cm beside the button, with a thin line to the sphere.
    - It has `public void SetVisible(bool on)`. The pulse is an unscaled-time sine on scale and emission.
    - It starts hidden.
- [ ] **Step 1:** Write the three scripts. Use the font the same way as `ApplicationConnection.Awake`.
- [ ] **Step 2:** Let Unity compile. Expected: no errors in the Console. Check with `Editor.log` or the user.
- [ ] **Step 3:** Propose a commit: `feat(vr): add tutorial sign and color interactable`.

### Task 3: `StreetTutorial` step logic + `ApplicationConnection` changes

**Files:**
- Create: `CN_VR/Assets/ApplicationConnection/Scripts/StreetTutorial.cs`
- Modify: `CN_VR/Assets/ApplicationConnection/Scripts/Tutorial.cs`
- Modify: `CN_VR/Assets/ApplicationConnection/Scripts/ApplicationConnection.cs`
- Modify: `CN_VR/Assets/ApplicationConnection/Scripts/SessionClient.cs:133-143,288-290,310`

**Interfaces:**
- Consumes: `TutorialSign.Show`, `ColorOnInteract.WasSelected/IsHovered` from Task 2, and the hub `StartTutorial()` from Task 1.
- Produces:
  - `Tutorial`: add `public virtual TutorialSign Sign => null;` (the sign for the connect/pair text) and `public virtual void ResetTutorial() { IsDone = false; }`.
  - `StreetTutorial : Tutorial` has the fields `TutorialSign sign1, sign2; ColorOnInteract box; XRSimpleInteractable beginButton; Collider walkZone;` and the overrides `Sign => sign1`, `Begin()` and `ResetTutorial()`.
  - `public static bool LookedAround(float minYaw, float maxYaw, float threshold = 70f) => minYaw <= -threshold && maxYaw >= threshold;`. The yaw is `Mathf.DeltaAngle(startYaw, head.eulerAngles.y)`, tracked as a running min and max.
  - Steps: `enum Step { Idle, Look, Walk, Point, Grip, Begin, Done }`, using the texts from the spec table, word for word. Walk finishes when the main camera's position is inside `walkZone.bounds`. That is simpler than a trigger callback, because the rig's CharacterController sits on a different object. `beginButton` starts inactive, is activated at `Step.Begin`, and its `selectEntered` sets `IsDone = true`.
  - `StreetTutorial` also gets `GripHint[] gripHints`. They are visible during `Step.Grip` and `Step.Begin`, and hidden otherwise.
  - The Grip text on sign 2: title "Press GRIP", small text "Squeeze the button under your middle finger, on the inside of the controller. Look at your hand: it's the glowing yellow one."
  - `ApplicationConnection`: add `public string nextScene = "";`.
- [ ] **Step 1:** Update `SessionClient`: `StartTutorial()` takes no arguments. Remove `SkipLobby`, `LobbySkipped` and the `LobbySkippedEvent` class.
- [ ] **Step 2:** Update `ApplicationConnection`.
  - `ShowVr` writes to `tutorial.Sign` when it is not null, and otherwise to the follow panel.
  - Remove `lobbySeconds`, `lobbyStart`, `LobbyLeft`, `Phase.WaitingForAudience`, the A/K skip, and the matching debug lines.
  - On `SessionLost`, call `tutorial.ResetTutorial()` before restarting.
  - After `WaitUntil(tutorial.IsDone)`, call `StartGame()` when online. Then, if `nextScene` is empty, run `PhoneVote()` as today. Otherwise call `KeepSession()` and `SceneManager.LoadScene(nextScene)`.
  - `void KeepSession()` creates a root `GameObject("Session")`, moves `sessionClient` and `playerView` onto it (re-parent their GameObjects if they have their own, otherwise `AddComponent` a new one and copy `config`), and calls `DontDestroyOnLoad`.
  - In `Awake`, reuse an existing `Session` object if one is found (`Find<SessionClient>()` already covers this).
  - Update the header comment and the debug overlay key list.
- [ ] **Step 3:** Update `PlayerViewStreamer`: on `SceneManager.sceneLoaded`, destroy `_cam` so that `CreateCamera` copies the new main camera.
- [ ] **Step 4:** Let Unity compile and open `ApplicationConnection.unity` in Play mode. Expected: code panel, then press S (offline), then the tutorial is done at once and the phone rings. That is the old behavior without the countdown.
- [ ] **Step 5:** Propose a commit: `feat(vr): street tutorial steps and scene handoff`.

### Task 4: Build `tutorial_scene`

**Files:**
- Modify: `CN_VR/Assets/Scenes/tutorial_scene.unity` (in the Unity editor)
- Modify: `CN_VR/ProjectSettings/EditorBuildSettings.asset`

- [ ] **Step 1: Day lighting.** Set the skybox to the default procedural sky and ambient mode to Skybox. Make the directional light the sun: intensity about 1.3, warm white, rotation about (50, -30, 0). Disable the `Light` components under the `LIGHTING` / `LIGHTONG` groups. Keep the meshes.
- [ ] **Step 2: Rig.** Place `XR Origin (XR Rig).prefab` at a `SpawnPoint` in the middle of the street, facing down the street.
  - Turn teleport off: disable the teleport interactor and locomotion on the right hand, and keep snap turn.
  - Confirm the left-hand continuous move is on.
- [ ] **Step 3: Game objects.**
  - Add `Game` with `ApplicationConnection` (`nextScene = "demo_city_night"`) and `StreetTutorial`.
  - Place sign 1 about 3 m in front of the spawn. Place sign 2 about 10 m further.
  - Add a `walkZone` (a BoxCollider with isTrigger, about 3×2×3 m) at sign 2.
  - Place the box (a cube of about 0.4 m on a stand of about 1 m, with `XRSimpleInteractable` and `ColorOnInteract`) next to sign 2.
  - Put the `Begin` button (a cube or panel with `XRSimpleInteractable`, labeled "Begin", inactive) under sign 2.
  - Add a `GripHint` under the left and right controller of the rig. Position it at the grip button and let the user fine-tune that in the headset.
- [ ] **Step 4: Bounds.** Close off both street ends and any gaps along the sides with invisible BoxColliders (no renderer) of about 3 m high. Give building fronts without colliders a wall collider as well.
- [ ] **Step 5: Build settings.** Keep the existing scenes. Put `tutorial_scene` first and `demo_city_night` second.
- [ ] **Step 6: Editor check.** Run Play mode with the server on localhost and pair through `/screen`. The user checks:
  - The sign shows the code, then "Look around".
  - "Walk" appears.
  - Walking to sign 2 works.
  - The box turns blue on pointing and green on grip.
  - Begin appears, and pressing it loads the next scene.
  - The street walls hold the player in.
- [ ] **Step 7:** Propose a commit: `feat(vr): tutorial street with signs, day light and bounds`.

### Task 5: `demo_city_night` spawn in the house

**Files:**
- Modify: `CN_VR/Assets/Scenes/demo_city_night.unity`

- [ ] **Step 1:** Place `XR Origin (XR Rig).prefab` (same settings as Task 4) at a `SpawnPoint` inside the house. Ask the user to confirm the spot. Don't add a `SessionClient` or `ApplicationConnection` there.
- [ ] **Step 2:** Check by running the whole flow from `tutorial_scene`. Expected:
  - After Begin, the player stands in the house and the big screen keeps showing the player view.
  - The debug log has no second `SessionClient`.
  - `/screen` shows the game phase.
- [ ] **Step 3:** Propose a commit: `feat(vr): spawn player in house after tutorial`.

### Task 6: Headset test + finish

- [ ] **Step 1:** Build to the Quest. The user checks:
  - The signs are readable.
  - The grip works.
  - The walls hold.
  - The scene switch keeps the big screen connected.
- [ ] **Step 2:** Run superpowers:verification-before-completion, and run `dotnet test webapp` again.
- [ ] **Step 3:** Ask the user to push and open a PR to `main`.
