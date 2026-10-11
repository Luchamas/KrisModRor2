using System.Collections.Generic;
using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.Rendering;

namespace KrisMod.Components
{
    /// <summary>
    /// The shared pieces of FIGHT's two procedural effects: the material, the vertex buffers and
    /// the camera lookup.
    ///
    /// Both are vertex-coloured meshes rebuilt every frame and drawn with the UI shader, for the
    /// reason SoulVisual gives: RoR2 makes no promise that any other plain shader survived
    /// stripping, and the HUD cannot draw without this one. The UI shader takes its depth test
    /// from the global unity_GUIZTestMode, which only a canvas sets, so each material pins its own.
    /// </summary>
    internal static class SlashMesh
    {
        private static Material depthTested;
        private static Material onTop;

        /// <summary>Hidden behind walls and Kris like any object in the world.</summary>
        internal static Material DepthTested => depthTested != null ? depthTested : (depthTested = Make(CompareFunction.LessEqual));

        /// <summary>Drawn over everything - the enemy's own body would otherwise eat half the cut.</summary>
        internal static Material OnTop => onTop != null ? onTop : (onTop = Make(CompareFunction.Always));

        private static Material Make(CompareFunction zTest)
        {
            var material = new Material(Canvas.GetDefaultCanvasMaterial());
            material.SetInt("unity_GUIZTestMode", (int)zTest);
            material.renderQueue = 3100;
            return material;
        }

        internal static Mesh Attach(GameObject host, Material material)
        {
            var mesh = new Mesh { name = host.name };
            mesh.MarkDynamic();
            host.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = host.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        internal static void Upload(Mesh mesh, List<Vector3> v, List<Color32> c, List<int> t)
        {
            mesh.Clear();
            if (t.Count == 0) return;
            mesh.SetVertices(v);
            mesh.SetColors(c);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
        }

        /// <summary>Joins two rows of <paramref name="count"/> vertices that start at a and b.</summary>
        internal static void Band(List<int> t, int a, int b, int count)
        {
            for (int i = 0; i < count - 1; i++)
            {
                t.Add(a + i); t.Add(b + i); t.Add(a + i + 1);
                t.Add(a + i + 1); t.Add(b + i); t.Add(b + i + 1);
            }
        }

        internal static Camera FindCamera()
        {
            foreach (CameraRigController rig in CameraRigController.readOnlyInstancesList)
            {
                if (rig != null && rig.sceneCam != null) return rig.sceneCam;
            }
            return Camera.main;
        }

        internal static Color32 Tint(Color color, float alpha) =>
            new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));

