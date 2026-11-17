using HarmonyLib;
using UnityEngine;

namespace CommsTablet.Tablet
{
    /// <summary>
    /// The shell and inventory art, made in code; no third-party meshes or
    /// textures. The chamfered shell is Realistic Firing's
    /// <c>FiringTabletBody</c> (d0f162e9), in AI Engineer's dark brown
    /// (decision 15), with a round home button under the screen.
    /// </summary>
    internal static class TabletBody
    {
        private static Material material;
        private static Mesh mesh;
        private static Sprite icon, droppedIcon;

        internal static Transform Build(Transform parent)
        {
            var body = new GameObject("[comms tablet shell]");
            body.transform.SetParent(parent, false);
            body.AddComponent<MeshFilter>().sharedMesh = Mesh;
            body.AddComponent<MeshRenderer>().sharedMaterial = Material;
            var home = new GameObject("[comms tablet home button]");
            home.transform.SetParent(body.transform, false);
            home.transform.localPosition = new Vector3(0f, TabletScreen.HomeButtonY, -TabletItem.Thickness * 0.5f - 0.0006f);
            home.AddComponent<MeshFilter>().sharedMesh = HomeMesh;
            home.AddComponent<MeshRenderer>().sharedMaterials = new[] { HomeMaterial(ref homeFace, new Color(0.06f, 0.055f, 0.05f), 0f, 0.7f),
                HomeMaterial(ref homeRing, new Color(0.79f, 0.64f, 0.29f), 0.8f, 0.55f) };
            return body.transform;
        }

        private static Mesh homeMesh;
        private static Material homeFace, homeRing;

        private static Material HomeMaterial(ref Material material, Color color, float metal, float gloss)
        {
            if (material != null)
                return material;
            material = new Material(Shader.Find("Standard")) { name = "CommsTablet_HomeButton", color = color };
            material.SetFloat("_Metallic", metal);
            material.SetFloat("_Glossiness", gloss);
            return material;
        }

        /// <summary>
        /// The round home button: a dark face (submesh 0) with a brass rim and
        /// a small brass ring at its middle (submesh 1), facing the viewer (-z).
        /// </summary>
        private static Mesh HomeMesh
        {
            get
            {
                if (homeMesh != null)
                    return homeMesh;
                const int segments = 40;
                var r = TabletScreen.HomeButtonRadius;
                var vertices = new System.Collections.Generic.List<Vector3> { Vector3.zero };
                var face = new System.Collections.Generic.List<int>();
                var rims = new System.Collections.Generic.List<int>();
                for (var i = 0; i < segments; i++)
                    vertices.Add(Point(i, r));
                for (var i = 0; i < segments; i++)
                    face.AddRange(new[] { 0, 1 + (i + 1) % segments, 1 + i });
                // Rings sit a hair in front of the face.
                void Ring(float inner, float outer)
                {
                    var start = vertices.Count;
                    for (var i = 0; i < segments; i++)
                    {
                        vertices.Add(Point(i, inner) + Vector3.back * 0.0002f);
                        vertices.Add(Point(i, outer) + Vector3.back * 0.0002f);
                    }
                    for (var i = 0; i < segments; i++)
                    {
                        int a = start + i * 2, b = a + 1, c = start + (i + 1) % segments * 2, d = c + 1;
                        rims.AddRange(new[] { a, d, b, a, c, d });
                    }
                }
                Ring(r * 0.86f, r);
                Ring(r * 0.3f, r * 0.38f);
                homeMesh = new Mesh { name = "CommsTablet_HomeButton", subMeshCount = 2 };
                homeMesh.SetVertices(vertices);
                homeMesh.SetTriangles(face, 0);
                homeMesh.SetTriangles(rims, 1);
                homeMesh.RecalculateNormals();
                homeMesh.RecalculateBounds();
                homeMesh.UploadMeshData(true);
                return homeMesh;

                Vector3 Point(int i, float radius)
                {
                    var angle = i * Mathf.PI * 2f / segments;
                    return new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
                }
            }
        }

        private static Material Material
        {
            get
            {
                if (material != null)
                    return material;
                material = new Material(Shader.Find("Standard")) { name = "CommsTablet_Shell", color = new Color(0.16f, 0.13f, 0.09f) };
                material.SetFloat("_Metallic", 0.45f);
                material.SetFloat("_Glossiness", 0.25f);
                return material;
            }
        }

