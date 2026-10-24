using System.Collections.Generic;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 聚合箱 TreeFilterable 资源筛选与自动发现管理
    /// </summary>
    public static class FilterAutoManager
    {
        private const float DrainUncheckedPerTickKg = 1000f;

        public static void InitFilterIfNeeded(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage, ref bool filterInitialized)
        {
            if (treeFilterable == null || filterInitialized) return;
            filterInitialized = true;

            var accepted = new HashSet<Tag>(treeFilterable.GetTags());
            if (storage != null && storage.storageFilters != null && DiscoveredResources.Instance != null)
            {
                foreach (Tag category in storage.storageFilters)
                {
                    foreach (Tag resource in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                    {
                        accepted.Add(resource);
                    }
                }

                if (collector.mode == GlobalItemCollector.CollectMode.Debris)
                {
                    foreach (Tag resource in new List<Tag>(DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(GameTags.MiscPickupable)))
                    {
                        GameObject prefab = Assets.TryGetPrefab(resource);
                        if (prefab != null && prefab.HasTag(GameTags.BionicUpgrade))
                        {
                            DiscoveredResources.Instance.Discover(resource, GameTags.BionicUpgrade);
                            accepted.Add(resource);
                        }
                    }

                    foreach (Tag category in new[] { GameTags.Clothes })
                    {
                        accepted.Remove(category);
                        foreach (Tag resource in DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category))
                        {
                            accepted.Remove(resource);
                        }
                    }
                }
            }
            treeFilterable.UpdateFilters(accepted);
        }

        public static bool ShouldAutoAccept(GlobalItemCollector collector, Storage storage, Tag category, Tag resource)
        {
            if (storage == null || storage.storageFilters == null) return false;

            GameObject prefab = Assets.TryGetPrefab(resource);
            KPrefabID pid = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            bool isSeed = pid != null && IsSeedItem(pid);
            bool isBionicUpgrade = pid != null && pid.HasTag(GameTags.BionicUpgrade);

            if (collector.mode == GlobalItemCollector.CollectMode.Debris)
            {
                if (category == GameTags.Clothes)
                {
                    return false;
                }
                if (isBionicUpgrade)
                {
                    DiscoveredResources.Instance?.Discover(resource, GameTags.BionicUpgrade);
                    return true;
                }
                return storage.storageFilters.Contains(category) || (isSeed && !category.IsValid);
            }
            return storage.storageFilters.Contains(category);
        }

        public static void OnResourceDiscovered(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage, Tag category, Tag resource)
        {
            if (treeFilterable != null && ShouldAutoAccept(collector, storage, category, resource))
            {
                treeFilterable.AddTagToFilter(resource);
            }
        }

        public static void BackfillDiscoveredOnce(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage, ref bool backfilled)
        {
            if (backfilled || treeFilterable == null || DiscoveredResources.Instance == null || storage == null || storage.storageFilters == null)
            {
                return;
            }
            backfilled = true;

            var accepted = new HashSet<Tag>(treeFilterable.GetTags());
            var categories = new List<Tag>(storage.storageFilters);
            if (collector.mode == GlobalItemCollector.CollectMode.Debris && !categories.Contains(GameTags.MiscPickupable))
            {
                categories.Add(GameTags.MiscPickupable);
            }

            int added = 0;
            foreach (Tag category in categories)
            {
                foreach (Tag resource in new List<Tag>(DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(category)))
                {
                    if (!accepted.Contains(resource) && ShouldAutoAccept(collector, storage, category, resource))
                    {
                        accepted.Add(resource);
                        added++;
                    }
                }
            }
            if (added > 0)
            {
                treeFilterable.UpdateFilters(accepted);
                ModLog.Info($"聚合箱补勾了 {added} 种后来发现的资源");
            }
        }

        public static bool IsSeedItem(KPrefabID id)
        {
            return id.HasTag(GameTags.Seed) || id.HasTag(GameTags.CropSeed);
        }

        public static bool PassesFilter(TreeFilterable treeFilterable, Tag prefabTag)
        {
            return treeFilterable == null || treeFilterable.ContainsTag(prefabTag);
        }

        public static bool IsAcceptedByFilter(GlobalItemCollector collector, TreeFilterable treeFilterable, KPrefabID id)
        {
            if (treeFilterable == null || id == null) return true;

            if (treeFilterable.ContainsTag(id.PrefabTag)) return true;

            if (collector.mode == GlobalItemCollector.CollectMode.Debris && id.HasTag(GameTags.BionicUpgrade))
            {
                if (DiscoveredResources.Instance != null && !DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(GameTags.BionicUpgrade).Contains(id.PrefabTag))
                {
                    DiscoveredResources.Instance.Discover(id.PrefabTag, GameTags.BionicUpgrade);
                    treeFilterable.AddTagToFilter(id.PrefabTag);
                    return true;
                }
                return treeFilterable.ContainsTag(GameTags.BionicUpgrade);
            }

            Tag category = DiscoveredResources.GetCategoryForEntity(id);
            if (!category.IsValid)
            {
                return collector.mode == GlobalItemCollector.CollectMode.Debris && IsSeedItem(id);
            }
            return treeFilterable.ContainsTag(category);
        }

        public static void ReleaseUncheckedDiscrete(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage)
        {
            if (AggregateBoxRegistry.DiscreteHome != collector || treeFilterable == null || storage == null || storage.items == null)
            {
                return;
            }

            for (int i = storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = storage.items[i];
                KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (id != null && p != null && p.ReservedAmount <= 0f && DiscreteDemandManager.IsDiscreteManaged(go) && !IsAcceptedByFilter(collector, treeFilterable, id))
                {
                    storage.Drop(go);
                }
            }
        }

        public static void ReleaseUncheckedFood(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage)
        {
            if (treeFilterable == null || storage == null || storage.items == null)
            {
                return;
            }

            for (int i = storage.items.Count - 1; i >= 0; i--)
            {
                GameObject go = storage.items[i];
                KPrefabID id = go != null ? go.GetComponent<KPrefabID>() : null;
                Pickupable p = go != null ? go.GetComponent<Pickupable>() : null;
                if (id != null && p != null && p.ReservedAmount <= 0f && !LivingThingsSafety.IsLivingThing(go) && !IsAcceptedByFilter(collector, treeFilterable, id))
                {
                    storage.Drop(go);
                }
            }
        }

        public static void DrainUncheckedElementToFloor(GlobalItemCollector collector, SimHashes element, Element e)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null || e == null || e.substance == null) return;

            float inPool = pool.GetMass(element);
            if (inPool <= 0.01f) return;

            float taken = pool.RemoveSolid(element, Mathf.Min(inPool, DrainUncheckedPerTickKg), out float tempK);
            if (taken <= 0f) return;

            e.substance.SpawnResource(collector.transform.GetPosition(), taken, tempK, 0, 0);
            ModLog.Info($"取消勾选 {e.name}：吐出 {taken:0.#} kg 到箱子脚下（池中剩 {inPool - taken:0.#} kg）");
        }
    }
}
