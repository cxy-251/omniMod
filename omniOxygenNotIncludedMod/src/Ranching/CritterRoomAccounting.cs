using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Collection;
using OmniMod.Pool;

namespace OmniMod.Ranching
{
    /// <summary>仅隔离共享库存实体；活体名册和数量统计全部交由原版。</summary>
    internal static class CritterRoomAccounting
    {
        private static readonly HashSet<KPrefabID> SuppressedFish = new HashSet<KPrefabID>();
        private static readonly AccessTools.FieldRef<FishOvercrowingManager, List<KPrefabID>> AquaticEntities =
            AccessTools.FieldRefAccess<FishOvercrowingManager, List<KPrefabID>>("allAquaticEntities");

        internal static bool IsShared(KPrefabID entity)
        {
            if (entity == null) return false;
            Storage storage = entity.GetComponent<Pickupable>()?.storage;
            return storage != null && (OmniEntityPool.IsPool(storage) ||
                storage.GetComponent<GlobalItemCollector>() != null);
        }

        internal static bool UpdateRegion(OvercrowdingMonitor.Instance monitor)
        {
            if (Game.Instance?.roomProber == null || monitor.kpid == null) return true;
            KPrefabID entity = monitor.kpid;
            FishOvercrowingManager fishManager = FishOvercrowingManager.Instance;
            if (IsShared(entity))
            {
                if (monitor.cavity != null)
                {
                    monitor.RemoveFromCavity();
                    monitor.cavity = null;
                    monitor.regionAnalysis.ForceDirty();
                }
                if (fishManager != null && monitor.IsFish && SuppressedFish.Add(entity))
                    RemoveAquatic(fishManager, entity);
                monitor.pond = null;
                return false;
            }
            if (fishManager != null && SuppressedFish.Remove(entity)) fishManager.Add(entity);
            return true;
        }

        private static void RemoveAquatic(FishOvercrowingManager manager, KPrefabID entity)
        {
            List<KPrefabID> entities = AquaticEntities(manager);
            for (int i = entities.Count - 1; i >= 0; i--)
                if (entities[i] == entity) entities.RemoveAt(i);
        }

        internal static bool RegisterAquatic(FishOvercrowingManager manager, KPrefabID entity)
        {
            if (entity == null) return false;
            if (IsShared(entity))
            {
                SuppressedFish.Add(entity);
                RemoveAquatic(manager, entity);
                return false;
            }
            SuppressedFish.Remove(entity);
            return true;
        }

        internal static void Forget(KPrefabID entity) { if (entity != null) SuppressedFish.Remove(entity); }
        internal static void Reset() { SuppressedFish.Clear(); }
    }

    [HarmonyPatch(typeof(OvercrowdingMonitor), "UpdateRegion")]
    internal static class OvercrowdingMonitor_SharedPoolRegion_Patch
    {
        private static bool Prefix(OvercrowdingMonitor.Instance smi) => CritterRoomAccounting.UpdateRegion(smi);
    }

    [HarmonyPatch(typeof(OvercrowdingMonitor.Instance), "OnCleanUp")]
    internal static class OvercrowdingMonitor_AccountingCleanup_Patch
    {
        private static void Postfix(OvercrowdingMonitor.Instance __instance) =>
            CritterRoomAccounting.Forget(__instance.kpid);
    }

    [HarmonyPatch(typeof(FishOvercrowingManager), nameof(FishOvercrowingManager.Add))]
    internal static class FishOvercrowingManager_SharedPoolRegister_Patch
    {
        private static bool Prefix(FishOvercrowingManager __instance, KPrefabID aquaticEntity) =>
            CritterRoomAccounting.RegisterAquatic(__instance, aquaticEntity);
    }
}
