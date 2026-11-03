using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using KSerialization;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed partial class PredatorPopulation : KMonoBehaviour
    {
        [Serialize] private bool enabledCulling = true;
        [Serialize] private int selectedId = -1;
        [Serialize] private bool selectedForCull;
        private int adultCursor, adultsChecked, babyCursor, cullCursor, adultSnapshotCount = -1;
        private PredatorPopulationRules.Zone scannedZone;
        private float retryAdultsAt;
        internal bool CullingEnabled => enabledCulling;
        internal int SelectedId => selectedId;
        internal bool CullIntent => selectedForCull;
        internal bool Aquatic => this.GetComponent<CreatureBrain>()?.species == GameTags.Creatures.Species.PrehistoricPacuSpecies;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.RefreshUserMenu, RefreshMenu);
            Subscribe((int)GameHashes.SpawnedFrom, InheritHuntStatistics);
        }
        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (selectedId >= 0) PredatorPopulationRules.Claim(selectedId, this);
            RestoreHuntStatistics();
        }
        protected override void OnCleanUp() { CleanupHuntStatistics(); ClearSelection(); base.OnCleanUp(); }
        internal void Select(KPrefabID prey, bool cull)
        {
            ClearSelection(); selectedId = prey.InstanceID; selectedForCull = cull;
            PredatorPopulationRules.Claim(selectedId, this);
        }
        internal void ClearSelection()
        {
            if (selectedId >= 0) PredatorPopulationRules.Release(selectedId, this);
            selectedId = -1; selectedForCull = false;
        }
        internal bool Matches(GameObject prey) => prey != null && selectedId == prey.GetComponent<KPrefabID>().InstanceID;

        internal bool Choose(SolidConsumerMonitor.Instance consumer)
        {
            var zone = PredatorPopulationRules.GetZone(this);
            if (zone == null) return false;
            bool hungry = this.HasTag(GameTags.Creatures.Hungry);
            if (!hungry)
                return enabledCulling && this.GetDef<BabyMonitor.Def>() == null && zone.HasDropoff &&
                    PredatorPopulationRules.ChooseCull(this, consumer, zone, ref cullCursor);
            if (PredatorPopulationRules.ChooseFood(consumer, zone)) return true;
            float now = GameClock.Instance.GetTime();
            if (scannedZone != zone || adultSnapshotCount != zone.AdultCount ||
                (adultsChecked >= zone.AdultCount && now >= retryAdultsAt))
            {
                scannedZone = zone; adultSnapshotCount = zone.AdultCount;
                adultsChecked = adultCursor = 0;
            }
            if (adultsChecked < zone.AdultCount)
            {
                int visited;
                bool found = PredatorPopulationRules.ChooseMeal(this, consumer, zone, false, ref adultCursor,
                    out visited, Mathf.Min(64, zone.AdultCount - adultsChecked));
                adultsChecked += visited;
                if (found) { adultsChecked = adultCursor = 0; retryAdultsAt = 0f; return true; }
                // 分批检查完所有成体后才允许找幼体；不会因为本批没找到就越过其它成体。
                if (adultsChecked < zone.AdultCount) return false;
                adultsChecked = zone.AdultCount; retryAdultsAt = now + 4f;
            }
            int ignored;
            return PredatorPopulationRules.ChooseMeal(this, consumer, zone, true, ref babyCursor, out ignored);
        }
        private void RefreshMenu(object data)
        {
            if (Game.Instance?.userMenu == null) return;
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo("action_harvest",
                ModStrings.T("平衡猎杀：", "Balanced culling: ").Resolve() +
                ModStrings.T(enabledCulling ? "开" : "关", enabledCulling ? "On" : "Off").Resolve(),
                () => { enabledCulling = !enabledCulling; Game.Instance.userMenu.Refresh(gameObject); },
                Action.NumActions, null, null, null, ModStrings.T(
                    "只在同区域有放生点时主动猎杀成体。霸王鹦处理陆生动物，颚鱼处理水生动物；海豹只杀不吃。密度越高，冷却越短，越多猎食者可同时追猎不同目标；肉库存不限制主动猎杀。密度下降后放缓，保持种源和成长窗口口粮。正常进食先吃现成食物，再吃富余成体，最后考虑幼体。",
                    "Actively culls adults only in a region with a drop-off. Rhexes handle land critters and jawbos handle aquatic critters. Seals are killed, never eaten. Higher density shortens cooldowns and lets more hunters pursue separate targets. Food stocks never limit active culling. Slows as density falls, preserving breeding stock and maturation-window food reserves. Meals prefer existing food, surplus adults, then juveniles.").Resolve()));
        }
    }

    internal static class PredatorPopulationRules
    {
        internal sealed class Group
        {
            internal Tag adult;
            internal int adults, juveniles, protectedFirst = -1, protectedSecond = -1;
            internal readonly List<int> pendingTargets = new List<int>();
            internal float maturity = 3000f, lastKill = float.NegativeInfinity;
        }
        internal struct Prey
        {
            internal int id;
            internal Tag morph;
            internal bool baby;
            internal float age;
        }
        internal sealed class Zone
        {
            internal int world, AdultCount;
            internal bool aquatic, HasDropoff;
            internal CavityInfo cavity;
            internal FishOvercrowingManager.Pond pond;
            internal float refreshAt, demand;
            internal float density;
            internal readonly List<Prey> adults = new List<Prey>();
            internal readonly List<Prey> babies = new List<Prey>();
            internal readonly Dictionary<Tag, Group> groups = new Dictionary<Tag, Group>();
        }
        private static ConditionalWeakTable<object, Zone> zones = new ConditionalWeakTable<object, Zone>();
        private static readonly Dictionary<int, int> claims = new Dictionary<int, int>();
        private static readonly AccessTools.FieldRef<BaggableCritterCapacityTracker, int> CapacityCell =
            AccessTools.FieldRefAccess<BaggableCritterCapacityTracker, int>("cavityCell");
        private static readonly string[] EatAnims = { "eat_pre", "eat_loop", "eat_pst" };
        internal static string[] CullingAnims => EatAnims;

        internal static void Claim(int id, PredatorPopulation owner) => claims[id] = owner.GetComponent<KPrefabID>().InstanceID;
        internal static void Release(int id, PredatorPopulation owner)
        {
            if (claims.TryGetValue(id, out int actor) && actor == owner.GetComponent<KPrefabID>().InstanceID) claims.Remove(id);
        }
        private static bool ClaimedByOther(KPrefabID prey, PredatorPopulation owner)
        {
            if (!claims.TryGetValue(prey.InstanceID, out int id)) return false;
            KPrefabID actor = KPrefabIDTracker.Get().GetInstance(id);
            var policy = actor != null ? actor.GetComponent<PredatorPopulation>() : null;
            if (policy == null || policy.SelectedId != prey.InstanceID) { claims.Remove(prey.InstanceID); return false; }
            return policy != owner;
        }
        internal static bool IsHunter(GameObject go)
        {
            var brain = go != null ? go.GetComponent<CreatureBrain>() : null;
            return brain != null && (brain.species == GameTags.Creatures.Species.RaptorSpecies ||
                brain.species == GameTags.Creatures.Species.PrehistoricPacuSpecies);
        }
        private static bool IsAquatic(KPrefabID id) => id.HasTag(GameTags.Creatures.Swimmer) || id.HasTag(GameTags.SwimmingCreature);
        private static bool Alive(KPrefabID id)
        {
            if (id == null || !id.HasTag(GameTags.Creature) || id.HasTag(GameTags.Dead) || id.HasTag(GameTags.Corpse) ||
                id.HasTag(GameTags.Dying) || id.HasTag(GameTags.Creatures.Die) || id.HasTag(GameTags.Stored) ||
                id.HasTag(GameTags.Creatures.Bagged) || id.HasTag(GameTags.Trapped)) return false;
            Health health = id.GetComponent<Health>();
            return health != null && !health.IsDefeated() && id.GetComponent<PrimaryElement>()?.Mass > 0f;
        }
        private static Tag Morph(KPrefabID id) => id.GetDef<BabyMonitor.Def>()?.adultPrefab ?? id.PrefabTag;
        internal static bool Belongs(Zone zone, int cell) => Grid.IsValidCell(cell) && Grid.WorldIdx[cell] == zone.world &&
            (zone.aquatic ? FishOvercrowingManager.Instance?.GetPond(cell) == zone.pond :
                Game.Instance.roomProber.GetCavityForCell(cell) == zone.cavity);

        internal static Zone GetZone(PredatorPopulation owner)
        {
            if (App.isLoading || Game.Instance?.roomProber == null || GameClock.Instance == null) return null;
            int cell = Grid.PosToCell(owner);
            if (!Grid.IsValidCell(cell)) return null;
            bool aquatic = owner.Aquatic;
            CavityInfo cavity = Game.Instance.roomProber.GetCavityForCell(cell);
            var pond = aquatic ? FishOvercrowingManager.Instance?.GetPond(cell) : null;
            object key = aquatic ? (object)pond : cavity;
            if (key == null) return null;
            Zone zone = zones.GetValue(key, _ => new Zone());
            float now = GameClock.Instance.GetTime();
            if (zone.world != Grid.WorldIdx[cell] || zone.aquatic != aquatic)
            { zone.groups.Clear(); zone.refreshAt = 0f; }
            zone.world = Grid.WorldIdx[cell]; zone.aquatic = aquatic; zone.cavity = cavity; zone.pond = pond;
            if (now >= zone.refreshAt) Rebuild(zone, now);
            return zone;
        }

        private static float Maturity(Tag adult)
        {
            GameObject prefab = Assets.TryGetPrefab(adult);
            var fertility = prefab != null ? prefab.GetDef<FertilityMonitor.Def>() : null;
            GameObject egg = fertility != null ? Assets.TryGetPrefab(fertility.eggPrefab) : null;
            var incubation = egg != null ? egg.GetDef<IncubationMonitor.Def>() : null;
            GameObject baby = incubation != null ? Assets.TryGetPrefab(incubation.spawnedCreature) : null;
            // 藻蝌蚪等无幼体阶段的动物采用一周期调节窗口，避免套用五周期的虚构成长时间。
            return Mathf.Max(600f, (baby?.GetDef<BabyMonitor.Def>()?.adultThreshold ?? 1f) * 600f);
        }
        private static void Rebuild(Zone zone, float now)
        {
            zone.refreshAt = now + 1f; zone.adults.Clear(); zone.babies.Clear();
            zone.demand = 0f;
            float occupiedSpace = 0f;
            foreach (Group group in zone.groups.Values)
            { group.adults = group.juveniles = 0; group.protectedFirst = group.protectedSecond = -1; }
            List<KPrefabID> source = zone.aquatic ? zone.pond.fishes : zone.cavity.creatures;
            foreach (KPrefabID id in source)
            {
                if (!Alive(id) || !Belongs(zone, Grid.PosToCell(id))) continue;
                if (IsAquatic(id) != zone.aquatic) continue;
                occupiedSpace += Mathf.Max(0, id.GetDef<OvercrowdingMonitor.Def>()?.spaceRequiredPerCreature ?? 1);
                if (IsHunter(id.gameObject))
                {
                    var hunter = id.GetComponent<PredatorPopulation>();
                    if (hunter != null && hunter.Aquatic == zone.aquatic)
                    {
                        var calories = Db.Get().Amounts.Calories.Lookup(id.gameObject);
                        if (calories != null) zone.demand += Mathf.Max(0f, -calories.deltaAttribute.GetTotalValue());
                    }
                    continue;
                }
                if (IsAquatic(id) != zone.aquatic || id.GetComponent<Butcherable>() == null) continue;
                Tag morph = Morph(id);
                if (!zone.groups.TryGetValue(morph, out Group group))
                    zone.groups[morph] = group = new Group { adult = morph, maturity = Maturity(morph) };
                bool baby = id.GetDef<BabyMonitor.Def>() != null;
                var age = Db.Get().Amounts.Age.Lookup(id.gameObject);
                var record = new Prey { id = id.InstanceID, morph = morph, baby = baby,
                    age = age != null && age.GetMax() > 0f ? age.value / age.GetMax() : 0f };
                if (baby) { group.juveniles++; zone.babies.Add(record); }
                else { group.adults++; zone.adults.Add(record); }
            }
            int availableSpace = zone.aquatic ? zone.pond.cellCount : zone.cavity.NumCells;
            zone.density = occupiedSpace / Mathf.Max(1, availableSpace);
            zone.adults.Sort((a, b) => b.age.CompareTo(a.age));
            zone.babies.Sort((a, b) => b.age.CompareTo(a.age));
            foreach (Prey record in zone.adults)
            {
                Group group = zone.groups[record.morph];
                group.protectedSecond = group.protectedFirst; group.protectedFirst = record.id;
            }
            zone.AdultCount = zone.adults.Count;
            zone.HasDropoff = false;
            foreach (CreatureDeliveryPoint point in CreatureDeliveryRegistry.Snapshot())
            {
                if (point == null) continue;
                var tracker = point.GetComponent<BaggableCritterCapacityTracker>();
                int cell = tracker != null ? CapacityCell(tracker) : Grid.OffsetCell(Grid.PosToCell(point), point.spawnOffset);
                if (Belongs(zone, cell)) { zone.HasDropoff = true; break; }
            }
        }
        internal static bool ChooseFood(SolidConsumerMonitor.Instance consumer, Zone zone)
        {
            int origin = Grid.PosToCell(consumer), x = origin % Grid.WidthInCells - 8, y = origin / Grid.WidthInCells - 8;
            foreach (CreatureFeeder feeder in Components.CreatureFeeders.GetItems(zone.world))
            {
                if (feeder == null || feeder.storages == null) continue;
                Vector2I cell = feeder.GetTargetFeederCell();
                if (cell.x < x || cell.x > x + 32 || cell.y < y || cell.y > y + 32 ||
                    !Belongs(zone, Grid.XYToCell(cell.x, cell.y))) continue;
                int cost = consumer.GetCost(Grid.XYToCell(cell.x, cell.y));
                if (cost < 0) continue;
                foreach (Storage storage in feeder.storages)
                    if (storage != null) foreach (GameObject item in storage.items)
                    {
                        var pickup = item != null ? item.GetComponent<Pickupable>() : null;
                        if (!EdibleFood(consumer.diet, pickup) || consumer.GetCost(Grid.PosToCell(item)) < 0) continue;
                        consumer.SetTargetEdible(item, cost); consumer.targetEdibleOffset = Vector3.zero; return true;
                    }
            }
            // 与原版散落食物搜索范围一致；直接选中，避免缓存里有食物、原版却找不到而持续饿着。
            var entries = ListPool<ScenePartitionerEntry, GameScenePartitioner>.Allocate();
            try
            {
                GameScenePartitioner.Instance.GatherEntries(x, y, 16, 16, GameScenePartitioner.Instance.pickupablesLayer, entries);
                foreach (ScenePartitionerEntry entry in entries)
                {
                    var pickup = entry.obj as Pickupable;
                    if (!EdibleFood(consumer.diet, pickup) || pickup.storage != null || !Belongs(zone, Grid.PosToCell(pickup))) continue;
                    int cost = consumer.GetCost(pickup.gameObject);
                    if (cost < 0) continue;
                    consumer.SetTargetEdible(pickup.gameObject, cost); consumer.targetEdibleOffset = Vector3.zero; return true;
                }
                return false;
            }
            finally { entries.Recycle(); }
        }
        private static bool EdibleFood(Diet diet, Pickupable pickup) => pickup != null && pickup.PrimaryElement != null &&
            pickup.PrimaryElement.Mass > 0f && !pickup.HasTag(GameTags.Creature) &&
            diet.GetDietInfo(pickup.PrefabID())?.foodType == Diet.Info.FoodType.EatSolid;
        private static bool IsSeal(Tag tag) => tag == SealConfig.ID.ToTag() || tag == BabySealConfig.ID.ToTag();
        private static float LiveCalories(Diet diet, Tag adult)
        {
            Diet.Info info = diet.GetDietInfo(adult);
            return info != null && (info.foodType == Diet.Info.FoodType.EatPrey || info.foodType == Diet.Info.FoodType.EatButcheredPrey)
                ? diet.AvailableCaloriesInPrey(adult) : 0f;
        }
        private static int Reserve(Zone zone, Group group, Diet diet)
        {
            float calories = LiveCalories(diet, group.adult);
            return calories > 0f ? 2 + Mathf.CeilToInt(zone.demand * group.maturity / calories) : 2;
        }
        private static bool CanCullGroup(Zone zone, Group group, Diet diet)
        {
            int reserved = Reserve(zone, group, diet);
            if (group.adults <= reserved || GameClock.Instance.GetTime() - group.lastKill < CullInterval(zone, group, diet)) return false;
            // 待追猎名额随密度增减；每个目标依然独占，失效追击释放名额而不消耗冷却。
            for (int i = group.pendingTargets.Count - 1; i >= 0; i--)
            {
                int target = group.pendingTargets[i];
                if (claims.TryGetValue(target, out int actorId))
                {
                    var actor = KPrefabIDTracker.Get().GetInstance(actorId);
                    var policy = actor != null ? actor.GetComponent<PredatorPopulation>() : null;
                    var prey = KPrefabIDTracker.Get().GetInstance(target);
                    if (policy != null && policy.CullIntent && policy.SelectedId == target && Alive(prey) &&
                        Belongs(zone, Grid.PosToCell(actor)) && Belongs(zone, Grid.PosToCell(prey))) continue;
                }
                group.pendingTargets.RemoveAt(i);
            }
            return group.pendingTargets.Count < ParallelHunts(zone) && group.adults - group.pendingTargets.Count > reserved;
        }
        private static int ParallelHunts(Zone zone) => Mathf.Clamp(1 + Mathf.FloorToInt(Mathf.Max(0f, zone.density - 1f) * 2f), 1, 8);
        private static float CullInterval(Zone zone, Group group, Diet diet)
        {
            float interval = group.maturity / Mathf.Max(1, group.adults - Reserve(zone, group, diet));
            float baseline = zone.aquatic ? Mathf.Clamp(interval * 0.2f, 3f, 300f) : Mathf.Clamp(interval, 10f, 3600f);
            // 密度 1 为原生空间需求恰好占满；2、4、8 倍密度分别缩至 1/4、1/16、1/64。
            float density = Mathf.Clamp(zone.density, 1f, 16f);
            return Mathf.Max(0.5f, baseline / (density * density));
        }
        internal static string DensitySummary(PredatorPopulation owner)
        {
            Zone zone = GetZone(owner);
            if (zone == null) return "";
            string mode = zone.density > 1f ? ModStrings.T("过密加速", "Overpopulation cleanup").Resolve() :
                ModStrings.T("正常节奏", "Normal pace").Resolve();
            return ModStrings.T($"\n区域密度：{zone.density:0.0} 倍（{mode}）。1 倍表示动物的原版空间需求恰好占满所在房间／水体。",
                $"\nRegional density: {zone.density:0.0}x ({mode}). 1x means native critter space requirements fill the room or connected pond.").Resolve();
        }
        internal static bool CanCull(PredatorPopulation owner, SolidConsumerMonitor.Instance consumer)
        {
            if (!owner.CullingEnabled || owner.GetDef<BabyMonitor.Def>() != null || owner.HasTag(GameTags.Creatures.Hungry)) return false;
            Zone zone = GetZone(owner);
            if (zone == null || !zone.HasDropoff) return false;
            if (owner.SelectedId >= 0 && owner.CullIntent) return true;
            foreach (Group group in zone.groups.Values) if (CanCullGroup(zone, group, consumer.diet)) return true;
            return false;
        }
        private static KPrefabID Candidate(PredatorPopulation owner, Zone zone, Prey record)
        {
            KPrefabID id = KPrefabIDTracker.Get().GetInstance(record.id);
            if (!Alive(id) || IsHunter(id.gameObject) || IsAquatic(id) != zone.aquatic ||
                id.HasTag(GameTags.Creatures.ReservedByCreature) || ClaimedByOther(id, owner) || !Belongs(zone, Grid.PosToCell(id))) return null;
            return id;
        }
        private static bool Protect(Group group, int id) => group.adults <= 2 || id == group.protectedFirst || id == group.protectedSecond;
        internal static bool ChooseMeal(PredatorPopulation owner, SolidConsumerMonitor.Instance consumer, Zone zone,
            bool babies, ref int cursor, out int visited, int budget = 64)
        {
            visited = 0; List<Prey> records = babies ? zone.babies : zone.adults;
            for (int i = 0, count = Mathf.Min(budget, records.Count); i < count; i++)
            {
                if (cursor >= records.Count) cursor = 0;
                Prey record = records[cursor++]; visited++;
                Group group = zone.groups[record.morph];
                if ((!babies && Protect(group, record.id)) || (babies && group.juveniles <= 1)) continue;
                KPrefabID prey = Candidate(owner, zone, record);
                if (prey == null || IsSeal(prey.PrefabTag) || consumer.diet.GetDietInfo(prey.PrefabTag) == null) continue;
                int cost = consumer.GetCost(prey.gameObject);
                if (cost < 0) continue;
                owner.Select(prey, false); consumer.SetTargetEdible(prey.gameObject, cost);
                consumer.targetEdibleOffset = consumer.GetBestEdibleOffset(prey.gameObject); return true;
            }
            return false;
        }
        internal static bool ChooseCull(PredatorPopulation owner, SolidConsumerMonitor.Instance consumer, Zone zone, ref int cursor)
        {
            int paths = 0;
            bool crowded = zone.density > 1f;
            KPrefabID best = null;
            int bestCost = int.MaxValue;
            for (int i = 0, count = Mathf.Min(64, zone.adults.Count); i < count; i++)
            {
                if (cursor >= zone.adults.Count) cursor = 0;
                Prey record = zone.adults[cursor++];
                Group group = zone.groups[record.morph];
                if (Protect(group, record.id) || !CanCullGroup(zone, group, consumer.diet)) continue;
                KPrefabID prey = Candidate(owner, zone, record);
                if (prey == null) continue;
                if (++paths > 32) break;
                int cost = consumer.GetCost(prey.gameObject);
                if (cost < 0 || cost >= bestCost) continue;
                best = prey; bestCost = cost;
                // 过密时优先本批里更近的成体，减少穿过整个鱼池追最老一只的时间。
                if (!crowded || cost < 3) break;
            }
            if (best == null) return false;
            if (!crowded) cursor = 0;
            owner.Select(best, true); consumer.SetTargetEdible(best.gameObject, bestCost);
            zone.groups[Morph(best)].pendingTargets.Add(best.InstanceID);
            consumer.targetEdibleOffset = consumer.GetBestEdibleOffset(best.gameObject); return true;
        }
        private static int RemainingAdults(Zone zone, Tag morph, int target)
        {
            int count = 0;
            var source = zone.aquatic ? zone.pond.fishes : zone.cavity.creatures;
            foreach (KPrefabID id in source)
                if (Alive(id) && id.GetDef<BabyMonitor.Def>() == null && Morph(id) == morph &&
                    Belongs(zone, Grid.PosToCell(id)) && (id.InstanceID == target || !id.HasTag(GameTags.Creatures.ReservedByCreature))) count++;
            return count;
        }
        internal static bool Complete(PredatorPopulation owner, SolidConsumerMonitor.Instance consumer, KPrefabID prey)
        {
            if (prey == null || owner.SelectedId != prey.InstanceID) return true;
            Zone zone = GetZone(owner);
            Tag morph = Morph(prey);
            bool baby = prey.GetDef<BabyMonitor.Def>() != null;
            bool valid = zone != null && Alive(prey) && !IsHunter(prey.gameObject) &&
                IsAquatic(prey) == zone.aquatic && Belongs(zone, Grid.PosToCell(prey));
            if (!baby && valid && RemainingAdults(zone, morph, prey.InstanceID) <= 2) valid = false;
            if (!owner.CullIntent)
            {
                valid = valid && !IsSeal(prey.PrefabTag);
                if (baby && valid)
                {
                    int juveniles = 0;
                    var source = zone.aquatic ? zone.pond.fishes : zone.cavity.creatures;
                    foreach (KPrefabID id in source)
                        if (Alive(id) && id.GetDef<BabyMonitor.Def>() != null && Morph(id) == morph &&
                            Belongs(zone, Grid.PosToCell(id)) &&
                            (id == prey || !id.HasTag(GameTags.Creatures.ReservedByCreature))) juveniles++;
                    valid = juveniles > 1;
                }
                if (valid) return true;
                consumer.ClearTargetEdible(); return false;
            }
            // 走原版死亡监视器，不清零质量、不手动 CreateDrops，也不结算进食热量或排泄。
            valid = valid && owner.CullingEnabled && !baby && !owner.HasTag(GameTags.Creatures.Hungry) &&
                zone.HasDropoff && zone.groups.TryGetValue(morph, out Group group) &&
                RemainingAdults(zone, morph, prey.InstanceID) > Reserve(zone, group, consumer.diet);
            if (valid)
            {
                var death = prey.GetSMI<DeathMonitor.Instance>();
                if (death != null)
                {
                    owner.TrackCull(prey);
                    death.Kill(Db.Get().Deaths.Slain);
                    Group completedGroup = zone.groups[morph];
                    completedGroup.lastKill = GameClock.Instance.GetTime();
                    completedGroup.pendingTargets.Remove(prey.InstanceID);
                    zone.refreshAt = 0f;
                }
            }
            consumer.ClearTargetEdible(); return false;
        }
        internal static void Reset() { zones = new ConditionalWeakTable<object, Zone>(); claims.Clear(); }
    }

    [HarmonyPatch(typeof(RaptorConfig), nameof(RaptorConfig.CreateRaptor))]
    internal static class RaptorPopulation_Configure
    {
        private static void Postfix(GameObject __result) { if (__result != null) __result.AddOrGet<PredatorPopulation>(); }
    }
    [HarmonyPatch(typeof(BasePrehistoricPacuConfig), nameof(BasePrehistoricPacuConfig.CreatePrefab))]
    internal static class JawboPopulation_Configure
    {
        private static void Postfix(GameObject __result) { if (__result != null) __result.AddOrGet<PredatorPopulation>(); }
    }
    [HarmonyPatch(typeof(SolidConsumerMonitor), "IsSportHuntingConditionMet")]
    internal static class PredatorPopulation_Sport
    {
        private static bool Prefix(SolidConsumerMonitor.Instance smi, ref bool __result)
        {
            var owner = smi.GetComponent<PredatorPopulation>();
            if (owner == null) return true;
            __result = PredatorPopulationRules.CanCull(owner, smi); return false;
        }
    }
    [HarmonyPatch(typeof(SolidConsumerMonitor.Instance), nameof(SolidConsumerMonitor.Instance.GetTargetEdibleEatAnims))]
    internal static class PredatorPopulation_Anims
    {
        private static bool Prefix(SolidConsumerMonitor.Instance __instance, ref string[] __result)
        {
            var owner = __instance.GetComponent<PredatorPopulation>();
            if (owner == null || !owner.CullIntent || !owner.Matches(__instance.targetEdible)) return true;
            __result = PredatorPopulationRules.CullingAnims; return false;
        }
    }
    [HarmonyPatch(typeof(SolidConsumerMonitor.Instance), nameof(SolidConsumerMonitor.Instance.ClearTargetEdible))]
    internal static class PredatorPopulation_Clear
    {
        private static void Prefix(SolidConsumerMonitor.Instance __instance) => __instance.GetComponent<PredatorPopulation>()?.ClearSelection();
    }
    [HarmonyPatch(typeof(SolidConsumerMonitor.Instance), nameof(SolidConsumerMonitor.Instance.OnEatSolidComplete))]
    internal static class PredatorPopulation_Complete
    {
        internal struct MealReceipt
        {
            internal PredatorPopulation owner;
            internal PrimaryElement element;
            internal string prefab;
        }
        private static bool Prefix(SolidConsumerMonitor.Instance __instance, object data, out MealReceipt __state)
        {
            __state = default;
            var owner = __instance.GetComponent<PredatorPopulation>();
            var prey = data as KPrefabID;
            bool runOriginal = owner == null || PredatorPopulationRules.Complete(owner, __instance, prey);
            if (runOriginal && owner != null && prey != null && prey.HasTag(GameTags.Creature))
            {
                var element = prey.GetComponent<PrimaryElement>();
                var info = __instance.diet.GetDietInfo(prey.PrefabTag);
                if (element != null && element.Mass > 0f && info != null &&
                    (info.foodType == Diet.Info.FoodType.EatPrey || info.foodType == Diet.Info.FoodType.EatButcheredPrey))
                    __state = new MealReceipt { owner = owner, element = element, prefab = prey.PrefabTag.ToString() };
            }
            return runOriginal;
        }
        private static void Postfix(MealReceipt __state)
        {
            // 原版成功吞食活体才会清零其质量；仅选中、扑空、受保护或吃掉落肉均不计。
            if (__state.owner != null && (__state.element == null || __state.element.Mass <= 0f) && __state.prefab != null)
                __state.owner.RecordHunt(__state.prefab, false);
        }
    }
    [HarmonyPatch(typeof(SolidConsumerMonitor.Instance), nameof(SolidConsumerMonitor.Instance.IsTargetEdibleValid))]
    internal static class PredatorPopulation_Validate
    {
        private static void Postfix(SolidConsumerMonitor.Instance __instance, ref bool __result)
        {
            if (!__result) return;
            var owner = __instance.GetComponent<PredatorPopulation>();
            if (owner != null && owner.CullIntent && owner.HasTag(GameTags.Creatures.Hungry)) __result = false;
        }
    }
    [HarmonyPatch(typeof(EatStates), "CheckHuntSuccess")]
    internal static class PredatorPopulation_HuntSuccess
    {
        private static bool Prefix(EatStates.Instance smi, ref bool __result)
        {
            var owner = smi.GetComponent<PredatorPopulation>();
            if (owner == null || !owner.Matches(smi.Edible)) return true;
            __result = true; return false;
        }
    }
    [HarmonyPatch(typeof(Game), "OnDestroy")]
    internal static class PredatorPopulation_Reset
    {
        private static void Postfix() { PredatorPopulationRules.Reset(); PredatorHuntStatistics.Reset(); }
    }
}
