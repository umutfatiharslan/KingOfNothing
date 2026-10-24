using System;
using UnityEngine;

namespace KingOfNothing.World.Objects
{
    /// <summary>
    /// A map object that gives resources when it is worked on: trees, stones, gold stones, gold mines...
    /// GDD stat efficiency: every hit removes "power" health (the worker's stat), so a 20 HP tree needs
    /// 20 hits at Woodcutting 1 and 3 hits at Woodcutting 7.
    /// </summary>
    public class ResourceNode : WorldObject, IDamageable
    {
        [Header("Resource")]
        [SerializeField] ResourceType resourceType = ResourceType.None;
        [Min(1)] [SerializeField] int maxHealth = 20;
        [SerializeField] int health = 20;
        [Min(0)] [SerializeField] int yieldAmount = 1;

        /// <summary>Raised for every resource node when it is depleted: (node, resource, amount).</summary>
        public static event Action<ResourceNode, ResourceType, int> AnyDepleted;

        public event Action<ResourceNode, int> Hit;
        public event Action<ResourceNode> Depleted;

        public ResourceType ResourceType => resourceType;
        public int MaxHealth => maxHealth;
        public int Health => health;
        public int YieldAmount => yieldAmount;
        public bool IsDepleted => health <= 0;
        public virtual bool CanBeHit => !IsDepleted;

        public override void Initialize(SpawnableDefinition source, Vector2Int originCell, MapData map)
        {
            base.Initialize(source, originCell, map);
            if (source == null) return;
            resourceType = source.ResourceType;
            maxHealth = Mathf.Max(1, source.MaxHealth);
            health = maxHealth;
            yieldAmount = source.YieldAmount;
        }

        /// <summary>Number of hits a worker with the given stat needs (int.MaxValue if the stat is 0).</summary>
        public int HitsNeeded(int statValue) => statValue <= 0 ? int.MaxValue : Mathf.CeilToInt(health / (float)statValue);

        public virtual void ApplyHit(int power, GameObject instigator)
        {
            if (!CanBeHit || power <= 0) return;
            health = Mathf.Max(0, health - power);
            Hit?.Invoke(this, power);
            if (health == 0) Deplete(instigator);
        }

        protected virtual void Deplete(GameObject instigator)
        {
            Depleted?.Invoke(this);
            AnyDepleted?.Invoke(this, resourceType, yieldAmount);
            OnDepleted(instigator);
        }

        /// <summary>What happens to the object itself. Default: it disappears.</summary>
        protected virtual void OnDepleted(GameObject instigator) => Remove();
    }
}
