using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
// RoR2 declares its own Path type, which shadows System.IO.Path once RoR2 is imported.
using Path = System.IO.Path;

namespace KrisMod.Modules
{
    /// <summary>
    /// Central place for every asset the mod needs: Kris's model from the asset bundle, their
    /// icons and HUD art from loose image files, and the vanilla effects they borrow.
    /// </summary>
    internal static class KrisAssets
    {
        internal const string BundleName = "kris";

        internal static AssetBundle Bundle;

        // Palette lifted from Kris's Dark World design.
        internal static readonly Color KrisBlue = new Color(0.28f, 0.72f, 0.96f);
        internal static readonly Color ShieldPink = new Color(0.95f, 0.36f, 0.62f);
        internal static readonly Color SoulRed = new Color(1f, 0.05f, 0.05f);
        internal static readonly Color TpOrange = new Color(1f, 0.63f, 0.25f);

        /// <summary>The TP bar fill, rgb(255, 160, 64).</summary>
        internal static readonly Color32 TpFillColor = new Color32(255, 160, 64, 255);

        // Borrowed vanilla assets. Keys verified against the shipped Addressables catalog.
        internal static GameObject SlashHitEffect;
        internal static GameObject SlashSwingEffect;

        /// <summary>FIGHT's own hits (Components/SlashVfx.cs), one per cut so the mark on the
        /// target slants the way the blade went: A rises to Kris's left, B falls from the right.</summary>
        internal static GameObject FightHitA;
        internal static GameObject FightHitB;
        internal static GameObject SoulLandEffect;
        internal static GameObject XSlashEffect;

        /// <summary>PROCEED's frost burst under its snowfall (Components/SnowVfx.cs): the Glacial
        /// elite's death blast, or the freeze-execute shatter if that key ever stops resolving.</summary>
        internal static GameObject ProceedFrostEffect;
        internal static GameObject BuffEffect;
        internal static GameObject Crosshair;

        /// <summary>The survivor portrait, as a texture - CharacterBody wants a Texture, not a Sprite.</summary>
        internal static Texture PortraitTexture;

        /// <summary>The TP bar frame, the same artwork recoloured into a fill overlay, and the
        /// supplied "%" and "MAX" lettering that sit under the number.</summary>
        internal static Sprite TpBarFrame;
        internal static Sprite TpBarFill;
        internal static Sprite TpPercent;
        internal static Sprite TpMax;

        /// <summary>The supplied ACT battle box (Hud/ActBox), nine-sliced so it stretches without
        /// bending its corner ornaments. Null without the file: the menu falls back to plain panels.</summary>
        internal static Sprite ActBox;

        /// <summary>ActBox's slice margin in its own pixels: 8 clear + 4 white + 2 grey, the corner
        /// ornaments reach 28, so 32 keeps them whole. The art is 2x pixel art (297x84 native).</summary>
        internal const float ActBoxSlice = 32f;

        // Each icon is a loose file under Assets/Icons when there is one, and a procedural
        // placeholder (ProceduralIcons) when there is not, so the kit is readable on day one.
        internal static Sprite IconKris;
        internal static Sprite IconPassive;
        internal static Sprite IconFight;
        internal static Sprite IconDefend;
        internal static Sprite IconSoul;
        internal static Sprite IconAct;

        /// <summary>The PETAL FEATHER buff (Icons/PetalFeather). Falls back to the ACT icon.</summary>
        internal static Sprite IconPetalFeather;

        /// <summary>The SOUL heart, used for the thrown SOUL.</summary>
        internal static Sprite SoulHeart;

        /// <summary>The ACT menu's hearts (Hud/ActHeart): 16px pixel art, white so the menu can
        /// tint it. Falls back to <see cref="SoulHeart"/>.</summary>
        internal static Sprite ActHeart;

        internal static void Init()
        {
            LoadBundle();
            LoadVanillaAssets();
            BuildFightEffects();
            BuildIcons();
        }

        private static void LoadBundle()
        {
            try
            {
                var dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(dir)) return;

                // Accept both "Assets/kris" and a bundle dropped next to the dll.
                var candidates = new[]
                {
                    Path.Combine(Path.Combine(dir, "Assets"), BundleName),
                    Path.Combine(dir, BundleName),
                };

                foreach (var path in candidates)
                {
                    if (!File.Exists(path)) continue;
                    Bundle = AssetBundle.LoadFromFile(path);
                    if (Bundle != null)
                    {
                        Log.Info("Loaded asset bundle from " + path);
                        return;
                    }
                }

                Log.Warning("No Kris asset bundle found - Kris will wear Loader's model. " +
                            "Expected it at [plugin folder]/Assets/" + BundleName + ".");
            }
            catch (Exception e)
            {
                Log.Error("Failed to load the Kris asset bundle: " + e);
            }
        }

