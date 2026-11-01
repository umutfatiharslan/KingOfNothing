using System.Collections.Generic;
using KingOfNothing.Utilities;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.Units
{
    /// <summary>A living thing that can suffer status effects (characters, animals).</summary>
    public interface IStatusHost
    {
        /// <summary>Damage from a status effect (bleeding). Never kills.</summary>
        void TakeStatusDamage(int amount);
        /// <summary>Unshakable (Defense 20): no status effect sticks.</summary>
        bool ImmuneToStatus { get; }
        /// <summary>Height of the icons above the feet.</summary>
        float StatusIconHeight { get; }
    }

    /// <summary>
    /// Status effects of a character or an animal, with icons above the head:
    ///  - stun (War Hammer): cannot move or act for a few seconds, yellow stars;
    ///  - bleeding (axe fights): loses health every game hour up to a total, red drop;
    ///  - broken defense (Pickaxe Blow): defense points broken, back after a game hour without new hits, cracked shield.
    /// Added on demand.
    /// </summary>
    [DisallowMultipleComponent]
    public class StatusEffects : MonoBehaviour
    {
        float stunUntil;
        int bleedLeft;
        int bleedPerHour;
        float nextBleedAt;
        int armorBroken;
        float armorBackAt;
        IStatusHost host;
        readonly List<SpriteRenderer> icons = new List<SpriteRenderer>();

        public bool IsStunned => Time.time < stunUntil;
        public bool IsBleeding => bleedLeft > 0;
        /// <summary>Defense points currently broken.</summary>
        public int ArmorBroken => armorBroken;
        public bool Any => IsStunned || IsBleeding || armorBroken > 0;

        public static StatusEffects Of(Component c)
        {
            if (c == null) return null;
            var s = c.GetComponent<StatusEffects>();
            return s != null ? s : c.gameObject.AddComponent<StatusEffects>();
        }

        void Awake() => host = GetComponent<IStatusHost>();

        bool Immune => host != null && host.ImmuneToStatus;

        public bool Stun(float seconds)
        {
            if (Immune || seconds <= 0f || IsStunned) return false;
            stunUntil = Time.time + seconds;
            var unit = GetComponent<Unit>();
            if (unit != null) unit.Stop();
            return true;
        }

        public bool Bleed(int perHour, int total)
        {
            if (Immune || perHour <= 0 || total <= 0) return false;
            // A new wound starts the bleeding again, never beyond the total
            bleedPerHour = perHour;
            bleedLeft = total;
            nextBleedAt = GameClock.Now + 1f;
            return true;
        }

        public bool BreakArmor(int points, int maxPoints, float hours)
        {
            if (Immune || points <= 0) return false;
            armorBroken = Mathf.Min(maxPoints, armorBroken + points);
            armorBackAt = GameClock.Now + Mathf.Max(0.01f, hours);
            return true;
        }

        /// <summary>Purification: everything is gone.</summary>
        public void ClearAll()
        {
            stunUntil = 0f;
            bleedLeft = 0;
            armorBroken = 0;
        }

        void Update()
        {
            if (Immune && Any) ClearAll();
            float now = GameClock.Now;
            if (bleedLeft > 0 && now >= nextBleedAt)
            {
                nextBleedAt = now + 1f;
                int amount = Mathf.Min(bleedPerHour, bleedLeft);
                bleedLeft -= amount;
                host?.TakeStatusDamage(amount);
            }
            if (armorBroken > 0 && now >= armorBackAt) armorBroken = 0;
            UpdateIcons();
        }

        void UpdateIcons()
        {
            int shown = 0;
            float height = host != null ? host.StatusIconHeight : 1.3f;
            var unit = GetComponent<Unit>();
            bool visible = unit == null || !unit.IsHidden;
            if (visible && IsStunned)
            {
                // Three stars circling above the head
                for (int i = 0; i < 3; i++)
                {
                    float a = Time.time * 4f + i * Mathf.PI * 2f / 3f;
                    Icon(shown++, ProceduralSprites.Star, new Color(1f, 0.9f, 0.3f), new Vector2(Mathf.Cos(a) * 0.25f, height + Mathf.Sin(a) * 0.08f), 0.35f);
                }
            }
            float x = -0.15f * ((IsBleeding ? 1 : 0) + (armorBroken > 0 ? 1 : 0) - 1);
            if (visible && IsBleeding)
            {
                Icon(shown++, ProceduralSprites.Drop, new Color(0.85f, 0.1f, 0.1f), new Vector2(x, height + 0.25f), 0.4f);
                x += 0.3f;
            }
            if (visible && armorBroken > 0)
                Icon(shown++, ProceduralSprites.CrackedShield, new Color(0.7f, 0.8f, 1f), new Vector2(x, height + 0.25f), 0.4f);
            for (int i = shown; i < icons.Count; i++) icons[i].enabled = false;
        }

        void Icon(int index, Sprite sprite, Color color, Vector2 local, float scale)
        {
            while (icons.Count <= index)
            {
                var go = new GameObject("Status Icon");
                go.transform.SetParent(transform, false);
                var r = go.AddComponent<SpriteRenderer>();
                r.sortingOrder = short.MaxValue - 15;
                icons.Add(r);
            }
            var icon = icons[index];
            icon.enabled = true;
            icon.sprite = sprite;
            icon.color = color;
            icon.transform.localPosition = local;
            icon.transform.localScale = new Vector3(scale, scale, 1f);
        }
    }
}
