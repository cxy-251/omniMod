using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Logistics.RemoteFetch;

namespace OmniMod.Collection
{
    /// <summary>按元素登记液气 Pickupable；取货时再检查实时归属，不扫描全星球物品。</summary>
    internal static class BottlePickupRegistry
    {
        private static readonly Dictionary<Tag, List<Pickupable>> ByElement = new Dictionary<Tag, List<Pickupable>>();
        private static readonly Dictionary<Pickupable, Tag> Registered = new Dictionary<Pickupable, Tag>();

        internal static void Register(Pickupable pickup)
        {
            PrimaryElement primary = pickup != null ? pickup.PrimaryElement : null;
            Element element = primary != null ? primary.Element : null;
            if (element == null || (!element.IsLiquid && !element.IsGas) || Registered.ContainsKey(pickup)) return;
            Tag tag = pickup.KPrefabID.PrefabTag;
            if (!ByElement.TryGetValue(tag, out List<Pickupable> list)) ByElement[tag] = list = new List<Pickupable>();
            list.Add(pickup);
            Registered[pickup] = tag;
        }

        internal static void Unregister(Pickupable pickup)
        {
            if (ReferenceEquals(pickup, null) || !Registered.TryGetValue(pickup, out Tag tag)) return;
            Registered.Remove(pickup);
            if (ByElement.TryGetValue(tag, out List<Pickupable> list))
            {
                list.Remove(pickup);
                if (list.Count == 0) ByElement.Remove(tag);
            }
        }

        internal static Pickupable FindMatch(int world, FetchChore chore, Storage destination)
        {
            if (chore.criteria == FetchChore.MatchCriteria.MatchID && chore.tags != null)
            {
                foreach (Tag tag in chore.tags)
                {
                    if (!ByElement.TryGetValue(tag, out List<Pickupable> list)) continue;
                    Pickupable match = FindInList(list, world, chore, destination);
                    if (match != null) return match;
                }
                return null;
            }
            if (ByElement.TryGetValue(chore.tagsFirst, out List<Pickupable> exact))
                return FindInList(exact, world, chore, destination);
            foreach (List<Pickupable> list in ByElement.Values)
            {
                Pickupable match = FindInList(list, world, chore, destination);
                if (match != null) return match;
            }
            return null;
        }

        private static Pickupable FindInList(List<Pickupable> list, int world, FetchChore chore, Storage destination)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Pickupable pickup = list[i];
                if (pickup == null || pickup.storage == destination || pickup.UnreservedFetchAmount <= 0f ||
                    (world >= 0 && pickup.GetMyWorldId() != world) ||
                    !SolidTransferArmRemotePickup.IsRemoteSource(pickup)) continue;
                if (pickup.storage != null) pickup.storage.ignoreSourcePriority = true;
                if (FetchManager.IsFetchablePickup(pickup, chore, destination)) return pickup;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Pickupable), "OnSpawn")]
    internal static class Pickupable_OnSpawn_BottleIndex_Patch
    {
        private static void Postfix(Pickupable __instance) => BottlePickupRegistry.Register(__instance);
    }

    [HarmonyPatch(typeof(Pickupable), "OnCleanUp")]
    internal static class Pickupable_OnCleanUp_BottleIndex_Patch
    {
        private static void Prefix(Pickupable __instance) => BottlePickupRegistry.Unregister(__instance);
    }
}
