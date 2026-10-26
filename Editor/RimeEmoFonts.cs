using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RimeEmo.Editor
{
    // The UI font of every RimeEmo window. Unity draws Japanese with a shared fallback font whose glyphs went missing
    // after Play mode and other atlas resets ("2. で違う をし とき"), and only a restart brought them back. RimeEmo
    // draws with its own dynamic font made from a Japanese OS font instead, and makes a fresh one when Play mode
    // starts or ends, so a reset atlas never stays on screen.
    [InitializeOnLoad]
    internal static class RimeEmoFonts
    {
        // Japanese UI fonts by platform, most suitable first. The first one found is the font; the next one found backs it
        // up for characters it lacks.
        private static readonly (string family, string style)[] Candidates =
        {
            ("Yu Gothic UI", "Regular"), ("Meiryo UI", "Regular"), ("Meiryo", "Regular"), ("MS UI Gothic", "Regular"),
            ("Hiragino Sans", "W3"), ("Hiragino Kaku Gothic ProN", "W3"),
            ("Noto Sans CJK JP", "Regular"), ("Noto Sans JP", "Regular"),
        };
        // Symbols (arrows, check marks) that Japanese fonts may lack.
        private static readonly (string family, string style)[] SymbolFallbacks = { ("Segoe UI Symbol", "Regular") };
        private const int SamplingPointSize = 48;

        private static FontAsset font;
        private static bool searched;
        private static readonly List<WeakReference<VisualElement>> roots = new List<WeakReference<VisualElement>>();

        static RimeEmoFonts()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode || state == PlayModeStateChange.EnteredPlayMode) Renew();
            };
        }

        internal static FontAsset Font
        {
            get
            {
                if (!font && !searched) font = Create();
                return font;
            }
        }

        // Gives a window's root the RimeEmo font; its text inherits it.
        internal static void Apply(VisualElement root)
        {
            if (root == null) return;
            var asset = Font;
            root.style.unityFontDefinition = asset ? FontDefinition.FromSDFFont(asset) : new StyleFontDefinition(StyleKeyword.Null);
            roots.RemoveAll(r => !r.TryGetTarget(out var e) || e == root);
            roots.Add(new WeakReference<VisualElement>(root));
        }

        // A fresh font for every open window; the old one is dropped after the windows have redrawn with the new one.
        internal static void Renew()
        {
            var old = font;
            font = null; searched = false;
            roots.RemoveAll(r => !r.TryGetTarget(out var e) || e.panel == null);
            foreach (var reference in roots.ToList())
                if (reference.TryGetTarget(out var root)) { Apply(root); root.MarkDirtyRepaint(); }
            if (old) EditorApplication.delayCall += () => Destroy(old);
        }

        private static FontAsset Create()
        {
            searched = true;
            var found = Candidates.Select(Load).Where(f => f).Take(2).ToList();
            if (found.Count == 0) return null;
            var primary = found[0];
            primary.fallbackFontAssetTable = found.Skip(1).Concat(SymbolFallbacks.Select(Load).Where(f => f)).ToList();
            return primary;
        }

        private static FontAsset Load((string family, string style) face)
        {
            try
            {
                var asset = FontAsset.CreateFontAsset(face.family, face.style, SamplingPointSize);
                if (!asset) return null;
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.name = "RimeEmo " + face.family;
                return asset;
            }
            catch (Exception) { return null; }
        }

        private static void Destroy(FontAsset asset)
        {
            if (!asset) return;
            foreach (var fallback in asset.fallbackFontAssetTable ?? new List<FontAsset>())
                Destroy(fallback);
            foreach (var texture in asset.atlasTextures ?? Array.Empty<Texture2D>())
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
            if (asset.material) UnityEngine.Object.DestroyImmediate(asset.material);
            UnityEngine.Object.DestroyImmediate(asset);
        }
    }
}
