using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace DreamLayer.SpriteImporter
{
    /// <summary>
    /// Imports a DreamLayer sprite-sheet ZIP into the project:
    /// the sheet is sliced from atlas.json, every frame gets the same ground-anchored pivot
    /// (measured from the pixels, so feet land exactly on the transform), and an AnimationClip
    /// is written with DreamLayer's per-frame durations instead of a guessed frame rate.
    ///
    /// Separate sprite jobs for one character (idle, run, attack...) are each generated at their
    /// own scale. Setting <see cref="Options.CharacterHeightUnits"/> picks a per-sheet
    /// pixels-per-unit so the character is the same height in world units in every clip.
    /// </summary>
    public static class DreamLayerSpriteZipImporter
    {
        public sealed class Options
        {
            /// <summary>Visible character height in world units; 0 keeps a fixed pixels-per-unit.</summary>
            public float CharacterHeightUnits = 2f;
            /// <summary>Used only when CharacterHeightUnits is 0.</summary>
            public float PixelsPerUnit = 100f;
            /// <summary>null = follow the atlas ("cycle" sampling loops).</summary>
            public bool? Loop;
            public FilterMode Filter = FilterMode.Bilinear;
            /// <summary>Alpha above this counts as part of the character when measuring.</summary>
            public byte AlphaThreshold = 10;
        }

        public sealed class Result
        {
            public string SheetPath;
            public string ClipPath;
            public DreamLayerAtlas Atlas;
            public Sprite[] Sprites;
            public AnimationClip Clip;
            public float PixelsPerUnit;
            public Vector2 Pivot;
            /// <summary>Union of visible pixels across all frames, cell space, bottom-left origin.</summary>
            public RectInt VisibleBounds;
        }

        [MenuItem("Assets/DreamLayer/Import Sprite Sheet ZIP...", priority = 40)]
        private static void ImportFromMenu()
        {
            string zip = EditorUtility.OpenFilePanel("Import DreamLayer sprite sheet", "", "zip");
            if (string.IsNullOrEmpty(zip)) return;
            string folder = SelectedFolder();
            var result = Import(zip, folder, new Options());
            Selection.activeObject = result.Clip;
            EditorGUIUtility.PingObject(result.Clip);
            Debug.Log($"DreamLayer: imported {result.Sprites.Length} frames, {result.Atlas.TotalSeconds:0.###}s " +
                      $"{(result.Clip.isLooping ? "loop" : "one-shot")} clip at {result.PixelsPerUnit:0.#} px/unit -> {result.ClipPath}");
        }

        private static string SelectedFolder()
        {
            foreach (var obj in Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets))
            {
                string p = AssetDatabase.GetAssetPath(obj);
                if (AssetDatabase.IsValidFolder(p)) return p;
                if (!string.IsNullOrEmpty(p)) return Path.GetDirectoryName(p)?.Replace('\\', '/');
            }
            return "Assets";
        }

        /// <summary>Imports <paramref name="zipPath"/> into <paramref name="assetFolder"/> (an "Assets/..." path).</summary>
        public static Result Import(string zipPath, string assetFolder, Options options)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("DreamLayer ZIP not found", zipPath);
            string name = Path.GetFileNameWithoutExtension(zipPath);

            byte[] sheetBytes;
            DreamLayerAtlas atlas;
            using (var archive = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read))
            {
                var atlasEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith("atlas.json", StringComparison.OrdinalIgnoreCase))
                                 ?? throw new InvalidDataException("No atlas.json in " + zipPath);
                atlas = DreamLayerAtlas.FromJson(ReadText(atlasEntry));
                var sheetEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(atlas.Image, StringComparison.OrdinalIgnoreCase))
                                 ?? throw new InvalidDataException("No " + atlas.Image + " in " + zipPath);
                sheetBytes = ReadBytes(sheetEntry);
            }

            // Measure the visible character from the pixels before Unity re-imports the texture.
            var probe = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!probe.LoadImage(sheetBytes)) throw new InvalidDataException("sheet.png could not be decoded");
            int sheetW = probe.width, sheetH = probe.height;
            RectInt visible = VisibleBounds(probe, atlas, options.AlphaThreshold);
            UnityEngine.Object.DestroyImmediate(probe);

            var frameSize = atlas.Frames[0].Rect.size;
            // Pivot: horizontally centred on the visible body, vertically on the lowest opaque
            // pixel, i.e. the feet. With ground_anchor alignment that line is shared by every frame.
            var pivot = new Vector2(
                (visible.x + visible.width * 0.5f) / frameSize.x,
                visible.y / (float)frameSize.y);
            float ppu = options.CharacterHeightUnits > 0
                ? Mathf.Max(1f, visible.height / options.CharacterHeightUnits)
                : options.PixelsPerUnit;

            Directory.CreateDirectory(ToSystemPath(assetFolder));
            string sheetPath = $"{assetFolder}/{name}.png";
            File.WriteAllBytes(ToSystemPath(sheetPath), sheetBytes);
            AssetDatabase.ImportAsset(sheetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(sheetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.spritePixelsPerUnit = ppu;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = options.Filter;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = Mathf.Max(2048, Mathf.NextPowerOfTwo(Mathf.Max(sheetW, sheetH)));
#pragma warning disable 618 // spritesheet is the dependency-free API on 2021.3; it still works on 2022+.
            importer.spritesheet = atlas.Frames.Select(f => new SpriteMetaData
            {
                name = f.Name,
                // atlas.json uses a top-left origin; Unity sprite rects are bottom-left.
                rect = new Rect(f.Rect.x, sheetH - f.Rect.y - f.Rect.height, f.Rect.width, f.Rect.height),
                alignment = (int)SpriteAlignment.Custom,
                pivot = pivot,
            }).ToArray();
#pragma warning restore 618
            importer.SaveAndReimport();

            var byName = AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>().ToDictionary(s => s.name);
            var sprites = atlas.Frames.Select(f => byName.TryGetValue(f.Name, out var s)
                ? s
                : throw new InvalidDataException("Sprite " + f.Name + " missing after import")).ToArray();

            string clipPath = $"{assetFolder}/{name}.anim";
            var clip = BuildClip(sprites, atlas, options.Loop ?? atlas.Loops);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (existing != null)
            {
                EditorUtility.CopySerialized(clip, existing);
                clip = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(clip, clipPath);
            }
            AssetDatabase.SaveAssets();

            return new Result
            {
                SheetPath = sheetPath,
                ClipPath = clipPath,
                Atlas = atlas,
                Sprites = sprites,
                Clip = clip,
                PixelsPerUnit = ppu,
                Pivot = pivot,
                VisibleBounds = visible,
            };
        }

        /// <summary>One keyframe per frame at its exact start time, plus a closing key so the last frame holds its full duration.</summary>
        public static AnimationClip BuildClip(IReadOnlyList<Sprite> sprites, DreamLayerAtlas atlas, bool loop)
        {
            var clip = new AnimationClip { frameRate = 60f };
            var keys = new ObjectReferenceKeyframe[sprites.Count + 1];
            float t = 0f;
            for (int i = 0; i < sprites.Count; i++)
            {
                keys[i] = new ObjectReferenceKeyframe { time = t, value = sprites[i] };
                t += atlas.FrameDurationsMs[i] / 1000f;
            }
            keys[sprites.Count] = new ObjectReferenceKeyframe { time = t, value = sprites[sprites.Count - 1] };
            var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
            AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            return clip;
        }

        /// <summary>Union of opaque pixels over every frame, in cell space with a bottom-left origin.</summary>
        public static RectInt VisibleBounds(Texture2D sheet, DreamLayerAtlas atlas, byte threshold)
        {
            var px = sheet.GetPixels32();
            int w = sheet.width, h = sheet.height;
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            var keptBackground = new HashSet<int>(atlas.KeptBackgroundFrames);
            for (int fi = 0; fi < atlas.Frames.Count; fi++)
            {
                // A frame whose background could not be removed would measure as the full cell.
                if (keptBackground.Contains(fi) || keptBackground.Contains(fi + 1)) continue;
                var r = atlas.Frames[fi].Rect;
                int bottom = h - r.y - r.height; // texture rows run bottom-up
                for (int y = 0; y < r.height; y++)
                {
                    int row = (bottom + y) * w;
                    for (int x = 0; x < r.width; x++)
                    {
                        if (px[row + r.x + x].a <= threshold) continue;
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                }
            }
            if (minX == int.MaxValue) return new RectInt(0, 0, atlas.Frames[0].Rect.width, atlas.Frames[0].Rect.height);
            return new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
        }

        private static string ReadText(ZipArchiveEntry e)
        {
            using (var r = new StreamReader(e.Open())) return r.ReadToEnd();
        }

        private static byte[] ReadBytes(ZipArchiveEntry e)
        {
            using (var s = e.Open())
            using (var ms = new MemoryStream())
            {
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        private static string ToSystemPath(string assetPath) =>
            Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? "", assetPath);
    }
}
