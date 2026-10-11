using KrisMod.Modules;
using RoR2;
using RoR2.UI;
using UnityEngine;
using UnityEngine.UI;

namespace KrisMod.Components
{
    /// <summary>
    /// Kris's TP meter, drawn down the left edge of the HUD from the supplied bar artwork.
    ///
    /// The art is an empty frame: black outline, dark red interior, and "TP" already lettered
    /// into it. The fill is the same artwork recoloured (see KrisAssets.BuildTpBar), revealed
    /// bottom-up with a Filled image so it follows the frame's slanted interior exactly. The
    /// "%" and the "MAX" that replaces it are supplied artwork too; only the number itself is
    /// drawn in code, in a matching pixel face.
    ///
    /// Built from raw uGUI rather than a prefab so it needs no asset bundle, and it attaches
    /// itself by looking for the HUD that is already targeting this body.
    /// </summary>
    public class TensionHud : MonoBehaviour
    {
        // Height and left margin are configurable: the bar shares the left edge with the chat
        // and the pickup log, and how much room those need depends on resolution and taste.

        // Layout inside the artwork, in normalised bar coordinates.
        //
        // The art is 172x587 and letters only "T" (y 109-150) and "P" (y 163-204), measured from
        // the top; everything below that in the left column is ours to fill. The number sits
        // under the P with the "%" beneath it, and at a full bar both are replaced by the
        // vertical "MAX" artwork spanning the pair. Anchors measure up from the bottom,
        // hence 587 - y.
        private const float ArtWidth = 172f;
        private const float ArtHeight = 587f;

        private const float NumberLeft = 15f / ArtWidth;
        private const float NumberRight = 83f / ArtWidth;
        private const float NumberBottom = (ArtHeight - 296f) / ArtHeight;
        private const float NumberTop = (ArtHeight - 226f) / ArtHeight;

        private const float PercentLeft = 20f / ArtWidth;
        private const float PercentRight = 59f / ArtWidth;
        private const float PercentBottom = (ArtHeight - 360f) / ArtHeight;
        private const float PercentTop = (ArtHeight - 312f) / ArtHeight;

        // MAX is supplied as tall vertical lettering, so it gets a narrow box in the same column
        // as the "TP", covering the space the number and the "%" share between them.
        private const float MaxLeft = 15f / ArtWidth;
        private const float MaxRight = 64f / ArtWidth;
        private const float MaxBottom = (ArtHeight - 362f) / ArtHeight;
        private const float MaxTop = (ArtHeight - 222f) / ArtHeight;

        private static readonly Color IdleTextColor = new Color(0.92f, 0.92f, 0.92f, 0.95f);

        private CharacterBody body;
        private TensionController tension;

        private GameObject root;
        private Image frameImage;
        private Image fillImage;
        private Image numberImage;
        private Image percentImage;
        private Image maxImage;
        private int shownPercent = -1;
        private float searchTimer;

        private void Awake()
        {
            body = GetComponent<CharacterBody>();
            tension = GetComponent<TensionController>();
        }

        private void OnDestroy()
        {
            if (root != null) Destroy(root);
        }

        private void Update()
        {
            if (body == null || tension == null) return;

            if (root == null)
            {
                searchTimer -= Time.deltaTime;
                if (searchTimer > 0f) return;
                searchTimer = 0.5f;

                HUD hud = FindOwnHud();
                if (hud == null) return;

                Build(hud);
                if (root == null) return;
            }

            Refresh();
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

        private void Refresh()
        {
            float fraction = tension.Fraction;

            if (fillImage != null) fillImage.fillAmount = fraction;

            int percent = Mathf.Clamp(Mathf.FloorToInt(fraction * 100f), 0, 100);
            bool atMax = percent >= 100;

            if (percent != shownPercent)
            {
                shownPercent = percent;

                // A full bar reads "MAX" across the space the number and the "%" share, so the
                // two swap places rather than sitting alongside each other.
                if (numberImage != null)
                {
                    numberImage.enabled = !atMax;
                    if (!atMax) numberImage.sprite = KrisAssets.NumberSprite(percent);
                }
                if (percentImage != null) percentImage.enabled = !atMax && percentImage.sprite != null;
                if (maxImage != null) maxImage.enabled = atMax && maxImage.sprite != null;
            }
        }

        private void Build(HUD hud)
        {
            // Missing artwork is reported once at startup (KrisAssets.BuildTpBar).
            Sprite frame = KrisAssets.TpBarFrame;
            if (frame == null)
            {
                enabled = false;
                return;
            }

            Transform parent = hud.mainContainer != null ? hud.mainContainer.transform : hud.transform;

            float barHeight = Mathf.Max(1f, KrisTuning.TpBarHeight);
            float width = barHeight * (frame.rect.width / frame.rect.height);

            root = new GameObject("KrisTensionBar", typeof(RectTransform));
            root.transform.SetParent(parent, false);

            var rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 0.5f);
            rootRect.anchorMax = new Vector2(0f, 0.5f);
            rootRect.pivot = new Vector2(0f, 0.5f);
            rootRect.anchoredPosition = new Vector2(KrisTuning.TpBarLeftMargin, 0f);
            rootRect.sizeDelta = new Vector2(width, barHeight);

            BuildFromArtwork(frame);
            BuildValueLabel();
        }

        private void BuildFromArtwork(Sprite frame)
        {
            var frameObject = new GameObject("Frame", typeof(RectTransform), typeof(Image));
            frameObject.transform.SetParent(root.transform, false);
            Stretch(frameObject.GetComponent<RectTransform>());

            frameImage = frameObject.GetComponent<Image>();
            frameImage.sprite = frame;
            frameImage.raycastTarget = false;
            frameImage.preserveAspect = true;

            Sprite fill = KrisAssets.TpBarFill;
            if (fill == null) return;

            var fillObject = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillObject.transform.SetParent(root.transform, false);
            Stretch(fillObject.GetComponent<RectTransform>());

            fillImage = fillObject.GetComponent<Image>();
            fillImage.sprite = fill;
            fillImage.raycastTarget = false;
            fillImage.preserveAspect = true;
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Vertical;
            fillImage.fillOrigin = (int)Image.OriginVertical.Bottom;
            fillImage.fillAmount = 0f;
        }

        /// <summary>
        /// The readout is drawn as images, not text: the same pixel face as the "TP" lettered
        /// into the artwork, which the HUD's own font would clash with.
        /// </summary>
        private void BuildValueLabel()
        {
            numberImage = BuildGlyph("Number", KrisAssets.NumberSprite(0), IdleTextColor,
                                     NumberLeft, NumberRight, NumberBottom, NumberTop);
            // Both arrive as artwork and are drawn untinted - MAX already carries its own yellow.
            percentImage = BuildGlyph("Percent", KrisAssets.TpPercent, Color.white,
                                      PercentLeft, PercentRight, PercentBottom, PercentTop);
            maxImage = BuildGlyph("Max", KrisAssets.TpMax, Color.white,
                                  MaxLeft, MaxRight, MaxBottom, MaxTop);

            // Either may be absent if the file is missing; an Image with no sprite draws a
            // white box, so keep those switched off.
            if (percentImage != null) percentImage.enabled = percentImage.sprite != null;
            if (maxImage != null) maxImage.enabled = false;
        }

        private Image BuildGlyph(string name, Sprite sprite, Color color, float left, float right,
                                 float bottom, float top)
        {
            var glyphObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            glyphObject.transform.SetParent(root.transform, false);

            var rect = glyphObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = glyphObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            image.color = color;
            return image;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
