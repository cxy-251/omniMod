using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using KSerialization;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    [SerializationConfig(MemberSerialization.OptIn)]
    public sealed class ComfortRanchPolicy : KMonoBehaviour
    {
        [Serialize]
        private bool ignoreCrowding;
        [SerializeField] public bool isGroomingStation;
        [SerializeField] public bool isAquaticStation;
        internal bool IgnoreCrowding => ignoreCrowding;

        private static readonly HashSet<ComfortRanchPolicy> Active = new HashSet<ComfortRanchPolicy>();
        private readonly struct PondKey : IEquatable<PondKey>
        {
            private readonly FishOvercrowingManager.Pond pond;
            private readonly int world;
            internal PondKey(FishOvercrowingManager.Pond pond, int world) { this.pond = pond; this.world = world; }
            public bool Equals(PondKey other) => ReferenceEquals(pond, other.pond) && world == other.world;
            public override bool Equals(object obj) => obj is PondKey other && Equals(other);
            public override int GetHashCode() => (pond != null ? pond.GetHashCode() : 0) ^ world;
        }
        private static readonly HashSet<CavityInfo> ComfortableRooms = new HashSet<CavityInfo>();
        private static readonly HashSet<PondKey> ComfortablePonds = new HashSet<PondKey>();
        private static readonly System.Action<CreatureDeliveryPoint> Rebalance =
            AccessTools.MethodDelegate<System.Action<CreatureDeliveryPoint>>(
                AccessTools.DeclaredMethod(typeof(CreatureDeliveryPoint), "RebalanceFetches"));
        private static readonly AccessTools.FieldRef<BaggableCritterCapacityTracker, int> CapacityCell =
            AccessTools.FieldRefAccess<BaggableCritterCapacityTracker, int>("cavityCell");
        private static int cachedFrame = -1;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.RefreshUserMenu, RefreshMenu);
            Subscribe((int)GameHashes.CopySettings, CopySettings);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (!isGroomingStation) ignoreCrowding = false;
            if (isGroomingStation && ignoreCrowding) Active.Add(this);
            cachedFrame = -1;
        }

        protected override void OnCleanUp()
        {
            Active.Remove(this);
            cachedFrame = -1;
            base.OnCleanUp();
        }

        private void CopySettings(object data)
        {
            if (!isGroomingStation) return;
            var source = data as GameObject;
            var policy = source != null ? source.GetComponent<ComfortRanchPolicy>() : null;
            if (policy != null && policy.isGroomingStation) SetIgnoreCrowding(policy.ignoreCrowding);
        }

        private CavityInfo ResolveCavity()
        {
            if (!isGroomingStation || Game.Instance?.roomProber == null) return null;
            var station = this.GetSMI<RanchStation.Instance>();
            if (!station.IsNullOrStopped())
            {
                var cavity = station.GetCavityInfo();
                if (!cavity.IsNone()) return cavity.Unwrap();
                int workCell = station.GetRanchNavTarget();
                if (Grid.IsValidCell(workCell))
                {
                    var workRoom = Game.Instance.roomProber.GetCavityForCell(workCell);
                    if (workRoom != null) return workRoom;
                }
            }
            var straw = GetComponent<BuildingPointStraw>();
            int cell = straw != null ? straw.GetStrawCell() : Grid.PosToCell(this);
            return Grid.IsValidCell(cell) ? Game.Instance.roomProber.GetCavityForCell(cell) : null;
        }

        private FishOvercrowingManager.Pond ResolvePond(out int world)
        {
            world = -1;
            if (!isAquaticStation || FishOvercrowingManager.Instance == null) return null;
            var station = this.GetSMI<RanchStation.Instance>();
            int cell = !station.IsNullOrStopped() ? station.GetRanchNavTarget() : Grid.PosToCell(this);
            if (!Grid.IsValidCell(cell) || Grid.WorldIdx == null) return null;
            world = Grid.WorldIdx[cell];
            var pond = FishOvercrowingManager.Instance.GetPond(cell);
            var straw = GetComponent<BuildingPointStraw>();
            if (pond == null && straw != null)
            {
                int bottom = straw.GetStrawCell();
                if (Grid.IsValidCell(bottom) && Grid.WorldIdx[bottom] == world)
                    pond = FishOvercrowingManager.Instance.GetPond(bottom);
            }
            return pond;
        }

        private void SetIgnoreCrowding(bool value)
        {
            if (!isGroomingStation || ignoreCrowding == value) return;
            ignoreCrowding = value;
            if (value) Active.Add(this); else Active.Remove(this);
            cachedFrame = -1;
            CavityInfo room = ResolveCavity();
            var pond = ResolvePond(out int world);
            if (!App.isLoading)
            {
                var animals = new HashSet<KPrefabID>();
                if (room != null) animals.UnionWith(room.creatures);
                if (pond != null) animals.UnionWith(pond.fishes);
                foreach (KPrefabID animal in animals)
                {
                    if (animal == null) continue;
                    int animalCell = Grid.PosToCell(animal);
                    if (pond != null && Grid.IsValidCell(animalCell) && Grid.WorldIdx[animalCell] != world) continue;
                    animal.GetSMI<OvercrowdingMonitor.Instance>()?.RoomRefreshUpdateCavity();
                }
                foreach (CreatureDeliveryPoint point in CreatureDeliveryRegistry.Snapshot())
                {
                    var tracker = point != null ? point.GetComponent<BaggableCritterCapacityTracker>() : null;
                    if (tracker == null || !point.isSpawned || Game.Instance?.roomProber == null) continue;
                    int cell = CapacityCell(tracker);
                    if (!Grid.IsValidCell(cell)) continue;
                    bool sameRoom = room != null && Game.Instance.roomProber.GetCavityForCell(cell) == room;
                    bool samePond = pond != null && Grid.WorldIdx[cell] == world && FishOvercrowingManager.Instance.GetPond(cell) == pond;
                    if (sameRoom || samePond) Rebalance(point);
                }
            }
            OmniMod.Core.Logging.ModLog.Info($"[免拥挤] {name}：{(value ? "开" : "关")}；作业房间 {room?.NumCells ?? 0} 格，水池 {pond?.cellCount ?? 0} 格。");
            Game.Instance?.userMenu?.Refresh(gameObject);
        }

        private void RefreshMenu(object data)
        {
            if (!isGroomingStation || Game.Instance?.userMenu == null) return;
            string title = ignoreCrowding
                ? ModStrings.T("免拥挤：开", "Ignore Crowding: On").Resolve()
                : ModStrings.T("免拥挤：关", "Ignore Crowding: Off").Resolve();
            string tooltip = ignoreCrowding
                ? ModStrings.T("已开启宽松养殖：本房间／水池动物免除拥挤、狭窄和封闭惩罚，严格遵守放生点设定的数量上限；显示的仍是真实数量。点击恢复原版规则。",
                    "Comfort mode is on: critters in this room or pond ignore crowded, cramped and confined penalties, and strictly respect drop-off target limits. Counts remain real. Click to restore normal rules.").Resolve()
                : ModStrings.T("默认使用原版计数和养殖规则。点击使本房间／水池动物免除拥挤、狭窄和封闭惩罚，严格遵守放生点设定的目标数量；水生范围按同星球相连水池计算。",
                    "Uses normal counts and ranching rules by default. Click to remove crowding, cramped and confined penalties in this room, strictly respecting drop-off target limits.").Resolve();
            var room = ResolveCavity();
            var pond = ResolvePond(out _);
            tooltip += ModStrings.T($"\n作业房间：{room?.NumCells ?? 0} 格；水池：{pond?.cellCount ?? 0} 格。水生动物同时按所在水池与星球匹配。",
                $"\nWork room: {room?.NumCells ?? 0} tiles; pond: {pond?.cellCount ?? 0} tiles. Aquatic scope also matches pond and world.").Resolve();
            if (ignoreCrowding && room == null && pond == null) title += ModStrings.T("（等待区域）", " (waiting for region)").Resolve();
            Game.Instance.userMenu.AddButton(gameObject, new KIconButtonMenu.ButtonInfo(
                "action_move_to_storage", title, () => SetIgnoreCrowding(!ignoreCrowding),
                Action.NumActions, null, null, null, tooltip));
        }

        private static void RefreshRegions()
        {
            if (cachedFrame == Time.frameCount || Game.Instance?.roomProber == null || App.isLoading) return;
            cachedFrame = Time.frameCount;
            ComfortableRooms.Clear();
            ComfortablePonds.Clear();
            foreach (ComfortRanchPolicy policy in Active)
            {
                if (policy == null || !policy.isSpawned || !policy.isGroomingStation || !policy.ignoreCrowding) continue;
                var room = policy.ResolveCavity();
                if (room != null) ComfortableRooms.Add(room);
                var pond = policy.ResolvePond(out int world);
                if (pond != null) ComfortablePonds.Add(new PondKey(pond, world));
            }
        }

        private static bool IsComfortableCell(int cell, bool aquatic)
        {
            if (Active.Count == 0 || !Grid.IsValidCell(cell) || Game.Instance?.roomProber == null || App.isLoading) return false;
            RefreshRegions();
            if (ComfortableRooms.Contains(Game.Instance.roomProber.GetCavityForCell(cell))) return true;
            var manager = FishOvercrowingManager.Instance;
            if (!aquatic || manager == null) return false;
            var pond = manager.GetPond(cell);
            return pond != null && ComfortablePonds.Contains(new PondKey(pond, Grid.WorldIdx[cell]));
        }

        internal static bool HasComfort(OvercrowdingMonitor.Instance critter) =>
            IsComfortableCell(Grid.PosToCell(critter), critter.IsFish);

        // 严格遵守玩家在放生点设置的上限（如 40 只），严禁越过设定数量导致无休止运入小动物
        internal static int DeliveryLimit(BaggableCritterCapacityTracker tracker)
        {
            return tracker != null ? tracker.creatureLimit : 0;
        }

        internal static void Reset()
        {
            Active.Clear(); ComfortableRooms.Clear(); ComfortablePonds.Clear(); cachedFrame = -1;
        }
    }

    [HarmonyPatch]
    internal static class ComfortRanch_Configure_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(RanchStationConfig), "DoPostConfigureComplete", new[] { typeof(GameObject) });
            yield return AccessTools.DeclaredMethod(typeof(UnderwaterRanchStationConfig), "DoPostConfigureComplete", new[] { typeof(GameObject) });
        }
        private static void Postfix(GameObject go)
        {
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = "OmniMod_GroomingComfort".ToTag();
            var policy = go.AddOrGet<ComfortRanchPolicy>();
            policy.isGroomingStation = true;
            policy.isAquaticStation = go.GetComponent<KPrefabID>().PrefabTag == UnderwaterRanchStationConfig.ID.ToTag();
        }
    }

    // 旧存档含此组件，保留读取兼容；放生点不再显示开关，也不作为免拥挤来源。
    [HarmonyPatch]
    internal static class ComfortRanch_LegacyDropOff_Patch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.DeclaredMethod(typeof(CritterDropOffConfig), "DoPostConfigureComplete", new[] { typeof(GameObject) });
            yield return AccessTools.DeclaredMethod(typeof(FishDeliveryPointConfig), "DoPostConfigureComplete", new[] { typeof(GameObject) });
        }
        private static void Postfix(GameObject go) => go.AddOrGet<ComfortRanchPolicy>().isGroomingStation = false;
    }

    [HarmonyPatch(typeof(OvercrowdingMonitor), "AlignTagsAndEffects")]
    internal static class ComfortRanch_AnimalEffects_Patch
    {
        private static bool Prefix(OvercrowdingMonitor.Instance smi)
        {
            if (!ComfortRanchPolicy.HasComfort(smi)) return true;
            smi.kpid.RemoveTag(GameTags.Creatures.Overcrowded);
            smi.kpid.RemoveTag(GameTags.Creatures.Confined);
            smi.kpid.SetTag(GameTags.Creatures.Expecting, !smi.isBaby);
            smi.effects.Remove(smi.overcrowded.Effect);
            smi.effects.Remove(smi.futureOvercrowded.Effect);
            smi.effects.Remove(smi.confined.Effect);
            smi.overcrowded.instance = null;
            smi.futureOvercrowded.instance = null;
            smi.confined.instance = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "RebalanceFetches")]
    internal static class ComfortRanch_DeliveryLimit_Patch
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.PropertyGetter(typeof(BaggableCritterCapacityTracker), "creatureLimit");
            MethodInfo replacement = AccessTools.Method(typeof(ComfortRanchPolicy), nameof(ComfortRanchPolicy.DeliveryLimit));
            var result = new List<CodeInstruction>(instructions);
            int replacements = 0;
            foreach (CodeInstruction instruction in result)
            {
                if (!instruction.Calls(original)) continue;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replacements++;
            }
            if (replacements != 1) throw new InvalidOperationException("Unexpected drop-off capacity reads: " + replacements);
            return result;
        }
    }
}
