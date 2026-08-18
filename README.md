# Asset Performance Lab

A small Unity **URP** project for demonstrating how asset choices and batching settings affect
rendering cost — built as a live teaching lab, so every switch is visible and every claim on screen
is one Unity actually makes.

**Unity 6000.3.15f1**, Universal Render Pipeline 17.3.

## Getting it — no tools, no account, no Git

1. Go to **<https://github.com/Asghar-Sheraz/AssetPerformanceLab>**
2. Click the green **`Code`** button → **`Download ZIP`** (about 400 MB)
3. Unzip it anywhere
4. Open **Unity Hub** → **Add** → **Add project from disk** → pick the unzipped folder
5. Unity will ask to install **6000.3.15f1** if you don't have it. Let it, or open with any
   Unity 6 version and accept the upgrade prompt.

The first import takes a few minutes while Unity builds its `Library` folder. That's normal and
happens once.

> **There is no Git LFS here, on purpose.** Everything in the ZIP is the real file — textures, meshes,
> the lot. You do not need Git, `git-lfs`, a GitHub account, or any command line. If your network
> blocks the ZIP, `git clone https://github.com/Asghar-Sheraz/AssetPerformanceLab.git` works with
> stock Git and needs no extensions.

### Trouble?

| Symptom | Cause |
| --- | --- |
| Pink / magenta materials | Wrong render pipeline. This is a URP project — open it as one rather than importing the assets into an existing Built-in project. |
| "This project was made with a different version" | Fine. Accept the upgrade, or install 6000.3.15f1 from Unity Hub. |
| Missing textures after a download | You have an LFS-era copy. Re-download from the link above; the current repo has no pointer files. |

## Scenes

| Scene | What's in it |
| --- | --- |
| `Assets/Scenes/gpuInstancing_01.unity` | The working scene. A ground plane, 25 cubes sharing one mesh and one material, an environment group, and ~13,700 `moto_prefab` renderers split into 12 groups (only the first enabled by default). |
| `Assets/Scenes/SampleScene.unity` | Near-bare URP template, kept as a clean starting point. |

## The batching demo

`Assets/Scripts/BatchingDemo/` drives the whole thing from a `BatchingDemoProfile` ScriptableObject.
Each mode is **data, not code** — every switch it flips is a field in the Inspector, so the truth
table is visible rather than buried in a method.

Modes, in running order:

1. **No Optimization** — everything off. One draw call per renderer. The control.
2. **SRP Batcher**
3. **GPU Instancing**
4. **Static Batching**
5. **Dynamic Batching**
6. **GPU Resident Drawer** (needs SRP Batcher on and a Forward+ renderer)

Modes are applied in **edit mode**, then you press Play. That matters: Unity does static batching at
scene load, exactly as it would in a build, so what you see is the real mechanism and not a runtime
approximation of it. If the live project state doesn't match any mode exactly, the HUD reports
`CUSTOM` rather than snapping to the nearest one.

Some modes deliberately turn the scene's rotators off — static batching bakes geometry in place, so
leaving the animation running would advertise movement that cannot happen.

## Camera

`Assets/Scripts/OrbitCameraController.cs` is a **Maya-style** play-mode camera:

| Input | Action |
| --- | --- |
| `Alt` + LMB drag | Tumble |
| `Alt` + MMB drag | Track |
| `Alt` + RMB drag | Dolly (right / up = in) |
| Wheel | Zoom, proportional to current distance |
| `F` | Frame selection |
| `A` | Frame all |
| Click | Select, and tumble about that object |
| Hold `Shift` | 3× faster |

Focus uses a ray against `Renderer.bounds` rather than a physics raycast, because imported FBX art
here has no colliders. Set `requireAltForNavigation` to `false` for bare-button dragging.

## Layout

```
Assets/
  Scenes/     fbx/       materials/   prefabs/
  Scripts/    shaders/   textures/    Settings/
Packages/
ProjectSettings/
```