        private static Mesh Mesh
        {
            get
            {
                if (mesh != null)
                    return mesh;
                const int corners = 8;
                var halfDepth = TabletItem.Thickness * 0.5f;
                const float bevelDepth = 0.002f;
                var vertices = new Vector3[corners * 4 + 2];
                var triangles = new System.Collections.Generic.List<int>();
                for (var ring = 0; ring < 4; ring++)
                {
                    var inset = ring == 0 || ring == 3 ? 0.003f : 0f;
                    var w = TabletItem.Size.x * 0.5f - inset;
                    var h = TabletItem.Size.y * 0.5f - inset;
                    var z = ring == 0 ? -halfDepth : ring == 1 ? -halfDepth + bevelDepth : ring == 2 ? halfDepth - bevelDepth : halfDepth;
                    const float cut = 0.012f;
                    var outline = new[] { new Vector2(-w + cut, -h), new Vector2(w - cut, -h), new Vector2(w, -h + cut), new Vector2(w, h - cut),
                        new Vector2(w - cut, h), new Vector2(-w + cut, h), new Vector2(-w, h - cut), new Vector2(-w, -h + cut) };
                    for (var i = 0; i < corners; i++)
                    {
                        vertices[ring * corners + i] = new Vector3(outline[i].x, outline[i].y, z);
                        if (ring == 0)
                            continue;
                        var a = (ring - 1) * corners + i;
                        var b = (ring - 1) * corners + (i + 1) % corners;
                        var c = ring * corners + i;
                        var d = ring * corners + (i + 1) % corners;
                        triangles.AddRange(new[] { a, b, c, b, d, c });
                    }
                }
                vertices[32] = new Vector3(0f, 0f, -halfDepth);
                vertices[33] = new Vector3(0f, 0f, halfDepth);
                for (var i = 0; i < corners; i++)
                    triangles.AddRange(new[] { 32, (i + 1) % corners, i, 33, 24 + i, 24 + (i + 1) % corners });
                mesh = new Mesh { name = "CommsTablet_Shell" };
                mesh.vertices = vertices;
                mesh.triangles = triangles.ToArray();
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                mesh.UploadMeshData(true);
                return mesh;
            }
        }

        internal static void SetIcons(InventoryItemSpec spec)
        {
            if (icon == null)
                icon = MakeIcon(false);
            if (droppedIcon == null)
                droppedIcon = MakeIcon(true);
            AccessTools.Field(typeof(InventoryItemSpec), "itemIconSprite").SetValue(spec, icon);
            AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteStandard").SetValue(spec, icon);
            AccessTools.Field(typeof(InventoryItemSpec), "itemIconSpriteDropped").SetValue(spec, droppedIcon);
        }

        // A dark-framed screen with a two-by-two grid of brass-rimmed icons, as on its home screen.
        private static Sprite MakeIcon(bool dropped)
        {
            const int size = 128;
            var pixels = new Color32[size * size];
            for (var y = 10; y < 118; y++)
                for (var x = 5; x < 123; x++)
                {
                    var edge = x < 11 || x > 116 || y < 16 || y > 111;
                    if (dropped)
                    {
                        if (edge) pixels[y * size + x] = new Color32(131, 209, 236, 160);
                    }
                    else
                        pixels[y * size + x] = edge ? new Color32(74, 60, 42, 255) : new Color32(21, 18, 15, 255);
                }
            if (!dropped)
            {
                var colors = new[] { new Color32(63, 122, 58, 255), new Color32(176, 106, 38, 255), new Color32(125, 63, 120, 255), new Color32(47, 95, 154, 255) };
                for (var row = 0; row < 2; row++)
                    for (var col = 0; col < 2; col++)
                        for (var y = 0; y < 36; y++)
                            for (var x = 0; x < 36; x++)
                            {
                                var px = 24 + col * 44 + x;
                                var py = 24 + row * 44 + y;
                                var rim = x < 2 || x > 33 || y < 2 || y > 33;
                                pixels[py * size + px] = rim ? new Color32(224, 191, 106, 255) : colors[row * 2 + col];
                            }
            }
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "CommsTablet_Icon", wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
        }
    }
}
