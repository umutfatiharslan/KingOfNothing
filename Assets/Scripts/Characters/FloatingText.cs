using System.Collections.Generic;
using KingOfNothing.Utilities;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// A short text with an icon above a character's head (experience: "+2 Woodcutting XP" with the green up arrow,
    /// level ups, milestones...). It rises slowly, stays for CharacterSettings.popupSeconds (3 s) and then fades
    /// out over popupFadeSeconds (2 s). Several popups of the same character stack upwards.
    /// </summary>
    public class FloatingText : MonoBehaviour
    {
        static readonly Dictionary<Transform, List<FloatingText>> Active = new Dictionary<Transform, List<FloatingText>>();
        const float LineHeight = 0.28f;

        Transform owner;
        TextMesh text;
        MeshRenderer textRenderer;
        SpriteRenderer icon;
        float age, life, fade, rise;
        Color color;
        float baseHeight;
        int slot;

        public static void Show(Transform owner, string message, Color color, Sprite iconSprite = null)
        {
            if (owner == null || string.IsNullOrEmpty(message)) return;
            var settings = CharacterSettings.Current;
            if (!Active.TryGetValue(owner, out var list))
            {
                list = new List<FloatingText>();
                Active[owner] = list;
            }
            list.RemoveAll(p => p == null);
            int free = 0;
            while (list.Exists(p => p.slot == free)) free++;

            var go = new GameObject("Popup");
            var popup = go.AddComponent<FloatingText>();
            popup.owner = owner;
            popup.slot = free;
            popup.color = color;
            popup.life = settings.popupSeconds;
            popup.fade = settings.popupFadeSeconds;
            popup.rise = settings.popupRiseSpeed;
            popup.baseHeight = settings.popupHeight;
            popup.Build(message, iconSprite, settings);
            list.Add(popup);
            popup.Place();
        }

        void Build(string message, Sprite iconSprite, CharacterSettings settings)
        {
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(transform, false);
            text = textGo.AddComponent<TextMesh>();
            text.text = message;
            text.anchor = TextAnchor.MiddleLeft;
            text.alignment = TextAlignment.Left;
            text.characterSize = settings.popupCharacterSize;
            text.fontSize = 48;
            text.fontStyle = FontStyle.Bold;
            text.color = color;
            textRenderer = textGo.GetComponent<MeshRenderer>();
            textRenderer.sortingOrder = short.MaxValue - 5;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                text.font = font;
                textRenderer.sharedMaterial = font.material;
            }

            var iconGo = new GameObject("Icon");
            iconGo.transform.SetParent(transform, false);
            icon = iconGo.AddComponent<SpriteRenderer>();
            icon.sprite = iconSprite != null ? iconSprite : ProceduralSprites.UpArrow;
            icon.color = iconSprite != null ? Color.white : color;
            icon.sortingOrder = short.MaxValue - 5;
            // About the height of a text line
            float size = icon.sprite.bounds.size.y;
            float scale = size > 0.001f ? 0.26f / size : 1f;
            iconGo.transform.localScale = new Vector3(scale, scale, 1f);

            // Centre the icon + text over the head
            float textWidth = textRenderer.bounds.size.x;
            if (textWidth < 0.01f) textWidth = message.Length * settings.popupCharacterSize * 48f * 0.055f;
            float width = 0.3f + textWidth;
            iconGo.transform.localPosition = new Vector3(-width / 2f + 0.13f, 0f, 0f);
            textGo.transform.localPosition = new Vector3(-width / 2f + 0.3f, 0f, 0f);
        }

        void Place()
        {
            if (owner == null) return;
            transform.position = owner.position + new Vector3(0f, baseHeight + slot * LineHeight + age * rise, 0f);
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            if (owner == null || age >= life + fade)
            {
                Destroy(gameObject);
                return;
            }
            Place();
            float alpha = age <= life ? 1f : 1f - (age - life) / Mathf.Max(0.01f, fade);
            var c = new Color(color.r, color.g, color.b, alpha);
            text.color = c;
            var ic = icon.color;
            icon.color = new Color(ic.r, ic.g, ic.b, alpha);
            // Hidden inside a building: hide the popup too
            var unit = owner.GetComponent<KingOfNothing.Units.Unit>();
            bool visible = unit == null || !unit.IsHidden;
            textRenderer.enabled = visible;
            icon.enabled = visible;
        }

        void OnDestroy()
        {
            if (owner != null && Active.TryGetValue(owner, out var list))
            {
                list.Remove(this);
                if (list.Count == 0) Active.Remove(owner);
            }
        }
    }
}
