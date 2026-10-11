using System.Collections.Generic;
using UnityEngine;

namespace KrisMod.Components
{
    /// <summary>
    /// PROCEED's snow, in the spirit of DELTARUNE's SnowGrave: a column of flakes thrown out from
    /// the target in one gust, then swirling round it and drifting down until they melt away.
    ///
    /// Every flake is a six-armed star facing the camera, all of them in one vertex-coloured mesh
    /// rebuilt each frame with the UI shader (SlashMesh says why that shader). Purely local:
    /// ProceedAct strikes on every client and each builds its own.
    /// </summary>
    public class SnowfallVfx : MonoBehaviour
    {
        /// <summary>The pale blue the snow and PROCEED's flash share.</summary>
        internal static readonly Color Ice = new Color(0.72f, 0.9f, 1f);

        private const float Life = 2.4f;
        private const float GustTime = 0.3f;
        private const float FadeIn = 0.08f;
        private const float FadeOut = 0.9f;

        private struct Flake
        {
            public float angle, distance, height, fall, swirl, size, spin, sway, phase;
            public bool blue;
        }

        private Flake[] flakes;
        private Mesh mesh;
        private float age;

        private readonly List<Vector3> verts = new List<Vector3>();
        private readonly List<Color32> colors = new List<Color32>();
        private readonly List<int> tris = new List<int>();

        /// <param name="radius">How far the snow spreads from the target's core.</param>
        /// <param name="height">How far above the core the column reaches.</param>
        internal static SnowfallVfx Spawn(Vector3 position, float radius, float height, int count)
        {
            var host = new GameObject("KrisSnowfall");
            host.transform.SetPositionAndRotation(position, Quaternion.identity);

            var snow = host.AddComponent<SnowfallVfx>();
            snow.flakes = new Flake[count];
            for (int i = 0; i < count; i++)
            {
                snow.flakes[i] = new Flake
                {
                    angle = Random.Range(0f, Mathf.PI * 2f),
                    // Square root spreads them evenly over the disc instead of bunching at the axis.
                    distance = radius * Mathf.Sqrt(Random.Range(0.04f, 1f)),
                    height = Random.Range(-0.4f * height, height),
                    fall = Random.Range(0.9f, 2.2f),
                    swirl = Random.Range(1.6f, 3.2f),
                    size = Random.Range(0.07f, 0.2f),
                    spin = Random.Range(-4f, 4f),
                    sway = Random.Range(0.05f, 0.25f),
                    phase = Random.Range(0f, Mathf.PI * 2f),
                    blue = Random.value < 0.35f,
                };
            }
            snow.mesh = SlashMesh.Attach(host, SlashMesh.DepthTested);
            return snow;
        }

        private void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= Life)
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

            Camera camera = SlashMesh.FindCamera();
            if (camera == null || flakes == null)
            {
                SlashMesh.Upload(mesh, verts, colors, tris);
                return;
            }

            // The mesh is in world axes (the host is never rotated), so the camera's own axes
            // turn each star to face it.
            Vector3 right = camera.transform.right;
            Vector3 up = camera.transform.up;

            // The gust: everything starts packed round the core and is flung out to its place,
            // spinning fast, then the swirl slows to a drift.
            float gust = SlashMesh.Smooth(age / GustTime);
            float spread = Mathf.Lerp(0.15f, 1f, 1f - (1f - gust) * (1f - gust));
            float turned = age < GustTime ? age * 1.8f : GustTime * 1.8f + (age - GustTime) * 0.35f;

            float alpha = Mathf.Clamp01(age / FadeIn) * Mathf.Clamp01((Life - age) / FadeOut);

            for (int i = 0; i < flakes.Length; i++)
            {
                Flake f = flakes[i];
                float angle = f.angle + f.swirl * turned;
                float distance = f.distance * spread;
                float drop = f.fall * Mathf.Max(0f, age - GustTime * 0.5f);
                Vector3 centre = new Vector3(
                    Mathf.Cos(angle) * distance + Mathf.Sin(age * 3f + f.phase) * f.sway,
                    f.height * spread - drop,
                    Mathf.Sin(angle) * distance);

                Color32 tint = SlashMesh.Tint(f.blue ? Ice : Color.white, alpha);
                AddStar(centre, right, up, f.size, f.phase + f.spin * age, tint);
            }

            SlashMesh.Upload(mesh, verts, colors, tris);
        }

        /// <summary>Three thin bars crossed at 60 degrees: a six-armed flake.</summary>
        private void AddStar(Vector3 centre, Vector3 right, Vector3 up, float size, float roll, Color32 tint)
        {
            float half = size * 0.16f;
            for (int arm = 0; arm < 3; arm++)
            {
                float a = roll + arm * Mathf.PI / 3f;
                Vector3 along = (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * size;
                Vector3 across = (right * -Mathf.Sin(a) + up * Mathf.Cos(a)) * half;

                int start = verts.Count;
                verts.Add(centre - along - across);
                verts.Add(centre - along + across);
                verts.Add(centre + along + across);
                verts.Add(centre + along - across);
                for (int k = 0; k < 4; k++) colors.Add(tint);

                tris.Add(start); tris.Add(start + 1); tris.Add(start + 2);
                tris.Add(start); tris.Add(start + 2); tris.Add(start + 3);
            }
        }
    }
}
