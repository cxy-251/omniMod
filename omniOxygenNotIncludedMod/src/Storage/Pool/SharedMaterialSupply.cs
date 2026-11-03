using OmniMod.Buildings;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>复制人和机械臂共用的数值材料池出口；实际实体与旧缓冲先由归属账本匹配。</summary>
    internal static class SharedMaterialSupply
    {
        internal static bool Matches(Element element, Tag tag) => element != null && tag.IsValid &&
            (element.tag == tag || element.HasTag(tag) || element.materialCategory == tag);

        private static bool MatchesChore(Element element, FetchChore chore)
        {
            if (element == null || !element.IsSolid ||
                (chore.criteria == FetchChore.MatchCriteria.MatchID && (chore.tags == null || !chore.tags.Contains(element.tag))) ||
                (chore.criteria == FetchChore.MatchCriteria.MatchTags && !Matches(element, chore.tagsFirst))) return false;
            GameObject prefab = Assets.TryGetPrefab(element.tag);
            KPrefabID id = prefab != null ? prefab.GetComponent<KPrefabID>() : null;
            if (id == null || (chore.criteria == FetchChore.MatchCriteria.MatchID &&
                (chore.tags == null || !chore.tags.Contains(id.PrefabTag))) ||
                (chore.criteria == FetchChore.MatchCriteria.MatchTags && !id.HasTag(chore.tagsFirst)) ||
                (chore.requiredTag.IsValid && !id.HasTag(chore.requiredTag)) || id.HasAnyTags(chore.forbiddenTags)) return false;
            Pickupable pickup = prefab.GetComponent<Pickupable>();
            return pickup != null && pickup.isChoreAllowedToPickup(chore.choreType);
        }

        internal static Pickupable ForChore(int world, FetchChore chore, Navigator navigator, bool arm)
        {
            if (!EntityPoolAccess.Ready || chore == null || chore.destination == null || SharedFetchRequest.GetAmount(chore) <= 0f ||
                AggregateBoxRegistry.IsBoxStorage(chore.destination)) return null;
            OmniPool pool = OmniPool.Instance;
            if (pool == null) return null;
            GlobalItemCollector portal = FindPortal(world, navigator, arm);
            if (portal == null || portal.BoxStorage == chore.destination) return null;
            TreeFilterable filter = portal.GetComponent<TreeFilterable>();
            // 施肥等明确元素请求直接查表；缺货时也不会逐次访问全部材料预制体。
            if (chore.criteria == FetchChore.MatchCriteria.MatchID)
            {
                if (chore.tags == null) return null;
                foreach (Tag tag in chore.tags)
                {
                    Element exact = ElementLoader.GetElement(tag);
                    if (exact == null || pool.GetMass(exact.id) <= 0f || !MatchesChore(exact, chore) ||
                        !FilterAutoManager.PassesFilter(filter, exact.tag)) continue;
                    return Supply(portal, exact, chore, pool.GetMass(exact.id));
                }
                return null;
            }
            Element required = chore.requiredTag.IsValid ? ElementLoader.GetElement(chore.requiredTag) : null;
            Element first = required ?? (chore.tagsFirst.IsValid ? ElementLoader.GetElement(chore.tagsFirst) : null);
            if (first != null && first.IsSolid)
            {
                float mass = pool.GetMass(first.id);
                return mass > 0f && MatchesChore(first, chore) && FilterAutoManager.PassesFilter(filter, first.tag)
                    ? Supply(portal, first, chore, mass) : null;
            }
            // 枚举数值池的元素种类，不扫描地图或别的星球；调用方共用任务／工作者限频。
            foreach (OmniPool.PoolLine line in pool.EnumerateJunk())
            {
                if (!(line.MassKg > 0f)) continue;
                Element element = ElementLoader.FindElementByHash(line.Element);
                if (!MatchesChore(element, chore) || !FilterAutoManager.PassesFilter(filter, element.tag)) continue;
                // RemoveSolid 会改动池字典，选中一种元素后结束枚举，再不推进旧枚举器。
                return Supply(portal, element, chore, line.MassKg);
            }
            return null;
        }

        private static Pickupable Supply(GlobalItemCollector portal, Element element, FetchChore chore, float mass)
        {
            if (chore.destination.storageNetworkID != -1 &&
                chore.destination.storageNetworkID == portal.BoxStorage.storageNetworkID) return null;
            Pickupable pickup = BulkBufferManager.MaterializeSolidFromPool(portal, element.id,
                Mathf.Min(SharedFetchRequest.GetAmount(chore), mass), chore);
            // 不连续生成更多候选；原版拒绝的出库物仍由同一归属账本按超时归还。
            if (pickup == null || !FetchManager.IsFetchablePickup(pickup, chore, chore.destination)) return null;
            SharedFetchRequest.Candidate(chore, pickup, portal.GetMyWorldId());
            return pickup;
        }

        private static GlobalItemCollector FindPortal(int world, Navigator navigator, bool arm)
        {
            GlobalItemCollector portal = AggregateBoxRegistry.GetActiveJunkBoxForWorld(world);
            if (CanUse(portal, navigator, arm)) return portal;
            if (!arm && navigator != null)
            {
                int parent = OmniPocketHabitat.GetExteriorWorldId(world);
                portal = AggregateBoxRegistry.GetActiveJunkBoxForWorld(parent);
                if (CanUse(portal, navigator, false)) return portal;
            }
            return null;
        }
        private static bool CanUse(GlobalItemCollector portal, Navigator navigator, bool arm) =>
            portal != null && !portal.IsRedundant && portal.BoxStorage != null && portal.BoxStorage.allowItemRemoval &&
            (arm || (navigator != null && (portal.BoxStorage.automatable == null ||
                !portal.BoxStorage.automatable.GetAutomationOnly()) && navigator.CanReach(portal.BoxStorage)));

        internal static float GetFilteredMass(Tag tag, Tag[] forbidden)
        {
            OmniPool pool = OmniPool.Instance;
            if (pool == null) return 0f;
            if (forbidden == null || forbidden.Length == 0) return pool.GetMassMatchingTag(tag);
            float mass = 0f;
            foreach (OmniPool.PoolLine line in pool.EnumerateJunk())
            {
                Element element = ElementLoader.FindElementByHash(line.Element);
                if (!Matches(element, tag)) continue;
                bool allowed = true;
                for (int i = 0; i < forbidden.Length; i++)
                    if (Matches(element, forbidden[i])) { allowed = false; break; }
                if (allowed) mass += line.MassKg;
            }
            return mass;
        }
    }
}
