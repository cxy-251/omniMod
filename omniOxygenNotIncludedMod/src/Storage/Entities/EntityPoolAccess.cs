using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OmniMod.Buildings;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Pool
{
    /// <summary>所有调用方共用的出库入口；查询库存只读，只有具体取用请求才会出库。</summary>
    internal static class EntityPoolAccess
    {
        private sealed class Search
        {
            internal float Next;
            internal long Revision = -1;
            internal int Frame = -1;
            internal int Cursor;
            internal int TagCursor;
            internal Tag[] Tags;
            internal ConditionalWeakTable<Navigator, Search> Workers;
            internal Dictionary<Tag, Search> Categories;
        }
        private static readonly ConditionalWeakTable<object, Search> Searches = new ConditionalWeakTable<object, Search>();
        [ThreadStatic] internal static int MatchingPoolItem;

        internal static bool Ready => OmniEntityPool.Instance != null && !OmniEntityPool.Stopping &&
            Game.Instance != null && !OmniEntityPool.Moving && !App.isLoading && Game.IsOnMainThread();

        internal static bool PortalAccepts(GlobalItemCollector box, Pickupable pickup) =>
            box != null && !box.IsRedundant && box.BoxStorage != null && box.BoxStorage.allowItemRemoval &&
            pickup != null && pickup.KPrefabID != null &&
            (OmniEntityPool.AccessFor(pickup.KPrefabID) & (1 << (int)box.mode)) != 0 &&
            FilterAutoManager.IsAcceptedByFilter(box, box.GetComponent<TreeFilterable>(), pickup.KPrefabID);

        internal static GlobalItemCollector FindPortal(int world, Pickupable pickup, Navigator navigator, bool arm)
        {
            for (int mode = 0; mode < 2; mode++)
            {
                GlobalItemCollector box = AggregateBoxRegistry.GetActiveBoxForWorld(world, (GlobalItemCollector.CollectMode)mode);
                if (CanUse(box, pickup, navigator, arm)) return box;
            }
            // 舱外入口必须通过真实门可达。机械臂仍要求本世界有实际入口。
            if (!arm && navigator != null)
            {
                int exterior = OmniPocketHabitat.GetExteriorWorldId(world);
                if (exterior != world && exterior >= 0)
                    for (int mode = 0; mode < 2; mode++)
                    {
                        GlobalItemCollector box = AggregateBoxRegistry.GetActiveBoxForWorld(exterior, (GlobalItemCollector.CollectMode)mode);
                        if (CanUse(box, pickup, navigator, false)) return box;
                    }
            }
            return null;
        }
        private static bool CanUse(GlobalItemCollector box, Pickupable pickup, Navigator navigator, bool arm) =>
            PortalAccepts(box, pickup) && (arm || (navigator != null &&
                (box.BoxStorage.automatable == null || !box.BoxStorage.automatable.GetAutomationOnly()) && navigator.CanReach(box.BoxStorage)));

        private static Search GetSearch(object request, Navigator navigator = null, Tag category = default)
        {
            Search root = Searches.GetValue(request, key => new Search());
            if (navigator != null)
            {
                if (root.Workers == null) root.Workers = new ConditionalWeakTable<Navigator, Search>();
                return root.Workers.GetValue(navigator, key => new Search());
            }
            if (category.IsValid)
            {
                if (root.Categories == null) root.Categories = new Dictionary<Tag, Search>();
                if (!root.Categories.TryGetValue(category, out Search state)) root.Categories[category] = state = new Search();
                return state;
            }
            return root;
        }
        private static bool CanSearch(Search state)
        {
            if (!Ready || state.Frame == Time.frameCount) return false;
            long revision = OmniEntityPool.Instance.Revision + (OmniPool.Instance?.Revision ?? 0);
            float now = Time.realtimeSinceStartup;
            if (state.Next > now && state.Revision == revision) return false;
            state.Frame = Time.frameCount; state.Next = now + 1f; state.Revision = revision;
            return true;
        }
        private static bool Available(Pickupable pickup, int world)
        {
            OmniEntityPool pool = OmniEntityPool.Instance;
            if (pickup == null || !pool.Owns(pickup) || pickup.ReservedAmount > 0f ||
                pickup.UnreservedFetchAmount <= 0f || pickup.KPrefabID.HasTag(GameTags.MarkedForMove)) return false;
            Assignable assignment = pickup.GetComponent<Assignable>();
            return assignment == null || !assignment.IsAssigned() ||
                SharedItemDispatcher.GetAssigneeWorldId(assignment.assignee) == world;
        }

        internal static Pickupable ForChore(int world, FetchChore chore, Navigator navigator, bool arm)
        {
            if (chore == null || chore.destination == null || SharedFetchRequest.GetAmount(chore) <= 0f ||
                AggregateBoxRegistry.IsBoxStorage(chore.destination) || !Ready) return null;
            Search state = GetSearch(chore, navigator);
            if (!CanSearch(state)) return null;
            OmniEntityPool pool = OmniEntityPool.Instance;
            int budget = 128;
            if (chore.criteria == FetchChore.MatchCriteria.MatchID)
            {
                if (chore.tags != null)
                {
                    if (state.Tags == null || state.Tags.Length != chore.tags.Count)
                    { state.Tags = new Tag[chore.tags.Count]; chore.tags.CopyTo(state.Tags); }
                    for (int i = 0; i < state.Tags.Length && budget > 0; i++)
                    {
                        if (state.TagCursor >= state.Tags.Length) state.TagCursor = 0;
                        Tag tag = state.Tags[state.TagCursor++];
                        Pickupable result = MatchChore(pool.Find(tag), world, chore, navigator, arm, state, ref budget);
                        if (result != null) return result;
                    }
                }
            }
            else
            {
                Tag tag = chore.requiredTag.IsValid ? chore.requiredTag : chore.tagsFirst;
                Pickupable result = MatchChore(pool.Find(tag), world, chore, navigator, arm, state, ref budget);
                if (result != null) return result;
            }
            return SharedMaterialSupply.ForChore(world, chore, navigator, arm);
        }
        private static Pickupable MatchChore(List<Pickupable> candidates, int world, FetchChore chore, Navigator navigator, bool arm, Search state, ref int budget)
        {
            if (candidates == null || budget <= 0) return null;
            for (int i = 0, limit = Math.Min(budget, candidates.Count); i < limit; i++)
            {
                if (state.Cursor >= candidates.Count) state.Cursor = 0;
                Pickupable pickup = candidates[state.Cursor++];
                budget--;
                if (!Available(pickup, world)) continue;
                GlobalItemCollector portal = FindPortal(world, pickup, navigator, arm);
                if (portal == null || portal.BoxStorage == chore.destination) continue;
                bool matches;
                MatchingPoolItem++;
                try { matches = FetchManager.IsFetchablePickup(pickup, chore, chore.destination); }
                finally { MatchingPoolItem--; }
                if (!matches) continue;
                float amount = Mathf.Min(SharedFetchRequest.GetAmount(chore), pickup.UnreservedFetchAmount);
                Pickupable result = OmniEntityPool.Instance.Checkout(pickup, portal, amount, chore);
                if (result != null)
                {
                    SharedFetchRequest.Candidate(chore, result, world);
                    return result;
                }
            }
            return null;
        }

        internal static Pickupable ForConsumption(Storage consumer, HashSet<Tag> excluded, Tag[] required,
            Tag category, bool food)
        {
            if (consumer == null || consumer.GetComponent<MinionIdentity>() == null || !Ready) return null;
            ConsumableConsumer diet = consumer.GetComponent<ConsumableConsumer>();
            // 第二次“忽略禁食”的状态提示查询只读，绝不能据此出库禁食物。
            if (diet == null || !ReferenceEquals(excluded, diet.forbiddenTagSet)) return null;
            Search state = GetSearch(consumer, category: category);
            if (!CanSearch(state)) return null;
            if (food)
            {
                CalorieMonitor.Instance hunger = consumer.GetSMI<CalorieMonitor.Instance>();
                if (hunger == null || !hunger.IsHungry()) return null;
            }
            Navigator navigator = consumer.GetComponent<Navigator>();
            if (navigator == null) return null;
            var candidates = OmniEntityPool.Instance.Find(category);
            if (candidates == null) return null;
            int world = consumer.GetMyWorldId();
            int workerId = consumer.GetComponent<KPrefabID>().InstanceID;
            required = required ?? new[] { category };
            for (int i = 0, limit = Math.Min(128, candidates.Count); i < limit; i++)
            {
                if (state.Cursor >= candidates.Count) state.Cursor = 0;
                Pickupable pickup = candidates[state.Cursor++];
                if (!Available(pickup, world) || !pickup.CouldBePickedUpByMinion(workerId) ||
                    !diet.IsPermitted(pickup.KPrefabID.PrefabTag.Name)) continue;
                GlobalItemCollector portal = FindPortal(world, pickup, navigator, false);
                if (portal == null) continue;
                bool matches;
                MatchingPoolItem++;
                try { matches = FetchManager.IsFetchablePickup_Exclude(pickup.KPrefabID, pickup.storage,
                    pickup.UnreservedFetchAmount, excluded, required, consumer); }
                finally { MatchingPoolItem--; }
                if (!matches) continue;
                float amount = pickup.PrimaryElement.MassPerUnit;
                if (food)
                {
                    Edible edible = pickup.GetComponent<Edible>();
                    if (edible == null || edible.FoodInfo == null || !(edible.FoodInfo.CaloriesPerUnit > 0f) ||
                        float.IsInfinity(edible.FoodInfo.CaloriesPerUnit)) continue;
                    var calories = Db.Get().Amounts.Calories.Lookup(consumer);
                    if (calories == null) return null;
                    float units = Mathf.Max(0f, calories.GetMax() - calories.value) / edible.FoodInfo.CaloriesPerUnit;
                    amount = units * pickup.PrimaryElement.MassPerUnit;
                }
                Pickupable result = OmniEntityPool.Instance.Checkout(pickup, portal, amount, consumer);
                if (result != null && result.GetNavigationCost(navigator, result.cachedCell) >= 0) return result;
                // 不继续向不可达入口发出更多物品；本次出库由超时归还处理。
                return null;
            }
            return null;
        }

        internal static bool ForAssignment(Pickupable pickup, int world, bool assignmentIntent = true)
        {
            if (!Ready || pickup == null) return false;
            OmniEntityPool pool = OmniEntityPool.Instance;
            if (!pool.Owns(pickup))
            {
                if (pickup.storage != null && AggregateBoxRegistry.TryGetBox(pickup.storage, out _)) pool.AdoptPortal(pickup.storage);
                else if (pickup.GetComponent<BionicUpgradeComponent>() != null && BionicUpgradeRegistry.IsDispatchSource(pickup))
                {
                    if (!pool.Deposit(pickup)) return false;
                }
            }
            if (!pool.Owns(pickup)) return false;
            GlobalItemCollector portal = BionicUpgradeRegistry.GetDestinationBox(world);
            return portal != null && pool.Checkout(pickup, portal, pickup.UnreservedFetchAmount,
                pickup.GetComponent<Assignable>(), assignmentIntent) != null;
        }
    }
}
