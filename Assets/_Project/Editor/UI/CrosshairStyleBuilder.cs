using System.Text;
using Invector.vShooter;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace OpenworldCrime.EditorTools
{
    /// <summary>
    /// Restyles the Invector aim canvas inside Player_Jason.prefab into a small GTA-style reticle: four short ticks around a
    /// small gap (about 22 px across at 1080p) with a dot at the bullet's aim point, a thin dark outline for contrast, and a
    /// modest spread while moving (Invector grows the gap with movement / mouse look).
    /// - The canvas scaler follows the screen height, so the reticle has the same pixel size on 16:9 monitors and wide phones
    ///   (it followed the width before: 2.4x at 1920x1080 and 3x on a 2400x1080 phone).
    /// - Only sizes, positions and colours of Invector's own crosshair objects change (plus one dot image on the handgun), so the
    ///   vAimCanvas events (show / hide, blocked aim) keep working. The shotgun and RPG reticles are left as they are (no such
    ///   weapons in the project).
    /// Re-runnable. Also run by GameplayUIBuilder. Menu: Tools > OpenworldCrime > Player > Style Crosshair.
    /// </summary>
    public static class CrosshairStyleBuilder
    {
        private const string PlayerPrefabPath = "Assets/_Project/Prefabs/Characters/Player_Jason.prefab";
        private const string AimCanvasPath = "Invector Components/AimCanvas/AimCanvas";
        private const string DotName = "OWC_Dot";

        // Canvas units: 600 units = screen height, so 1 unit = 1.8 px at 1080p.
        private const float Gap = 5f;            // empty square in the middle (Invector scales this with movement)
        private const float TickLength = 3.5f;
        private const float TickThickness = 1.1f;
        private const float DotSize = 1.6f;
        private const float BlockedScale = 1.6f; // red X shown when something blocks the shot
        private const float MovementSpread = 1.4f; // Invector default 2
        private static readonly Color OutlineColor = new Color(0f, 0f, 0f, 0.45f);

        [MenuItem("Tools/OpenworldCrime/Player/Style Crosshair")]
        public static void StyleMenu() => Debug.Log(Style());

        public static string Style()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            var log = new StringBuilder("[CrosshairStyleBuilder]\n");
            try
            {
                Transform aimCanvas = root.transform.Find(AimCanvasPath);
                if (aimCanvas == null) return $"[CrosshairStyleBuilder] '{AimCanvasPath}' not found in {PlayerPrefabPath}.";

                var scaler = aimCanvas.GetComponent<CanvasScaler>();
                if (scaler != null)
                {
                    scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                    scaler.matchWidthOrHeight = 1f;
                }

                Sprite dotSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
                foreach (vAimCanvas aim in aimCanvas.GetComponentsInChildren<vAimCanvas>(true))
                {
                    aim.scaleWithMovement = MovementSpread;
                    int styled = 0;
                    if (StyleReticle(aim.aimCenter)) styled++;
                    if (StyleReticle(aim.aimTarget))
                    {
                        styled++;
                        EnsureDot(aim.aimTarget, dotSprite);
                    }
                    else if (IsPlainDot(aim.aimTarget))
                    {
                        StyleDot(aim.aimTarget);
                    }
                    log.AppendLine($"  {aim.name}: {styled} reticle(s) restyled, spread x{MovementSpread}");
                }

                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            log.AppendLine("  Player_Jason.prefab saved (aim canvas scales with screen height).");
            return log.ToString();
        }

        // A reticle is a rect whose children are Invector's "CrossHairLine" holders (one tick image each, anchored to an edge).
        private static bool StyleReticle(RectTransform reticle)
        {
            if (reticle == null) return false;
            bool any = false;
            foreach (Transform child in reticle)
            {
                if (child.name.Contains("Line"))
                {
                    StyleTick((RectTransform)child);
                    any = true;
                }
                else if (child.name.StartsWith("AimBlocked"))
                {
                    child.localScale = new Vector3(BlockedScale, BlockedScale, 1f);
                }
            }
            if (!any) return false;
            reticle.sizeDelta = new Vector2(Gap, Gap);
            reticle.localScale = Vector3.one;
            return true;
        }

        private static void StyleTick(RectTransform holder)
        {
            // Holder: zero-size point on the middle of one edge of the gap square; the tick points away from the centre.
            Vector2 outward = new Vector2(Mathf.Round((holder.anchorMin.x - 0.5f) * 2f), Mathf.Round((holder.anchorMin.y - 0.5f) * 2f));
            holder.pivot = new Vector2(0.5f, 0.5f);
            holder.sizeDelta = Vector2.zero;
            holder.anchoredPosition = Vector2.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;

            bool first = true;
            foreach (Transform part in holder)
            {
                var image = part.GetComponent<Graphic>();
                if (image == null) continue;
                if (!first)
                {
                    part.gameObject.SetActive(false); // Invector's small end caps
                    continue;
                }
                first = false;
                part.gameObject.SetActive(true);
                var rect = (RectTransform)part;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
                rect.localRotation = Quaternion.identity;
                rect.localScale = Vector3.one;
                rect.sizeDelta = Mathf.Abs(outward.x) > 0.5f ? new Vector2(TickLength, TickThickness) : new Vector2(TickThickness, TickLength);
                rect.anchoredPosition = outward * (TickLength * 0.5f);
                AddOutline(image);
            }
        }

        private static bool IsPlainDot(RectTransform target) => target != null && target.childCount == 0 && target.GetComponent<Image>() != null;

        private static void StyleDot(RectTransform dot)
        {
            dot.sizeDelta = new Vector2(DotSize, DotSize);
            dot.localScale = Vector3.one;
            AddOutline(dot.GetComponent<Graphic>());
        }

        // The handgun reticle has no dot of its own: one is added at its centre (it follows the aim point with the reticle).
        private static void EnsureDot(RectTransform reticle, Sprite sprite)
        {
            Transform existing = reticle.Find(DotName);
            Image image = existing != null ? existing.GetComponent<Image>() : null;
            if (image == null)
            {
                var go = new GameObject(DotName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                go.layer = reticle.gameObject.layer;
                go.transform.SetParent(reticle, false);
                image = go.GetComponent<Image>();
            }
            image.sprite = sprite;
            image.color = Color.white;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            StyleDot(rect);
        }

        private static void AddOutline(Graphic graphic)
        {
            if (graphic == null) return;
            graphic.raycastTarget = false;
            var outline = graphic.GetComponent<Outline>();
            if (outline == null) outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectColor = OutlineColor;
            outline.effectDistance = new Vector2(0.5f, -0.5f);
            outline.useGraphicAlpha = true;
        }
    }
}
