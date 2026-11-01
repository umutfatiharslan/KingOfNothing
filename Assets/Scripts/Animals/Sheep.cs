using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using KingOfNothing.World.Objects;
using UnityEngine;

namespace KingOfNothing.Animals
{
    /// <summary>
    /// A sheep (Tiny Swords Sheep_Idle / Move / Grass). It wanders and grazes around its herd, runs away from a
    /// hunter that comes closer than the hunter's flee distance (5 - hunting / 5 cells), can be hunted with a knife
    /// or attacked by soldiers, and drops raw meat when it dies. A Shepherd (Hunting 5) can herd it to another spot;
    /// a hunter with Curious Prey (Hunting 10) makes it stop and look back after a few seconds of running.
    /// Sheep are saved with the characters (CharacterSave).
    /// </summary>
    public class Sheep : MonoBehaviour, ICombatTarget, IStatusHost, IHealthDisplay
    {
        public static readonly List<Sheep> All = new List<Sheep>();

        enum State
        {
            Idle,
            Graze,
            Walk,
            Flee,
            LookBack,
            Follow,
        }

        string id;
        float health, maxHealth;
        int herd;
        Vector2 home;
        bool markedForHunt;
        SpriteRenderer body;
        SpriteRenderer mark;
        State state;
        float stateUntil;
        Vector2 target;
        Unit threat;
        Unit leader;
        Vector2 leaderDestination;
        float animationTime;
        float nextThreatCheck;
        StatusEffects status;

        public string Id => id;
        public int Herd => herd;
        public Vector2 Home => home;
        public bool MarkedForHunt => markedForHunt;
        public float Health => health;

        static CharacterSettings C => CharacterSettings.Current;

        public static Sheep Create(Vector2 position, int herdId, Vector2 herdHome, Transform parent, string savedId = null, float savedHealth = -1f)
        {
            var go = new GameObject("Sheep");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            var sheep = go.AddComponent<Sheep>();
            sheep.id = string.IsNullOrEmpty(savedId) ? System.Guid.NewGuid().ToString("N") : savedId;
            sheep.herd = herdId;
            sheep.home = herdHome;
            sheep.maxHealth = C.sheepHealth;
            sheep.health = savedHealth > 0f ? Mathf.Min(savedHealth, sheep.maxHealth) : sheep.maxHealth;
            return sheep;
        }

