using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>When a hidden trait reveals itself (GDD chapter 8).</summary>
    public enum TraitTrigger
    {
        Random,
        GameStart,
        RepeatedActivity,
        LowMorale,
        Starvation,
        DarknessExposure,
        CarryingWhenOrdered,
        Manual,
    }

    /// <summary>
    /// Template of a character trait (Tree Lover, Arsonist, Night Owl...). Traits are hidden until triggered
    /// and then appear in the character book. Create one asset per trait; subclass it for traits with
    /// special behaviour and override the hooks.
    /// </summary>
    [CreateAssetMenu(fileName = "Trait", menuName = "King of Nothing/Characters/Trait")]
    public class TraitDefinition : ScriptableObject
    {
        [SerializeField] string displayName = "Trait";
        [TextArea(2, 5)] [SerializeField] string description;
        [Tooltip("Funny line shown when the trait is revealed.")]
        [TextArea(1, 3)] [SerializeField] string flavorText;
        [SerializeField] Sprite icon;

        [Header("Trigger")]
        [SerializeField] TraitTrigger trigger = TraitTrigger.Random;
        [Tooltip("Chance per trigger check (e.g. per in-game day for Random).")]
        [Range(0f, 1f)] [SerializeField] float triggerChance = 0.05f;
        [SerializeField] bool hiddenUntilTriggered = true;

        [Header("Simple modifiers")]
        [Tooltip("Work speed multiplier while the trait is active (Night Owl by day = 0.8).")]
        [Min(0f)] [SerializeField] float workSpeedMultiplier = 1f;
        [Tooltip("Walk speed multiplier (Hasty = always runs).")]
        [Min(0f)] [SerializeField] float moveSpeedMultiplier = 1f;
        [Tooltip("Morale change per second while active.")]
        [SerializeField] float moraleDrainPerSecond;

        public string DisplayName => displayName;
        public string Description => description;
        public string FlavorText => flavorText;
        public Sprite Icon => icon;
        public TraitTrigger Trigger => trigger;
        public float TriggerChance => triggerChance;
        public bool HiddenUntilTriggered => hiddenUntilTriggered;
        public float WorkSpeedMultiplier => workSpeedMultiplier;
        public float MoveSpeedMultiplier => moveSpeedMultiplier;
        public float MoraleDrainPerSecond => moraleDrainPerSecond;

        /// <summary>Called once when the trait becomes visible.</summary>
        public virtual void OnRevealed(Villager villager) { }

        /// <summary>Called every frame while the trait is active.</summary>
        public virtual void Tick(Villager villager, float deltaTime)
        {
            if (moraleDrainPerSecond != 0f) villager.Needs.Morale.Change(-moraleDrainPerSecond * deltaTime);
        }

        /// <summary>Final work speed after this trait.</summary>
        public virtual float ModifyWorkSpeed(Villager villager, float speed) => speed * workSpeedMultiplier;

#if UNITY_EDITOR
        public void EditorSetup(string newName, string newDescription, string newFlavor, TraitTrigger newTrigger, float chance,
            bool hidden, float workSpeed, float moveSpeed, float moraleDrain)
        {
            displayName = newName;
            description = newDescription;
            flavorText = newFlavor;
            trigger = newTrigger;
            triggerChance = chance;
            hiddenUntilTriggered = hidden;
            workSpeedMultiplier = workSpeed;
            moveSpeedMultiplier = moveSpeed;
            moraleDrainPerSecond = moraleDrain;
        }
#endif
    }
}
