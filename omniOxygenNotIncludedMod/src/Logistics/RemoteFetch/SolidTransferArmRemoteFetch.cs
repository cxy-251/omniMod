using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Collection;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Logistics.RemoteFetch
{
    /// <summary>
    /// 直接将生物（可装袋/捕捉）与瓶装流体（液体/气体元素）标签注入 Assets.SolidTransferArmConeyableTags，
    /// 让原版 Assets.IsTagSolidTransferArmConveyable 原生 O(1) 返回 true。
    /// 彻底消除 Harmony Postfix 拦截带来的巨大性能开销。
    /// </summary>
    internal static class SolidTransferArmTagsHelper
    {
        private static bool _initialized = false;

        internal static void EnsureInitialized()
        {
            if (_initialized) return;

            try
            {
                var field = AccessTools.Field(typeof(Assets), "SolidTransferArmConeyableTags");
                if (field == null) return;
                var set = field.GetValue(null) as HashSet<Tag>;
                if (set == null) return;

                set.Add(GameTags.BagableCreature);
                set.Add(GameTags.Creatures.Deliverable);
                set.Add(GameTags.Creature);
                set.Add(GameTags.Liquid);
                set.Add(GameTags.Gas);
                set.Add(GameTags.Egg);
                set.Add(GameTags.IncubatableEgg);
                if (Assets.Prefabs != null)
                {
                    for (int i = 0; i < Assets.Prefabs.Count; i++)
                    {
                        var prefab = Assets.Prefabs[i];
                        if (prefab != null && (prefab.GetComponent<Baggable>() != null || prefab.GetComponent<Capturable>() != null))
                        {
                            var kpid = prefab.GetComponent<KPrefabID>();
                            if (kpid != null)
                            {
                                set.Add(kpid.PrefabTag);
                            }
                        }
                    }
                }

                if (ElementLoader.elements != null)
                {
                    for (int i = 0; i < ElementLoader.elements.Count; i++)
                    {
                        Element e = ElementLoader.elements[i];
                        if (e != null && (e.IsLiquid || e.IsGas))
                        {
                            set.Add(e.tag);
                        }
                    }
                }

                _initialized = true;
            }
            catch (Exception ex)
            {
                ModLog.Error("SolidTransferArmTagsHelper.EnsureInitialized failed", ex);
            }
        }
    }

    [HarmonyPatch(typeof(SolidTransferArm), "OnSpawn")]
    internal static class SolidTransferArm_OnSpawn_PermitRanching_Patch
    {
        private static void Postfix(SolidTransferArm __instance)
        {
            ExceptionShield.Execute("SolidTransferArm.OnSpawn", () =>
            {
                SolidTransferArmTagsHelper.EnsureInitialized();
                if (__instance != null)
                {
                    var choreConsumer = __instance.GetComponent<ChoreConsumer>();
                    if (choreConsumer != null && Db.Get()?.ChoreGroups?.Ranching != null)
                    {
                        choreConsumer.SetPermittedByUser(Db.Get().ChoreGroups.Ranching, true);
                    }
                }
            });
        }
    }

    /// <summary>
    /// 确保聚合箱内的物品始终视为 Reachable（即使箱子被砌在封闭房间里也正常注册进 FetchManager 与 WorldInventory）。
    /// </summary>
    [HarmonyPatch(typeof(ReachabilityMonitor.Instance), nameof(ReachabilityMonitor.Instance.UpdateReachability))]
    internal static class ReachabilityMonitor_UpdateReachability_Patch
    {
        private static bool Prefix(ReachabilityMonitor.Instance __instance)
        {
            if (__instance != null && __instance.master is Pickupable p && (AggregateBoxRegistry.IsBoxStorage(p.storage) || p.storage?.GetComponent<Bottler>() != null))
            {
                __instance.sm.isReachable.Set(true, __instance);
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// 仅限机械臂（SolidTransferArm）隔空取物：允许机械臂无视距离抓取【本星球】生效聚合箱、装瓶器与袋装小动物。
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.IsCellReachable))]
    internal static class SolidTransferArm_IsCellReachable_Patch
    {
        private static void Postfix(SolidTransferArm __instance, int cell, ref bool __result)
        {
            if (__result || __instance == null || !Grid.IsValidCell(cell))
            {
                return;
            }

            try
            {
                int myCell = Grid.PosToCell(__instance);
                if (!Grid.IsValidCell(myCell))
                {
                    return;
                }

                int myWorld = Grid.WorldIdx[myCell];
                int cellWorld = Grid.WorldIdx[cell];
                if (myWorld != cellWorld)
                {
                    return;
                }

                // 1. 如果在火箭舱内部：整舱内所有格子均视为可达（无视舱内距离与死角）
                if (ClusterManager.Instance != null)
                {
                    WorldContainer wc = ClusterManager.Instance.GetWorld(myWorld);
                    if (wc != null && wc.IsModuleInterior)
                    {
                        __result = true;
                        return;
                    }
                }

                // 2. 常规世界：纯 O(1) 字典查找聚合箱与装瓶器，零遍历、零 GC、零延迟
                if (AggregateBoxRegistry.IsSameWorldActiveBoxCell(cell, myCell) ||
                    BottlerRegistry.IsSameWorldActiveBottlerCell(cell, myCell))
                {
                    __result = true;
                    return;
                }

                // 3. 动物放生点与袋装小动物：同星球下支持机械臂隔空抓取袋装动物并投送至放生点
                if (CreatureDeliveryRegistry.IsDeliveryCell(cell) ||
                    BaggedCreatureRegistry.HasBaggedCreatureAtCell(cell))
                {
                    __result = true;
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("SolidTransferArm_IsCellReachable_Patch failed", ex);
            }
        }
    }

    /// <summary>
    /// 主线程上寻找搬运目标：如果机械臂身边的物品列表没找到匹配物，
    /// 依次在本星球聚合箱物品快照和本星球液体/气体装瓶器（Bottler）中查找。
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.FindFetchTarget))]
    internal static class SolidTransferArm_FindFetchTarget_Patch
    {
        private static void Postfix(SolidTransferArm __instance, Storage destination, FetchChore chore, ref Pickupable __result)
        {
            if (__result != null || __instance == null || destination == null || chore == null)
            {
                return;
            }

            try
            {
                __result = FindTargetInternal(__instance, destination, chore);
            }
            catch (Exception ex)
            {
                ModLog.Error("SolidTransferArm_FindFetchTarget_Patch failed", ex);
            }
        }

        private static Pickupable FindTargetInternal(SolidTransferArm arm, Storage destination, FetchChore chore)
        {
            SolidTransferArmTagsHelper.EnsureInitialized();

            int world = arm.GetMyWorldId();
            bool isRocket = false;
            if (ClusterManager.Instance != null)
            {
                WorldContainer wc = ClusterManager.Instance.GetWorld(world);
                if (wc != null && wc.IsModuleInterior)
                {
                    isRocket = true;
                }
            }

            // 目标建筑过滤：
            // 禁止机械臂从聚合箱/装瓶器取物送去聚合箱自身、装瓶器自身、或自动分配器（ObjectDispenser 会把物品直接清倒到地面导致死循环）。
            if (AggregateBoxRegistry.IsBoxStorage(destination) ||
                destination.GetComponent<Bottler>() != null ||
                destination.GetComponent<ObjectDispenser>() != null)
            {
                return null;
            }

            if (isRocket)
            {
                // 火箭舱内如果没有杂物箱，按需低频刷新火箭快照（节流 1 秒）
                AggregateBoxRegistry.RefreshRocketInteriorIfNeeded(world);
            }

            // 1. 本星球聚合箱及火箭舱内储物（基于 Tag 字典 O(1) 索引查找）
            Pickupable matched = AggregateBoxRegistry.FindMatchInWorldBox(world, chore, destination);
            if (matched != null)
            {
                ModLog.DebugLog("隔空取物：机械臂锁定目标: " + matched.name);
                return matched;
            }

            // 1.2 本星球袋装小动物：仅当搬运需求为小动物交付（CreatureFetch）时快速匹配
            if (Db.Get() != null && chore.choreType == Db.Get().ChoreTypes.CreatureFetch)
            {
                var baggedList = BaggedCreatureRegistry.GetBaggedCreatures();
                for (int i = 0; i < baggedList.Count; i++)
                {
                    Baggable bag = baggedList[i];
                    if (bag == null || bag.gameObject == null || !bag.wrangled || bag.GetMyWorldId() != world)
                    {
                        continue;
                    }
                    Pickupable p = bag.GetComponent<Pickupable>();
                    if (p != null && p.KPrefabID != null && p.UnreservedFetchAmount > 0f)
                    {
                        if (FetchManager.IsFetchablePickup(p, chore, destination))
                        {
                            ModLog.DebugLog("隔空取物：机械臂锁定袋装小动物: " + p.name);
                            return p;
                        }
                    }
                }
            }

            // 1.5 若本星球聚合箱未匹配到目标，检查是否有其它星球聚合箱（主杂物箱 DiscreteHome 或 主食物箱 FoodHome）中的闲置离散物品/食物满足需求，
            //     若有，立即按需调拨 1 件到本星球的对应箱子，并直接锁定供机械臂抓取！
            GlobalItemCollector localJunk = AggregateBoxRegistry.GetActiveJunkBoxForWorld(world);
            if (localJunk != null && localJunk.BoxStorage != null)
            {
                GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
                if (home != null && home != localJunk && home.BoxStorage != null)
                {
                    int homeWorld = home.GetMyWorldId();
                    Pickupable homeMatched = AggregateBoxRegistry.FindMatchInWorldBox(homeWorld, chore, destination, isCrossPlanetDiscrete: true);
                    if (homeMatched != null)
                    {
                        AggregateBoxRegistry.TransferItemToWorldJunkBox(homeMatched.gameObject, world);
                        ModLog.DebugLog("隔空取物：跨星调拨离散物品并锁定: " + homeMatched.name);
                        return homeMatched;
                    }
                }
            }

            // 1.6 仅当需求涉及食物或烹饪原料时，且本星聚合箱无匹配时，才跨星检索食物主箱 FoodHome
            if (IsFoodRelatedChore(chore))
            {
                GlobalItemCollector localFood = AggregateBoxRegistry.GetActiveBoxForWorld(world, GlobalItemCollector.CollectMode.Edibles);
                if (localFood != null && localFood.BoxStorage != null)
                {
                    GlobalItemCollector foodHome = AggregateBoxRegistry.FoodHome;
                    if (foodHome != null && foodHome != localFood && foodHome.BoxStorage != null)
                    {
                        int foodWorld = foodHome.GetMyWorldId();
                        Pickupable foodMatched = AggregateBoxRegistry.FindMatchInWorldBox(foodWorld, chore, destination, isCrossPlanetDiscrete: true);
                        if (foodMatched != null)
                        {
                            localFood.BoxStorage.Store(foodMatched.gameObject, hide_popups: true);
                            ModLog.DebugLog("隔空取物：跨星调拨食物并锁定: " + foodMatched.name);
                            return foodMatched;
                        }
                    }
                }
            }

            // 2. 本星球装瓶器（仅当需求涉及液体或气体时才查装瓶器，普通固体搬运绝不扫描装瓶器）
            if (ChoreRequestsLiquidOrGas(chore))
            {
                List<Bottler> bottlers = BottlerRegistry.GetWorldBottlers(world);
                if (bottlers != null)
                {
                    for (int b = 0; b < bottlers.Count; b++)
                    {
                        Bottler bottler = bottlers[b];
                        if (bottler == null || bottler.storage == null || bottler.storage.items == null)
                        {
                            continue;
                        }
                        Operational op = bottler.GetComponent<Operational>();
                        if (op != null && !op.IsOperational)
                        {
                            continue;
                        }
                        for (int i = 0; i < bottler.storage.items.Count; i++)
                        {
                            GameObject go = bottler.storage.items[i];
                            if (go == null) continue;
                            Pickupable p = go.GetComponent<Pickupable>();
                            if (p == null || p.KPrefabID == null || p.UnreservedFetchAmount <= 0f)
                            {
                                continue;
                            }
                            if (p.storage != null)
                            {
                                p.storage.allowItemRemoval = true;
                                p.storage.ignoreSourcePriority = true;
                            }
                            if (FetchManager.IsFetchablePickup(p, chore, destination))
                            {
                                ModLog.DebugLog("隔空取水：机械臂锁定装瓶器内流体: " + p.name);
                                return p;
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool ChoreRequestsLiquidOrGas(FetchChore chore)
        {
            if (chore == null) return false;

            if (chore.requiredTag.IsValid)
            {
                if (chore.requiredTag == GameTags.Liquid || chore.requiredTag == GameTags.Gas) return true;
                Element e = ElementLoader.FindElementByHash(ElementLoader.GetElementID(chore.requiredTag));
                if (e != null && (e.IsLiquid || e.IsGas)) return true;
            }

            if (chore.tagsFirst.IsValid)
            {
                if (chore.tagsFirst == GameTags.Liquid || chore.tagsFirst == GameTags.Gas) return true;
                Element e = ElementLoader.FindElementByHash(ElementLoader.GetElementID(chore.tagsFirst));
                if (e != null && (e.IsLiquid || e.IsGas)) return true;
            }

            if (chore.tags != null)
            {
                foreach (Tag t in chore.tags)
                {
                    if (t == GameTags.Liquid || t == GameTags.Gas) return true;
                    Element e = ElementLoader.FindElementByHash(ElementLoader.GetElementID(t));
                    if (e != null && (e.IsLiquid || e.IsGas)) return true;
                }
            }

            return false;
        }

        private static bool IsFoodRelatedChore(FetchChore chore)
        {
            if (chore == null) return false;
            if (Db.Get()?.ChoreTypes?.FoodFetch != null && chore.choreType == Db.Get().ChoreTypes.FoodFetch) return true;
            if (Db.Get()?.ChoreTypes?.Cook != null && chore.choreType == Db.Get().ChoreTypes.Cook) return true;
            if (chore.tags != null)
            {
                foreach (Tag t in chore.tags)
                {
                    if (t == GameTags.Edible || t == GameTags.CookingIngredient) return true;
                }
            }
            return chore.requiredTag == GameTags.Edible || chore.requiredTag == GameTags.CookingIngredient ||
                   chore.tagsFirst == GameTags.Edible || chore.tagsFirst == GameTags.CookingIngredient;
        }
    }
}
