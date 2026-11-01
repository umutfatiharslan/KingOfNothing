using System.Collections.Generic;
using KingOfNothing.Characters;
using KingOfNothing.Units;
using KingOfNothing.Utilities;
using KingOfNothing.World;
using KingOfNothing.World.Buildings;
using UnityEngine;
using Random = UnityEngine.Random;

namespace KingOfNothing.Interaction
{
    /// <summary>
    /// Windows of a building, opened from its right click menu:
    ///  - Character list: who is inside (or on top of a tower) and what can be done with them. Houses and the
    ///    castle: Exit. School / army house: Exit, Improve stat (waits for stats). Army house: class change
    ///    (warrior, lancer, archer; back to villager). Church: Exit, Heal (gold coins), Pay indulgence (waits for
    ///    morale), Skill development for priests and Random skill (wait for stats), Become priest. Tower: Come down,
    ///    face left / right.
    ///  - Inventory: the stored items; selected characters can take items out (the furnace's gold coins...).
    /// </summary>
    public class BuildingWindows : MonoBehaviour
    {
        const int CharactersWindowId = 0x434841;
        const int InventoryWindowId = 0x494E56;

        Building charactersOf;
        Building inventoryOf;
        Rect charactersRect = new Rect(340f, 90f, 460f, 10f);
        Rect inventoryRect = new Rect(720f, 120f, 320f, 10f);
        Vector2 charactersScroll, inventoryScroll;
        string message;
        float messageUntil;

        public static BuildingWindows Instance { get; private set; }

        void Awake() => Instance = this;

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void ShowCharacters(Building building)
        {
            charactersOf = building;
            charactersRect.height = 10f;
        }

        public void ShowInventory(Building building)
        {
            inventoryOf = building;
            inventoryRect.height = 10f;
        }

        void Say(string text)
        {
            message = text;
            messageUntil = Time.unscaledTime + 4f;
            Debug.Log("[Buildings] " + text);
        }

        static GameplaySettings Settings => MapData.Instance != null ? MapData.Instance.Gameplay : GameplaySettings.Fallback;

        void OnGUI()
        {
            if (charactersOf == null) charactersOf = null;
            if (inventoryOf == null) inventoryOf = null;
            if (charactersOf != null)
            {
                charactersRect = GUILayout.Window(CharactersWindowId, charactersRect, DrawCharacters,
                    $"{charactersOf.DisplayName} - characters ({CountText(charactersOf)})", UiStyle.Window, GUILayout.Width(460f));
                ScreenUi.Block(charactersRect);
            }
            if (inventoryOf != null)
            {
                inventoryRect = GUILayout.Window(InventoryWindowId, inventoryRect, DrawInventory,
                    $"{inventoryOf.DisplayName} - inventory", UiStyle.Window, GUILayout.Width(320f));
                ScreenUi.Block(inventoryRect);
            }
        }

        static string CountText(Building b) => b.Capacity < 0 ? $"{b.OccupantCount}" : $"{b.OccupantCount}/{b.Capacity}";

        // ------------------------------------------------------------------ character list

