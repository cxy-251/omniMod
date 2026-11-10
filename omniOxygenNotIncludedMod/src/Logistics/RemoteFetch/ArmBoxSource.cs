using System;
using System.Collections.Generic;
using OmniMod.Collection;
using OmniMod.Pool;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Logistics.RemoteFetch
{
    /// <summary>按任务匹配本地取货来源，必要时从虚拟池物化；查询界面不调用此类。</summary>
    internal static class ArmBoxSource
    {
        internal static Pickupable FindMatchInWorldBox(int worldId, FetchChore chore, Storage destination)
        {
            // 快照存在也可能已脏；原先仅首次创建时刷新，普通箱新入库物品会长期不可见。
            Dictionary<Tag, List<Pickupable>> tagIndex = WorldStorageIndex.GetLocalPickupIndex(worldId);

            if (tagIndex == null) return EntityPoolAccess.ForChore(worldId, chore, null, true);

            if (chore.criteria == FetchChore.MatchCriteria.MatchID)
            {
                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        if (tagIndex.TryGetValue(t, out List<Pickupable> list) && list != null)
                        {
                            Pickupable p = CheckCandidateList(list, chore, destination, worldId);
                            if (p != null) return p;
                        }
                    }
                }
            }
            else if (chore.criteria == FetchChore.MatchCriteria.MatchTags)
            {
                if (chore.requiredTag.IsValid && tagIndex.TryGetValue(chore.requiredTag, out List<Pickupable> reqList) && reqList != null)
                {
                    Pickupable p = CheckCandidateList(reqList, chore, destination, worldId);
                    if (p != null) return p;
                }

                if (chore.tagsFirst.IsValid && tagIndex.TryGetValue(chore.tagsFirst, out List<Pickupable> firstList) && firstList != null)
                {
                    Pickupable p = CheckCandidateList(firstList, chore, destination, worldId);
                    if (p != null) return p;
                }

                if (chore.tags != null)
                {
                    foreach (Tag t in chore.tags)
                    {
                        if (t != chore.requiredTag && t != chore.tagsFirst && tagIndex.TryGetValue(t, out List<Pickupable> list) && list != null)
                        {
                            Pickupable p = CheckCandidateList(list, chore, destination, worldId);
                            if (p != null) return p;
                        }
                    }
                }
            }

            return EntityPoolAccess.ForChore(worldId, chore, null, true);
        }

        private static Pickupable CheckCandidateList(List<Pickupable> list, FetchChore chore, Storage destination, int sourceWorld)
        {
            for (int i = 0; i < list.Count; i++)
            {
                Pickupable p = list[i];
                if (p == null || p.KPrefabID == null || p.UnreservedFetchAmount <= 0f)
                {
                    continue;
                }
                if (destination != null)
                {
                    var safety = destination.GetComponent<OmniMod.Buildings.EggCrackerSafetyControl>();
                    if (safety != null && p.KPrefabID.HasTag(GameTags.Egg) && !safety.CanCrackEgg(p.KPrefabID.PrefabTag))
                        continue;
                }
                if (p.storage == null || !SolidTransferArmRemotePickup.IsRemoteSource(p) ||
                    p.storage.GetMyWorldId() != sourceWorld) continue;
                if (p.storage != null)
                {
                    if (p.storage == destination)
                    {
                        continue;
                    }
                    p.storage.allowItemRemoval = true;
                    p.storage.ignoreSourcePriority = true;
                }
                if (FetchManager.IsFetchablePickup(p, chore, destination))
                {
                    return p;
                }
            }
            return null;
        }

    }
}
