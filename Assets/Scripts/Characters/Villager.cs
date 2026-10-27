using System;
using System.Collections.Generic;
using KingOfNothing.Units;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>
    /// A character of the kingdom: identity (name, age, story), role, stats, needs and traits.
    /// Movement / selection is handled by the Unit component on the same object.
    /// This is the template the later phases build on (work orders, character book UI, aging...).
    /// </summary>
    [RequireComponent(typeof(Unit))]
    [DisallowMultipleComponent]
    public class Villager : MonoBehaviour
    {
        public static readonly List<Villager> All = new List<Villager>();

        [SerializeField] VillagerIdentity identity = new VillagerIdentity();
        [SerializeField] VillagerRole role = VillagerRole.Villager;
        [SerializeField] StatBlock stats = new StatBlock();
        [SerializeField] VillagerNeeds needs = new VillagerNeeds();
        [SerializeField] List<TraitInstance> traits = new List<TraitInstance>();

        [Header("Name tag")]
        [SerializeField] bool showNameTag = true;
        [SerializeField] Vector3 nameTagOffset = new Vector3(0f, 1.35f, 0f);
        [Min(0.01f)] [SerializeField] float nameTagCharacterSize = 0.08f;
        [SerializeField] Color nameTagColor = Color.white;

        /// <summary>(villager, trait) when a hidden trait shows up.</summary>
        public static event Action<Villager, TraitInstance> TraitRevealed;
        public event Action<Villager, VillagerRole> RoleChanged;

        Unit unit;
        TextMesh nameTag;
        MeshRenderer nameTagRenderer;

        public VillagerIdentity Identity => identity;
        public string DisplayName => identity.displayName;
        public VillagerRole Role => role;
        public StatBlock Stats => stats;
        public VillagerNeeds Needs => needs;
        public IReadOnlyList<TraitInstance> Traits => traits;
        public Unit Movement => unit;

        void Awake()
        {
            unit = GetComponent<Unit>();
            if (showNameTag) CreateNameTag();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            needs.Tick(dt);

            float moveMultiplier = 1f;
            foreach (var trait in traits)
            {
                if (trait.definition == null || !trait.revealed) continue;
                trait.definition.Tick(this, dt);
                moveMultiplier *= trait.definition.MoveSpeedMultiplier;
            }
            // Traits change how fast the character walks (Hasty runs everywhere...)
            unit.SpeedModifier = moveMultiplier;
        }

        /// <summary>Sets up a freshly spawned villager.</summary>
        public void Setup(string displayName, int age, VillagerRole newRole)
        {
            identity.displayName = displayName;
            identity.age = age;
            gameObject.name = displayName;
            SetRole(newRole);
            if (nameTag != null) nameTag.text = displayName;
        }

        public void SetRole(VillagerRole newRole)
        {
            if (role == newRole) return;
            role = newRole;
            RoleChanged?.Invoke(this, role);
        }

        /// <summary>Work speed multiplier after all active traits (1 = normal).</summary>
        public float WorkSpeedMultiplier
        {
            get
            {
                float speed = 1f;
                foreach (var trait in traits)
                    if (trait.definition != null && trait.revealed) speed = trait.definition.ModifyWorkSpeed(this, speed);
                return speed;
            }
        }

        public bool HasTrait(TraitDefinition definition) => traits.Exists(t => t.definition == definition);

        public void AddTrait(TraitDefinition definition, bool revealed)
        {
            if (definition == null || HasTrait(definition)) return;
            var instance = new TraitInstance(definition, revealed || !definition.HiddenUntilTriggered);
            traits.Add(instance);
            if (instance.revealed) OnTraitRevealed(instance);
        }

        /// <summary>Makes a hidden trait visible (it then shows in the character book and becomes active).</summary>
        public bool RevealTrait(TraitDefinition definition)
        {
            var instance = traits.Find(t => t.definition == definition);
            if (instance == null || instance.revealed) return false;
            instance.revealed = true;
            OnTraitRevealed(instance);
            return true;
        }

        void OnTraitRevealed(TraitInstance instance)
        {
            instance.definition.OnRevealed(this);
            TraitRevealed?.Invoke(this, instance);
        }

        void CreateNameTag()
        {
            var go = KingOfNothing.Utilities.RuntimeChild.Replace(transform, "Name Tag");
            go.transform.localPosition = nameTagOffset;
            nameTag = go.AddComponent<TextMesh>();
            nameTag.text = identity.displayName;
            nameTag.anchor = TextAnchor.LowerCenter;
            nameTag.alignment = TextAlignment.Center;
            nameTag.characterSize = nameTagCharacterSize;
            nameTag.fontSize = 48;
            nameTag.color = nameTagColor;
            nameTagRenderer = go.GetComponent<MeshRenderer>();
            // Always drawn above the map objects
            nameTagRenderer.sortingOrder = short.MaxValue - 10;
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                nameTag.font = font;
                nameTagRenderer.sharedMaterial = font.material;
            }
        }
    }
}
