using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace DreamLayer.SpriteImporter
{
    /// <summary>The atlas.json that ships inside every DreamLayer sprite-sheet ZIP.</summary>
    public sealed class DreamLayerAtlas
    {
        public sealed class Frame
        {
            public string Name;
            /// <summary>Pixel rect in the sheet, top-left origin, exactly as written in atlas.json.</summary>
            public RectInt Rect;
        }

        public string Action;
        public string Alignment;
        public string Sampling;
        public string Image = "sheet.png";
        public int Cell;
        public int Columns;
        public int Rows;
        public int SheetWidth;
        public int SheetHeight;
        public double Scale;
        public readonly List<Frame> Frames = new List<Frame>();
        public readonly List<int> FrameDurationsMs = new List<int>();
        public readonly List<int> KeptBackgroundFrames = new List<int>();

        /// <summary>DreamLayer marks repeating animations with "cycle" sampling.</summary>
        public bool Loops => string.Equals(Sampling, "cycle", StringComparison.OrdinalIgnoreCase);

        public bool GroundAnchored => string.Equals(Alignment, "ground_anchor", StringComparison.OrdinalIgnoreCase);

        public float TotalSeconds => FrameDurationsMs.Sum() / 1000f;

        public static DreamLayerAtlas FromJson(string json)
        {
            if (!(MiniJson.Parse(json) is Dictionary<string, object> root)) throw new FormatException("atlas.json is not an object");
            var a = new DreamLayerAtlas
            {
                Action = Str(root, "action"),
                Alignment = Str(root, "alignment"),
                Sampling = Str(root, "sampling"),
                Image = Str(root, "image") ?? "sheet.png",
                Cell = Int(root, "cell"),
                Columns = Int(root, "columns"),
                Rows = Int(root, "rows"),
                Scale = Num(root, "scale"),
            };
            if (root.TryGetValue("size", out var sizeObj) && sizeObj is Dictionary<string, object> size)
            {
                a.SheetWidth = Int(size, "w");
                a.SheetHeight = Int(size, "h");
            }

            if (!(root.TryGetValue("frames", out var framesObj) && framesObj is Dictionary<string, object> frames) || frames.Count == 0)
                throw new FormatException("atlas.json has no frames");
            foreach (var kv in frames.OrderBy(kv => FrameOrder(kv.Key)))
            {
                var f = (Dictionary<string, object>)kv.Value;
                var r = f.TryGetValue("frame", out var fr) && fr is Dictionary<string, object> rect ? rect : f;
                a.Frames.Add(new Frame { Name = kv.Key, Rect = new RectInt(Int(r, "x"), Int(r, "y"), Int(r, "w"), Int(r, "h")) });
            }

            if (root.TryGetValue("frame_durations_ms", out var dObj) && dObj is List<object> durations && durations.Count == a.Frames.Count)
            {
                a.FrameDurationsMs.AddRange(durations.Select(d => Math.Max(1, (int)Math.Round(Convert.ToDouble(d, CultureInfo.InvariantCulture)))));
            }
            else
            {
                // Older bundles only carry the total; spread it evenly.
                int total = Int(root, "duration_ms");
                int each = Math.Max(1, total > 0 ? total / a.Frames.Count : 100);
                a.FrameDurationsMs.AddRange(Enumerable.Repeat(each, a.Frames.Count));
            }

            if (root.TryGetValue("kept_background_frames", out var kObj) && kObj is List<object> kept)
                a.KeptBackgroundFrames.AddRange(kept.Select(k => Convert.ToInt32(k, CultureInfo.InvariantCulture)));
            return a;
        }

        /// <summary>Natural order: walk_2 before walk_10.</summary>
        private static long FrameOrder(string name)
        {
            var m = Regex.Match(name, @"(\d+)(?!.*\d)");
            return m.Success ? long.Parse(m.Value, CultureInfo.InvariantCulture) : long.MaxValue;
        }

        private static string Str(Dictionary<string, object> d, string k) => d.TryGetValue(k, out var v) ? v as string : null;

        private static double Num(Dictionary<string, object> d, string k) =>
            d.TryGetValue(k, out var v) && v != null && !(v is string) && !(v is bool) ? Convert.ToDouble(v, CultureInfo.InvariantCulture) : 0;

        private static int Int(Dictionary<string, object> d, string k) => (int)Math.Round(Num(d, k));
    }
}
