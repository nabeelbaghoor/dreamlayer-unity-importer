using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DreamLayer.SpriteImporter.Tests
{
    /// <summary>
    /// Headless check, run with:
    ///   Unity -batchmode -nographics -projectPath P -executeMethod DreamLayer.SpriteImporter.Tests.ImporterSmokeTest.Run
    ///         -dlZips a.zip,b.zip -dlReport report.json
    /// Imports every ZIP, verifies slicing, pivots, timing and cross-sheet height, then builds a character.
    /// </summary>
    public static class ImporterSmokeTest
    {
        private const string Folder = "Assets/_DreamLayerSmokeTest";
        private const float CharacterHeight = 2f;

        public static void Run()
        {
            var report = new StringBuilder("{\n  \"checks\": [\n");
            int failures = 0;
            void Check(string what, bool ok, string detail)
            {
                if (!ok) failures++;
                report.Append($"    {{\"check\": \"{what}\", \"ok\": {(ok ? "true" : "false")}, \"detail\": \"{detail.Replace("\"", "'")}\"}},\n");
                Debug.Log($"[DreamLayer smoke] {(ok ? "PASS" : "FAIL")} {what}: {detail}");
            }

            try
            {
                string[] zips = Arg("-dlZips").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                AssetDatabase.DeleteAsset(Folder);
                var options = new DreamLayerSpriteZipImporter.Options { CharacterHeightUnits = CharacterHeight };
                var results = zips.Select(z => DreamLayerSpriteZipImporter.Import(z.Trim(), Folder, options)).ToArray();

                foreach (var r in results)
                {
                    string n = Path.GetFileNameWithoutExtension(r.SheetPath);
                    Check($"{n}: one sprite per atlas frame", r.Sprites.Length == r.Atlas.Frames.Count,
                        $"{r.Sprites.Length} sprites / {r.Atlas.Frames.Count} frames");
                    Check($"{n}: sprite rects match atlas",
                        r.Sprites.Select((s, i) => (int)s.rect.width == r.Atlas.Frames[i].Rect.width && (int)s.rect.height == r.Atlas.Frames[i].Rect.height).All(b => b),
                        $"cell {r.Atlas.Frames[0].Rect.width}x{r.Atlas.Frames[0].Rect.height}");
                    var pivots = r.Sprites.Select(s => s.pivot / s.rect.size).ToArray();
                    Check($"{n}: one shared ground pivot", pivots.All(p => Vector2.Distance(p, pivots[0]) < 1e-4f),
                        $"pivot {pivots[0].x:0.###},{pivots[0].y:0.###}");
                    float clipLen = r.Clip.length;
                    Check($"{n}: clip length equals DreamLayer timing", Mathf.Abs(clipLen - r.Atlas.TotalSeconds) < 0.002f,
                        $"{clipLen:0.###}s vs {r.Atlas.TotalSeconds:0.###}s");
                    var keys = AnimationUtility.GetObjectReferenceCurve(r.Clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"));
                    float t = 0f;
                    bool timesOk = keys.Length == r.Sprites.Length + 1;
                    for (int i = 0; timesOk && i < r.Sprites.Length; i++)
                    {
                        timesOk = Mathf.Abs(keys[i].time - t) < 0.002f && keys[i].value == r.Sprites[i];
                        t += r.Atlas.FrameDurationsMs[i] / 1000f;
                    }
                    Check($"{n}: keyframes at exact frame start times", timesOk, $"{keys.Length} keys");
                    Check($"{n}: loop flag follows atlas sampling", r.Clip.isLooping == r.Atlas.Loops, $"sampling={r.Atlas.Sampling}");
                    float worldH = r.VisibleBounds.height / r.PixelsPerUnit;
                    Check($"{n}: character is {CharacterHeight} units tall", Mathf.Abs(worldH - CharacterHeight) < 0.01f,
                        $"{worldH:0.###} units at {r.PixelsPerUnit:0.##} px/unit (atlas scale {r.Atlas.Scale})");
                }

                var prefab = DreamLayerCharacterBuilder.Build(results.Select(r => r.Clip).ToArray(), Folder, "SmokeCharacter");
                var animator = prefab.GetComponent<Animator>();
                Check("character prefab has an Animator with every clip", animator != null &&
                      animator.runtimeAnimatorController.animationClips.Length == results.Length,
                      $"{animator?.runtimeAnimatorController.animationClips.Length} clips");
            }
            catch (Exception e)
            {
                Check("import ran without exceptions", false, e.GetType().Name + ": " + e.Message);
            }

            report.Append($"    {{\"check\": \"summary\", \"ok\": {(failures == 0 ? "true" : "false")}, \"detail\": \"{failures} failure(s)\"}}\n  ],\n");
            report.Append($"  \"unity\": \"{Application.unityVersion}\"\n}}\n");
            File.WriteAllText(Arg("-dlReport", "dreamlayer-smoke-report.json"), report.ToString());
            EditorApplication.Exit(failures == 0 ? 0 : 1);
        }

        private static string Arg(string name, string fallback = null)
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            if (i >= 0 && i + 1 < args.Length) return args[i + 1];
            if (fallback != null) return fallback;
            throw new ArgumentException("Missing command-line argument " + name);
        }
    }
}