        void DrawCharacters(int id)
        {
            var b = charactersOf;
            if (b == null) return;
            var info = b.Info;
            var coin = Settings.goldCoin;
            if (b.Type == BuildingType.Church && coin != null)
                GUILayout.Label($"Gold coins in the stores: {Building.TreasuryCount(coin)}");

            if (b.Type == BuildingType.GoldMine)
                GUILayout.Label(b.MineReserve >= 0 ? $"Gold left in the mine: {b.MineReserve}" : "Gold left in the mine: unlimited");
            var list = new List<Unit>(b.Occupants);
            if (b.Percher != null) list.Insert(0, b.Percher);
            if (TrainingService.TrainsHere(b))
                GUILayout.Label("Click Training... to train a stat (the character must be inside).");
            // Tall enough for every character with its own buttons (scrolls beyond ~4 characters)
            float rowHeight = b.Type == BuildingType.Church ? 118f : info != null && info.allowsClassChange ? 96f : 70f;
            charactersScroll = GUILayout.BeginScrollView(charactersScroll, GUILayout.Height(Mathf.Clamp(list.Count * rowHeight + 10f, 90f, 470f)));
            if (list.Count == 0) GUILayout.Label(b.Type == BuildingType.Tower ? "Nobody on the tower." : "Nobody inside.");
            foreach (var unit in list)
            {
                if (unit == null) continue;
                var villager = unit.Villager;
                var role = villager != null ? villager.Role : VillagerRole.Villager;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{(villager != null ? villager.DisplayName : unit.name)} - {role}" + HealthText(villager));

                if (b.Type == BuildingType.Tower) DrawTowerRow(b, unit);
                else
                {
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("Exit"))
                        {
                            b.Release(unit);
                            villager?.Brain?.ResumeRoutine();
                        }
                        if (villager != null && GUILayout.Button("Bio")) CharacterWindows.Ensure().ShowBio(villager);
                        if (villager != null && TrainingService.TrainsHere(b) && (b.Type != BuildingType.Church || role == VillagerRole.Priest))
                        {
                            var t = villager.Training;
                            string label = t.active && t.buildingUid == b.Uid ? $"Training {StatInfo.Name(t.stat)}..." : "Training...";
                            if (GUILayout.Button(label)) CharacterWindows.Ensure().ShowTraining(villager, b);
                        }
                    }
                    if (info != null && info.allowsClassChange && role != VillagerRole.Priest) DrawClassChange(villager, role);
                    if (b.Type == BuildingType.Church) DrawChurchRow(b, unit, villager, role, coin);
                }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(message) && Time.unscaledTime < messageUntil) GUILayout.Label(message);
            if (GUILayout.Button("Close")) charactersOf = null;
            GUI.DragWindow();
        }

        static string HealthText(Villager villager)
        {
            if (villager == null) return "";
            var h = villager.Needs.Health;
            return $"  (health {Mathf.RoundToInt(h.current)}/{Mathf.RoundToInt(h.max)})";
        }

        void DrawTowerRow(Building b, Unit unit)
        {
            if (Building.HasFearOfHeights(unit)) GUILayout.Label("Afraid of heights: useless on the tower.");
            else if (Building.IsUsefulArcher(unit))
                GUILayout.Label($"Range x{b.RangeMultiplierFor(unit):0.#}, archery +{b.ArcheryBonus} (max +{b.Info.archeryBonusMax}), tower +{b.Info.mannedHealthBonus} HP");
            else GUILayout.Label("Only archers defend the tower.");
            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Come down")) b.Release(unit);
                if (GUILayout.Button("Face left")) unit.Face(Vector2.left);
                if (GUILayout.Button("Face right")) unit.Face(Vector2.right);
            }
        }

        void DrawClassChange(Villager villager, VillagerRole role)
        {
            if (villager == null) return;
            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Class:", GUILayout.Width(40f));
                foreach (var r in new[] { VillagerRole.Warrior, VillagerRole.Lancer, VillagerRole.Archer, VillagerRole.Villager })
                {
                    // A class needs its weapon stat (0 = can never use that weapon)
                    var weapon = StatRoller.WeaponStat(r);
                    bool possible = !weapon.HasValue || villager.Stats.CanDo(weapon.Value);
                    GUI.enabled = role != r && possible;
                    if (GUILayout.Button(new GUIContent(r.ToString(), possible ? null : $"{StatInfo.Name(weapon.Value)} 0: cannot use this weapon")))
                    {
                        villager.SetRole(r);
                        Say($"{villager.DisplayName} is now a {r}.");
                    }
                }
                GUI.enabled = true;
            }
        }

        void DrawChurchRow(Building b, Unit unit, Villager villager, VillagerRole role, SpawnableDefinition coin)
        {
            var info = b.Info;
            int coins = coin != null ? Building.TreasuryCount(coin) : 0;
            using (new GUILayout.HorizontalScope())
            {
                bool hurt = villager != null && villager.Needs.Health.current < villager.Needs.Health.max;
                bool healedToday = villager != null && villager.Memory.healedDay == GameClock.Today;
                GUI.enabled = villager != null && coin != null && hurt && !healedToday && coins >= info.healCost;
                string healLabel = $"Heal +{info.healAmount} ({info.healCost} coins)" + (!hurt ? " - healthy" : healedToday ? " - once a day" : "");
                if (GUILayout.Button(healLabel))
                {
                    if (b.TryChurchHeal(villager, out string reason)) Say($"{villager.DisplayName} was healed.");
                    else Say(reason);
                }
                GUI.enabled = false;
                GUILayout.Button($"Pay indulgence ({info.indulgenceCost} coins)");
                GUI.enabled = true;
            }
            using (new GUILayout.HorizontalScope())
            {
                GUI.enabled = villager != null && coin != null && coins >= info.randomSkillCost;
                if (GUILayout.Button($"Random skill +1 ({info.randomSkillCost} coins)") && RandomSkill(villager, coin, info.randomSkillCost, out string what))
                    Say($"{villager.DisplayName}: {what}");
                GUI.enabled = true;
                if (info.unlocksPriest && role != VillagerRole.Priest && villager != null && GUILayout.Button("Become priest"))
                {
                    villager.SetRole(VillagerRole.Priest);
                    Say($"{villager.DisplayName} is now a priest.");
                }
            }
        }

        /// <summary>Church: a random stat (that can still grow) gets a level for gold coins.</summary>
        static bool RandomSkill(Villager v, SpawnableDefinition coin, int cost, out string what)
        {
            what = null;
            var candidates = new List<StatType>();
            foreach (var s in StatInfo.All)
                if (v.Stats.CanDo(s) && v.Stats.Base(s) < StatBlock.MaxLevel && s != StatType.Healing) candidates.Add(s);
            if (candidates.Count == 0 || !Building.SpendFromTreasury(coin, cost)) return false;
            var stat = candidates[Random.Range(0, candidates.Count)];
            v.Stats.Set(stat, v.Stats.Base(stat) + 1);
            what = $"{StatInfo.Name(stat)} +1 ({v.Stats.Base(stat)}).";
            v.AddJournal($"Bought a blessing at the church: {what}");
            return true;
        }

        // ------------------------------------------------------------------ inventory

        void DrawInventory(int id)
        {
            var b = inventoryOf;
            if (b == null) return;
            var info = b.Info;
            var inventory = b.Inventory;
            if (!b.IsUsable) GUILayout.Label(b.IsComplete ? "Destroyed: repair it to use the inventory." : "Only finished buildings store items.");
            GUILayout.Label($"Slots: {inventory.UsedSlots}/{inventory.SlotCount}, {inventory.SlotCapacity} per item");
            if (info != null && info.privateStorage)
            {
                GUILayout.Label("Training storage: trainings here take their cost from it (not part of the kingdom's stores).");
                foreach (var r in b.Requests)
                    if (r != null && r.item != null) GUILayout.Label($"Requested from the haulers: {r.item.DisplayName} {r.count}");
                if (b.Requests.Count > 0 && GUILayout.Button("Cancel the requests")) b.ClearRequests();
            }
            if (info != null && info.IsFurnace)
            {
                foreach (var r in info.Recipes)
                {
                    var parts = new List<string>();
                    foreach (var i in r.inputs) if (i != null && i.item != null) parts.Add($"{i.amount} {i.item.DisplayName}");
                    GUILayout.Label($"{r.name}: {string.Join(" + ", parts)} -> {r.outputAmount} {Name(r.output)} ({r.seconds:0.#} s at baking 10)");
                }
                GUILayout.Label(b.FurnaceWorking ? $"A baker works it: {b.CurrentRecipe?.name} {Mathf.RoundToInt(b.FurnaceCycleProgress * 100f)}%"
                                                 : "Idle: it only works with a baker (Work the furnace, or the Baking work type).");
            }

            var selection = SelectionManager.Instance != null ? SelectionManager.Instance.Selected : null;
            bool hasUnits = selection != null && selection.Count > 0;
            inventoryScroll = GUILayout.BeginScrollView(inventoryScroll, GUILayout.MaxHeight(320f));
            if (inventory.UsedSlots == 0) GUILayout.Label("Empty.");
            foreach (var slot in new List<InventorySlot>(inventory.Slots))
            {
                if (slot == null || slot.item == null) continue;
                using (new GUILayout.HorizontalScope())
                {
                    BuildMenuController.DrawItem(GUILayoutUtility.GetRect(24f, 24f, GUILayout.Width(24f)), slot.item);
                    GUILayout.Label($"{slot.item.DisplayName}: {slot.count}/{inventory.SlotCapacity}");
                    GUI.enabled = hasUnits && b.IsUsable;
                    if (GUILayout.Button(new GUIContent("Take out", "A selected character carries it out as loot"), GUILayout.Width(80f)))
                        TakeOut(b, slot.item, selection);
                    GUI.enabled = true;
                }
            }
            GUILayout.EndScrollView();
            if (!hasUnits && inventory.UsedSlots > 0) GUILayout.Label("Select a character to take items out.");
            if (GUILayout.Button("Close")) inventoryOf = null;
            GUI.DragWindow();
        }

        static string Name(SpawnableDefinition item) => item != null ? item.DisplayName : "?";

        static void TakeOut(Building b, SpawnableDefinition item, IReadOnlyList<Unit> selection)
        {
            Unit best = null;
            float bestDistance = float.MaxValue;
            foreach (var u in selection)
            {
                if (u == null || u.IsHidden) continue;
                float d = Vector2.Distance(u.transform.position, b.GroundPoint);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = u;
                }
            }
            best?.Worker.Assign(new WithdrawJob(b, item));
        }
    }
}