        void Awake()
        {
            var visual = new GameObject("Body");
            visual.transform.SetParent(transform, false);
            body = visual.AddComponent<SpriteRenderer>();
            float scale = C.sheepScale;
            visual.transform.localScale = new Vector3(scale, scale, 1f);
            visual.transform.localPosition = new Vector3(0f, 0.4f * scale, 0f);
            status = StatusEffects.Of(this);
            animationTime = Random.Range(0f, 1f);
            SetState(State.Graze, Random.Range(2f, 6f));
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        // ------------------------------------------------------------------ marks and herding

        public void SetMarked(bool marked)
        {
            markedForHunt = marked;
            if (marked && mark == null)
            {
                var go = new GameObject("Hunt Mark");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                mark = go.AddComponent<SpriteRenderer>();
                mark.sortingOrder = short.MaxValue - 40;
            }
            if (mark != null)
            {
                mark.enabled = marked;
                mark.sprite = C.huntMark != null ? C.huntMark : KingOfNothing.Utilities.ProceduralSprites.Dot;
                float s = C.markScale;
                mark.transform.localScale = new Vector3(s, s, 1f);
            }
        }

        /// <summary>Shepherd: follows the unit to a destination, then makes that spot its home.</summary>
        public void Follow(Unit shepherd, Vector2 destination)
        {
            leader = shepherd;
            leaderDestination = destination;
            SetState(State.Follow, 120f);
        }

        // ------------------------------------------------------------------ behaviour

        void SetState(State newState, float seconds)
        {
            state = newState;
            stateUntil = Time.time + seconds;
        }

        void Update()
        {
            if (status != null && status.IsStunned)
            {
                Animate(C.sheepIdle);
                UpdateSorting();
                return;
            }
            if (Time.time >= nextThreatCheck)
            {
                nextThreatCheck = Time.time + 0.25f;
                CheckThreat();
            }

            var c = C;
            switch (state)
            {
                case State.Flee:
                    if (Time.time >= stateUntil || threat == null) SetState(State.Idle, Random.Range(1f, 2f));
                    else
                    {
                        var away = (Vector2)transform.position - (Vector2)threat.transform.position;
                        if (away.sqrMagnitude < 0.0001f) away = Random.insideUnitCircle;
                        StepTowards((Vector2)transform.position + away.normalized * 2f, c.sheepFleeSpeed);
                    }
                    break;
                case State.LookBack:
                    if (threat != null) Face(threat.transform.position.x - transform.position.x);
                    if (Time.time >= stateUntil) SetState(State.Idle, 0.5f);
                    break;
                case State.Follow:
                    UpdateFollow();
                    break;
                case State.Walk:
                    if (Time.time >= stateUntil || StepTowards(target, c.sheepWalkSpeed)) SetState(State.Graze, Random.Range(3f, 8f));
                    break;
                default:
                    if (Time.time >= stateUntil) ChooseNext();
                    break;
            }
            Animate(state == State.Flee || state == State.Walk || (state == State.Follow && moving) ? c.sheepMove
                  : state == State.Graze ? c.sheepGrass : c.sheepIdle);
            UpdateSorting();
        }

        bool moving;

        void ChooseNext()
        {
            if (Random.value < 0.6f)
            {
                target = home + Random.insideUnitCircle * 4f;
                SetState(State.Walk, 8f);
            }
            else SetState(Random.value < 0.6f ? State.Graze : State.Idle, Random.Range(2f, 6f));
        }

        void UpdateFollow()
        {
            if (leader == null)
            {
                SetState(State.Idle, 1f);
                return;
            }
            Vector2 leaderPos = leader.transform.position;
            bool leaderArrived = (leaderPos - leaderDestination).sqrMagnitude < 1.5f * 1.5f && !leader.IsMoving;
            var behind = leaderPos + ((Vector2)transform.position - leaderPos).normalized * 1.2f;
            moving = ((Vector2)transform.position - leaderPos).sqrMagnitude > 1.6f * 1.6f;
            if (moving) StepTowards(behind, Mathf.Max(C.sheepWalkSpeed * 2.5f, leader.CurrentSpeed));
            if (leaderArrived && !moving || Time.time >= stateUntil)
            {
                home = transform.position;
                leader = null;
                SetState(State.Graze, Random.Range(2f, 5f));
            }
        }

        /// <summary>Runs from a hunter (a unit whose prey is a sheep) that comes too close, or from whoever hit it.</summary>
        void CheckThreat()
        {
            if (state == State.Follow) return;
            Unit nearest = null;
            float best = float.MaxValue;
            foreach (var v in Villager.All)
            {
                if (v == null || v.Movement == null || v.Movement.IsHidden) continue;
                if (!(v.Movement.CurrentTarget is Sheep)) continue;
                int hunting = v.Stats.Get(StatType.Hunting);
                float flee = StatEffects.FleeDistance(hunting);
                float d = Vector2.Distance(v.transform.position, transform.position);
                if (d < flee && d < best)
                {
                    best = d;
                    nearest = v.Movement;
                }
            }
            if (nearest != null && state != State.Flee && state != State.LookBack) StartFleeing(nearest);
        }

        void StartFleeing(Unit from)
        {
            threat = from;
            float seconds = C.sheepFleeSeconds;
            var v = from != null ? from.Villager : null;
            // Curious Prey (Hunting 10): stops after a few seconds and looks back
            if (v != null && v.Stats.Has(StatType.Hunting, 10)) seconds = Mathf.Min(seconds, StatSettings.Current.curiousStopSeconds10);
            SetState(State.Flee, seconds);
            if (v != null && v.Stats.Has(StatType.Hunting, 10)) pendingLookBack = true;
        }

        bool pendingLookBack;

        void LateUpdate()
        {
            if (pendingLookBack && state != State.Flee)
            {
                pendingLookBack = false;
                if (threat != null) SetState(State.LookBack, 2.5f);
            }
        }

        /// <summary>Walks towards a point; true when it is there (or cannot go on).</summary>
        bool StepTowards(Vector2 point, float speed)
        {
            Vector2 p = transform.position;
            var to = point - p;
            float step = speed * Time.deltaTime;
            if (to.magnitude <= step) return true;
            var next = p + to.normalized * step;
            var map = MapData.Instance;
            if (map != null && map.IsReady && !map.IsWalkable(MapData.WorldToCell(next)))
            {
                // Blocked (water, a solid object): try sideways, else give up
                var side = new Vector2(-to.y, to.x).normalized * step;
                if (map.IsWalkable(MapData.WorldToCell(p + side))) next = p + side;
                else return true;
            }
            transform.position = new Vector3(next.x, next.y, 0f);
            Face(to.x);
            return false;
        }

        void Face(float dx)
        {
            if (body != null && Mathf.Abs(dx) > 0.01f) body.flipX = dx < 0f;
        }

        void Animate(Sprite[] frames)
        {
            if (body == null || frames == null || frames.Length == 0) return;
            animationTime += Time.deltaTime;
            body.sprite = frames[(int)(animationTime * 8f) % frames.Length];
        }

        void UpdateSorting()
        {
            int order = MapSorting.Order(transform.position.x, transform.position.y);
            if (body != null) body.sortingOrder = order;
        }

        // ------------------------------------------------------------------ combat target

        public bool IsAlive => this != null && health > 0f;
        public string DisplayName => markedForHunt ? "Sheep (marked for hunting)" : "Sheep";
        public Rect Hitbox
        {
            get
            {
                Vector2 p = transform.position;
                return new Rect(p.x - 0.35f, p.y - 0.05f, 0.7f, 0.5f);
            }
        }
        public Vector2 GroundPoint => transform.position;
        public TargetKind TargetKind => TargetKind.Animal;
        public bool Moves => true;
        public bool ImmuneToStatus => false;
        public float StatusIconHeight => 0.8f;
        public float CurrentHealth => health;
        public float MaximumHealth => maxHealth;
        public Vector3 BarAnchor => transform.position + new Vector3(0f, 0.75f, 0f);
        public bool BarHidden => false;

        public void TakeStatusDamage(int amount)
        {
            if (health <= 0f) return;
            health = Mathf.Max(1f, health - amount); // bleeding alone does not kill
            HealthBar.NotifyUnit(this);
        }

        public int ReceiveHit(HitInfo hit)
        {
            if (health <= 0f || hit.Training || hit.Damage <= 0) return 0;
            float before = health;
            health = Mathf.Max(0f, health - hit.Damage);
            HealthBar.NotifyUnit(this);
            CombatRules.ApplyStatus(this, hit);
            if (hit.Attacker != null && state != State.Flee) StartFleeing(hit.Attacker);
            int done = Mathf.RoundToInt(before - health);
            if (health <= 0f) Die(hit.Attacker);
            return done;
        }

        void Die(Unit killer)
        {
            var map = MapData.Instance;
            var c = C;
            var killerVillager = killer != null ? killer.Villager : null;
            if (killerVillager != null) killerVillager.GainXp(StatType.Hunting, StatSettings.Current.huntKillXp);
            if (map != null && map.IsReady && c.rawMeat != null)
            {
                int meat = Random.Range(c.sheepMeatMin, c.sheepMeatMax + 1);
                float bonus = killerVillager != null ? StatSettings.Current.huntingBonusPerLevel * killerVillager.Stats.Get(StatType.Hunting) : 0f;
                float more = meat * bonus;
                meat += Mathf.FloorToInt(more) + (Random.value < more - Mathf.Floor(more) ? 1 : 0);
                var cell = MapData.WorldToCell(transform.position);
                Transform parent = null;
                var generator = Object.FindAnyObjectByType<MapGenerator>();
                if (generator != null) parent = generator.EditsRoot;
                ItemDropper.Drop(new List<DropEntry> { new DropEntry(c.rawMeat, meat) }, new RectInt(cell, Vector2Int.one), parent, map);
            }
            Debug.Log($"[Sheep] A sheep died{(killerVillager != null ? " (" + killerVillager.DisplayName + ")" : "")}.", this);
            Destroy(gameObject);
        }
    }
}
