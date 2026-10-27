using System;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// A map object with health that can be hit: trees, stones, gold stones, bushes, pumpkins, signposts...
    /// Every hit removes "power" health (villagers hit for 10 until stats exist). At 0 health it drops its items
    /// (wood, gold, stones... in piles, see SpawnableDefinition.Drops) and disappears (trees leave their stump).
    /// Objects with health -1 cannot be hit: they are collected directly (inventory comes later).
    /// It shakes on every hit and can switch to other frames after the first hit (gold loses its highlight).
    /// </summary>
    public class ResourceNode : WorldObject, IDamageable
    {
        [Header("Resource")]
        [SerializeField] ResourceType resourceType = ResourceType.None;
        [Min(1)] [SerializeField] int maxHealth = 20;
        [SerializeField] int health = 20;
        [Min(0)] [SerializeField] int yieldAmount = 1;
        [SerializeField] bool collectable;

        /// <summary>Raised for every resource node when it is depleted: (node, resource, amount).</summary>
        public static event Action<ResourceNode, ResourceType, int> AnyDepleted;
        /// <summary>Raised for every health change of any node: (node, health before, health after).</summary>
        public static event Action<ResourceNode, int, int> AnyHealthChanged;

        public event Action<ResourceNode, int> Hit;
        public event Action<ResourceNode> Depleted;

        bool showsDamagedFrames;

        public ResourceType ResourceType => resourceType;
        public int MaxHealth => maxHealth;
        public int Health => health;
        public int YieldAmount => yieldAmount;
        public bool IsDepleted => health <= 0;
        /// <summary>Health -1 in the asset: picked up directly, never hit.</summary>
        public bool IsCollectable => collectable;
        public virtual bool CanBeHit => !collectable && !IsDepleted;

        public override void Initialize(SpawnableDefinition source, Vector2Int originCell, Vector2 cellOffset, MapData map)
        {
            base.Initialize(source, originCell, cellOffset, map);
            if (source == null) return;
            resourceType = source.ResourceType;
            collectable = source.IsCollectable;
            maxHealth = Mathf.Max(1, source.MaxHealth);
            health = maxHealth;
            yieldAmount = source.YieldAmount;
        }

        /// <summary>Number of hits a worker with the given damage needs (int.MaxValue if it is 0).</summary>
        public int HitsNeeded(int damage) => damage <= 0 ? int.MaxValue : Mathf.CeilToInt(health / (float)damage);

        public virtual void ApplyHit(int power, GameObject instigator)
        {
            if (!CanBeHit || power <= 0) return;
            int before = health;
            health = Mathf.Max(0, health - power);
            UpdateDamagedLook();
            HitShake.Play(this);
            HealthBar.Notify(this);
            AnyHealthChanged?.Invoke(this, before, health);
            Hit?.Invoke(this, power);
            if (health == 0) Deplete(instigator);
        }

        protected virtual void Deplete(GameObject instigator)
        {
            Depleted?.Invoke(this);
            AnyDepleted?.Invoke(this, resourceType, yieldAmount);

            // Remember everything before the object is gone, then let it go (trees leave their stump) and drop the items
            var definition = Definition;
            var area = Area;
            var parent = transform.parent;
            var map = MapData.Instance;
            OnDepleted(instigator);
            if (definition != null && map != null) ItemDropper.Drop(definition.Drops, area, parent, map);
        }

        /// <summary>
        /// Sets the health directly, without hit effects or depletion (used when loading the edited map and by
        /// undo / redo). Clamped to 0..MaxHealth.
        /// </summary>
        public void SetHealth(int value, bool notify = true)
        {
            int before = health;
            health = Mathf.Clamp(value, 0, maxHealth);
            UpdateDamagedLook();
            if (notify && before != health) AnyHealthChanged?.Invoke(this, before, health);
        }

        /// <summary>After the first hit some objects look different for good (gold stones lose their highlight).</summary>
        void UpdateDamagedLook()
        {
            var definition = Definition;
            if (definition == null || definition.DamagedFrames == null || definition.DamagedFrames.Length == 0) return;
            bool damaged = health < maxHealth;
            if (damaged == showsDamagedFrames) return;
            showsDamagedFrames = damaged;
            var frames = damaged ? definition.DamagedFrames : definition.Frames;
            if (frames == null || frames.Length == 0 || frames[0] == null) return;

            var spriteRenderer = Renderers.Length > 0 ? Renderers[0] : null;
            if (spriteRenderer == null) return;
            var animator = spriteRenderer.GetComponent<SpriteSheetAnimator>();
            spriteRenderer.sprite = frames[0];
            // The animator turns itself on when the object becomes visible, so it always gets the current frames
            if (animator == null && frames.Length > 1) animator = spriteRenderer.gameObject.AddComponent<SpriteSheetAnimator>();
            if (animator != null) animator.Setup(frames, definition.AnimationFps);
        }

        /// <summary>What happens to the object itself. Default: it disappears.</summary>
        protected virtual void OnDepleted(GameObject instigator) => Remove();
    }
}
