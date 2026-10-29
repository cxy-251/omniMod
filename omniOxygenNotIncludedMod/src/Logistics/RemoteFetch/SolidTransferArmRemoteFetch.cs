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
                        if (e != null)
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

    [HarmonyPatch(typeof(SolidTransferArmConfig), nameof(SolidTransferArmConfig.DoPostConfigureComplete))]
    internal static class SolidTransferArmConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null && go.GetComponent<SymbolOverrideController>() == null)
            {
                SymbolOverrideControllerUtil.AddToPrefab(go);
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
                    if (__instance.GetComponent<SymbolOverrideController>() == null)
                    {
                        SymbolOverrideControllerUtil.AddToPrefab(__instance.gameObject);
                    }
                    var choreConsumer = __instance.GetComponent<ChoreConsumer>();
                    if (choreConsumer != null && Db.Get()?.ChoreGroups?.Ranching != null)
                    {
                        choreConsumer.SetPermittedByUser(Db.Get().ChoreGroups.Ranching, true);
                    }

                    // 火箭舱内机械臂：覆盖整舱开敞空间（设置 range=8，ChoreConsumer.stationaryReach=8），
                    // 确保机械臂能感知舱内任意角落的储物箱与放生点
                    int myWorld = __instance.GetMyWorldId();
                    if (ClusterManager.Instance != null)
                    {
                        WorldContainer wc = ClusterManager.Instance.GetWorld(myWorld);
                        if (wc != null && wc.IsModuleInterior)
                        {
                            __instance.pickupRange = 8;
                            if (choreConsumer != null)
                            {
                                choreConsumer.SetReach(8);
                            }
                        }
                    }
                }
            });
        }
    }

    /// <summary>
    /// 屏蔽机械臂或非复制人工作者（无 SymbolOverrideController）加载小动物或物品的姿势贴图覆写（minionAnimOverride）。
    /// 原版中 StandardWorker.AttachOverrideAnims 会对 Worker 的动画控制器调用 AddAnimOverrides，
    /// 若 worker 没有 SymbolOverrideController（如机械臂），将触发底层 DebugUtil.Assert 报错并中断搬运！
    /// </summary>
    [HarmonyPatch(typeof(StandardWorker), "AttachOverrideAnims")]
    internal static class StandardWorker_AttachOverrideAnims_Patch
    {
        private static bool Prefix(StandardWorker __instance, KAnimControllerBase worker_controller)
        {
            if (worker_controller == null || worker_controller.GetComponent<SymbolOverrideController>() == null)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(StandardWorker), "DetachAnimOverrides")]
    internal static class StandardWorker_DetachAnimOverrides_Patch
    {
        private static bool Prefix(StandardWorker __instance)
        {
            if (__instance == null) return true;
            var controller = __instance.GetComponent<KAnimControllerBase>();
            if (controller == null || controller.GetComponent<SymbolOverrideController>() == null)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.AddAnimOverrides), new Type[] { typeof(KAnimFile), typeof(float) })]
    internal static class KAnimControllerBase_AddAnimOverrides_Patch
    {
        private static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file, float priority)
        {
            if (__instance == null || kanim_file == null) return false;
            try
            {
                var data = kanim_file.GetData();
                if (data != null && data.build != null && data.build.symbols != null && data.build.symbols.Length > 0)
                {
                    if (__instance.GetComponent<SymbolOverrideController>() == null)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("KAnimControllerBase_AddAnimOverrides_Patch failed", ex);
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(KAnimControllerBase), nameof(KAnimControllerBase.RemoveAnimOverrides), new Type[] { typeof(KAnimFile) })]
    internal static class KAnimControllerBase_RemoveAnimOverrides_Patch
    {
        private static bool Prefix(KAnimControllerBase __instance, KAnimFile kanim_file)
        {
            if (__instance == null || kanim_file == null) return false;
            try
            {
                var data = kanim_file.GetData();
                if (data != null && data.build != null && data.build.symbols != null && data.build.symbols.Length > 0)
                {
                    if (__instance.GetComponent<SymbolOverrideController>() == null)
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("KAnimControllerBase_RemoveAnimOverrides_Patch failed", ex);
            }
            return true;
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
            if (__instance != null && __instance.master is Pickupable p)
            {
                var storage = p.storage;
                if (storage != null)
                {
                    if (AggregateBoxRegistry.IsBoxStorage(storage) || BottlerRegistry.IsBottlerStorage(storage))
                    {
                        __instance.sm.isReachable.Set(true, __instance);
                        return false;
                    }
                }
                else
                {
                    var kpid = p.KPrefabID;
                    if (kpid != null && kpid.HasTag(GameTags.Creatures.Bagged))
                    {
                        __instance.sm.isReachable.Set(true, __instance);
                        return false;
                    }
                }
            }
            return true;
        }
    }

    /// <summary>
    /// 维护当前各机械臂（SolidTransferArm）在隔空抓取过程中锁定的有效目标源物品，
    /// 确保 IsCellReachable 仅对真实的隔空来源格放行，绝不越界放大送货目标格（Destination）的到达范围。
    /// </summary>
    internal static class SolidTransferArmActiveTargets
    {
        private static readonly Dictionary<SolidTransferArm, Pickupable> _activeArmFetchTargets = new Dictionary<SolidTransferArm, Pickupable>();

        internal static void SetActiveFetchTarget(SolidTransferArm arm, Pickupable target)
        {
            if (arm == null) return;
            lock (_activeArmFetchTargets)
            {
                if (target != null) _activeArmFetchTargets[arm] = target;
                else _activeArmFetchTargets.Remove(arm);
            }
        }

        internal static void ClearActiveFetchTarget(SolidTransferArm arm)
        {
            if (arm == null) return;
            lock (_activeArmFetchTargets)
            {
                _activeArmFetchTargets.Remove(arm);
            }
        }

        internal static bool IsActiveTargetCell(SolidTransferArm arm, int cell)
        {
            if (arm == null) return false;
            lock (_activeArmFetchTargets)
            {
                if (_activeArmFetchTargets.TryGetValue(arm, out Pickupable target) && target != null && target.gameObject != null)
                {
                    if (cell == target.cachedCell || cell == Grid.PosToCell(target))
                    {
                        return true;
                    }
                    if (target.storage != null)
                    {
                        OccupyArea area = target.storage.GetComponent<OccupyArea>();
                        if (area != null)
                        {
                            int[] occCells = area.GetOccupiedGridCells();
                            if (occCells != null)
                            {
                                for (int i = 0; i < occCells.Length; i++)
                                {
                                    if (occCells[i] == cell) return true;
                                }
                            }
                        }
                        else if (Grid.PosToCell(target.storage) == cell)
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(SolidTransferArm), "OnEndChore")]
    internal static class SolidTransferArm_OnEndChore_TargetCleanup_Patch
    {
        private static void Postfix(SolidTransferArm __instance)
        {
            SolidTransferArmActiveTargets.ClearActiveFetchTarget(__instance);
        }
    }

    [HarmonyPatch(typeof(SolidTransferArm), "OnCleanUp")]
    internal static class SolidTransferArm_OnCleanUp_TargetCleanup_Patch
    {
        private static void Prefix(SolidTransferArm __instance)
        {
            SolidTransferArmActiveTargets.ClearActiveFetchTarget(__instance);
        }
    }

    /// <summary>
    /// 仅限机械臂（SolidTransferArm）隔空取物：允许机械臂无视距离抓取【本星球】生效聚合箱、装瓶器、袋装小动物及已锁定的储物箱物品。
    /// 配送目的地（Destination）严格遵循机械臂本体物理工作范围，禁止越界跨区域送货。
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.IsCellReachable))]
    internal static class SolidTransferArm_IsCellReachable_Patch
    {
        private static void Postfix(SolidTransferArm __instance, int cell, ref bool __result)
        {
            if (__result || __instance == null || (uint)cell >= (uint)Grid.WorldIdx.Length)
            {
                return;
            }

            // 0. 袋装小动物：全星系只要该格有打包好的小动物，机械臂均可隔空抓取！
            if (BaggedCreatureRegistry.HasBaggedCreatureAtCell(cell))
            {
                __result = true;
                return;
            }

            // 1. 生效聚合箱：全星系生效聚合箱（包括本星球、所属母星、火箭舱内）均视为可达，允许机械臂隔空取物！
            if (AggregateBoxRegistry.IsActiveBoxCell(cell))
            {
                __result = true;
                return;
            }

            // 2. 生效装瓶器：全星系生效装瓶器均视为可达，允许机械臂隔空取水！
            if (BottlerRegistry.IsActiveBottlerCell(cell))
            {
                __result = true;
                return;
            }

            // 3. 当前机械臂隔空取物已匹配锁定的来源格子（如舱内/星球上任意储物箱内的物品）
            if (SolidTransferArmActiveTargets.IsActiveTargetCell(__instance, cell))
            {
                __result = true;
                return;
            }
        }
    }

    /// <summary>
    /// 主线程上寻找搬运目标：如果机械臂身边的物品列表没找到匹配物，
    /// 依次在本星球聚合箱物品快照、舱内储物箱、袋装小动物和液体/气体装瓶器（Bottler）中查找。
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
                if (__result != null)
                {
                    SolidTransferArmActiveTargets.SetActiveFetchTarget(__instance, __result);
                }
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
            // 禁止机械臂从聚合箱/装瓶器取物送去聚合箱自身或装瓶器自身。
            if (AggregateBoxRegistry.IsBoxStorage(destination) ||
                destination.GetComponent<Bottler>() != null)
            {
                return null;
            }

            if (isRocket)
            {
                // 火箭舱内如果没有杂物箱，按需低频刷新火箭快照（节流 1 秒）
                AggregateBoxRegistry.RefreshRocketInteriorIfNeeded(world);
            }

            // 0. 全星系袋装小动物：当搬运需求为小动物交付（放生点 CreatureDeliveryPoint 或 CreatureFetch/RanchingFetch）时快速匹配
            bool isCreatureChore = destination.GetComponent<CreatureDeliveryPoint>() != null ||
                                   (chore.requiredTag.IsValid && (chore.requiredTag == GameTags.Creatures.Deliverable || chore.requiredTag == GameTags.BagableCreature)) ||
                                   (chore.tags != null && (chore.tags.Contains(GameTags.Creatures.Deliverable) || chore.tags.Contains(GameTags.BagableCreature))) ||
                                   (Db.Get() != null && (chore.choreType == Db.Get().ChoreTypes.CreatureFetch ||
                                                         chore.choreType == Db.Get().ChoreTypes.RanchingFetch));
            if (isCreatureChore)
            {
                // 1. 本世界优先（例如舱内已有袋装小动物，或本星球地面已有袋装小动物）
                Pickupable matchedBag = FindMatchingBaggedCreature(world, chore, destination);
                if (matchedBag != null)
                {
                    ModLog.Throttle("arm_bag_" + matchedBag.name, "隔空取物：机械臂锁定本世界袋装小动物: " + matchedBag.name, 3f);
                    return matchedBag;
                }

                // 2. 所属母星/着陆星球优先（例如机械臂位于火箭舱内，检索着陆所在的母星星球表面）
                if (ClusterManager.Instance != null)
                {
                    WorldContainer wc = ClusterManager.Instance.GetWorld(world);
                    if (wc != null && wc.ParentWorldId >= 0 && wc.ParentWorldId != 255 && wc.ParentWorldId != world)
                    {
                        matchedBag = FindMatchingBaggedCreature(wc.ParentWorldId, chore, destination);
                        if (matchedBag != null)
                        {
                            ModLog.Throttle("arm_bag_parent_" + matchedBag.name, "隔空取物：舱内机械臂锁定所属星球表面袋装小动物: " + matchedBag.name, 3f);
                            return matchedBag;
                        }
                    }
                }

                // 3. 全星系跨星球检索（如果本星和所属星均无可用小动物，检索其它所有星球的袋装小动物）
                matchedBag = FindMatchingBaggedCreatureAcrossAllWorlds(world, chore, destination);
                if (matchedBag != null)
                {
                    ModLog.Throttle("arm_bag_cross_" + matchedBag.name, "隔空取物：机械臂跨星球锁定袋装小动物: " + matchedBag.name, 3f);
                    return matchedBag;
                }

                return null;
            }

            // 1. 本星球聚合箱及火箭舱内储物（基于 Tag 字典 O(1) 索引查找）
            Pickupable matched = AggregateBoxRegistry.FindMatchInWorldBox(world, chore, destination);
            if (matched != null)
            {
                ModLog.DebugLog("隔空取物：机械臂锁定目标: " + matched.name);
                return matched;
            }

            // 1.1 若为火箭舱内机械臂，检索母星（着陆星球表面）的聚合箱！
            if (isRocket && ClusterManager.Instance != null)
            {
                WorldContainer wc = ClusterManager.Instance.GetWorld(world);
                if (wc != null && wc.ParentWorldId >= 0 && wc.ParentWorldId != 255 && wc.ParentWorldId != world)
                {
                    matched = AggregateBoxRegistry.FindMatchInWorldBox(wc.ParentWorldId, chore, destination);
                    if (matched != null)
                    {
                        ModLog.DebugLog($"隔空取物：舱内机械臂从母星 (World {wc.ParentWorldId}) 聚合箱锁定目标: {matched.name}");
                        return matched;
                    }
                }
            }

            // 1.2 若仍未匹配，且为火箭舱内或无杂物箱，检索全星系主箱（DiscreteHome 或 FoodHome）
            if (matched == null)
            {
                if (IsFoodRelatedChore(chore))
                {
                    GlobalItemCollector foodHome = AggregateBoxRegistry.FoodHome;
                    if (foodHome != null && foodHome.BoxStorage != null && foodHome.GetMyWorldId() != world)
                    {
                        int foodWorld = foodHome.GetMyWorldId();
                        matched = AggregateBoxRegistry.FindMatchInWorldBox(foodWorld, chore, destination);
                        if (matched != null)
                        {
                            ModLog.DebugLog("隔空取物：机械臂从全星系食物主箱锁定目标: " + matched.name);
                            return matched;
                        }
                    }
                }
                else
                {
                    GlobalItemCollector home = AggregateBoxRegistry.DiscreteHome;
                    if (home != null && home.BoxStorage != null && home.GetMyWorldId() != world)
                    {
                        int homeWorld = home.GetMyWorldId();
                        matched = AggregateBoxRegistry.FindMatchInWorldBox(homeWorld, chore, destination);
                        if (matched != null)
                        {
                            ModLog.DebugLog("隔空取物：机械臂从全星系主杂物箱锁定目标: " + matched.name);
                            return matched;
                        }
                    }
                }
            }

            // 1.5 若常规世界聚合箱未匹配到离散目标，检查主杂物箱调拨
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
                if ((bottlers == null || bottlers.Count == 0) && isRocket && ClusterManager.Instance != null)
                {
                    WorldContainer wc = ClusterManager.Instance.GetWorld(world);
                    if (wc != null && wc.ParentWorldId >= 0 && wc.ParentWorldId != 255)
                    {
                        bottlers = BottlerRegistry.GetWorldBottlers(wc.ParentWorldId);
                    }
                }
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

        private static Pickupable FindMatchingBaggedCreature(int worldId, FetchChore chore, Storage destination)
        {
            var baggedList = BaggedCreatureRegistry.GetBaggedCreatures(worldId);
            if (baggedList == null || baggedList.Length == 0) return null;
            for (int i = 0; i < baggedList.Length; i++)
            {
                Baggable bag = baggedList[i];
                if (bag == null || bag.gameObject == null || !bag.wrangled) continue;
                Pickupable p = bag.GetComponent<Pickupable>();
                if (p != null && (p.storage == null || p.storage.allowItemRemoval) && p.KPrefabID != null && p.UnreservedFetchAmount > 0f)
                {
                    if (p.storage != null)
                    {
                        p.storage.ignoreSourcePriority = true;
                    }
                    if (FetchManager.IsFetchablePickup(p, chore, destination)) return p;
                }
            }
            return null;
        }

        private static Pickupable FindMatchingBaggedCreatureAcrossAllWorlds(int excludeWorldId, FetchChore chore, Storage destination)
        {
            var allBagged = BaggedCreatureRegistry.GetAllBaggedCreatures();
            if (allBagged == null || allBagged.Length == 0) return null;
            for (int i = 0; i < allBagged.Length; i++)
            {
                Baggable bag = allBagged[i];
                if (bag == null || bag.gameObject == null || !bag.wrangled) continue;
                if (bag.GetMyWorldId() == excludeWorldId) continue;
                Pickupable p = bag.GetComponent<Pickupable>();
                if (p != null && (p.storage == null || p.storage.allowItemRemoval) && p.KPrefabID != null && p.UnreservedFetchAmount > 0f)
                {
                    if (p.storage != null)
                    {
                        p.storage.ignoreSourcePriority = true;
                    }
                    if (FetchManager.IsFetchablePickup(p, chore, destination)) return p;
                }
            }
            return null;
        }
    }
}
