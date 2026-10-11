using System;
using UnityEngine;

namespace KrisMod.Modules
{
    /// <summary>
    /// Placeholder skill icons, drawn at startup so Kris is readable before any art exists.
    ///
    /// The last link of the asset fallback chain (loose file, then this): drop a PNG with the
    /// same name into Assets/Icons and it wins. Every shape is an implicit function sampled per
    /// pixel with a little supersampling, which is plenty at 128px and needs no art pipeline.
    /// </summary>
    internal static class ProceduralIcons
    {
        private const int Size = 128;
        private const int Samples = 3;

        private static readonly Color Background = new Color(0.07f, 0.06f, 0.12f, 1f);
        private static readonly Color Frame = new Color(1f, 1f, 1f, 1f);

        /// <summary>Signed coverage test in a -1..1 square, y up.</summary>
        private delegate bool Shape(float x, float y);

        internal static Sprite Sword() => Icon((x, y) => SwordShape(x, y), new Color(0.82f, 0.86f, 0.95f));

        internal static Sprite Shield() => Icon(ShieldShape, KrisAssets.ShieldPink);

        internal static Sprite Soul() => Icon((x, y) => HeartShape(x * 1.35f, y * 1.35f + 0.1f), KrisAssets.SoulRed);

        internal static Sprite Tension() => Layered(KrisAssets.TpOrange, TpBar, "TP");

        internal static Sprite Act() => Layered(KrisAssets.TpOrange, null, "ACT");

        internal static Sprite Portrait() => Icon((x, y) => HeartShape(x * 1.2f, y * 1.2f + 0.05f), KrisAssets.KrisBlue);

        /// <summary>A bare heart on transparency, for the SOUL itself and the menu cursor.</summary>
        internal static Sprite Heart(int size, Color color, bool framed)
        {
            return Draw(size, (x, y) => HeartShape(x * 1.1f, y * 1.1f + 0.05f), color, framed ? Background : Color.clear, framed);
        }

        private static Sprite Icon(Shape shape, Color color) => Draw(Size, shape, color, Background, true);

        /// <summary>An icon with pixel lettering over an optional shape.</summary>
        private static Sprite Layered(Color color, Shape shape, string text)
        {
            Sprite baseSprite = Draw(Size, shape ?? ((x, y) => false), color, Background, true);
            Sprite letters = KrisAssets.TextSprite(text, text.Length);
            if (baseSprite == null || letters == null) return baseSprite;

            Texture2D target = baseSprite.texture;
            Texture2D glyphs = letters.texture;

            // Lettering spans 80% of the width, centred, sampled nearest so it stays crisp.
            int width = Mathf.RoundToInt(Size * 0.8f);
            int height = Mathf.RoundToInt(width * (glyphs.height / (float)glyphs.width));
            int x0 = (Size - width) / 2;
            int y0 = shape == null ? (Size - height) / 2 : Size / 2 - height - 6;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color g = glyphs.GetPixel(x * glyphs.width / width, y * glyphs.height / height);
                    if (g.a < 0.5f) continue;
                    target.SetPixel(x0 + x, y0 + y, shape == null ? color : Color.white);
                }
            }
            target.Apply(false, false);
            return baseSprite;
        }

        private static Sprite Draw(int size, Shape shape, Color color, Color background, bool framed)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color[size * size];
            float step = 1f / Samples;
            int border = Mathf.Max(2, size / 32);

            for (int py = 0; py < size; py++)
            {
                for (int px = 0; px < size; px++)
                {
                    bool edge = framed && (px < border || py < border || px >= size - border || py >= size - border);
                    if (edge) { pixels[py * size + px] = Frame; continue; }

                    int hits = 0;
                    for (int sy = 0; sy < Samples; sy++)
                    {
                        for (int sx = 0; sx < Samples; sx++)
                        {
                            float x = ((px + (sx + 0.5f) * step) / size) * 2f - 1f;
                            float y = ((py + (sy + 0.5f) * step) / size) * 2f - 1f;
                            if (shape(x, y)) hits++;
                        }
                    }

                    float coverage = hits / (float)(Samples * Samples);
                    pixels[py * size + px] = Color.Lerp(background, color, coverage);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        // ---- shapes -------------------------------------------------------------------------

        /// <summary>The classic heart curve, (x^2 + y^2 - 1)^3 - x^2 y^3 &lt;= 0, spanning about -1.1..1.3.</summary>
        private static bool HeartShape(float x, float y)
        {
            float a = x * x + y * y - 1f;
            return a * a * a - x * x * y * y * y <= 0f;
        }

        /// <summary>A kite shield: flat top, straight sides, curving to a point at the bottom.</summary>
        private static bool ShieldShape(float x, float y)
        {
            if (y > 0.7f || y < -0.85f) return false;
            float halfWidth = y > 0f ? 0.62f : 0.62f * Mathf.Sqrt(Mathf.Max(0f, 1f - (y * y) / (0.85f * 0.85f)));
            if (Mathf.Abs(x) > halfWidth) return false;
            // A darker cross would need a second colour; a gap line reads the same at this size.
            return !(Mathf.Abs(x) < 0.05f && y < 0.55f && y > -0.6f);
        }

        /// <summary>A sword on the diagonal: blade, crossguard, grip and pommel.</summary>
        private static bool SwordShape(float x, float y)
        {
            // Rotate 45 degrees so the sword can be described upright: u along it, v across.
            const float c = 0.70710678f;
            float u = (x + y) * c;
            float v = (y - x) * c;

            bool blade = u > -0.35f && u < 0.9f && Mathf.Abs(v) < 0.09f * Mathf.Clamp01((0.95f - u) * 4f);
            bool guard = u > -0.43f && u < -0.35f && Mathf.Abs(v) < 0.32f;
            bool grip = u > -0.75f && u <= -0.43f && Mathf.Abs(v) < 0.05f;
            bool pommel = (u + 0.8f) * (u + 0.8f) + v * v < 0.09f * 0.09f;
            return blade || guard || grip || pommel;
        }

        /// <summary>The TP bar shape for the passive: a slanted gauge, lower half filled.</summary>
        private static bool TpBar(float x, float y)
        {
            float slant = x * 0.25f;
            return Math.Abs(x) < 0.3f && y + slant > 0.05f && y + slant < 0.8f;
        }
    }
}
