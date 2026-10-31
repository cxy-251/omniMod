using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Logistics.RemoteFetch;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>按类型登记增幅块，让技能面板在分配前也能看到可跨星调拨的实体。</summary>
    internal static class BionicUpgradeRegistry
    {
        private static readonly Dictionary<Tag, List<BionicUpgradeComponent>> Upgrades =
            new Dictionary<Tag, List<BionicUpgradeComponent>>();

        internal static void Register(BionicUpgradeComponent upgrade)
        {
            Tag tag = upgrade.PrefabID();
            if (!Upgrades.TryGetValue(tag, out List<BionicUpgradeComponent> list))
                Upgrades[tag] = list = new List<BionicUpgradeComponent>();
            if (!list.Contains(upgrade)) list.Add(upgrade);
        }

        internal static void Unregister(BionicUpgradeComponent upgrade)
        {
            if (Upgrades.TryGetValue(upgrade.PrefabID(), out List<BionicUpgradeComponent> list))
                list.Remove(upgrade);
        }

        internal static GlobalItemCollector GetDestinationBox(int worldId)
        {
            GlobalItemCollector local = AggregateBoxRegistry.GetActiveJunkBoxForWorld(worldId);
            if (local != null) return local;
            WorldContainer world = ClusterManager.Instance != null ? ClusterManager.Instance.GetWorld(worldId) : null;
            // 舱内没有箱子时，允许从同一入口所属星球的箱子领取，最终仍由 Navigator 检查路径。
            return world != null && world.IsModuleInterior && world.ParentWorldId != worldId
                ? AggregateBoxRegistry.GetActiveJunkBoxForWorld(world.ParentWorldId) : null;
        }

        internal static bool IsDispatchSource(Pickupable pickup)
        {
            if (pickup == null || pickup.ReservedAmount > 0f || pickup.IsEntombed || pickup.KPrefabID == null ||
                pickup.KPrefabID.HasTag(GameTags.MarkedForMove)) return false;
            Storage storage = pickup.storage;
            if (storage == null)
                return !pickup.KPrefabID.HasTag(GameTags.Stored) &&
                       GetDestinationBox(pickup.GetMyWorldId()) != null;
            if (!SolidTransferArmRemotePickup.IsRemoteStorage(storage)) return false;
            return AggregateBoxRegistry.IsBoxStorage(storage) ||
                   GetDestinationBox(storage.GetMyWorldId()) != null;
        }

        internal static void AppendToInventory(WorldInventory inventory, Tag tag, ref List<Pickupable> result)
        {
            WorldContainer world = inventory != null ? inventory.WorldContainer : null;
            if (world == null || !Game.IsOnMainThread() || !Upgrades.TryGetValue(tag, out var upgrades) ||
                GetDestinationBox(world.id) == null) return;
            var seen = result != null ? new HashSet<Pickupable>(result) : new HashSet<Pickupable>();
            for (int i = 0; i < upgrades.Count; i++)
            {
                BionicUpgradeComponent upgrade = upgrades[i];
                Pickupable pickup = upgrade != null ? upgrade.GetComponent<Pickupable>() : null;
                if (!IsDispatchSource(pickup) || !seen.Add(pickup)) continue;
                if (result == null) result = new List<Pickupable>();
                result.Add(pickup);
            }
        }
    }

    [HarmonyPatch(typeof(BionicUpgradeComponent), "OnSpawn")]
    internal static class BionicUpgrade_OnSpawn_Registry_Patch
    {
        private static void Postfix(BionicUpgradeComponent __instance) => BionicUpgradeRegistry.Register(__instance);
    }

    [HarmonyPatch(typeof(Assignable), "OnCleanUp")]
    internal static class BionicUpgrade_OnCleanUp_Registry_Patch
    {
        private static void Prefix(Assignable __instance)
        {
            if (__instance is BionicUpgradeComponent upgrade) BionicUpgradeRegistry.Unregister(upgrade);
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.CreatePickupablesList))]
    internal static class WorldInventory_CreatePickupablesList_Boosters_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, ref List<Pickupable> __result) =>
            BionicUpgradeRegistry.AppendToInventory(__instance, tag, ref __result);
    }
}
