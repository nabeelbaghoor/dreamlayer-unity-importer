# DreamLayer Sprite Importer for Unity

Turns a [DreamLayer](https://dreamlayer.io) sprite-sheet ZIP into something you can drop straight into a
Unity scene: sliced sprites, a frame-accurate `AnimationClip`, and a character prefab with an `Animator`.

## Why

DreamLayer's sprite bundles are good (`sheet.png`, `atlas.json`, per-frame durations, ground-anchored
alignment), but getting one into Unity properly still takes manual work, and two production problems
show up as soon as a character has more than one animation:

| Problem | What this importer does |
|---|---|
| The sheet has to be sliced by hand, frame by frame | Slices `sheet.png` from the atlas rects (top-left atlas origin converted to Unity's bottom-left) |
| Pivots default to the centre, so characters float | Measures the lowest opaque row across all frames and puts one shared pivot exactly at the feet |
| Frame timing is per frame, not a single fps | Writes one keyframe per frame at its exact start time from `frame_durations_ms`; clip length matches DreamLayer's timing to the millisecond (Unity adds one frame to sprite-clip length; the closing key compensates) |
| **Each sprite job has its own scale**, so idle and run of the same character are different sizes and the character pops when animations switch | Picks a per-sheet pixels-per-unit from the measured body height, so every clip of a character is the same height in world units |
| Frames whose background DreamLayer could not remove (`kept_background_frames`) would distort the measurements | Excluded from measurement |
| Clips still need an Animator | **Assets > DreamLayer > Build Character From Selected Clips** creates a controller (idle as default state) and a prefab |
| A sprite job opens with the reference pose easing into the action, so looping every frame hitches | `FirstFrame` / `FrameCount` build the clip from just the loop frames |
| Exported durations follow the source video's sampling (a 12-frame run came back at 500 ms per frame) | `TargetSeconds` retimes the clip to game speed while keeping DreamLayer's relative frame timing |

## Use

1. Install: Package Manager > Add package from git URL (or `file:` path to this folder).
2. Select a folder in the Project window, then **Assets > DreamLayer > Import Sprite Sheet ZIP...**
3. Select the clips you imported for one character, then **Assets > DreamLayer > Build Character From Selected Clips**.

Scripted:

```csharp
var result = DreamLayerSpriteZipImporter.Import(zipPath, "Assets/Characters/Wren",
    new DreamLayerSpriteZipImporter.Options { CharacterHeightUnits = 2f });

// A run cycle: skip the 3 intro frames and play the loop at game speed.
DreamLayerSpriteZipImporter.Import(runZip, "Assets/Characters/Wren",
    new DreamLayerSpriteZipImporter.Options { CharacterHeightUnits = 2f, FirstFrame = 3, TargetSeconds = 0.7f });
```

## Validation

`Tests/Editor/ImporterSmokeTest.cs` imports DreamLayer's public sample walk cycle plus a second sheet of the
same character at a different scale, then checks slicing, pivots, keyframe times, clip length, loop flags,
equal world height across sheets, and the generated Animator. Result on Unity 2021.3.7f1:
**16/16 checks pass** (`Tests/Results/smoke-unity-2021.3.7f1.json`). Note: the two sheets were imported at
63 and 48 px/unit and both come out exactly 2.0 units tall.

Built by Nabeel Hassan while making *The Last Lamplighter* for the DreamLayer Jam.
