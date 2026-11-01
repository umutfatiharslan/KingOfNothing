using System.Collections.Generic;
using KingOfNothing.Units;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using UnityEngine;

namespace KingOfNothing.Characters
{
    /// <summary>What a character is doing (character list, bio).</summary>
    public enum Activity
    {
        Idle,
        PlayerOrder,
        Holding,
        Working,
        Eating,
        Sleeping,
        Recreation,
        Patrol,
        Training,
        ScarecrowTraining,
        Hospital,
        Healing,
    }

    /// <summary>
    /// The daily routine of a character (RimWorld style): it follows its day plan (Schedule: work / recreation /
    /// sleep per hour) and its work priorities (Work panel), eats when hungry, goes to its training, to the church
    /// when an army training hurt it, sleeps in its home. Soldiers train on scarecrows during work hours and patrol
    /// around the settlement in their recreation hours. Player orders always win: after a work order the routine
    /// comes back, after a plain move order the character holds its place until "Resume routine" or sleeping time.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterBrain : MonoBehaviour
    {
        Villager villager;
        Unit unit;
        UnitWorker worker;
        UnitJob assigned;
        Activity assignedActivity;
        float nextThink;
        bool needsHospital;
        float noFoodUntil;
        float noWorkUntil;
        Building routineHost;
        string doing = "";
        Activity activity;
        int lastHour = -1;
        float trainingLossCarry;

        public Activity Activity => activity;
        /// <summary>Text for the character list ("Chopping Tree 2", "Sleeping in House 1").</summary>
        public string Doing => doing;
        public bool IsSleeping => activity == Activity.Sleeping;

        static CharacterSettings C => CharacterSettings.Current;
        static StatSettings S => StatSettings.Current;

        void Awake()
        {
            villager = GetComponent<Villager>();
            unit = GetComponent<Unit>();
        }

        void Start()
        {
            worker = unit != null ? unit.Worker : GetComponent<UnitWorker>();
            if (worker != null) worker.Finished += OnJobFinished;
            nextThink = Time.time + Random.Range(0.2f, 0.8f);
        }

        void OnDestroy()
        {
            if (worker != null) worker.Finished -= OnJobFinished;
        }

        void OnJobFinished(UnitJob job, JobState state)
        {
            if (job == null) return;
            if (job.IsRoutine)
            {
                if (state == JobState.Failed && job.TargetObject != null) JobTargets.MarkFailed(job.TargetObject, 45f);
                if (job is EatJob && state == JobState.Failed) noFoodUntil = Time.time + 20f;
                if (job is HospitalJob) needsHospital = false;
                if (job is EnterBuildingJob enter && state == JobState.Done) routineHost = unit.Host as Building;
                if (job is SleepJob && state == JobState.Done) routineHost = unit.Host as Building;
                nextThink = Mathf.Min(nextThink, Time.time + 0.1f);
                return;
            }
            // A player job ended: inside a building the character stays there, otherwise the routine comes back
            if (unit.Host != null) villager.Memory.manualHold = true;
        }

        // ------------------------------------------------------------------ player orders

        /// <summary>
        /// The player gave an order (SelectionManager / actions menu). A plain move holds the character at its spot;
        /// any order stops the training (it can be resumed in the building's training window).
        /// </summary>
        public void OnPlayerOrder(bool moveOnly)
        {
            if (villager == null) return;
            villager.OnPlayerOrder();
            villager.Memory.manualHold = moveOnly;
            if (villager.Training.active && !TrainingInside())
            {
                villager.Training.Stop();
                villager.AddJournal("Training interrupted by an order.");
            }
            needsHospital = false;
            assigned = null;
            routineHost = null;
        }

        /// <summary>Back to the day plan.</summary>
        public void ResumeRoutine()
        {
            villager.Memory.manualHold = false;
            nextThink = Time.time;
        }

        // ------------------------------------------------------------------ every frame: needs of the current activity

        void Update()
        {
            if (villager == null || unit == null) return;
            float hours = GameClock.HoursFromSeconds(Time.deltaTime);
            ApplyActivityEffects(hours);
            if (activity == Activity.Training) TickTraining(hours);
            int hour = GameClock.CurrentHour;
            if (hour != lastHour)
            {
                lastHour = hour;
                nextThink = Time.time; // a new hour may be a new block of the plan
            }
            if (Time.time < nextThink) return;
            nextThink = Time.time + C.thinkInterval;
            Think();
        }

        void ApplyActivityEffects(float hours)
        {
            var c = C;
            var needs = villager.Needs;
            switch (activity)
            {
                case Activity.Sleeping:
                    bool inBed = unit.Host != null;
                    villager.Heal((inBed ? c.sleepRegenInBed : c.sleepRegenOnGround) * hours, null, false);
                    needs.Morale.Change(c.sleepMoralePerHour * hours);
                    break;
                case Activity.Recreation:
                    needs.Morale.Change(c.recreationMoralePerHour * hours);
                    break;
                case Activity.Working:
                case Activity.Training:
                case Activity.ScarecrowTraining:
                case Activity.Healing:
                    needs.Morale.Change(-c.workMoraleLossPerHour * hours);
                    break;
            }
        }

        // ------------------------------------------------------------------ decisions

        void Think()
        {
            if (unit.IsStunned) return;
            var current = worker != null ? worker.Current : null;

            // A player job is running
            if (current != null && !current.IsRoutine)
            {
                Set(Activity.PlayerOrder, current.Activity);
                return;
            }
            var block = villager.BlockAt(GameClock.CurrentHour);
            var memory = villager.Memory;
            if (memory.manualHold)
            {
                if (block != ScheduleBlock.Sleep)
                {
                    Set(Activity.Holding, unit.Host is Building h ? $"Staying in {h.DisplayName}" : "Holding position");
                    return;
                }
                memory.manualHold = false;
            }

            // Inside a building for the routine: stay or come out
            if (unit.Host is Building host && StayInside(host, block)) return;

            // Needs first
            if (needsHospital && !(current is HospitalJob))
            {
                var church = NearestChurch();
                if (church != null)
                {
                    Assign(new HospitalJob(church), Activity.Hospital);
                    return;
                }
                needsHospital = false;
            }
            if (current is HospitalJob) { Set(Activity.Hospital, current.Activity); return; }

            if (villager.Needs.Satiety.current < C.eatThreshold && Time.time >= noFoodUntil)
            {
                if (current is EatJob) { Set(Activity.Eating, current.Activity); return; }
                if (FoodSources.Find(villager, out _, out _, out _))
                {
                    Assign(new EatJob(), Activity.Eating);
                    return;
                }
                noFoodUntil = Time.time + 20f;
            }
            if (current is EatJob) { Set(Activity.Eating, current.Activity); return; }

            switch (block)
            {
                case ScheduleBlock.Sleep: Sleep(current); break;
                case ScheduleBlock.Recreation:
                    if (TrainingWanted(true)) GoTrain(current);
                    else if (VillagerRoles.IsSoldier(villager.Role)) Patrol(current);
                    else Recreate(current, false);
                    break;
                default: Work(current); break;
            }
        }

        /// <summary>Keeps a routine character inside (sleeping, mining, training) or lets it out. True = it stays.</summary>
        bool StayInside(Building host, ScheduleBlock block)
        {
            bool hungry = villager.Needs.Satiety.current < C.eatThreshold && Time.time >= noFoodUntil;
            var training = villager.Training;
            // Training inside its building
            if (training.active && host.Uid == training.buildingUid)
            {
                if (block == ScheduleBlock.Sleep || !TrainingEligible(out _))
                {
                    host.Release(unit);
                    return false;
                }
                if (hungry && FoodSources.Find(villager, out _, out _, out _))
                {
                    training.resumeAfterMeal = true;
                    villager.AddJournal("Left the training to eat.");
                    host.Release(unit);
                    return false;
                }
                Set(Activity.Training, $"Training {StatInfo.Name(training.stat)} in {host.DisplayName}");
                return true;
            }
            // Sleeping at home
            if (block == ScheduleBlock.Sleep && (host == routineHost || host.Uid == villager.Memory.homeUid || host.Type == BuildingType.Castle))
            {
                Set(Activity.Sleeping, $"Sleeping in {host.DisplayName}");
                return true;
            }
            // Mining inside a gold mine during work hours
            if (block == ScheduleBlock.Work && host.Info != null && host.Info.IsMine && !hungry && villager.PriorityOf(StatType.Mining) > 0)
            {
                Set(Activity.Working, $"Mining in {host.DisplayName}");
                return true;
            }
            host.Release(unit);
            routineHost = null;
            return false;
        }

        void Set(Activity a, string text)
        {
            activity = a;
            doing = text;
        }

        /// <summary>Gives the character a routine job (replacing whatever routine job it had).</summary>
        void Assign(UnitJob job, Activity a)
        {
            job.IsRoutine = true;
            assigned = job;
            assignedActivity = a;
            worker.Assign(job);
            Set(a, job.Activity);
        }

        /// <summary>The routine job of this activity keeps running (true), or the old one is dropped.</summary>
        bool Keep(UnitJob current, Activity a)
        {
            if (current != null && current.IsRoutine && current == assigned && assignedActivity == a)
            {
                Set(a, current.Activity);
                return true;
            }
            if (current != null && current.IsRoutine) worker.ClearJobs();
            return false;
        }

        // ------------------------------------------------------------------ blocks

        void Sleep(UnitJob current)
        {
            if (Keep(current, Activity.Sleeping))
            {
                if (current is SleepJob s && s.AsleepOnGround) Set(Activity.Sleeping, "Sleeping on the ground");
                return;
            }
            var bed = Housing.BedFor(villager);
            if (bed != null && JobTargets.RecentlyFailed(bed)) bed = null;
            var spot = Settlement.Center + Random.insideUnitCircle * 3f;
            Assign(new SleepJob(bed, spot), Activity.Sleeping);
        }

        void Recreate(UnitJob current, bool idle)
        {
            var a = idle ? Activity.Idle : Activity.Recreation;
            if (Keep(current, a)) return;
            Assign(new RecreationJob(idle ? Settlement.Center : Settlement.RecreationCenter, C.recreationRadius, idle), a);
        }

        void Patrol(UnitJob current)
        {
            if (Keep(current, Activity.Patrol)) return;
            Assign(new PatrolJob(Settlement.Center), Activity.Patrol);
        }

        void Work(UnitJob current)
        {
            if (TrainingWanted(false))
            {
                GoTrain(current);
                return;
            }
            if (VillagerRoles.IsSoldier(villager.Role))
            {
                if (Keep(current, Activity.ScarecrowTraining)) return;
                var scarecrow = WorkGivers.FreeScarecrow(villager);
                if (scarecrow != null)
                {
                    Assign(new AttackJob(new List<ICombatTarget> { scarecrow }, true), Activity.ScarecrowTraining);
                    return;
                }
                Patrol(current);
                return;
            }
            var workActivity = villager.Role == VillagerRole.Priest ? Activity.Healing : Activity.Working;
            if (Keep(current, workActivity)) return;
            if (Time.time >= noWorkUntil)
            {
                var job = NextWork();
                if (job != null)
                {
                    Assign(job, workActivity);
                    return;
                }
                noWorkUntil = Time.time + 3f;
            }
            Recreate(current, true);
        }

        /// <summary>Work types by priority (1 first), ties in the Work panel's column order.</summary>
        UnitJob NextWork()
        {
            for (int priority = 1; priority <= 5; priority++)
                foreach (var work in WorkTypes.All)
                {
                    if (villager.PriorityOf(work) != priority) continue;
                    var job = WorkGivers.Find(villager, work);
                    if (job != null) return job;
                }
            return null;
        }

        // ------------------------------------------------------------------ training

        bool TrainingInside()
        {
            var t = villager.Training;
            return t.active && unit.Host is Building b && b.Uid == t.buildingUid;
        }

        Building TrainingBuilding()
        {
            var map = MapData.Instance;
            var t = villager.Training;
            return map != null && t.buildingUid > 0 ? map.FindObject(t.buildingUid) as Building : null;
        }

        /// <summary>The training can go on (building usable, not finished, health for an army training).</summary>
        public bool TrainingEligible(out string reason)
        {
            reason = null;
            var t = villager.Training;
            if (!t.active) { reason = "No training"; return false; }
            var b = TrainingBuilding();
            if (b == null || !b.IsUsable) { reason = "The building is gone"; t.Stop(); return false; }
            var stats = villager.Stats;
            if (!stats.CanDo(t.stat) || stats.Base(t.stat) >= StatBlock.MaxLevel || stats.IsTrained(t.stat)) { reason = "Nothing to train"; t.Stop(); return false; }
            if (b.Type == BuildingType.ArmyHouse && villager.Needs.Health.Normalized < S.trainingMinHealth) { reason = "Too hurt"; return false; }
            return true;
        }

        /// <summary>Go (back) to the training: during work hours, or right after a meal (not at sleeping time).</summary>
        bool TrainingWanted(bool recreationBlock)
        {
            var t = villager.Training;
            if (!t.active || (recreationBlock && !t.resumeAfterMeal)) return false;
            return TrainingEligible(out _);
        }

        void GoTrain(UnitJob current)
        {
            if (Keep(current, Activity.Training)) return;
            var b = TrainingBuilding();
            if (b == null || JobTargets.RecentlyFailed(b)) return;
            villager.Training.resumeAfterMeal = false;
            Assign(new EnterBuildingJob(b, false), Activity.Training);
        }

        /// <summary>
        /// Training progress while inside: game hours add up; an army house training hurts now and then (chance per
        /// game hour to lose "target level" health, never below 1); below 25% health the character goes to the church.
        /// </summary>
        void TickTraining(float hours)
        {
            if (!TrainingInside() || hours <= 0f) return;
            var t = villager.Training;
            var p = villager.Stats.Progress(t.stat);
            int level = p.level;
            if (p.trainingLevel != level)
            {
                p.trainingLevel = level;
                p.trainingHours = 0f;
            }
            p.trainingHours += hours;
            var b = TrainingBuilding();
            if (b != null && b.Type == BuildingType.ArmyHouse)
            {
                trainingLossCarry += hours;
                while (trainingLossCarry >= 1f)
                {
                    trainingLossCarry -= 1f;
                    if (Random.value < S.armyHealthLossChance)
                    {
                        int loss = level + 1;
                        villager.LoseHealth(loss);
                        FloatingText.Show(villager.transform, $"-{loss} health (training)", new Color(1f, 0.45f, 0.4f));
                    }
                }
                if (villager.Needs.Health.Normalized < S.trainingMinHealth)
                {
                    villager.AddJournal($"Hurt in the {StatInfo.Name(t.stat)} training: going to the church.");
                    b.Release(unit);
                    needsHospital = true;
                    activity = Activity.Hospital;
                    nextThink = Time.time;
                    return;
                }
            }
            if (p.trainingHours >= S.TrainingHoursFor(level)) CompleteTraining(p);
        }

        void CompleteTraining(StatProgress p)
        {
            var t = villager.Training;
            p.trainedLevel = p.level;
            p.trainingLevel = -1;
            p.trainingHours = 0f;
            p.trainingPaid = false;
            t.Stop();
            float multiplier = S.TrainingMultiplier(p.level);
            villager.AddJournal($"Finished the {StatInfo.Name(p.type)} training of level {p.level}: XP x{multiplier:0.0#} until level {p.level + 1}.");
            FloatingText.Show(villager.transform, $"{StatInfo.Name(p.type)} trained: XP x{multiplier:0.0#}", C.levelUpColor, C.xpArrow);
            var b = TrainingBuilding();
            if (b != null) b.Release(unit);
            nextThink = Time.time;
        }

        Building NearestChurch()
        {
            Building best = null;
            float bestDistance = float.MaxValue;
            foreach (var b in Building.All)
            {
                if (b == null || b.Type != BuildingType.Church || !b.IsUsable) continue;
                float d = Vector2.Distance(villager.transform.position, b.GroundPoint);
                if (d < bestDistance) { bestDistance = d; best = b; }
            }
            return best;
        }
    }
}