        /// <summary>Loads an asset from the mod bundle, returning null (not throwing) when absent.</summary>
        internal static T FromBundle<T>(string name) where T : UnityEngine.Object
        {
            if (Bundle == null || string.IsNullOrEmpty(name)) return null;
            try
            {
                return Bundle.LoadAsset<T>(name);
            }
            catch (Exception e)
            {
                Log.Warning("Bundle asset '" + name + "' failed to load: " + e.Message);
                return null;
            }
        }

        /// <summary>The DELTARUNE text font's bundle (KrisSetup.BuildFont), beside the model's.</summary>
        internal const string FontBundleName = "krisfont";
        private const string ActFontName = "DeltaruneText";

        private static TMP_FontAsset actFont;
        private static bool actFontTried;

        /// <summary>
        /// The DELTARUNE text font for the ACT menu, or null to keep the HUD font. The bundle holds
        /// a plain Font; it becomes a dynamic TextMeshPro font asset here, on first use, so it does
        /// not depend on the TMP version it was built with. Anything it lacks falls back to
        /// <paramref name="fallback"/>.
        /// </summary>
        internal static TMP_FontAsset ActFont(TMP_FontAsset fallback)
        {
            if (actFontTried) return actFont;
            actFontTried = true;
            try
            {
                string dir = PluginFolder();
                if (string.IsNullOrEmpty(dir)) return null;
                string path = Path.Combine(Path.Combine(dir, "Assets"), FontBundleName);
                if (!File.Exists(path))
                {
                    Log.Warning("No ACT font bundle at " + path + " - the ACT menu keeps the HUD font.");
                    return null;
                }

                AssetBundle bundle = AssetBundle.LoadFromFile(path);
                Font font = bundle != null ? bundle.LoadAsset<Font>(ActFontName) : null;
                if (font == null)
                {
                    Log.Warning("The ACT font bundle has no font '" + ActFontName + "'.");
                    return null;
                }

                actFont = TMP_FontAsset.CreateFontAsset(font);
                if (actFont == null) return null;
                actFont.name = ActFontName;
                if (fallback != null) actFont.fallbackFontAssetTable = new List<TMP_FontAsset> { fallback };
                Log.Info("Loaded the ACT font from " + path);
            }
            catch (Exception e)
            {
                Log.Error("Failed to load the ACT font: " + e);
                actFont = null;
            }
            return actFont;
        }

        /// <summary>Synchronously resolves a vanilla Addressables key.</summary>
        internal static T Load<T>(string key) where T : UnityEngine.Object
        {
            try
            {
                return Addressables.LoadAssetAsync<T>(key).WaitForCompletion();
            }
            catch (Exception e)
            {
                Log.Warning("Could not load addressable '" + key + "': " + e.Message);
                return null;
            }
        }

        private static void LoadVanillaAssets()
        {
            SlashHitEffect = Load<GameObject>("RoR2/Base/Merc/OmniImpactVFXSlashMerc.prefab");
            SlashSwingEffect = Load<GameObject>("RoR2/Base/Merc/MercSwordSlash.prefab");
            SoulLandEffect = Load<GameObject>("RoR2/Base/Common/VFX/MuzzleflashSmokeRing.prefab");
            XSlashEffect = Load<GameObject>("RoR2/Base/Merc/MercSwordSlashWhirlwind.prefab");
            BuffEffect = Load<GameObject>("RoR2/Base/Common/VFX/TeleportOutBoom.prefab");
            ProceedFrostEffect = Load<GameObject>("RoR2/Base/EliteIce/AffixWhiteExplosion.prefab");
            if (ProceedFrostEffect == null) ProceedFrostEffect = Load<GameObject>("RoR2/Base/Common/VFX/IceCullExplosion.prefab");
            Crosshair = Load<GameObject>("RoR2/Base/UI/SimpleDotCrosshair.prefab");
        }

