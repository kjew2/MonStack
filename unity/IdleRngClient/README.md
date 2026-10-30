# IdleRngClient (Unity POC)

Ugly-UI Unity client for the idle RNG prototype (`specs/07`). It is a thin shell over the pure C# sim in
`src/IdleRng.Sim`. The sim comes in as a local package: `Packages/manifest.json` has `"com.idlerng.sim": "file:../../../src/IdleRng.Sim"`.

## Run it

1. Install **Unity 6.3 LTS** (`6000.3.x`) with Unity Hub. Any 6000.3 patch works. Other Unity 6 versions also work if
   you let the Package Manager pick the URP version that matches the editor.
2. Unity Hub → **Add project from disk** → choose `unity/IdleRngClient` (this folder).
3. On first open Unity imports packages and generates `ProjectSettings/*` and the `.meta` files, including some in
   `src/IdleRng.Sim/`. Commit them. If Unity asks to enable the new Input System backends, answer **Yes**. Either
   setting works: the code picks the matching UI input module.
4. Open any scene (the default `SampleScene`, or an empty new scene) and press **Play**. No scene, prefab or UXML
   setup is needed. `GameRoot` bootstraps itself through `[RuntimeInitializeOnLoadMethod]` and builds the whole UI
   from code.

For a phone-like view, set the Game view to a portrait resolution such as 1080x1920. Landscape also works, with the
layout centred.

Content is read directly from the repo's `Content/` folder: the loader walks up from `Assets/`. Nothing is copied. Edit
`content_src/*.csv`, run `python3 tools/content_build.py`, then press Play again.

**Device builds** cannot see the repo. Copy the content in first with
`python3 tools/content_build.py --copy-to unity/IdleRngClient/Assets/Resources/Content`. The loader falls back to
`Resources/Content`.

## What's there

The HUD (gold, gems, free pulls, shards) and the combat strip are always visible. The strip shows the stage, kill
progress, a WALL badge, party, player and enemy rectangles, HP and boss-timer bars, DPS, and a Boss retry button that
appears while you are walled.

The tabs are:

| Tab | Contents |
|---|---|
| **Combat** | Wall card with "need ~N.Nx DPS", event feed, idle chest, lifetime stats |
| **Stats** | Buy x1 / x10 / Max ATK levels |
| **Gear** | 6 slots, selected detail, Enhance (and "enhance weakest") |
| **Heroes** | Party of 6 (tap a slot to place the selected hero, or to unequip it), 54-hero inventory, auto-equip |
| **Gacha** | Pity bar, pull x1 / x10, result cards, rates with pity-consolidated L+/M |
| **Class** | Automatic advances at stages 50, 150, 300 and 500, with a progress bar |
| **Debug** | Editor/Development builds or `IDLERNG_CHEATS`. Grant gold, gems or heroes; set stage; "Run N h" (real sim ticks); "Clock +N h" (offline earnings popup); x1/x5/x20 speed; save, validate, reset |

Two popups appear in a modal: offline earnings ("Welcome back") and class advance. Toasts cover gear drops, rejects,
quests and the day rollover.

## Layout

```
Assets/IdleRng.Client/
  IdleRng.Client.asmdef        Unity side (UnityEngine, UI Toolkit, uGUI EventSystem, Input System)
  Boot/      GameRoot.cs (bootstrap + MonoBehaviour), ContentLocator.cs, InputSetup.cs
  Screens/   Ui.cs (style kit), Shell.cs, HudAndStrip.cs, *Screen(s).cs, DebugScreen.cs
  Core/      IdleRng.Client.Core.asmdef (noEngineReferences: plain C#)
             GameViewModel.cs, ViewModels.cs, Feedback.cs, TextFmt.cs, SimRunnerCore.cs (10 Hz tick driver)
             SaveBridge.cs   <- the ONLY save/load/session code
Assets/Resources/IdleRngTheme.tss   imports Unity's default runtime theme (optional; styles are inline)
```

## Saving

`Core/SaveBridge.cs` saves through `IdleRng.Runtime` (`specs/06`) to `<persistentDataPath>/saves/`: a CRC-checked
file with a `.prev` backup. Bad files are quarantined and a new profile is created. Offline gold since the last save
is credited on launch. Debug > Reset profile deletes the save. `EphemeralSaveBridge` (no persistence) is still there
for quick tests: return it from `SaveBridgeFactory.Create`.

## Not done (POC)

These are not built yet:

- Gacha card-flip reveal.
- Safe-area padding.
- Android back button.
- Perf overlay.
- Build profiles.
- PlayMode tests.
- URP asset configuration. URP is installed but no pipeline asset is assigned, so the built-in renderer draws the
  camera clear. The UI is UI Toolkit either way.
