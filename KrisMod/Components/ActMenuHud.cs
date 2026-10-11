using KrisMod.Content;
using KrisMod.Modules;
using RoR2;
using RoR2.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace KrisMod.Components
{
    /// <summary>
    /// The ACT menu: the supplied DELTARUNE battle box (Hud/ActBox) above the skill bar, with
    /// the four ACTs on offer in a two-by-two grid, each with its key and its TP price.
    ///
    /// Opened and closed by <see cref="SkillStates.KrisActMenu"/>, on the authority only. Built
    /// from raw uGUI like the TP bar, so it needs nothing from the model bundle; text uses the
    /// game's own HGTextMeshProUGUI, in the DELTARUNE font from the small "krisfont" bundle
    /// (the HUD font if that is missing).
    /// </summary>
    public class ActMenuHud : MonoBehaviour
    {
        /// <summary>Wide enough that "[4] PETAL FEATHER" (the longest ACT) and a "100% TP" price fit
        /// side by side at the full name size: the font runs ~14.5 units a glyph at 22.</summary>
        private const float Width = 960f;
        private const float Height = 230f;
        private const float Border = 5f;

        /// <summary>ActBox is 2x pixel art; this draws each of its pixels 1.5 units, so a native
        /// pixel is 3 (crisp at 1080p). Its corner ornaments then reach 42 units in.</summary>
        private const float BoxPixel = 1.5f;

        /// <summary>The text and timer area, kept clear of the box's frame and corner ornaments.</summary>
        private static readonly Vector2 ContentInset = new Vector2(48f, 30f);
        private const float DenyFlash = 0.35f;

        /// <summary>Hud/ActHeart is 16px art: drawn 1:1, about the height of a name's capitals.</summary>
        private const float HeartSize = 16f;

        /// <summary>Every ACT name is drawn at this one size, never shrunk to fit.</summary>
        private const float NameSize = 22f;

        // Inside one column of the grid, in canvas units from its left edge.
        private const float HeartCell = 28f;
        private const float NameStart = 36f;
        private const float CostWidth = 110f;
        private const float ColumnGap = 24f;

        private static readonly Color Box = new Color(0f, 0f, 0f, 0.92f);
        private static readonly Color Affordable = Color.white;
        private static readonly Color TooExpensive = new Color(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color Denied = new Color(1f, 0.2f, 0.2f, 1f);

        private CharacterBody body;
        private GameObject root;
        private Transform content;
        private Image timerBar;

        private KrisAct[] options;
        private TensionController tension;
        private HGTextMeshProUGUI[] names;
        private HGTextMeshProUGUI[] costs;
        private float[] deniedUntil;
        private float openedAt;
        private int latchedKey = -1;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root);
        }

        internal void Show(KrisAct[] offered, TensionController meter)
        {
            Hide();

            HUD hud = FindOwnHud();
            if (hud == null)
            {
                Log.Warning("ACT menu: no HUD is targeting Kris, so the menu cannot be drawn. " +
                            "It will time out or can be cancelled with jump.");
                return;
            }

            options = offered;
            tension = meter;
            openedAt = Time.time;
            latchedKey = -1;
            Build(hud);
        }

        internal void Hide()
        {
            if (root != null) Destroy(root);
            root = null;
            content = null;
            options = null;
            latchedKey = -1;
        }

        /// <summary>Flashes an option the player cannot afford.</summary>
        internal void Deny(int index)
        {
            if (deniedUntil != null && index >= 0 && index < deniedUntil.Length) deniedUntil[index] = Time.time + DenyFlash;
        }

        /// <summary>The number key pressed since the last call, or -1. See KrisActMenu.PickedIndex.</summary>
        internal int TakeKeyPick()
        {
            int key = latchedKey;
            latchedKey = -1;
            return key;
        }

        private void Update()
        {
            if (root == null || options == null) return;

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1)) latchedKey = 0;
            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2)) latchedKey = 1;
            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3)) latchedKey = 2;
            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4)) latchedKey = 3;

            for (int i = 0; i < options.Length; i++)
            {
                bool canPay = tension != null && tension.CanSpend(options[i].Cost);
                Color color = Time.time < deniedUntil[i] ? Denied : canPay ? Affordable : TooExpensive;
                if (names[i] != null) names[i].color = color;
                if (costs[i] != null) costs[i].color = canPay ? KrisAssets.TpOrange : TooExpensive;
            }

            if (timerBar != null)
            {
                float timeout = Mathf.Max(1f, KrisTuning.ActMenuTimeout);
                timerBar.fillAmount = Mathf.Clamp01(1f - (Time.time - openedAt) / timeout);
            }
        }

        private HUD FindOwnHud()
        {
            var huds = HUD.readOnlyInstanceList;
            for (int i = 0; i < huds.Count; i++)
            {
                HUD hud = huds[i];
                if (hud != null && hud.targetBodyObject == gameObject) return hud;
            }
            return null;
        }

        private void Build(HUD hud)
        {
            Transform parent = hud.mainContainer != null ? hud.mainContainer.transform : hud.transform;

            root = new GameObject("KrisActMenu", typeof(RectTransform));
            root.transform.SetParent(parent, false);
            var rect = root.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 250f);
            rect.sizeDelta = new Vector2(Width, Height);

            if (KrisAssets.ActBox != null)
            {
                BoxArt(root.transform);
            }
            else
            {
                // No art dropped in: a white border, then the black box inset inside it.
                Panel("Border", root.transform, Color.white, 0f);
                Panel("Box", root.transform, Box, Border);
            }

            var contentObject = new GameObject("Content", typeof(RectTransform));
            contentObject.transform.SetParent(root.transform, false);
            var contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = Vector2.zero;
            contentRect.anchorMax = Vector2.one;
            contentRect.offsetMin = ContentInset;
            contentRect.offsetMax = -ContentInset;
            content = contentObject.transform;

            // Sizes are for the DELTARUNE font, which runs much wider than the HUD font at the same size.
            Text("Title", "* ACT", new Vector2(0f, 0.8f), new Vector2(0.5f, 1f), 24f, Affordable,
                 TextAlignmentOptions.Left);
            Text("Hint", "1-4 to pick  ·  Jump to cancel", new Vector2(0.45f, 0.8f), new Vector2(1f, 1f),
                 16f, TooExpensive, TextAlignmentOptions.Right);

            names = new HGTextMeshProUGUI[options.Length];
            costs = new HGTextMeshProUGUI[options.Length];
            deniedUntil = new float[options.Length];

            for (int i = 0; i < options.Length; i++)
            {
                int column = i % 2;
                int row = i / 2;
                float y1 = 0.76f - row * 0.35f;
                float y0 = y1 - 0.32f;

                // Column edges as fractions of the content width.
                float contentWidth = Width - 2f * ContentInset.x;
                float columnWidth = (contentWidth - ColumnGap) * 0.5f;
                float left = column * (columnWidth + ColumnGap);
                float x0 = left / contentWidth;
                float heartEnd = (left + HeartCell) / contentWidth;
                float nameStart = (left + NameStart) / contentWidth;
                float costStart = (left + columnWidth - CostWidth) / contentWidth;
                float costEnd = (left + columnWidth) / contentWidth;

                Heart(new Vector2(x0, y0 + 0.06f), new Vector2(heartEnd, y1 - 0.06f),
                      KrisAssets.SoulRed);

                KrisAct act = options[i];
                names[i] = Text("Name" + i, "[" + (i + 1) + "] " + Language.GetString(act.NameToken),
                                new Vector2(nameStart, y0), new Vector2(costStart, y1), NameSize, Affordable,
                                TextAlignmentOptions.Left);
                costs[i] = Text("Cost" + i, Mathf.RoundToInt(act.Cost) + "% TP",
                                new Vector2(costStart, y0), new Vector2(costEnd, y1), 20f, KrisAssets.TpOrange,
                                TextAlignmentOptions.Right);
            }

            // Time left before the menu closes on its own, as a bar that drains along the bottom.
            var timerObject = new GameObject("Timer", typeof(RectTransform), typeof(Image));
            timerObject.transform.SetParent(content, false);
            var timerRect = timerObject.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0f, 0f);
            timerRect.anchorMax = new Vector2(1f, 0.03f);
            timerRect.offsetMin = timerRect.offsetMax = Vector2.zero;
            timerBar = timerObject.GetComponent<Image>();
            timerBar.sprite = WhiteSprite();
            timerBar.color = KrisAssets.TpOrange;
            timerBar.type = Image.Type.Filled;
            timerBar.fillMethod = Image.FillMethod.Horizontal;
            timerBar.fillAmount = 1f;
            timerBar.raycastTarget = false;
        }

        private static Sprite white;

        /// <summary>A Filled image does not fill without a sprite, so the timer gets a plain white one.</summary>
        private static Sprite WhiteSprite()
        {
            if (white != null) return white;
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            white = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            return white;
        }

        private void Panel(string name, Transform parent, Color color, float inset)
        {
            var panel = new GameObject(name, typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            var rect = panel.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(inset, inset);
            rect.offsetMax = new Vector2(-inset, -inset);
            var image = panel.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void BoxArt(Transform parent)
        {
            var art = new GameObject("Box", typeof(RectTransform), typeof(Image));
            art.transform.SetParent(parent, false);
            var rect = art.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = art.GetComponent<Image>();
            image.sprite = KrisAssets.ActBox;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            // A sprite unit is one canvas unit at the default 100 ppu, so this scales the slices.
            image.pixelsPerUnitMultiplier = 1f / BoxPixel;
            image.raycastTarget = false;
        }

        private void Heart(Vector2 min, Vector2 max, Color color)
        {
            var heart = new GameObject("Heart", typeof(RectTransform), typeof(Image));
            heart.transform.SetParent(content, false);
            var rect = heart.GetComponent<RectTransform>();
            // Centred in its cell at a whole multiple of the art, so every pixel of it draws the same size.
            rect.anchorMin = rect.anchorMax = (min + max) * 0.5f;
            rect.sizeDelta = new Vector2(HeartSize, HeartSize);
            var image = heart.GetComponent<Image>();
            image.sprite = KrisAssets.ActHeart;
            image.preserveAspect = true;
            image.color = color;
            image.raycastTarget = false;
        }

        private HGTextMeshProUGUI Text(string name, string text, Vector2 min, Vector2 max, float size, Color color,
                                       TextAlignmentOptions alignment)
        {
            var textObject = new GameObject(name, typeof(RectTransform));
            textObject.transform.SetParent(content, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;

            var label = textObject.AddComponent<HGTextMeshProUGUI>();
            // DELTARUNE's own text font. Off the language default, or a language change puts the
            // HUD font back.
            TMP_FontAsset font = KrisAssets.ActFont(label.font);
            if (font != null)
            {
                label.useLanguageDefaultFont = false;
                label.font = font;
            }
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.enableWordWrapping = false;
            label.raycastTarget = false;
            return label;
        }
    }
}