        internal static float Smooth(float x)
        {
            x = Mathf.Clamp01(x);
            return x * x * (3f - 2f * x);
        }
    }

    /// <summary>
    /// FIGHT's swing: a crescent laid along the sword tip's path, drawn like the slash in
    /// DELTARUNE's battles - a hard white edge on a Kris-blue body. The head runs from
    /// <see cref="startAngle"/> to <see cref="endAngle"/> over <see cref="reveal"/> seconds with
    /// the tail chasing it, then the whole cut thins to a line and is gone.
    ///
    /// The arc lives in the local Y/Z plane, centred on the transform: angle 0 is local forward
    /// (Z) and 90 is local up (Y). The spawner rolls the transform about forward to tilt the cut.
    /// Purely local: every client runs KrisSlash and builds its own.
    /// </summary>
    public class SlashArcVfx : MonoBehaviour
    {
        private const int Segments = 36;

        public float radius = 1.7f;
        public float width = 0.55f;
        public float startAngle;
        public float endAngle;
        public float reveal = 0.12f;
        public float fade = 0.2f;

        private Mesh mesh;
        private float age;

        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> tris = new List<int>();

        internal static SlashArcVfx Spawn(Transform parent, Vector3 position, Quaternion rotation,
                                          float startAngle, float endAngle, float reveal)
        {
            var host = new GameObject("KrisSlashArc");
            host.transform.SetPositionAndRotation(position, rotation);
            if (parent != null) host.transform.SetParent(parent, true);

            var arc = host.AddComponent<SlashArcVfx>();
            arc.startAngle = startAngle;
            arc.endAngle = endAngle;
            arc.reveal = Mathf.Max(reveal, 0.02f);
            arc.fade = arc.reveal * 1.6f;
            arc.mesh = SlashMesh.Attach(host, SlashMesh.DepthTested);
            arc.Rebuild();
            return arc;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= reveal + fade)
            {
                Destroy(gameObject);
                return;
            }
            Rebuild();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void Rebuild()
        {
            verts.Clear(); colors.Clear(); tris.Clear();

            // The head eases out to the end of the cut; the tail starts late and arrives with the
            // fade, so the crescent is longest just as the blade finishes.
            float head = SlashMesh.Smooth(age / reveal);
            float tail = SlashMesh.Smooth((age - reveal * 0.35f) / (reveal * 0.65f + fade));
            float after = Mathf.Clamp01((age - reveal) / fade);
            float thick = 1f - after * after;
            float alpha = 1f - after * after * after;

            if (head - tail > 0.005f && thick > 0.01f)
            {
                // Five rows, inner to outer: soft blue, blue, white core, white core, blue edge.
                float[] at = { -0.30f, 0.05f, 0.30f, 0.62f, 0.85f };
                Color32[] tint =
                {
                    SlashMesh.Tint(KrisAssets.KrisBlue, 0f),
                    SlashMesh.Tint(KrisAssets.KrisBlue, 0.75f * alpha),
                    SlashMesh.Tint(Color.white, alpha),
                    SlashMesh.Tint(Color.white, alpha),
                    SlashMesh.Tint(KrisAssets.KrisBlue, 0.9f * alpha),
                };

                for (int row = 0; row < at.Length; row++)
                {
                    for (int i = 0; i < Segments; i++)
                    {
                        float s = i / (Segments - 1f);
                        float u = Mathf.Lerp(tail, head, s);
                        float angle = Mathf.Lerp(startAngle, endAngle, u) * Mathf.Deg2Rad;

                        // Fattest two thirds of the way to the head, pointed at both ends.
                        float w = width * thick * Mathf.Sin(Mathf.PI * Mathf.Pow(s, 1.6f));
                        float r = radius + at[row] * w;
                        verts.Add(new Vector3(0f, Mathf.Sin(angle) * r, Mathf.Cos(angle) * r));
                        colors.Add(tint[row]);
                    }
                }

                for (int row = 0; row < at.Length - 1; row++)
                {
                    SlashMesh.Band(tris, row * Segments, (row + 1) * Segments, Segments);
                }
            }

            SlashMesh.Upload(mesh, verts, colors, tris);
        }
    }

    /// <summary>
    /// FIGHT's hit: DELTARUNE's damage slash - a white lens that shoots across the target on a
    /// diagonal, then pinches thin and vanishes, with a few blue chips thrown off it. It always
    /// faces the camera and is drawn over the target.
    ///
    /// A catalogued effect (OverlapAttack spawns it through EffectManager and transmits it), so
    /// it is pooled: everything is reset in OnEnable, never in Awake or Start.
    /// </summary>
    public class SlashCutVfx : MonoBehaviour
    {
        private const int Segments = 16;
        private const int Chips = 6;

        /// <summary>The cut's diagonal on screen; the two prefabs differ only in its sign.</summary>
        public float slant = 35f;
        public float length = 2.6f;
        public float thickness = 0.16f;
        public float grow = 0.07f;
        public float thin = 0.22f;

        /// <summary>The camera-facing child that carries the mesh; set by BuildPrefab.</summary>
        public Transform visual;

        private Mesh mesh;
        private float age;
        private float angle;
        private readonly Vector2[] chipDir = new Vector2[Chips];
        private readonly float[] chipSpeed = new float[Chips];

        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> tris = new List<int>();

        /// <summary>Total time on screen; the prefab's DestroyOnTimer must not be shorter.</summary>
        internal float Lifetime => grow + thin;

        private void OnEnable()
        {
            // The template BuildPrefab clones fires this before visual is assigned.
            if (visual == null) return;
            if (mesh == null)
            {
                // Per instance: a mesh shared through the prefab would be one cut drawn everywhere.
                mesh = new Mesh { name = "KrisSlashCut" };
                mesh.MarkDynamic();
                visual.GetComponent<MeshFilter>().sharedMesh = mesh;
            }

            age = 0f;
            angle = slant + Random.Range(-10f, 10f);
            for (int i = 0; i < Chips; i++)
            {
                // Off both sides of the cut, roughly square to it.
                float a = (angle + 90f + (i % 2 == 0 ? 0f : 180f) + Random.Range(-50f, 50f)) * Mathf.Deg2Rad;
                chipDir[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                chipSpeed[i] = Random.Range(2.5f, 5f);
            }
            Rebuild();
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            Rebuild();
        }

        private void Rebuild()
        {
            if (mesh == null) return;
            Camera camera = SlashMesh.FindCamera();
            if (camera != null) visual.rotation = camera.transform.rotation;

            verts.Clear(); colors.Clear(); tris.Clear();

            float growth = SlashMesh.Smooth(age / grow);
            float after = Mathf.Clamp01((age - grow) / thin);
            float pinch = 1f - after;
            if (pinch > 0f)
            {
                Vector2 along = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                Vector2 across = new Vector2(-along.y, along.x);

                // The blue glow first, the white core over it - triangle order is draw order.
                Lens(along, across, length * growth * 1.12f, thickness * pinch * 2.6f,
                     SlashMesh.Tint(KrisAssets.KrisBlue, 0.7f * pinch));
                Lens(along, across, length * growth, thickness * pinch,
                     SlashMesh.Tint(Color.white, 1f));

                float t = Mathf.Max(age - grow * 0.5f, 0f);
                for (int i = 0; i < Chips && t > 0f; i++)
                {
                    float size = 0.11f * pinch;
                    Vector2 centre = chipDir[i] * (chipSpeed[i] * t + 0.15f) + along * ((i - 2.5f) * 0.25f);
                    Lens(chipDir[i], new Vector2(-chipDir[i].y, chipDir[i].x), size * 2.2f, size,
                         SlashMesh.Tint(i % 3 == 0 ? Color.white : KrisAssets.KrisBlue, pinch), centre);
                }
            }

            SlashMesh.Upload(mesh, verts, colors, tris);
        }

        /// <summary>A pointed lens of the given length and greatest thickness, in the camera's plane.</summary>
        private void Lens(Vector2 along, Vector2 across, float len, float thick, Color32 color,
                          Vector2 centre = default(Vector2))
        {
            if (len <= 0.001f || thick <= 0.001f) return;

            int start = verts.Count;
            for (int i = 0; i < Segments; i++)
            {
                float x = i / (Segments - 1f) * 2f - 1f;
                float h = thick * 0.5f * Mathf.Pow(1f - x * x, 0.75f);
                Vector2 middle = centre + along * (x * len * 0.5f);
                Vector2 top = middle + across * h;
                Vector2 bottom = middle - across * h;
                verts.Add(new Vector3(top.x, top.y, 0f));
                verts.Add(new Vector3(bottom.x, bottom.y, 0f));
                colors.Add(color);
                colors.Add(color);
            }
            for (int i = 0; i < Segments - 1; i++)
            {
                int a = start + i * 2;
                tris.Add(a); tris.Add(a + 1); tris.Add(a + 2);
                tris.Add(a + 2); tris.Add(a + 1); tris.Add(a + 3);
            }
        }

        /// <summary>
        /// Builds one catalogued hit prefab. EffectManager refuses anything without an
        /// EffectComponent (see KrisEffects), and DestroyOnTimer hands it back to the pool.
        /// </summary>
        internal static GameObject BuildPrefab(string name, float slant)
        {
            var template = new GameObject(name);
            var child = new GameObject("Cut");
            child.transform.SetParent(template.transform, false);
            child.AddComponent<MeshFilter>();
            var renderer = child.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = SlashMesh.OnTop;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var cut = template.AddComponent<SlashCutVfx>();
            cut.slant = slant;
            cut.visual = child.transform;
            GameObject prefab = R2API.PrefabAPI.InstantiateClone(template, name, false);
            Destroy(template);

            var effect = prefab.AddComponent<EffectComponent>();
            effect.applyScale = false;
            effect.parentToReferencedTransform = false;
            effect.positionAtReferencedTransform = false;

            var attributes = prefab.AddComponent<VFXAttributes>();
            attributes.vfxPriority = VFXAttributes.VFXPriority.Always;
            attributes.vfxIntensity = VFXAttributes.VFXIntensity.Low;

            var timer = prefab.AddComponent<DestroyOnTimer>();
            timer.duration = prefab.GetComponent<SlashCutVfx>().Lifetime + 0.05f;
            timer.resetAgeOnDisable = true;

            R2API.ContentAddition.AddEffect(prefab);
            return prefab;
        }
    }
}
