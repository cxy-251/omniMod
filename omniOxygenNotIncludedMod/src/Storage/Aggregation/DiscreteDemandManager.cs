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

        private static readonly Dictionary<int, float> _lastServeTimes = new Dictionary<int, float>();

        public static void ServeDiscreteDemand(GlobalItemCollector collector, Storage storage)
        {
            if (storage == null || storage.items == null || AggregateBoxRegistry.DiscreteHome == collector)
            {
                return;
            }

            int myWorld = collector.GetMyWorldId();
            int myParentWorld = collector.GetMyParentWorldId();
            if (myWorld < 0 || myWorld == 255) return;

            float now = Time.time;
            if (_lastServeTimes.TryGetValue(myWorld, out float lastTime) && now - lastTime < 10.0f)
            {
                return;
            }
            _lastServeTimes[myWorld] = now;

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

                // 3. 响应本世界活跃 FetchChore（按需调拨离散物资）
                List<FetchChore> chores = null;
                if (GlobalChoreProvider.Instance == null ||
                    (!GlobalChoreProvider.Instance.fetchMap.TryGetValue(myWorld, out chores) &&
                     (myParentWorld < 0 || !GlobalChoreProvider.Instance.fetchMap.TryGetValue(myParentWorld, out chores))) ||
                    chores == null)
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

                    Tag targetTag = fc.tagsFirst.IsValid ? fc.tagsFirst : (fc.requiredTag.IsValid ? fc.requiredTag : Tag.Invalid);
                    if (!targetTag.IsValid && fc.tags != null && fc.tags.Count > 0)
                    {
                        foreach (Tag t in fc.tags)
                        {
                            targetTag = t;
                            break;
                        }
                    }
                    if (!targetTag.IsValid || targetTag == GameTags.Empty) continue;

                    Element elem = ElementLoader.FindElementByHash(ElementLoader.GetElementID(targetTag));
                    if (elem != null && elem.IsSolid) continue; // 散装矿石建材由 BulkBufferManager 管理，不走离散调拨

                    // 检查本地箱是否已有该物资（O(1) 字典快速判定，绝不全量线性扫描）
                    if (AggregateBoxRegistry.TryGetWorldBoxMass(myWorld, targetTag, out float localMass) && localMass > 0.001f)
                    {
                        continue;
                    }

                    // 按需调拨该离散物资至本地杂物箱
                    AggregateBoxRegistry.EnsureDiscreteTagInWorldJunkBox(targetTag, Tag.Invalid, myWorld, 1f);
                }
            });
        }

        public static void ReturnIdleDiscrete(GlobalItemCollector collector, Storage storage)
        {
            // 闲置离散物品保留在本地杂物箱中，供本星球小人随时取用或其它星球按需调拨。
            // 绝不每秒退回，杜绝跨星球反复争抢转移引发的引擎卡顿与状态震荡。
        }
    }
}
