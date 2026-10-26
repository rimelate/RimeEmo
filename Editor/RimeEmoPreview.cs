using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace RimeEmo.Editor
{
    internal sealed class RimeEmoPreview : IDisposable
    {
        private sealed class TransformState
        {
            public Transform transform;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool active;
        }

        private sealed class BlendShapeState
        {
            public SkinnedMeshRenderer renderer;
            public float[] weights;
        }
        private sealed class SampleBinding
        {
            public EditorCurveBinding binding;
            public SkinnedMeshRenderer renderer;
            public int index;
            public AnimationCurve curve;
        }

        private readonly PreviewRenderUtility preview;
        private readonly List<TransformState> transforms = new List<TransformState>();
        private readonly List<BlendShapeState> blendShapes = new List<BlendShapeState>();
        private GameObject clone;
        private Bounds bounds;
        private Vector2 orbit = new Vector2(180f, 0f);
        private float distance = 2.5f;
        private Vector3 pivot;
        private bool disposed;
        private readonly Dictionary<AnimationClip, List<SampleBinding>> clipCache = new Dictionary<AnimationClip, List<SampleBinding>>();
        private readonly Dictionary<string, Transform> pathMap = new Dictionary<string, Transform>();
        private Texture cachedImage;
        private AnimationClip cachedClip;
        private RimeEmoMotion cachedMotion, sampledMotion;
        private readonly List<SampleBinding> motionBindings = new List<SampleBinding>();
        private float cachedTime = float.NaN;
        private Rect cachedRect;
        private Vector2 cachedOrbit;
        private float cachedDistance;
        private bool invalidated = true;
        private string facePath;
        private AnimationClip liveClip;
        private SampleBinding liveShape;
        private float liveValue;

        public void SetLiveShape(AnimationClip clip, EditorCurveBinding binding, float value)
        {
            if (liveClip != clip || liveShape == null || !liveShape.binding.Equals(binding))
            {
                liveClip = clip; liveShape = GetBindings(clip).Find(sample => sample.binding.Equals(binding));
                // A shape the clip does not contain yet is previewed straight from the face mesh.
                if (liveShape == null) liveShape = Resolve(binding, null);
            }
            liveValue = value; invalidated = true;
        }

        public void ClearLiveShape()
        {
            if (liveShape != null) invalidated = true;
            liveClip = null; liveShape = null;
        }

        public RimeEmoPreview()
        {
            preview = new PreviewRenderUtility();
            preview.cameraFieldOfView = 30f;
            preview.camera.orthographic = true;
            preview.camera.nearClipPlane = 0.01f;
            preview.camera.farClipPlane = 1000f;
            preview.camera.clearFlags = CameraClearFlags.Color;
            preview.camera.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1f);
        }

        public void SetAvatar(GameObject source)
        {
            if (disposed) return;
            ClearAvatar();
            if (source == null) return;

            var map = new Dictionary<Transform, Transform>();
            clone = BuildVisual(source.transform, null, map);
            clone.name = source.name + " (Preview)";
            clone.hideFlags = HideFlags.HideAndDontSave;
            preview.AddSingleGO(clone);
            foreach (var pair in map) pathMap[AnimationUtility.CalculateTransformPath(pair.Value, clone.transform)] = pair.Value;
            clone.SetActive(true);
            // Bones commonly live in a sibling branch that is created after the mesh.
            // Resolve references only after the complete hierarchy exists.
            foreach (var pair in map)
            {
                var original = pair.Key.GetComponent<SkinnedMeshRenderer>();
                if (!original) continue;
                var copy = pair.Value.GetComponent<SkinnedMeshRenderer>();
                var bones = new Transform[original.bones.Length];
                for (int i = 0; i < bones.Length; i++)
                    if (original.bones[i]) map.TryGetValue(original.bones[i], out bones[i]);
                copy.bones = bones;
                copy.rootBone = original.rootBone && map.TryGetValue(original.rootBone, out var root) ? root : null;
                copy.updateWhenOffscreen = true;
            }

            foreach (var transform in clone.GetComponentsInChildren<Transform>(true))
            {
                transforms.Add(new TransformState { transform = transform, position = transform.localPosition, rotation = transform.localRotation, scale = transform.localScale, active = transform.gameObject.activeSelf });
            }
            foreach (var renderer in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = renderer.sharedMesh;
                if (mesh == null) continue;
                var weights = new float[mesh.blendShapeCount];
                for (var i = 0; i < weights.Length; i++) weights[i] = renderer.GetBlendShapeWeight(i);
                blendShapes.Add(new BlendShapeState { renderer = renderer, weights = weights });
            }

            var renderers = clone.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                bounds = renderers[0].bounds;
                for (var i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            }
            else bounds = new Bounds(clone.transform.position, Vector3.one);
            var animator = source.GetComponentInChildren<Animator>(true);
            var sourceHead = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform head = null;
            if (sourceHead != null) map.TryGetValue(sourceHead, out head);
            pivot = head != null ? head.position : bounds.center;
            distance = head != null ? Mathf.Max(0.4f, bounds.size.y * 0.45f) : Mathf.Max(bounds.size.magnitude * 1.35f, 0.5f);
            foreach (var component in source.GetComponents<Component>())
            {
                if (!component || component.GetType().FullName != "VRC.SDK3.Avatars.Components.VRCAvatarDescriptor") continue;
                // Read the descriptor without introducing an SDK assembly dependency.
                using (var serialized = new SerializedObject(component))
                {
                    var view = serialized.FindProperty("ViewPosition");
                    if (view == null || view.propertyType != SerializedPropertyType.Vector3) break;
                    var value = view.vector3Value;
                    if (!Finite(value) || value.sqrMagnitude < 0.000001f) break;
                    pivot = ViewPivot(source.transform, clone.transform, value);
                    float height = Mathf.Abs(Vector3.Dot(pivot - clone.transform.position, clone.transform.up));
                    distance = Mathf.Max(0.15f, height * 0.5f);
                }
                break;
            }
            orbit = new Vector2(clone.transform.eulerAngles.y + 180f, 0f);
        }

        // SDK ViewPosition is an origin-relative vector; account for the source
        // hierarchy when transferring it into the unparented preview clone.
        internal static Vector3 ViewPivot(Transform source, Transform previewRoot, Vector3 viewPosition)
            => previewRoot.TransformPoint(source.InverseTransformVector(viewPosition));

        private static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z)
            && !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);

        private static GameObject BuildVisual(Transform source, Transform parent, Dictionary<Transform, Transform> map)
        {
            var go = new GameObject(source.name);
            go.hideFlags = HideFlags.HideAndDontSave;
            var t = go.transform;
            if (parent != null) t.SetParent(parent, false);
            t.localPosition = source.localPosition;
            t.localRotation = source.localRotation;
            t.localScale = source.localScale;
            go.SetActive(source.gameObject.activeSelf);
            map[source] = t;
            var mf = source.GetComponent<MeshFilter>();
            if (mf != null) go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
            var mr = source.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var copy = go.AddComponent<MeshRenderer>();
                EditorUtility.CopySerialized(mr, copy);
                copy.sharedMaterials = mr.sharedMaterials;
            }
            var smr = source.GetComponent<SkinnedMeshRenderer>();
            if (smr != null)
            {
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                EditorUtility.CopySerialized(smr, copy);
                copy.sharedMaterials = smr.sharedMaterials;
                copy.sharedMesh = smr.sharedMesh;
            }
            foreach (Transform child in source) BuildVisual(child, t, map);
            return go;
        }

        public void SetDefaults(RimeEmoProject project)
        {
            facePath = project ? RimeEmoFace.Path(project) : null;
            Invalidate();
            if (clone == null || project == null || project.shapes == null) return;
            foreach (var shape in project.shapes)
            {
                var target = string.IsNullOrEmpty(shape.path) ? clone.transform : clone.transform.Find(shape.path);
                var renderer = target != null ? target.GetComponent<SkinnedMeshRenderer>() : null;
                if (renderer == null || renderer.sharedMesh == null) continue;
                var index = renderer.sharedMesh.GetBlendShapeIndex(shape.shape);
                if (index >= 0) renderer.SetBlendShapeWeight(index, shape.defaultValue);
            }
            foreach (var state in blendShapes)
            {
                if (state.renderer == null || state.renderer.sharedMesh == null) continue;
                var path = AnimationUtility.CalculateTransformPath(state.renderer.transform, clone.transform);
                foreach (var shape in project.shapes)
                    if (shape.path == path)
                    {
                        var index = state.renderer.sharedMesh.GetBlendShapeIndex(shape.shape);
                        if (index >= 0)
                        {
                            state.renderer.SetBlendShapeWeight(index, shape.defaultValue);
                            state.weights[index] = shape.defaultValue;
                        }
                    }
            }
        }

        public void Invalidate() { invalidated = true; clipCache.Clear(); sampledMotion = null; }
        public void InvalidateMotion() { invalidated = true; sampledMotion = null; }

        public void InvalidateCurve(AnimationClip clip, EditorCurveBinding binding)
        {
            invalidated = true;
            sampledMotion = null;
            if (!clip || !clipCache.TryGetValue(clip, out var bindings)) return;
            if (binding.type == null) { clipCache.Remove(clip); return; }
            foreach (var sample in bindings)
            {
                if (!sample.binding.Equals(binding)) continue;
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null) clipCache.Remove(clip);
                else sample.curve = curve;
                return;
            }
            // A newly added property needs a fresh binding map on the next repaint.
            if (RimeEmoClips.IsShape(binding)) clipCache.Remove(clip);
        }

        public void Draw(Rect rect, AnimationClip clip, float time, bool allowRefresh = true, RimeEmoMotion motion = null)
        {
            if (disposed) return;
            HandleInput(rect);
            if (Event.current != null && Event.current.type != EventType.Repaint) return;
            // Timeline input can repaint cheaply without rendering the avatar again.
            // The owner schedules the next sample and forces a final sample on release.
            if (!allowRefresh && cachedImage != null)
            {
                GUI.DrawTexture(rect, cachedImage, ScaleMode.StretchToFill, false);
                return;
            }
            if (!invalidated && cachedImage != null && cachedClip == clip && cachedMotion == motion && cachedTime == time && cachedRect == rect && cachedOrbit == orbit && Mathf.Approximately(cachedDistance, distance))
            {
                GUI.DrawTexture(rect, cachedImage, ScaleMode.StretchToFill, false);
                return;
            }
            if (clone != null)
            {
                RestoreBaseline();
                preview.camera.aspect = rect.height > 0f ? rect.width / rect.height : 1f;
                if (motion != null)
                    foreach (var binding in GetMotionBindings(motion)) binding.renderer.SetBlendShapeWeight(binding.index, binding.curve.Evaluate(time));
                else if (clip != null)
                    foreach (var binding in GetBindings(clip)) binding.renderer.SetBlendShapeWeight(binding.index, binding.curve.Evaluate(time));
                if (motion == null && clip == liveClip && liveShape != null && liveShape.renderer)
                    liveShape.renderer.SetBlendShapeWeight(liveShape.index, liveValue);
                SetupCamera();
            }
            preview.BeginPreview(rect, GUIStyle.none);
            preview.Render(true);
            var image = preview.EndPreview();
            cachedImage = image;
            cachedMotion = motion;
            cachedClip = clip; cachedTime = time; cachedRect = rect; cachedOrbit = orbit; cachedDistance = distance; invalidated = false;
            if (image != null) GUI.DrawTexture(rect, image, ScaleMode.StretchToFill, false);
        }

        private List<SampleBinding> GetBindings(AnimationClip clip)
        {
            if (clipCache.TryGetValue(clip, out var result)) return result;
            result = new List<SampleBinding>();
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var sample = Resolve(binding, clip);
                if (sample != null) result.Add(sample);
            }
            clipCache[clip] = result;
            return result;
        }

        private SampleBinding Resolve(EditorCurveBinding binding, AnimationClip clip)
        {
            if (!RimeEmoClips.IsShape(binding) || (facePath != null && binding.path != facePath)) return null;
            var path = binding.path ?? string.Empty;
            if (!pathMap.TryGetValue(path, out var target)) return null;
            var renderer = target.GetComponent<SkinnedMeshRenderer>();
            if (renderer == null || renderer.sharedMesh == null) return null;
            var index = renderer.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length));
            if (index < 0) return null;
            return new SampleBinding { binding = binding, renderer = renderer, index = index, curve = clip ? AnimationUtility.GetEditorCurve(clip, binding) : null };
        }

        private List<SampleBinding> GetMotionBindings(RimeEmoMotion motion)
        {
            if (sampledMotion == motion) return motionBindings;
            motionBindings.Clear();
            RimeEmoCompiler.BuildCurves(motion, out var bindings, out var curves);
            for (int i = 0; i < bindings.Length; i++)
            {
                var binding = bindings[i];
                if (!pathMap.TryGetValue(binding.path ?? "", out var target)) continue;
                var renderer = target.GetComponent<SkinnedMeshRenderer>();
                if (!renderer || !renderer.sharedMesh) continue;
                int index = renderer.sharedMesh.GetBlendShapeIndex(binding.propertyName.Substring("blendShape.".Length));
                if (index >= 0) motionBindings.Add(new SampleBinding { renderer = renderer, index = index, curve = curves[i] });
            }
            sampledMotion = motion;
            return motionBindings;
        }

        private void HandleInput(Rect rect)
        {
            var e = Event.current;
            if (e == null || !rect.Contains(e.mousePosition)) return;
            if (e.type == EventType.ScrollWheel)
            {
                distance = Mathf.Clamp(distance * Mathf.Exp(e.delta.y * 0.08f), 0.05f, 1000f);
                GUI.changed = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && (e.button == 0 || e.button == 1))
            {
                orbit.x += e.delta.x * 0.5f;
                orbit.y = Mathf.Clamp(orbit.y - e.delta.y * 0.5f, -85f, 85f);
                GUI.changed = true;
                e.Use();
            }
        }

        private void SetupCamera()
        {
            var camera = preview.camera;
            var rotation = Quaternion.Euler(orbit.y, orbit.x, 0f);
            camera.orthographic = true;
            // Preserve the previous framing, but zoom by view size rather than
            // moving through the face. Camera clearance is independent of zoom.
            camera.orthographicSize = Mathf.Max(0.01f, distance * Mathf.Tan(15f * Mathf.Deg2Rad));
            float clearance = Mathf.Max(0.5f, Vector3.Distance(pivot, bounds.center) + bounds.extents.magnitude + 0.5f);
            camera.farClipPlane = Mathf.Max(1000f, clearance * 3);
            camera.transform.position = pivot + rotation * (Vector3.back * clearance);
            camera.transform.rotation = rotation;
            camera.transform.LookAt(pivot);
        }

        private void RestoreBaseline()
        {
            foreach (var state in blendShapes)
            {
                if (state.renderer == null) continue;
                if (facePath != null && AnimationUtility.CalculateTransformPath(state.renderer.transform, clone.transform) != facePath) continue;
                for (var i = 0; i < state.weights.Length; i++) state.renderer.SetBlendShapeWeight(i, state.weights[i]);
            }
        }

        private void ClearAvatar()
        {
            ClearLiveShape();
            if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
            clone = null;
            transforms.Clear();
            blendShapes.Clear();
            pathMap.Clear(); clipCache.Clear(); cachedImage = null; invalidated = true;
            sampledMotion = cachedMotion = null; motionBindings.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearAvatar();
            preview.Cleanup();
        }
    }
}
