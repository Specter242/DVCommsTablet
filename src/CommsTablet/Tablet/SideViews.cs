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

        /// <summary>How far past the car's box (at least) a part still counts as the car's: 3 m, or a quarter of its length.</summary>
        private const float Reach = 3f;

        /// <summary>Most side views kept at once; oldest unused entries are evicted and destroyed.</summary>
        private const int MaxCached = 80;
        private const float InitialFailBackoff = 5f, MaxFailBackoff = 120f;

        private struct Entry
        {
            internal (TrainCarLivery Livery, PaintTheme Theme) Key;
            internal Sprite Sprite;
        }

        private static readonly Dictionary<(TrainCarLivery, PaintTheme), LinkedListNode<Entry>> views =
            new Dictionary<(TrainCarLivery, PaintTheme), LinkedListNode<Entry>>();
        private static readonly LinkedList<Entry> lru = new LinkedList<Entry>();
        private static readonly Dictionary<(TrainCarLivery, PaintTheme), (float retryAt, int attempts)> failed =
            new Dictionary<(TrainCarLivery, PaintTheme), (float, int)>();
        private static readonly Dictionary<TabletScreen, HashSet<(TrainCarLivery, PaintTheme)>> pinnedByScreen =
            new Dictionary<TabletScreen, HashSet<(TrainCarLivery, PaintTheme)>>();
        private static readonly List<TabletScreen> deadScreens = new List<TabletScreen>();
        private static readonly List<(TrainCarLivery livery, PaintTheme theme, TrainCar car)> wanted = new List<(TrainCarLivery, PaintTheme, TrainCar)>();
        private static bool sceneHooked;
        private static GameObject host;
        private static Camera camera;
        private static Light light;
        private static int frame = -1;

#if !PUBLIC_RELEASE
        /// <summary>For the self-test: each livery's car box and the frame drawn (stage coordinates), its parts copied, and how many were its exterior interactables'.</summary>
        internal static readonly Dictionary<string, (Bounds Box, Bounds Frame, int Parts, int Interactables, Rect Seen)> FramedForTest =
            new Dictionary<string, (Bounds, Bounds, int, int, Rect)>();
