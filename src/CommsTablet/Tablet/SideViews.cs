using System;
using System.Collections.Generic;
using System.Linq;
using DV.Customization.Paint;
using DV.ThingTypes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CommsTablet.Tablet
{
 internal static class SideViews
    {
        // Twice the tile picture's 148 × 48 canvas units, as the screen is drawn.
        internal const int Width = 444, Height = 144;
        private const int Layer = 30;
        private static readonly Vector3 Stage = new Vector3(0f, -2000f, 0f);

        private static readonly Dictionary<(TrainCarLivery, PaintTheme), Sprite> views = new Dictionary<(TrainCarLivery, PaintTheme), Sprite>();
        private static readonly HashSet<(TrainCarLivery, PaintTheme)> failed = new HashSet<(TrainCarLivery, PaintTheme)>();
        private static readonly List<(TrainCarLivery livery, PaintTheme theme, TrainCar car)> wanted = new List<(TrainCarLivery, PaintTheme, TrainCar)>();
        private static GameObject host;
        private static Camera camera;
        private static Light light;
        private static int frame = -1;

        /// <summary>Raised when a view finishes, so a screen can redraw.</summary>
        internal static event Action Drawn;

        /// <summary>The side view of <paramref name="livery"/>, in <paramref name="theme"/> if given, if it is drawn; else null.</summary>
        internal static Sprite Get(TrainCarLivery livery, PaintTheme theme = null) =>
            livery != null && views.TryGetValue((livery, theme), out var view) && view != null ? view : null;

        /// <summary>
        /// Makes these the views to draw next, in order: the tiles on screen.
        /// Views asked for by a page no longer shown are dropped. A themed
        /// view names the car it is for: Skin Manager paints by the car's data.
        /// </summary>
        internal static void Request(IEnumerable<(TrainCarLivery livery, PaintTheme theme, TrainCar car)> views)
        {
            wanted.Clear();
            foreach (var view in views)
                if (view.livery != null && !failed.Contains((view.livery, view.theme)) && Get(view.livery, view.theme) == null
                    && !wanted.Any(w => w.livery == view.livery && w.theme == view.theme))
                    wanted.Add(view);
        }

        /// <summary>Draws at most one waiting view this frame; called by each visible screen.</summary>
        internal static void Pump()
        {
            if (wanted.Count == 0 || frame == Time.frameCount)
                return;
            frame = Time.frameCount;
            var (livery, theme, car) = wanted[0];
            wanted.RemoveAt(0);
            // A paint's view of a car deleted meanwhile: dropped, not failed, so a later car of that livery gets it.
            if (theme != null && car == null)
                return;
            Sprite view = null;
            try
            {
                view = Draw(livery, theme, car);
            }
            catch (Exception e)
            {
                Main.Mod?.Logger.Warning($"No side view for {livery?.id}{(theme != null ? " in " + theme.name : "")}: {e.GetBaseException().Message}");
            }
            if (view == null)
            {
                failed.Add((livery, theme));
                return;
            }
            views[(livery, theme)] = view;
            Drawn?.Invoke();
        }

        private static Sprite Draw(TrainCarLivery livery, PaintTheme theme, TrainCar car)
        {
            var prefab = livery.prefab;
            if (prefab == null)
                return null;
            Setup();
            var stage = new GameObject("[CommsTablet side view stage]");
            stage.transform.position = Stage;
            RenderTexture target = null, resolved = null;
            var previous = RenderTexture.active;
            var made = new List<Material>();
            try
            {
                var bounds = Copy(prefab.transform, stage.transform);
                if (!bounds.HasValue)
                    return null;
                if (theme != null && !Paint(stage, theme, car, made))
                    return null;
                var b = Frame(prefab, bounds.Value);
                // Looking at the vehicle's left side, its front to the left, as in the game's icons.
                var aspect = (float)Width / Height;
                camera.aspect = aspect;
                camera.orthographicSize = Mathf.Max(b.extents.y, b.extents.z / aspect) * 1.06f;
                var distance = b.extents.x + 10f;
                camera.transform.SetPositionAndRotation(b.center + Vector3.left * distance, Quaternion.LookRotation(Vector3.right, Vector3.up));
                camera.nearClipPlane = 0.1f;
                camera.farClipPlane = distance + b.extents.x + 1f;
                // From the camera's side, above and a little ahead.
                light.transform.rotation = Quaternion.LookRotation(new Vector3(0.6f, -0.5f, -0.35f), Vector3.up);

                target = RenderTexture.GetTemporary(Width, Height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
                camera.targetTexture = target;
                host.SetActive(true);
                camera.Render();
                // Resolved into a plain texture before reading: reading an
                // antialiased one directly is backend-dependent.
                resolved = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
                Graphics.Blit(target, resolved);
                RenderTexture.active = resolved;
                var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false)
                    { name = "CommsTablet_SideView_" + livery.id + (theme != null ? "_" + theme.name : ""), wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                texture.Apply(false, true);
                var sprite = Sprite.Create(texture, new Rect(0, 0, Width, Height), new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                sprite.hideFlags = HideFlags.HideAndDontSave;
                return sprite;
            }
            finally
            {
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;
                if (host != null) host.SetActive(false);
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (resolved != null) RenderTexture.ReleaseTemporary(resolved);
                Object.Destroy(stage);
                foreach (var material in made)
                    if (material != null) Object.Destroy(material);
            }
        }

        /// <summary>
        /// Paints the preview: a Skin Manager skin through its own texture code;
        /// a stock theme by its material substitutions, as <c>TrainCarPaint</c>
        /// applies them.
        /// </summary>
        private static bool Paint(GameObject stage, PaintTheme theme, TrainCar car, List<Material> made)
        {
            if (SkinManagerLink.IsSkin(theme))
                return SkinManagerLink.PaintCopy(stage, theme, car, made);
            foreach (var renderer in stage.GetComponentsInChildren<MeshRenderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && theme.TryGetSubstitute(materials[i], out var substitution) && substitution.substitute != null)
                    {
                        materials[i] = substitution.substitute;
                        changed = true;
                    }
                if (changed)
                    renderer.sharedMaterials = materials;
            }
            return true;
        }

        /// <summary>
        /// Copies the prefab's visible mesh renderers under <paramref name="stage"/>,
        /// as meshes and materials only, and returns their combined bounds.
        /// </summary>
        private static Bounds? Copy(Transform prefab, Transform stage)
        {
            // Only the nearest level of detail: farther levels would draw over it.
            var farther = new HashSet<Renderer>(prefab.GetComponentsInChildren<LODGroup>(true)
                .SelectMany(g => g.GetLODs().Skip(1)).SelectMany(l => l.renderers).Where(r => r != null));
            Bounds? bounds = null;
            foreach (var source in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!source.enabled || farther.Contains(source) || !ActiveInPrefab(source.transform, prefab))
                    continue;
                var mesh = source.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    continue;
                var copy = new GameObject(source.name) { layer = Layer };
                copy.transform.SetParent(stage, false);
                copy.transform.localPosition = prefab.InverseTransformPoint(source.transform.position);
                copy.transform.localRotation = Quaternion.Inverse(prefab.rotation) * source.transform.rotation;
                copy.transform.localScale = Divide(source.transform.lossyScale, prefab.lossyScale);
                copy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = copy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = source.sharedMaterials;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (bounds.HasValue)
                {
                    var b = bounds.Value;
                    b.Encapsulate(renderer.bounds);
                    bounds = b;
                }
                else
                    bounds = renderer.bounds;
            }
            return bounds;
        }

        /// <summary>
        /// What the camera frames: the car's own box (its collision bounds and
        /// couplers, as the spawner measures it) along its length, and the
        /// meshes' height within a little of that box, so roof fittings show but
        /// a stray part far off the body cannot shrink the view.
        /// </summary>
        private static Bounds Frame(GameObject prefab, Bounds meshes)
        {
            var car = prefab.GetComponent<TrainCar>();
            if (car == null)
                return meshes;
            var box = car.Bounds;
            if (box.size.z <= 0f)
                return meshes;
            var center = Stage + box.center;
            var bottom = Mathf.Max(meshes.min.y, center.y - box.extents.y - 1.5f);
            var top = Mathf.Min(meshes.max.y, center.y + box.extents.y + 1.5f);
            if (top <= bottom)
            {
                bottom = center.y - box.extents.y;
                top = center.y + box.extents.y;
            }
            return new Bounds(new Vector3(center.x, (bottom + top) * 0.5f, center.z),
                new Vector3(box.size.x, top - bottom, box.size.z));
        }

        private static bool ActiveInPrefab(Transform t, Transform root)
        {
            for (; t != null; t = t == root ? null : t.parent)
                if (!t.gameObject.activeSelf)
                    return false;
            return true;
        }

        private static Vector3 Divide(Vector3 a, Vector3 b) =>
            new Vector3(b.x != 0f ? a.x / b.x : a.x, b.y != 0f ? a.y / b.y : a.y, b.z != 0f ? a.z / b.z : a.z);

        private static void Setup()
        {
            if (host != null)
                return;
            host = new GameObject("[CommsTablet side view camera]");
            host.SetActive(false);
            Object.DontDestroyOnLoad(host);
            camera = host.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            // The tile plate's color, so the view sits on it without an edge.
            camera.backgroundColor = TabletArt.Enamel;
            camera.cullingMask = 1 << Layer;
            camera.renderingPath = RenderingPath.Forward;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            var lamp = new GameObject("[CommsTablet side view light]");
            lamp.transform.SetParent(host.transform, false);
            light = lamp.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = Color.white;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << Layer;
        }
    }
}
