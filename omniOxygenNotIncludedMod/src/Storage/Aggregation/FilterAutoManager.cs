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
                // 如果是元素，检查分类是否属于 storageFilters（如后期发现的浓缩铀、异构树脂等）
                Element e = ElementLoader.GetElement(resource);
                if (e != null && e.IsSolid && storage.storageFilters.Contains(e.materialCategory))
                {
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

        public static void SyncDiscoveredResources(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage)
        {
            if (treeFilterable == null || DiscoveredResources.Instance == null || storage == null || storage.storageFilters == null)
            {
                return;
            }

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

            // 对杂物箱：池中已有的散装固体资源（包括中后期生产的浓缩铀、异构树脂等），确保自动发现并勾选
            if (collector.mode == GlobalItemCollector.CollectMode.Debris && OmniPool.Instance != null)
            {
                foreach (OmniPool.PoolLine line in OmniPool.Instance.EnumerateJunk())
                {
                    Element e = ElementLoader.FindElementByHash(line.Element);
                    if (e != null && e.IsSolid && !accepted.Contains(e.tag))
                    {
                        if (!DiscoveredResources.Instance.IsDiscovered(e.tag))
                        {
                            DiscoveredResources.Instance.Discover(e.tag, e.materialCategory);
                        }
                        accepted.Add(e.tag);
                        added++;
                    }
                }
            }

            if (added > 0)
            {
                treeFilterable.UpdateFilters(accepted);
                ModLog.Info($"聚合箱自动同步并补勾了 {added} 种已发现/池中存在的资源");
            }
        }

        public static void BackfillDiscoveredOnce(GlobalItemCollector collector, TreeFilterable treeFilterable, Storage storage, ref bool backfilled)
        {
            SyncDiscoveredResources(collector, treeFilterable, storage);
            backfilled = true;
        }

        public static bool IsSeedItem(KPrefabID id)
        {
            return id.HasTag(GameTags.Seed) || id.HasTag(GameTags.CropSeed);
        }

        public static bool PassesFilter(TreeFilterable treeFilterable, Tag prefabTag)
        {
            if (treeFilterable == null || treeFilterable.ContainsTag(prefabTag)) return true;

            // 容错兜底：若该标签属于合法的散装固体元素且分类在储物柜白名单内，自动补全筛选器
            Element e = ElementLoader.GetElement(prefabTag);
            if (e != null && e.IsSolid)
            {
                Storage storage = treeFilterable.GetFilterStorage();
                if (storage != null && storage.storageFilters != null && storage.storageFilters.Contains(e.materialCategory))
                {
                    treeFilterable.AddTagToFilter(prefabTag);
                    return true;
                }
            }
            return false;
        }

        private static readonly Dictionary<Tag, Tag> _cachedCategories = new Dictionary<Tag, Tag>(128);

        public static Tag GetCategoryCached(KPrefabID id)
        {
            if (id == null) return Tag.Invalid;
            Tag prefabTag = id.PrefabTag;
            if (_cachedCategories.TryGetValue(prefabTag, out Tag cat))
            {
                return cat;
            }
            cat = DiscoveredResources.GetCategoryForEntity(id);
            _cachedCategories[prefabTag] = cat;
            return cat;
        }

        public static bool IsAcceptedByFilter(GlobalItemCollector collector, TreeFilterable treeFilterable, KPrefabID id)
        {
            if (treeFilterable == null || id == null) return true;

            if (treeFilterable.ContainsTag(id.PrefabTag)) return true;

            if (collector.mode == GlobalItemCollector.CollectMode.Debris)
            {
                if (id.HasTag(GameTags.BionicUpgrade))
                {
                    if (DiscoveredResources.Instance != null && !DiscoveredResources.Instance.GetDiscoveredResourcesFromTag(GameTags.BionicUpgrade).Contains(id.PrefabTag))
                    {
                        DiscoveredResources.Instance.Discover(id.PrefabTag, GameTags.BionicUpgrade);
                        treeFilterable.AddTagToFilter(id.PrefabTag);
                        return true;
                    }
                    return treeFilterable.ContainsTag(GameTags.BionicUpgrade);
                }

                // 散装固体元素检查（纯 O(1) 静态表查，零 GetComponent）
                Element e = ElementLoader.GetElement(id.PrefabTag);
                if (e != null && e.IsSolid)
                {
                    Storage storage = treeFilterable.GetFilterStorage();
                    if (storage != null && storage.storageFilters != null && storage.storageFilters.Contains(e.materialCategory))
                    {
                        treeFilterable.AddTagToFilter(id.PrefabTag);
                        return true;
                    }
                }
            }

            Tag category = GetCategoryCached(id);
            if (!category.IsValid)
            {
                return collector.mode == GlobalItemCollector.CollectMode.Debris && IsSeedItem(id);
            }
            return treeFilterable.ContainsTag(category);
        }

    }
}
