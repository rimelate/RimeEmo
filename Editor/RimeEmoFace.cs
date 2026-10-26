using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal static class RimeEmoFace
    {
        internal static string Path(RimeEmoProject project)
        {
            if (project.faceMeshSelected) return project.faceMeshPath;
            var body = project.shapes.Select(s => s.path).Distinct()
                .Where(p => p.Split('/').Last().Equals("Body", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '/')).FirstOrDefault();
            if (body != null) return body;
            return project.shapes.GroupBy(s => s.path).OrderByDescending(g =>
                g.Count(s => IsFacial(s.shape)) * 10 + (g.Key.Equals("Face", StringComparison.OrdinalIgnoreCase) ? 5 : 0))
                .Select(g => g.Key).FirstOrDefault();
        }

        private static bool IsFacial(string name)
        {
            string n = name.ToLowerInvariant();
            return n.Contains("eye") || n.Contains("brow") || n.Contains("mouth") || n.Contains("blink") || n.StartsWith("vrc.v_");
        }

        internal static IEnumerable<ShapeEntry> Shapes(RimeEmoProject project)
        {
            string path = Path(project);
            return project.shapes.Where(s => s.path == path);
        }

        internal static void Resolve(RimeEmoProject project, GameObject avatar)
        {
            if (!project || project.faceMeshSelected) return;
            string path = project.shapes.Select(s => s.path).Distinct()
                .Where(p => p.Split('/').Last().Equals("Body", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p.Count(c => c == '/')).FirstOrDefault();
            if (path == null && avatar)
            {
                var descriptor = avatar.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().FullName == "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor");
                if (descriptor)
                {
                    var renderer = descriptor.GetType().GetField("VisemeSkinnedMesh")?.GetValue(descriptor) as SkinnedMeshRenderer;
                    if (renderer && renderer.sharedMesh && renderer.transform.IsChildOf(avatar.transform))
                        path = AnimationUtility.CalculateTransformPath(renderer.transform, avatar.transform);
                }
            }
            path = path ?? Path(project);
            if (path == null) return;
            project.faceMeshPath = path;
            project.faceMeshSelected = true;
            EditorUtility.SetDirty(project);
        }
    }
}
