using KrisMod.Modules;
using RoR2;
using UnityEngine;
using UnityEngine.UI;

namespace KrisMod.Components
{
    /// <summary>
    /// Kris's SOUL as a thing in the world: a red heart that always faces the camera, with a
    /// tether back to Kris's chest.
    ///
    /// Purely cosmetic and purely local - every client that runs the SOUL states builds its own,
    /// positioned from state that is already shared (the aim, and the serialized target of the
    /// pull), so nothing about it is networked.
    ///
    /// Drawn with a world-space canvas rather than a SpriteRenderer: RoR2 ships no guarantee that
    /// Unity's sprite shader survived stripping, while the UI shader has to exist for the HUD to
    /// draw at all.
    /// </summary>
    public class SoulVisual : MonoBehaviour
    {
        private const float WorldSize = 0.55f;

        /// <summary>Half the heart, so one resting on a surface can sit on it rather than in it.</summary>
        internal const float Radius = WorldSize / 2f;
        private const float CanvasPixels = 64f;

        private LineRenderer tether;
        private Transform anchor;
        private Image image;
        private float pulse;

        private bool absorbing;
        private Transform absorbInto;
        private Vector3 absorbFrom;
        private float absorbScale;
        private float absorbTime;
        private float absorbAge;

        /// <summary>Builds a SOUL at <paramref name="position"/>. <paramref name="alpha"/> below 1 draws a ghost.</summary>
        internal static SoulVisual Create(Vector3 position, float alpha = 1f, float scale = 1f)
        {
            var root = new GameObject("KrisSoul");
            root.transform.position = position;

            var canvasObject = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(root.transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(CanvasPixels, CanvasPixels);
            // Canvas pixels to metres.
            canvasObject.transform.localScale = Vector3.one * (WorldSize * scale / CanvasPixels);

            var imageObject = new GameObject("Heart", typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(canvasObject.transform, false);
            var imageRect = imageObject.GetComponent<RectTransform>();
            imageRect.anchorMin = Vector2.zero;
            imageRect.anchorMax = Vector2.one;
            imageRect.offsetMin = Vector2.zero;
            imageRect.offsetMax = Vector2.zero;

            var soul = root.AddComponent<SoulVisual>();
            soul.image = imageObject.GetComponent<Image>();
            soul.image.sprite = KrisAssets.SoulHeart;
            soul.image.raycastTarget = false;
            soul.image.color = new Color(1f, 1f, 1f, alpha);
            return soul;
        }

        /// <summary>Draws a line from <paramref name="from"/> to the SOUL; null removes it.</summary>
        internal void SetTether(Transform from)
        {
            anchor = from;
            if (from == null)
            {
                if (tether != null) tether.enabled = false;
                return;
            }

            if (tether == null)
            {
                tether = gameObject.AddComponent<LineRenderer>();
                tether.material = Canvas.GetDefaultCanvasMaterial();
                tether.positionCount = 2;
                tether.startWidth = 0.06f;
                tether.endWidth = 0.03f;
                tether.useWorldSpace = true;
                tether.numCapVertices = 2;
                tether.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tether.receiveShadows = false;
                Color red = KrisAssets.SoulRed;
                tether.startColor = new Color(red.r, red.g, red.b, 0.25f);
                tether.endColor = new Color(red.r, red.g, red.b, 0.9f);
            }
            tether.enabled = true;
        }

        /// <summary>
        /// Draws the SOUL back into <paramref name="into"/>, shrinking to nothing over
        /// <paramref name="seconds"/>, then removes it. The heart outlives the state that let it
        /// go, so that state can end without the heart blinking out.
        /// </summary>
        internal void Absorb(Transform into, float seconds)
        {
            if (seconds <= 0f)
            {
                Remove();
                return;
            }

            SetTether(null);
            absorbing = true;
            absorbInto = into;
            absorbFrom = transform.position;
            absorbScale = transform.localScale.x;
            absorbTime = seconds;
            absorbAge = 0f;
        }

        private void LateUpdate()
        {
            if (absorbing)
            {
                absorbAge += Time.deltaTime;
                float t = Mathf.Clamp01(absorbAge / absorbTime);
                if (t >= 1f)
                {
                    Destroy(gameObject);
                    return;
                }

                // Slow off the hand, quick into the chest. A chest that is gone (Kris died) leaves
                // the heart to shrink where it is.
                float e = t * t;
                if (absorbInto != null) transform.position = Vector3.Lerp(absorbFrom, absorbInto.position, e);
                transform.localScale = Vector3.one * (absorbScale * (1f - e));
            }

            Camera camera = FindCamera();
            if (camera != null)
            {
                // Billboard: the canvas faces the same way as the camera, so the heart reads flat.
                transform.rotation = Quaternion.LookRotation(camera.transform.forward, camera.transform.up);
            }

            // A slow heartbeat, so a SOUL held in place still looks alive. A ghost (alpha below 1)
            // is only a preview of where the SOUL will be, so it holds still.
            if (image != null && image.color.a >= 1f)
            {
                pulse += Time.deltaTime * 6f;
                float beat = 1f + 0.08f * Mathf.Max(0f, Mathf.Sin(pulse));
                image.rectTransform.localScale = Vector3.one * beat;
            }

            if (tether != null && tether.enabled && anchor != null)
            {
                tether.SetPosition(0, anchor.position);
                tether.SetPosition(1, transform.position);
            }
        }

        private static Camera FindCamera()
        {
            var rigs = CameraRigController.readOnlyInstancesList;
            for (int i = 0; i < rigs.Count; i++)
            {
                if (rigs[i] != null && rigs[i].sceneCam != null) return rigs[i].sceneCam;
            }
            return Camera.main;
        }

        internal void Remove()
        {
            if (this != null && gameObject != null) Destroy(gameObject);
        }
    }
}