        private static void BuildFightEffects()
        {
            try
            {
                FightHitA = Components.SlashCutVfx.BuildPrefab("KrisFightHitA", -35f);
                FightHitB = Components.SlashCutVfx.BuildPrefab("KrisFightHitB", 35f);
            }
            catch (Exception e)
            {
                // FIGHT falls back to Merc's impact (KrisSlash.HitEffect).
                Log.Error("Could not build FIGHT's hit effects: " + e);
            }
        }

        private static void BuildIcons()
        {
            LoadImageFolders();

            IconKris = FileOr("CharSelection", () => ProceduralIcons.Portrait());
            IconPassive = FileOr("Passive", () => ProceduralIcons.Tension());
            IconFight = FileOr("Primary", () => ProceduralIcons.Sword());
            IconDefend = FileOr("Secondary", () => ProceduralIcons.Shield());
            IconSoul = FileOr("Utility", () => ProceduralIcons.Soul());
            IconAct = FileOr("Special", () => ProceduralIcons.Act());
            IconPetalFeather = FromFile("PetalFeather");
            if (IconPetalFeather != null)
            {
                // 38px pixel art: point filtering keeps it crisp at buff-bar size.
                IconPetalFeather.texture.filterMode = FilterMode.Point;
            }
            else
            {
                IconPetalFeather = IconAct;
            }
            SoulHeart = ProceduralIcons.Heart(64, SoulRed, false);
            ActHeart = FromFile("ActHeart");
            if (ActHeart != null)
            {
                ActHeart.texture.filterMode = FilterMode.Point;
            }
            else
            {
                ActHeart = SoulHeart;
            }

            PortraitTexture = IconKris != null ? IconKris.texture : null;
            // Pixel art: point filtering keeps the portrait's cells crisp at any size.
            if (PortraitTexture != null) PortraitTexture.filterMode = FilterMode.Point;

            // Supplied artwork, so both match the hand-lettered "TP" exactly. MAX arrives
            // already coloured, so it is drawn untinted.
            TpPercent = FromFile("Percentage");
            if (TpPercent == null)
            {
                Log.Warning("No Hud/Percentage image found - the TP bar will show no percent sign.");
            }

            TpMax = FromFile("MAX");
            if (TpMax == null)
            {
                Log.Warning("No Hud/MAX image found - a full TP bar will show nothing in its place.");
            }

            BuildTpBar();
            BuildActBox();
        }