#endif

        /// <summary>Raised when a view finishes, so a screen can redraw.</summary>
        internal static event Action Drawn;

        /// <summary>The side view of <paramref name="livery"/>, in <paramref name="theme"/> if given, if it is drawn; else null.</summary>
        internal static Sprite Get(TrainCarLivery livery, PaintTheme theme = null)
        {
            if (livery == null)
                return null;
            var key = (livery, theme);
            if (!views.TryGetValue(key, out var node))
                return null;
            var sprite = node.Value.Sprite;
            if (sprite == null)
            {
                lru.Remove(node);
                views.Remove(key);
                return null;
            }
            if (node != lru.First)
            {
                lru.Remove(node);
                lru.AddFirst(node);
            }
            return sprite;
        }

        private static void EnsureSceneHook()
        {
            if (sceneHooked)
                return;
            sceneHooked = true;
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += (_, __) => failed.Clear();
        }

        private static bool CanRetry((TrainCarLivery, PaintTheme) key) =>
            !failed.TryGetValue(key, out var info) || Time.unscaledTime >= info.retryAt;

        private static bool IsWanted((TrainCarLivery livery, PaintTheme theme) key)
        {
            for (var i = 0; i < wanted.Count; i++)
                if (wanted[i].livery == key.livery && wanted[i].theme == key.theme)
                    return true;
            return false;
        }

        /// <summary>Records the side-view keys currently bound to a screen's tiles so LRU eviction never destroys a live sprite.</summary>
        internal static void Pin(TabletScreen screen, IList<(TrainCarLivery livery, PaintTheme theme, TrainCar car)> shown)
        {
            if (screen == null)
                return;
            if (shown == null || shown.Count == 0)
            {
                pinnedByScreen.Remove(screen);
                return;
            }
            if (!pinnedByScreen.TryGetValue(screen, out var set))
                pinnedByScreen[screen] = set = new HashSet<(TrainCarLivery, PaintTheme)>();
            else
                set.Clear();
            for (var i = 0; i < shown.Count; i++)
                if (shown[i].livery != null)
                    set.Add((shown[i].livery, shown[i].theme));
        }

        internal static void Unpin(TabletScreen screen)
        {
            if (screen != null)
                pinnedByScreen.Remove(screen);
        }

        private static bool IsPinned((TrainCarLivery, PaintTheme) key)
        {
            deadScreens.Clear();
            var pinned = false;
            foreach (var kv in pinnedByScreen)
            {
                if (kv.Key == null)
                    deadScreens.Add(kv.Key);
                else if (kv.Value.Contains(key))
                    pinned = true;
            }
            for (var i = 0; i < deadScreens.Count; i++)
                pinnedByScreen.Remove(deadScreens[i]);
            return pinned;
        }

        private static void DestroySprite(Sprite sprite)
        {
            if (sprite == null)
                return;
            var tex = sprite.texture;
            Object.Destroy(sprite);
            if (tex != null)
                Object.Destroy(tex);
        }

        private static void Store((TrainCarLivery livery, PaintTheme theme) key, Sprite view)
        {
            if (views.TryGetValue(key, out var existing))
            {
                var old = existing.Value.Sprite;
                existing.Value = new Entry { Key = key, Sprite = view };
                if (existing != lru.First)
                {
                    lru.Remove(existing);
                    lru.AddFirst(existing);
                }
                if (old != view)
                    DestroySprite(old);
            }
            else
            {
                var node = lru.AddFirst(new Entry { Key = key, Sprite = view });
                views[key] = node;
            }
            while (views.Count > MaxCached)
            {
                var candidate = lru.Last;
                while (candidate != null && IsPinned(candidate.Value.Key))
                    candidate = candidate.Previous;
                if (candidate == null)
                    break;
                lru.Remove(candidate);
                views.Remove(candidate.Value.Key);
                DestroySprite(candidate.Value.Sprite);
            }
        }

        /// <summary>
        /// Makes these the views to draw next, in order: the tiles on screen.
        /// Views asked for by a page no longer shown are dropped. A themed
        /// view names the car it is for: Skin Manager paints by the car's data.
        /// </summary>
        internal static void Request(IEnumerable<(TrainCarLivery livery, PaintTheme theme, TrainCar car)> views)
        {
            EnsureSceneHook();
            wanted.Clear();
            foreach (var view in views)
            {
                if (view.livery == null)
                    continue;
                var key = (view.livery, view.theme);
                if (!CanRetry(key) || Get(view.livery, view.theme) != null || IsWanted(key))
                    continue;
                wanted.Add(view);
            }
        }

        /// <summary>Draws at most one waiting view this frame; called by each visible screen.</summary>
        internal static void Pump()
        {
            if (wanted.Count == 0 || frame == Time.frameCount)
                return;
            EnsureSceneHook();
            frame = Time.frameCount;
            var (livery, theme, car) = wanted[0];
            wanted.RemoveAt(0);
            // A paint's view of a car deleted meanwhile: dropped, not failed, so a later car of that livery gets it.
            if (theme != null && car == null)
                return;
            var key = (livery, theme);
            Sprite view = null;
            var threw = false;
            try
            {
                view = Draw(livery, theme, car);
            }
            catch (Exception e)
            {
                threw = true;
                if (!failed.ContainsKey(key))
                    Main.Mod?.Logger.Warning($"No side view for {livery?.id}{(theme != null ? " in " + theme.name : "")}: {e.GetBaseException().Message}");
            }
            if (view == null)
            {
                if (!threw)
                {
                    // Clean null (e.g. missing prefab or no renderable parts): don't re-render on a timer until scene load.
                    failed[key] = (float.PositiveInfinity, 1);
                    return;
                }
                failed.TryGetValue(key, out var prev);
                var attempts = prev.attempts + 1;
                var delay = Mathf.Min(MaxFailBackoff, InitialFailBackoff * (1 << Mathf.Min(attempts - 1, 5)));
                failed[key] = (Time.unscaledTime + delay, attempts);
                return;
            }
            failed.Remove(key);
            Store(key, view);
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
                var parts = new List<Bounds>();
                Copy(prefab.transform, stage.transform, parts);
                // The doors and handles: the game loads them under the car's [interior], which it
                // places at the car's own position and rotation (TrainCar.SetupInterior).
                var interactables = 0;
                var outside = livery.externalInteractablesPrefab;
                if (outside != null)
                {
                    var holder = new GameObject("[exterior interactables]");
                    holder.transform.SetParent(stage.transform, false);
                    holder.transform.localPosition = outside.transform.localPosition;
                    holder.transform.localRotation = outside.transform.localRotation;
                    holder.transform.localScale = outside.transform.localScale;
                    interactables = Copy(outside.transform, holder.transform, parts);
                }
                if (parts.Count == 0)
                    return null;
                if (theme != null && !Paint(stage, theme, car, made))
                    return null;
                var b = Frame(prefab, parts, out var box);
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
                host.SetActive(true);
                // The meshes' bounds take in what no picture shows (lamp beams, glass, helpers far
                // above or below the body): framed again on the pixels the first look drew, then
                // once more at about the picture's own scale, where thin edges (a wheel's rim, a
                // step) the first, coarser look under-measured show (0.1.1's second test run).
                var seen = Refit(aspect);
                Refit(aspect);
#if !PUBLIC_RELEASE
                FramedForTest[livery.id] = (box, b, parts.Count, interactables, seen);
#endif

                target = RenderTexture.GetTemporary(Width, Height, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 4);
                camera.targetTexture = target;
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
        /// Copies a prefab's visible mesh renderers under <paramref name="stage"/>,
        /// as meshes and materials only, adds each copy's bounds to
        /// <paramref name="parts"/>, and returns how many it copied.
        /// </summary>
        private static int Copy(Transform prefab, Transform stage, List<Bounds> parts)
        {
            // Only the nearest level of detail: farther levels would draw over it.
            var farther = new HashSet<Renderer>(prefab.GetComponentsInChildren<LODGroup>(true)
                .SelectMany(g => g.GetLODs().Skip(1)).SelectMany(l => l.renderers).Where(r => r != null));
            var copied = 0;
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
                parts.Add(renderer.bounds);
                copied++;
            }
            return copied;
        }

        /// <summary>
        /// What the camera frames first: the model itself, so each vehicle fills its
        /// tile. The parts that are the car's are those reaching into its box (its
        /// collision bounds and couplers, as the spawner measures it) widened by
        /// <see cref="Reach"/> or a quarter of its length, so a stray part far off
        /// cannot shrink the view; the frame is those parts' box, cut to that
        /// widened box. The car's box alone is not the frame: a Custom Car Loader
        /// engine's box can be much longer than its body (a small engine drawn
        /// small) or shorter (one drawn too big and cut off; 0.1.0).
        /// </summary>
        private static Bounds Frame(GameObject prefab, List<Bounds> parts, out Bounds box)
        {
            var all = Union(parts);
            box = all;
            var car = prefab.GetComponent<TrainCar>();
            if (car == null || car.Bounds.size.z <= 0f)
                return all;
            box = new Bounds(Stage + car.Bounds.center, car.Bounds.size);
            var near = box;
            near.Expand(2f * Mathf.Max(Reach, box.size.z * 0.25f));
            var picked = parts.Where(p => p.Intersects(near)).ToList();
            if (picked.Count == 0)
                return box;
            var model = Union(picked);
            var min = Vector3.Max(model.min, near.min);
            var max = Vector3.Min(model.max, near.max);
            if (max.x <= min.x || max.y <= min.y || max.z <= min.z)
                return box;
            var frame = new Bounds();
            frame.SetMinMax(min, max);
            return frame;
        }

        // A color no vehicle is painted in: what the first look sees is what is not it.
        private static readonly Color Key = new Color(1f, 0f, 1f, 1f);

        /// <summary>
        /// Draws the camera's view once against <see cref="Key"/>, at twice the
        /// picture's size, and frames the camera again on the drawn pixels, with
        /// the same margin. Returns where they were, as a part of the first view
        /// (0 to 1, from the bottom left); the whole view if none were drawn.
        /// </summary>
        private static Rect Refit(float aspect)
        {
            var background = camera.backgroundColor;
            var look = RenderTexture.GetTemporary(Width * 2, Height * 2, 16, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default, 1);
            var probe = new Texture2D(look.width, look.height, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            try
            {
                camera.backgroundColor = Key;
                camera.targetTexture = look;
                camera.Render();
                RenderTexture.active = look;
                probe.ReadPixels(new Rect(0, 0, look.width, look.height), 0, 0, false);
                int w = look.width, h = look.height;
                int x0 = w, y0 = h, x1 = -1, y1 = -1;
                if (probe.format == TextureFormat.RGBA32)
                {
                    var pixels = probe.GetRawTextureData<Color32>();
                    for (var y = 0; y < h; y++)
                    {
                        var row = y * w;
                        for (var x = 0; x < w; x++)
                        {
                            var c = pixels[row + x];
                            if (255 - c.r + c.g + 255 - c.b < 48)
                                continue;
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                    }
                }
                else
                {
                    var pixels = probe.GetPixels32();
                    for (var y = 0; y < h; y++)
                    {
                        var row = y * w;
                        for (var x = 0; x < w; x++)
                        {
                            var c = pixels[row + x];
                            if (255 - c.r + c.g + 255 - c.b < 48)
                                continue;
                            if (x < x0) x0 = x;
                            if (x > x1) x1 = x;
                            if (y < y0) y0 = y;
                            if (y > y1) y1 = y;
                        }
                    }
                }
                if (x1 < 0)
                    return new Rect(0, 0, 1, 1);
                var seen = Rect.MinMaxRect((float)x0 / w, (float)y0 / h, (x1 + 1f) / w, (y1 + 1f) / h);
                var halfHeight = camera.orthographicSize;
                var halfWidth = halfHeight * aspect;
                var t = camera.transform;
                t.position += t.right * ((seen.center.x - 0.5f) * 2f * halfWidth) + t.up * ((seen.center.y - 0.5f) * 2f * halfHeight);
                camera.orthographicSize = Mathf.Max(seen.height * halfHeight, seen.width * halfWidth / aspect) * 1.06f;
                return seen;
            }
            finally
            {
                camera.backgroundColor = background;
                camera.targetTexture = null;
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(look);
                Object.Destroy(probe);
            }
        }

        private static Bounds Union(List<Bounds> parts)
        {
            var all = parts[0];
            for (var i = 1; i < parts.Count; i++)
                all.Encapsulate(parts[i]);
            return all;
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
