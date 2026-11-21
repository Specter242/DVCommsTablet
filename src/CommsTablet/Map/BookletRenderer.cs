using System;
using System.Collections.Generic;
using System.Linq;
using CommsTablet.Core.Booklet;
using DV.RenderTextureSystem.BookletRender;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace CommsTablet.Map
{
 internal static class BookletRenderer
    {
        /// <summary>
        /// The map's background, on every page and past its edges: a clean,
        /// warm light gray between the tablet's brass and the paper it
        /// replaces, light enough that the pages' black track lines and
        /// labels stay crisp.
        /// </summary>
        public static readonly Color Paper = new Color(0.925f, 0.91f, 0.875f);

        /// <summary>The pages' own paper: the booklet's scuffed sheets, its cover's, and the route map's stained, gridded one.</summary>
        private static bool IsPaper(string sprite) =>
            sprite != null && (sprite.StartsWith("Bk_Bg", StringComparison.Ordinal) || sprite.StartsWith("RouteMap_Background", StringComparison.Ordinal)
                               || sprite == "MapSchematicCoverBg");

#if !PUBLIC_RELEASE
        /// <summary>The paper the copies had, as replaced (the sprites' names and how many), for the self-test's log.</summary>
        public static readonly Dictionary<string, int> PaperReplaced = new Dictionary<string, int>();
#endif

        // A page's parts the tablet shows its own way: the header, the tabs, the route map's title.
        private static readonly HashSet<string> Hidden = new HashSet<string>(StringComparer.Ordinal)
        {
            "StationBg", "Text-StationName", "Text-StationID", "YardIDBg", "Text-YardID", "Text-YardType", "Navigator", "Arrow",
            "Title", "Info", "LogoDV",
        };

        private static Camera camera;
        private static Material lineMaterial;
        private static GameObject preparedFor;

        /// <summary>
        /// Draws a page's view: (<paramref name="centerX"/>, <paramref name="centerY"/>) is
        /// the schematic point (y up) at the texture's middle, <paramref name="scale"/>
        /// its pixels a schematic unit. <paramref name="overlay"/> then draws over it,
        /// given where a schematic point falls in the texture's pixels (y down).
        /// </summary>
        public static bool Render(BookletPage page, double centerX, double centerY, double scale, RenderTexture target, Action<Func<double, double, Vector2>> overlay = null)
        {
            var root = Show(page);
            if (root == null || target == null)
                return false;
            EnsureCamera();
            var k = Mathf.Abs(root.lossyScale.x);
            var at = root.TransformPoint(new Vector3((float)centerX, (float)centerY, 0));
            camera.transform.SetPositionAndRotation(at - root.forward * 100 * k, Quaternion.LookRotation(root.forward, root.up));
            camera.orthographicSize = (float)(target.height / scale / 2) * Mathf.Abs(root.lossyScale.y);
            camera.nearClipPlane = 1 * k;
            camera.farClipPlane = 300 * k;
            camera.aspect = target.width / (float)target.height;
            camera.cullingMask = 1 << shown.gameObject.layer;
            var previousTarget = RenderTexture.active;
            camera.targetTexture = target;
            try
            {
                camera.Render();
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previousTarget;
            }
            if (overlay != null)
            {
                EnsureMaterial();
                var previous = RenderTexture.active;
                var pushed = false;
                try
                {
                    RenderTexture.active = target;
                    GL.PushMatrix();
                    pushed = true;
                    GL.LoadPixelMatrix(0, target.width, target.height, 0);
                    lineMaterial.SetPass(0);
                    double w = target.width, h = target.height;
                    overlay((x, y) => new Vector2((float)((x - centerX) * scale + w / 2), (float)((centerY - y) * scale + h / 2)));
                }
                finally
                {
                    if (pushed)
                        GL.PopMatrix();
                    RenderTexture.active = previous;
                }
            }
            return true;
        }

        /// <summary>
        /// Makes a page the one the camera sees: the copy active, far below
        /// the world, with only that page on. It stays so while the page is
        /// shown, so its canvas is built once, not at each drawing; returns
        /// the page's schematic, or null if it can't be shown.
        /// </summary>
        private static RectTransform Show(BookletPage page)
        {
            var holder = BookletReader.Holder;
            var paper = page != null ? FindPage(page) : null;
            if (holder == null || paper == null)
                return null;
            if (paper != shown || !holder.activeSelf)
            {
                Prepare(holder);
                holder.SetActive(true);
                foreach (var b in new[] { BookletReader.BookletCopy, BookletReader.RouteCopy })
                {
                    if (b == null)
                        continue;
                    b.gameObject.SetActive(b.staticPages.Contains(paper));
                    foreach (var p in b.staticPages)
                        p.gameObject.SetActive(p == paper);
                }
                shown = paper;
                shownRoot = paper.GetComponentsInChildren<RectTransform>(true).FirstOrDefault(t => t.name == (page.IsRoute ? "OuterWrapper" : "SMap") && ActiveUnder(t, paper.transform));
                Canvas.ForceUpdateCanvases();
            }
            return shownRoot;
        }

        /// <summary>Puts the copies away: nothing of them is active until a page is drawn again.</summary>
        public static void Hide()
        {
            var holder = BookletReader.Holder;
            if (holder != null)
                holder.SetActive(false);
            shown = null;
            shownRoot = null;
        }

        private static StaticPageTemplatePaper shown;
        private static RectTransform shownRoot;

        private static StaticPageTemplatePaper FindPage(BookletPage page)
        {
            if (page.IsRoute)
                return BookletReader.RouteCopy != null && BookletReader.RouteCopy.staticPages.Count > 0 ? BookletReader.RouteCopy.staticPages[0] : null;
            return BookletReader.BookletCopy?.staticPages.FirstOrDefault(p => p != null && p.gameObject.name == page.Source.Name);
        }

        private static bool ActiveUnder(Transform t, Transform top)
        {
            for (var x = t; x != null && x != top; x = x.parent)
                if (!x.gameObject.activeSelf)
                    return false;
            return true;
        }

        /// <summary>
        /// Once per copy: the parts the tablet shows its own way hidden, and
        /// the pages' paper made flat in <see cref="Paper"/>: the sprite let
        /// go, the image kept (a mask on it still masks), its color the
        /// background's.
        /// </summary>
        private static void Prepare(GameObject holder)
        {
            if (preparedFor == holder)
                return;
            preparedFor = holder;
            foreach (var t in holder.GetComponentsInChildren<Transform>(true))
                if (Hidden.Contains(t.name))
                    t.gameObject.SetActive(false);
#if !PUBLIC_RELEASE
            PaperReplaced.Clear();
#endif
            foreach (var image in holder.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            {
                var name = image.sprite != null ? image.sprite.name : null;
                if (!IsPaper(name))
                    continue;
                image.sprite = null;
                image.overrideSprite = null;
                image.type = UnityEngine.UI.Image.Type.Simple;
                image.color = Paper;
#if !PUBLIC_RELEASE
                PaperReplaced[name] = PaperReplaced.TryGetValue(name, out var n) ? n + 1 : 1;
#endif
            }
            foreach (var raw in holder.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
            {
                var name = raw.texture != null ? raw.texture.name : null;
                if (!IsPaper(name))
                    continue;
                raw.texture = null;
                raw.color = Paper;
#if !PUBLIC_RELEASE
                PaperReplaced[name] = PaperReplaced.TryGetValue(name, out var n) ? n + 1 : 1;
#endif
            }
        }

        private static void EnsureCamera()
        {
            if (camera != null)
                return;
            var go = new GameObject("[CommsTablet booklet camera]");
            Object.DontDestroyOnLoad(go);
            camera = go.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Paper;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.useOcclusionCulling = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
        }

        private static void EnsureMaterial()
        {
            if (lineMaterial != null)
                return;
            lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            lineMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);
            lineMaterial.SetInt("_ZTest", (int)CompareFunction.Always);
        }

        /// <summary>A line <paramref name="width"/> pixels wide, in the overlay.</summary>
        public static void Line(Vector2 a, Vector2 b, float width, Color color)
        {
            var d = b - a;
            if (d.sqrMagnitude < 1e-6f)
                return;
            var n = new Vector2(-d.y, d.x).normalized * (width / 2);
            GL.Begin(GL.QUADS);
            GL.Color(color);
            GL.Vertex3(a.x + n.x, a.y + n.y, 0);
            GL.Vertex3(b.x + n.x, b.y + n.y, 0);
            GL.Vertex3(b.x - n.x, b.y - n.y, 0);
            GL.Vertex3(a.x - n.x, a.y - n.y, 0);
            GL.End();
        }

        /// <summary>A filled circle, in the overlay.</summary>
        public static void Dot(Vector2 at, float radius, Color color)
        {
            GL.Begin(GL.TRIANGLES);
            GL.Color(color);
            const int sides = 20;
            for (var i = 0; i < sides; i++)
            {
                var a0 = i * Mathf.PI * 2 / sides;
                var a1 = (i + 1) * Mathf.PI * 2 / sides;
                GL.Vertex3(at.x, at.y, 0);
                GL.Vertex3(at.x + Mathf.Cos(a0) * radius, at.y + Mathf.Sin(a0) * radius, 0);
                GL.Vertex3(at.x + Mathf.Cos(a1) * radius, at.y + Mathf.Sin(a1) * radius, 0);
            }
            GL.End();
        }

        /// <summary>A run of a page drawn over, along its line: a chosen track, a route.</summary>
        public static void Run(Func<double, double, Vector2> toPixels, SchematicRun run, float width, Color color)
        {
            for (var i = 1; i < run.Points.Length; i++)
                Line(toPixels(run.Points[i - 1].X, run.Points[i - 1].Z), toPixels(run.Points[i].X, run.Points[i].Z), width, color);
        }
    }
}
