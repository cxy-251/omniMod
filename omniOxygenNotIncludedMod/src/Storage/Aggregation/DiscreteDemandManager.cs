using System.Collections.Generic;
using OmniMod.Core.Safety;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星系离散物品（种子、增幅块、电池、宇航服、蛋、神器等）按需调拨与归还管理器：
    /// 确保离散物品 100% 共享、绝不均分，按各星球当前实际任务需求即时调拨。
    /// </summary>
    public static class DiscreteDemandManager
    {
        private static readonly List<KeyValuePair<Tag, int>> _seedScratch = new List<KeyValuePair<Tag, int>>(32);

        public static bool IsDiscreteManaged(GameObject go)
        {
            if (go == null || LivingThingsSafety.IsLivingThing(go)) return false;
            Pickupable p = go.GetComponent<Pickupable>();
            if (p != null && BulkBufferManager.IsBulkSolidElementChunk(p)) return false;
            return true;
        }

        public static bool WorldHasLiveBionicMinion(int worldId)
        {
            if (worldId < 0 || worldId == 255 || Components.LiveMinionIdentities == null)
            {
                return false;
            }
            List<MinionIdentity> minions = Components.LiveMinionIdentities.GetWorldItems(worldId, checkChildWorlds: true);
            for (int i = 0; i < minions.Count; i++)
            {
                MinionIdentity m = minions[i];
                if (m != null && (m.model == BionicMinionConfig.MODEL || m.HasTag(GameTags.Minions.Models.Bionic)))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsStorageToStorageDestination(Storage dest)
        {
            if (dest == null) return true;
            return dest.GetComponent<StorageLocker>() != null ||
                   dest.GetComponent<ObjectDispenser>() != null ||
                   AggregateBoxRegistry.IsBoxStorage(dest);
        }

        public static float GetUnreservedTagMass(Storage storage, Tag prefabTag)
        {
            if (storage == null || storage.items == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < storage.items.Count; i++)
            {
                GameObject go = storage.items[i];
                if (go == null) continue;
                KPrefabID id = go.GetComponent<KPrefabID>();
                Pickupable p = go.GetComponent<Pickupable>();
                if (id != null && p != null && p.ReservedAmount <= 0f && id.PrefabTag == prefabTag)
                {
                    sum += p.TotalAmount;
                }
            }
            return sum;
        }

        public static void MigratePoolSeedsToHomeStorage(GlobalItemCollector collector, Storage storage)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || storage == null) return;

            _seedScratch.Clear();
            foreach (KeyValuePair<Tag, int> pair in pool.EnumerateSeeds())
            {
                if (pair.Value > 0)
                {
                    _seedScratch.Add(pair);
                }
            }
            if (_seedScratch.Count == 0) return;

            for (int i = 0; i < _seedScratch.Count; i++)
            {
                Tag seedTag = _seedScratch[i].Key;
                int count = _seedScratch[i].Value;
                if (count <= 0 || !pool.RemoveSeed(seedTag, count)) continue;

                GameObject prefab = Assets.TryGetPrefab(seedTag);
                if (prefab == null) continue;

                GameObject inst = GameUtil.KInstantiate(prefab, collector.transform.GetPosition(), Grid.SceneLayer.Ore);
                inst.SetActive(true);
                PrimaryElement pe = inst.GetComponent<PrimaryElement>();
                if (pe != null && count > 1)
                {
                    pe.Units = count;
                }
                storage.Store(inst, hide_popups: true);
            }
        }

        public static void ServeDiscreteDemand(GlobalItemCollector collector, Storage storage)
        {
            if (storage == null || storage.items == null || AggregateBoxRegistry.DiscreteHome == collector)
            {
                return;
            }

            int myWorld = collector.GetMyWorldId();
            int myParentWorld = collector.GetMyParentWorldId();
            if (myWorld < 0 || myWorld == 255) return;

            ExceptionShield.Execute("DiscreteDemandManager.ServeDemand", () =>
            {
                // 1. 调拨已分配给本星球小人的增幅块与衣物装备
                if (Components.AssignableItems != null)
                {
                    List<Assignable> assignables = Components.AssignableItems.Items;
                    for (int i = 0; i < assignables.Count; i++)
                    {
                        Assignable a = assignables[i];
                        if (a == null || a.gameObject == null || !a.IsAssigned()) continue;
                        if (a is Equippable eq && eq.isEquipped) continue;
                        if (a.HasTag(GameTags.StoredPrivate)) continue;

                        int targetWorld = AggregateBoxRegistry.GetAssigneeWorldId(a.assignee);
                        if (targetWorld == myWorld && a.GetMyWorldId() != myWorld)
                        {
                            AggregateBoxRegistry.TransferItemToWorldJunkBox(a.gameObject, myWorld);
                        }
                    }
                }

                // 2. 若本星球有仿生人，常备最多 2 块便携电池
                if (WorldHasLiveBionicMinion(myWorld))
                {
                    AggregateBoxRegistry.EnsureDiscreteTagInWorldJunkBox(GameTags.ChargedPortableBattery, Tag.Invalid, myWorld, 2f);
                }

                // 3. 响应本星球活跃 FetchChore
                if (GlobalChoreProvider.Instance == null ||
                    !GlobalChoreProvider.Instance.fetchMap.TryGetValue(myParentWorld, out List<FetchChore> chores) ||
                    chores == null)
                {
                    return;
                }

                GlobalItemCollector donor = AggregateBoxRegistry.DiscreteHome;
                if (donor == null || donor == collector || donor.BoxStorage == null || donor.BoxStorage.items == null)
                {
                    return;
                }

                for (int cIdx = 0; cIdx < chores.Count; cIdx++)
                {
                    FetchChore fc = chores[cIdx];
                    if (fc == null || fc.destination == null || fc.fetchTarget != null || fc.driver != null || fc.amount <= 0f)
                    {
                        continue;
                    }
                    if (IsStorageToStorageDestination(fc.destination))
                    {
                        continue;
                    }

                    if (fc.criteria == FetchChore.MatchCriteria.MatchID && fc.tags != null)
                    {
                        bool isBulk = false;
                        foreach (Tag t in fc.tags)
                        {
                            Element elem = ElementLoader.FindElementByHash(ElementLoader.GetElementID(t));
                            if (elem != null && elem.IsSolid)
                            {
                                isBulk = true;
                                break;
                            }
                        }
                        if (isBulk) continue;
                    }

                    bool alreadyHave = false;
                    for (int i = 0; i < storage.items.Count; i++)
                    {
                        GameObject localGo = storage.items[i];
                        Pickupable localP = localGo != null ? localGo.GetComponent<Pickupable>() : null;
                        if (localP != null && localP.UnreservedFetchAmount > 0f && FetchManager.IsFetchablePickup(localP, fc, fc.destination))
                        {
                            alreadyHave = true;
                            break;
                        }
                    }
                    if (alreadyHave) continue;

                    List<GameObject> donorItems = donor.BoxStorage.items;
                    for (int i = donorItems.Count - 1; i >= 0; i--)
                    {
                        GameObject go = donorItems[i];
                        if (go == null || !IsDiscreteManaged(go)) continue;

                        Pickupable p = go.GetComponent<Pickupable>();
                        if (p == null || p.ReservedAmount > 0f || p.UnreservedFetchAmount <= 0f) continue;

                        Assignable assignable = go.GetComponent<Assignable>();
                        if (assignable != null && assignable.IsAssigned() && AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) != myWorld)
                        {
                            continue;
                        }
                        if (!FetchManager.IsFetchablePickup(p, fc, fc.destination)) continue;

                        PrimaryElement pe = p.PrimaryElement;
                        float massPerUnit = pe != null && pe.MassPerUnit > 0f ? pe.MassPerUnit : 1f;
                        float unitsNeeded = Mathf.Max(1f, Mathf.Ceil(fc.amount / massPerUnit));
                        if (pe != null && pe.Units > unitsNeeded)
                        {
                            Pickupable split = p.TakeUnit(unitsNeeded);
                            if (split != null)
                            {
                                storage.Store(split.gameObject, hide_popups: true);
                            }
                        }
                        else
                        {
                            donor.BoxStorage.Transfer(go, storage, block_events: false, hide_popups: true);
                        }
                        break;
                    }
                }
            });
        }

        public static void ReturnIdleDiscrete(GlobalItemCollector collector, Storage storage)
        {
            if (storage == null || storage.items == null || storage.items.Count == 0) return;

            GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
            Storage homeStore = home != null && home != collector ? home.BoxStorage : null;
            if (homeStore == null) return;

            int myWorld = collector.GetMyWorldId();
            int myParentWorld = collector.GetMyParentWorldId();
            bool hasBionic = WorldHasLiveBionicMinion(myWorld);
            float keptBatteries = 0f;

            List<FetchChore> chores = null;
            if (GlobalChoreProvider.Instance != null)
            {
                GlobalChoreProvider.Instance.fetchMap.TryGetValue(myParentWorld, out chores);
            }

            ExceptionShield.Execute("DiscreteDemandManager.ReturnIdle", () =>
            {
                for (int i = storage.items.Count - 1; i >= 0; i--)
                {
                    GameObject go = storage.items[i];
                    if (go == null || !IsDiscreteManaged(go)) continue;

                    Pickupable p = go.GetComponent<Pickupable>();
                    KPrefabID id = go.GetComponent<KPrefabID>();
                    if (p == null || id == null || p.ReservedAmount > 0f) continue;

                    Assignable assignable = go.GetComponent<Assignable>();
                    if (assignable != null && assignable.IsAssigned() && AggregateBoxRegistry.GetAssigneeWorldId(assignable.assignee) == myWorld)
                    {
                        continue;
                    }

                    if (hasBionic && id.HasTag(GameTags.ChargedPortableBattery) && keptBatteries < 2f)
                    {
                        keptBatteries += p.PrimaryElement != null ? p.PrimaryElement.Units : 1f;
                        continue;
                    }

                    bool wantedByChore = false;
                    if (chores != null)
                    {
                        for (int cIdx = 0; cIdx < chores.Count; cIdx++)
                        {
                            FetchChore fc = chores[cIdx];
                            if (fc != null && fc.destination != null && fc.driver == null && fc.amount > 0f
                                && !IsStorageToStorageDestination(fc.destination)
                                && FetchManager.IsFetchablePickup(p, fc, fc.destination))
                            {
                                wantedByChore = true;
                                break;
                            }
                        }
                    }
                    if (wantedByChore) continue;

                    storage.Transfer(go, homeStore, block_events: false, hide_popups: true);
                }
            });
        }
    }
}