        private static void BuildActBox()
        {
            Sprite source = FromFile("ActBox");
            if (source == null)
            {
                Log.Warning("No Hud/ActBox image found - the ACT menu will draw a plain box.");
                return;
            }

            // Pixel art: point filtering keeps the frame's lines sharp when scaled.
            Texture2D texture = source.texture;
            texture.filterMode = FilterMode.Point;
            ActBox = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f),
                                   100f, 0, SpriteMeshType.FullRect,
                                   new Vector4(ActBoxSlice, ActBoxSlice, ActBoxSlice, ActBoxSlice));
        }

        // ---------------------------------------------------------------------
        // Loose image files. Same idea as the voice clips: drop a PNG in a folder
        // and it is in the game next build, with no Unity round-trip.
        // ---------------------------------------------------------------------

        private static readonly string StatusFolder = Path.Combine("Icons", "Status");
        private const string StatusPrefix = "Status/";

        /// <summary>Clear pixels between the stat and its arrow in a composed status icon.</summary>
        private const int StatusGap = 1;

        private static readonly Dictionary<string, Sprite> FileSprites =
            new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);

        private static Sprite FromFile(string name)
        {
            return FileSprites.TryGetValue(name, out Sprite sprite) ? sprite : null;
        }

        /// <summary>
        /// A buff or debuff icon, composed the way the supplied art lays it out: the stat it
        /// touches (Icons/Status/Attack, Defense, ...) followed by the arrow for which way it
        /// went (Status/Up for a buff, Status/Down for a debuff). Both parts are 1:1 pixel art,
        /// grey-and-white so the buff colour tints them. Without either part: the ACT icon.
        /// </summary>
        internal static Sprite StatusIcon(string stat, bool up)
        {
            Sprite statPart = FromFile(StatusPrefix + stat);
            Sprite arrow = FromFile(StatusPrefix + (up ? "Up" : "Down"));
            if (statPart == null || arrow == null)
            {
                Log.Warning("No Icons/Status art for " + stat + (up ? " up" : " down") + " - using the ACT icon.");
                return IconAct;
            }

            try
            {
                return SideBySide(statPart.texture, arrow.texture);
            }
            catch (Exception e)
            {
                Log.Warning("Could not compose the " + stat + " status icon: " + e.Message);
                return IconAct;
            }
        }

        /// <summary>Two textures in a row, the pair centred in a square (buff icons draw square).</summary>
        private static Sprite SideBySide(Texture2D left, Texture2D right)
        {
            int width = left.width + StatusGap + right.width;
            int height = Mathf.Max(left.height, right.height);
            int size = Mathf.Max(width, height);

            // Pixel art: point filtering keeps every pixel square when the HUD scales it up.
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(new Color32[size * size]);

            int x = (size - width) / 2;
            int y = (size - height) / 2;
            texture.SetPixels32(x, y + (height - left.height) / 2, left.width, left.height, left.GetPixels32());
            texture.SetPixels32(x + left.width + StatusGap, y + (height - right.height) / 2,
                                right.width, right.height, right.GetPixels32());
            texture.Apply();

            return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
        }

        /// <summary>A loose icon file if one was dropped in, otherwise the procedural placeholder.</summary>
        private static Sprite FileOr(string name, Func<Sprite> placeholder)
        {
            Sprite sprite = FromFile(name);
            if (sprite != null) return sprite;
            try
            {
                return placeholder();
            }
            catch (Exception e)
            {
                Log.Warning("Could not draw the placeholder for Icons/" + name + ": " + e.Message);
                return null;
            }
        }

        private static void LoadImageFolders()
        {
            string root = PluginFolder();
            if (root == null) return;

            foreach (string folder in new[] { "Icons", "Hud", StatusFolder })
            {
                string path = Path.Combine(Path.Combine(root, "Assets"), folder);
                if (!Directory.Exists(path)) continue;

                foreach (string file in Directory.GetFiles(path))
                {
                    string extension = Path.GetExtension(file).ToLowerInvariant();
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") continue;

                    // Status parts keep their folder in the name, so "Up" cannot shadow an icon.
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (folder == StatusFolder) name = StatusPrefix + name;

                    Sprite sprite = LoadSpriteFromFile(file);
                    if (sprite != null) FileSprites[name] = sprite;
                }
            }

            if (FileSprites.Count > 0)
            {
                var names = new List<string>(FileSprites.Keys);
                names.Sort();
                Log.Info("Loaded " + FileSprites.Count + " image(s): " + string.Join(", ", names.ToArray()));
            }
        }

        private static string PluginFolder()
        {
            try
            {
                return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            }
            catch (Exception e)
            {
                Log.Error("Could not resolve the plugin folder: " + e.Message);
                return null;
            }
        }

        private static Sprite LoadSpriteFromFile(string file)
        {
            try
            {
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };

                if (!texture.LoadImage(System.IO.File.ReadAllBytes(file)))
                {
                    Log.Warning("Could not decode image " + Path.GetFileName(file));
                    return null;
                }

                return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                     new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Log.Warning("Failed to load image " + Path.GetFileName(file) + ": " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// The TP bar art is an empty frame: black outline, dark red interior, and "TP" already
        /// lettered on the left. The fill overlay is that same artwork with the red interior
        /// recoloured and everything else erased, so the fill follows the frame's slanted shape
        /// exactly instead of being an approximate rectangle laid over it.
        /// </summary>
        private static void BuildTpBar()
        {
            TpBarFrame = FromFile("TP_Bar_Empty");
            if (TpBarFrame == null)
            {
                Log.Warning("No Hud/TP_Bar_Empty image found - there will be no TP bar.");
                return;
            }

            try
            {
                Texture2D source = TpBarFrame.texture;
                Color32[] pixels = source.GetPixels32();
                Color32 fill = TpFillColor;
                var clear = new Color32(0, 0, 0, 0);

                for (int i = 0; i < pixels.Length; i++)
                {
                    Color32 pixel = pixels[i];
                    bool isInterior = pixel.a > 8 && pixel.r > pixel.g + 40 && pixel.r > pixel.b + 40;
                    pixels[i] = isInterior ? fill : clear;
                }

                var texture = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false)
                {
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, false);

                TpBarFill = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
                                          new Vector2(0.5f, 0.5f));
            }
            catch (Exception e)
            {
                Log.Warning("Could not build the TP bar fill overlay: " + e.Message);
            }
        }


        // ---------------------------------------------------------------------
        // Pixel digits for the TP readout.
        //
        // The bar artwork letters "TP" and "%" in a chunky pixel face; the HUD's
        // own TMP font next to them looks out of place. Drawing the number as
        // pixels with point filtering keeps it in the same visual language.
        // ---------------------------------------------------------------------

        private const int GlyphWidth = 5;
        private const int GlyphHeight = 7;
        private const int GlyphSpacing = 1;
        private const int GlyphScale = 8;

        /// <summary>
        /// Readouts are drawn into a canvas a fixed number of glyphs wide, centred, whatever the
        /// text. Sprites of differing aspect ratio get scaled differently by the layout - a lone
        /// "5" is tall and narrow, so it fits to height and comes out far larger than "48" - and
        /// a constant canvas keeps the glyphs one size. The number never reaches three digits
        /// because 100 reads as MAX instead.
        /// </summary>
        private const int NumberCanvasGlyphs = 2;

        /// <summary>Row bitmaps, most significant bit leftmost, five columns wide.</summary>
        private static readonly Dictionary<char, byte[]> Glyphs = new Dictionary<char, byte[]>
        {
            { '0', new byte[] { 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E } },
            { '1', new byte[] { 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E } },
            { '2', new byte[] { 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F } },
            { '3', new byte[] { 0x1F, 0x02, 0x04, 0x02, 0x01, 0x11, 0x0E } },
            { '4', new byte[] { 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02 } },
            { '5', new byte[] { 0x1F, 0x10, 0x1E, 0x01, 0x01, 0x11, 0x0E } },
            { '6', new byte[] { 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E } },
            { '7', new byte[] { 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08 } },
            { '8', new byte[] { 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E } },
            { '9', new byte[] { 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C } },
            { 'A', new byte[] { 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11 } },
            { 'C', new byte[] { 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E } },
            { 'T', new byte[] { 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04 } },
            { 'P', new byte[] { 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10 } },
        };

        private static readonly Dictionary<string, Sprite> TextSprites =
            new Dictionary<string, Sprite>();

        /// <summary>A white pixel-font sprite of <paramref name="value"/>, cached per value.</summary>
        internal static Sprite NumberSprite(int value)
        {
            return TextSprite(Mathf.Clamp(value, 0, 99).ToString(), NumberCanvasGlyphs);
        }

        internal static Sprite TextSprite(string text, int canvasGlyphs)
        {
            string key = canvasGlyphs + ":" + text;
            if (TextSprites.TryGetValue(key, out Sprite cached)) return cached;

            Sprite sprite = DrawText(text, canvasGlyphs);
            TextSprites[key] = sprite;
            return sprite;
        }


        private static Sprite DrawText(string text, int canvasGlyphs)
        {
            int columns = canvasGlyphs * GlyphWidth + (canvasGlyphs - 1) * GlyphSpacing;
            int width = columns * GlyphScale;
            int height = GlyphHeight * GlyphScale;

            var pixels = new Color32[width * height];
            var white = new Color32(255, 255, 255, 255);

            // Centre the text in the fixed canvas, rounding left so a two-glyph value sits
            // fractionally left of centre rather than drifting right.
            int used = text.Length * GlyphWidth + (text.Length - 1) * GlyphSpacing;
            int startColumn = (columns - used) / 2;

            for (int i = 0; i < text.Length; i++)
            {
                if (!Glyphs.TryGetValue(text[i], out byte[] glyph)) continue;
                int columnOffset = startColumn + i * (GlyphWidth + GlyphSpacing);

                for (int row = 0; row < GlyphHeight; row++)
                {
                    for (int column = 0; column < GlyphWidth; column++)
                    {
                        // Bit 4 is the leftmost column; rows run top-down but textures are
                        // bottom-up, so the row index is flipped on the way out.
                        if ((glyph[row] & (1 << (GlyphWidth - 1 - column))) == 0) continue;

                        int x0 = (columnOffset + column) * GlyphScale;
                        int y0 = (GlyphHeight - 1 - row) * GlyphScale;

                        for (int y = 0; y < GlyphScale; y++)
                        {
                            int rowStart = (y0 + y) * width + x0;
                            for (int x = 0; x < GlyphScale; x++) pixels[rowStart + x] = white;
                        }
                    }
                }
            }

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                // Point filtering, so scaling the sprite keeps hard pixel edges.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            return Sprite.Create(texture, new Rect(0f, 0f, width, height), new Vector2(0.5f, 0.5f));
        }
    }
}
