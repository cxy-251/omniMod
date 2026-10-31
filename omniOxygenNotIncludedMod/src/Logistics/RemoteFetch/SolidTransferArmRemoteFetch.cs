using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Buildings;
using OmniMod.Collection;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Pool;
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

                    // 隔空能力只用于取货；送货范围保持 prefab 的原版 pickupRange。
                    if (choreConsumer != null)
                    {
                        choreConsumer.SetReach(__instance.pickupRange);
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
    /// 主线程上寻找搬运目标：如果机械臂身边的物品列表没找到匹配物，
    /// 依次在本星球聚合箱物品快照、舱内储物箱、袋装小动物和液体/气体装瓶器（Bottler）中查找。
    /// </summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.FindFetchTarget))]
    internal static class SolidTransferArm_FindFetchTarget_Patch
    {
        private static readonly Dictionary<Tag, float> _failedTagTimes = new Dictionary<Tag, float>();
        private const float FailedTagCooldownSeconds = 1.0f;

        internal static void InvalidateFailedCache()
        {
            _failedTagTimes.Clear();
        }

        private static bool IsRecentlyFailed(FetchChore chore)
        {
            float now = Time.unscaledTime;
            if (chore.tagsFirst.IsValid && _failedTagTimes.TryGetValue(chore.tagsFirst, out float t1) && now - t1 < FailedTagCooldownSeconds)
            {
                return true;
            }
            if (chore.requiredTag.IsValid && _failedTagTimes.TryGetValue(chore.requiredTag, out float t2) && now - t2 < FailedTagCooldownSeconds)
            {
                return true;
            }
            return false;
        }

        private static void RecordFailure(FetchChore chore)
        {
            float now = Time.unscaledTime;
            if (chore.tagsFirst.IsValid) _failedTagTimes[chore.tagsFirst] = now;
            if (chore.requiredTag.IsValid) _failedTagTimes[chore.requiredTag] = now;
            if (chore.tags != null)
            {
                foreach (Tag t in chore.tags)
                {
                    _failedTagTimes[t] = now;
                }
            }
        }

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

            // 失败节流：若该 Tag 在全星系刚经历过全面检索未果，冷却期内直接跳过，杜绝数十条机械臂高频重叠遍历引发的数百毫秒卡顿
            if (IsRecentlyFailed(chore))
            {
                return null;
            }

            int world = arm.GetMyWorldId();
            bool isRocket = false;
            int parentWorld = OmniPocketHabitat.GetExteriorWorldId(world);

            if (parentWorld >= 0)
            {
                isRocket = true;
            }
            else if (ClusterManager.Instance != null)
            {
                WorldContainer wc = ClusterManager.Instance.GetWorld(world);
                if (wc != null && wc.IsModuleInterior)
                {
                    isRocket = true;
                    if (wc.ParentWorldId >= 0 && wc.ParentWorldId != 255 && wc.ParentWorldId != world)
                    {
                        parentWorld = wc.ParentWorldId;
                    }
                }
            }

            // 目标建筑过滤：
            // 禁止机械臂从聚合箱/装瓶器取物送去聚合箱自身或装瓶器自身。
            if (AggregateBoxRegistry.IsBoxStorage(destination) ||
                destination.GetComponent<Bottler>() != null)
            {
                return null;
            }

            // 特殊优先通道：农田/种植箱选种搬运（FarmFetch）
            // 种子均由离散物品之家（DiscreteHome）集中归档管理，直接快速检索 DiscreteHome，
            // 彻底杜绝因分片索引延迟或星球隔离导致的【没有可用种子】与机械臂无法隔空取种问题！
            bool isFarmFetch = (Db.Get() != null && chore.choreType == Db.Get().ChoreTypes.FarmFetch) ||
                               destination.GetComponent<PlantablePlot>() != null;
            if (isFarmFetch)
            {
                Pickupable seed = FindMatchingSeed(chore, destination);
                if (seed != null)
                {
                    ModLog.DebugLog("隔空取种：机械臂锁定种子: " + seed.name);
                    return seed;
                }
            }

            // 0. 仅对明确的小动物交付查袋装动物。RanchingFetch 也用于喂食器补料，必须走普通物资查找。
            bool isCreatureChore = destination.GetComponent<CreatureDeliveryPoint>() != null ||
                                   (chore.requiredTag.IsValid && (chore.requiredTag == GameTags.Creatures.Deliverable || chore.requiredTag == GameTags.BagableCreature)) ||
                                   (chore.tags != null && (chore.tags.Contains(GameTags.Creatures.Deliverable) || chore.tags.Contains(GameTags.BagableCreature))) ||
                                   (Db.Get() != null && chore.choreType == Db.Get().ChoreTypes.CreatureFetch);
            if (isCreatureChore)
            {
                // 1. 本世界优先（例如舱内已有袋装小动物，或本星球地面已有袋装小动物）
                Pickupable matchedBag = FindMatchingBaggedCreature(world, chore, destination);
                if (matchedBag != null)
                {
                    ModLog.Throttle("arm_bag_" + matchedBag.name, "隔空取物：机械臂锁定本世界袋装小动物: " + matchedBag.name, 3f);
                    return matchedBag;
                }

                // 2. 所属母星表面优先（例如机械臂位于火箭舱内，检索着陆所在的母星星球表面）
                if (parentWorld >= 0)
                {
                    matchedBag = FindMatchingBaggedCreature(parentWorld, chore, destination);
                    if (matchedBag != null)
                    {
                        ModLog.Throttle("arm_bag_parent_" + matchedBag.name, $"隔空取物：舱内机械臂锁定所属星球表面 (World {parentWorld}) 袋装小动物: {matchedBag.name}", 3f);
                        return matchedBag;
                    }
                }

                // 2.1 若为外部母星机械臂，检索建造在该星球上的所有生态巨舱内部！
                if (!isRocket)
                {
                    List<int> interiorWorlds = new List<int>();
                    OmniPocketHabitat.CollectInteriorWorldsOf(world, interiorWorlds);
                    for (int iw = 0; iw < interiorWorlds.Count; iw++)
                    {
                        matchedBag = FindMatchingBaggedCreature(interiorWorlds[iw], chore, destination);
                        if (matchedBag != null)
                        {
                            ModLog.Throttle("arm_bag_interior_" + matchedBag.name, $"隔空取物：舱外机械臂锁定生态舱 (World {interiorWorlds[iw]}) 袋装小动物: {matchedBag.name}", 3f);
                            return matchedBag;
                        }
                    }
                }

                // 3. 全星系跨星球检索（如果本星和所属母星均无可用小动物，检索其它所有星球的袋装小动物）
                matchedBag = FindMatchingBaggedCreatureAcrossAllWorlds(world, chore, destination);
                if (matchedBag != null)
                {
                    ModLog.Throttle("arm_bag_cross_" + matchedBag.name, "隔空取物：机械臂跨星球锁定袋装小动物: " + matchedBag.name, 3f);
                    return matchedBag;
                }

                return null;
            }

            // 2. 装瓶器（仅当需求涉及液体或气体时才查装瓶器，普通固体搬运绝不扫描装瓶器）
            if (ChoreRequestsLiquidOrGas(chore))
            {
                // 2.1 优先查找本世界的装瓶器
                Pickupable bottle = BottlePickupRegistry.FindMatch(world, chore, destination) ??
                                    SearchBottlers(BottlerRegistry.GetWorldBottlers(world), chore, destination);
                if (bottle != null) return bottle;

                // 2.2 若为舱内机械臂，查找所属母星表面的装瓶器
                if (parentWorld >= 0)
                {
                    bottle = BottlePickupRegistry.FindMatch(parentWorld, chore, destination) ??
                             SearchBottlers(BottlerRegistry.GetWorldBottlers(parentWorld), chore, destination);
                    if (bottle != null) return bottle;
                }

                // 2.3 若为舱外母星机械臂，查找建造在该星球上的所有生态巨舱内部的装瓶器！
                if (!isRocket)
                {
                    List<int> interiorWorlds = new List<int>();
                    OmniPocketHabitat.CollectInteriorWorldsOf(world, interiorWorlds);
                    for (int iw = 0; iw < interiorWorlds.Count; iw++)
                    {
                        bottle = BottlePickupRegistry.FindMatch(interiorWorlds[iw], chore, destination) ??
                                 SearchBottlers(BottlerRegistry.GetWorldBottlers(interiorWorlds[iw]), chore, destination);
                        if (bottle != null) return bottle;
                    }
                }

                // 2.4 全星系所有装瓶器检索
                bottle = BottlePickupRegistry.FindMatch(-1, chore, destination) ??
                         SearchBottlers(BottlerRegistry.GetAllBottlers(), chore, destination);
                return bottle;
            }


            // 1. 本星球聚合箱及火箭舱内储物（基于 Tag 字典 O(1) 索引查找）
            Pickupable matched = AggregateBoxRegistry.FindMatchInWorldBox(world, chore, destination);
            if (matched != null)
            {
                ModLog.DebugLog("隔空取物：机械臂锁定目标: " + matched.name);
                return matched;
            }

            // 1.1 若为火箭舱内机械臂，检索母星（着陆星球表面）的聚合箱！
            if (parentWorld >= 0)
            {
                matched = AggregateBoxRegistry.FindMatchInWorldBox(parentWorld, chore, destination);
                if (matched != null)
                {
                    ModLog.DebugLog($"隔空取物：舱内机械臂从母星 (World {parentWorld}) 聚合箱锁定目标: {matched.name}");
                    return matched;
                }
            }

            // 1.2 若为舱外母星机械臂，检索建造在该星球上的所有生态巨舱内部储物！
            if (!isRocket)
            {
                List<int> interiorWorlds = new List<int>();
                OmniPocketHabitat.CollectInteriorWorldsOf(world, interiorWorlds);
                for (int iw = 0; iw < interiorWorlds.Count; iw++)
                {
                    matched = AggregateBoxRegistry.FindMatchInWorldBox(interiorWorlds[iw], chore, destination);
                    if (matched != null)
                    {
                        ModLog.DebugLog($"隔空取物：舱外机械臂从生态舱 (World {interiorWorlds[iw]}) 储物锁定目标: {matched.name}");
                        return matched;
                    }
                }
            }

            // 1.3 若仍未匹配，且为火箭舱内或无杂物箱，检索全星系主箱（DiscreteHome 或 FoodHome）
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

            // 1.4 若全星系主箱仍未匹配到，检索全星系所有生效的杂物箱！
            if (matched == null)
            {
                for (int i = 0; i < AggregateBoxRegistry.BoxesCount; i++)
                {
                    var box = AggregateBoxRegistry.GetBox(i);
                    if (box != null && box.BoxStorage != null && !box.IsRedundant)
                    {
                        int boxWorld = box.GetMyWorldId();
                        if (boxWorld != world && boxWorld != parentWorld)
                        {
                            matched = AggregateBoxRegistry.FindMatchInWorldBox(boxWorld, chore, destination);
                            if (matched != null)
                            {
                                ModLog.DebugLog($"隔空取物：机械臂从 World {boxWorld} 聚合箱锁定目标: {matched.name}");
                                return matched;
                            }
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


            RecordFailure(chore);
            return null;
        }

        private static Pickupable FindMatchingSeed(FetchChore chore, Storage destination)
        {
            // 1. 优先直接检查离散物品之家 DiscreteHome.BoxStorage
            GlobalItemCollector discreteHome = AggregateBoxRegistry.DiscreteHome;
            if (discreteHome != null && discreteHome.BoxStorage != null)
            {
                List<GameObject> items = discreteHome.BoxStorage.items;
                for (int i = 0; i < items.Count; i++)
                {
                    GameObject go = items[i];
                    if (go == null) continue;
                    Pickupable p = go.GetComponent<Pickupable>();
                    if (p == null || p.UnreservedFetchAmount <= 0f) continue;
                    if (!SolidTransferArmRemotePickup.IsRemoteSource(p)) continue;
                    p.storage.allowItemRemoval = true;
                    p.storage.ignoreSourcePriority = true;
                    if (FetchManager.IsFetchablePickup(p, chore, destination))
                    {
                        return p;
                    }
                }
            }

            // 2. 如果 DiscreteHome 中暂时没有实体，但 OmniPool 中存有该种子，即时物化一个种子进 DiscreteHome 并交付
            if (OmniPool.Instance != null && discreteHome != null && discreteHome.BoxStorage != null && chore.tags != null)
            {
                foreach (Tag t in chore.tags)
                {
                    if (OmniPool.Instance.GetSeedCount(t) > 0 && OmniPool.Instance.RemoveSeed(t, 1))
                    {
                        GameObject prefab = Assets.TryGetPrefab(t);
                        if (prefab != null)
                        {
                            GameObject inst = GameUtil.KInstantiate(prefab, discreteHome.transform.GetPosition(), Grid.SceneLayer.Ore);
                            inst.SetActive(true);
                            discreteHome.BoxStorage.Store(inst, hide_popups: true);
                            Pickupable p = inst.GetComponent<Pickupable>();
                            if (p != null)
                            {
                                p.storage.allowItemRemoval = true;
                                p.storage.ignoreSourcePriority = true;
                                return p;
                            }
                        }
                    }
                }
            }

            // 3. 检查全星系所有生效聚合箱
            for (int b = 0; b < AggregateBoxRegistry.BoxesCount; b++)
            {
                GlobalItemCollector box = AggregateBoxRegistry.GetBox(b);
                if (box == null || box == discreteHome || box.BoxStorage == null || box.IsRedundant) continue;
                List<GameObject> items = box.BoxStorage.items;
                for (int i = 0; i < items.Count; i++)
                {
                    GameObject go = items[i];
                    if (go == null) continue;
                    Pickupable p = go.GetComponent<Pickupable>();
                    if (p == null || p.UnreservedFetchAmount <= 0f) continue;
                    if (!SolidTransferArmRemotePickup.IsRemoteSource(p)) continue;
                    p.storage.allowItemRemoval = true;
                    p.storage.ignoreSourcePriority = true;
                    if (FetchManager.IsFetchablePickup(p, chore, destination))
                    {
                        return p;
                    }
                }
            }

            return null;
        }

        private static Pickupable SearchBottlers(List<Bottler> bottlers, FetchChore chore, Storage destination)
        {
            if (bottlers == null || bottlers.Count == 0) return null;
            for (int b = 0; b < bottlers.Count; b++)
            {
                Bottler bottler = bottlers[b];
                if (bottler == null || bottler.storage == null || bottler.storage.items == null)
                {
                    continue;
                }
                // 即便装瓶器暂停进料，已缓存的流体仍可取用；同时同步 StoredPrivate 标签。
                BottlerRegistry.PrepareStorage(bottler);
                for (int i = 0; i < bottler.storage.items.Count; i++)
                {
                    GameObject go = bottler.storage.items[i];
                    if (go == null) continue;
                    Pickupable p = go.GetComponent<Pickupable>();
                    if (p == null || p.storage == destination || p.UnreservedFetchAmount <= 0f ||
                        !SolidTransferArmRemotePickup.IsRemoteSource(p)) continue;
                    if (FetchManager.IsFetchablePickup(p, chore, destination))
                    {
                        ModLog.DebugLog("隔空取水/气：机械臂锁定装瓶器内流体: " + p.name);
                        return p;
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
            if (baggedList != null && baggedList.Length > 0)
            {
                for (int i = 0; i < baggedList.Length; i++)
                {
                    Baggable bag = baggedList[i];
                    if (bag == null || bag.gameObject == null || !bag.wrangled) continue;
                    if (!bag.HasTag(GameTags.Creatures.Deliverable))
                    {
                        bag.gameObject.AddTag(GameTags.Creatures.Deliverable);
                    }
                    Pickupable p = bag.GetComponent<Pickupable>();
                    if (SolidTransferArmRemotePickup.IsRemoteSource(p) && p.storage != destination && p.UnreservedFetchAmount > 0f)
                    {
                        if (p.storage != null)
                        {
                            p.storage.ignoreSourcePriority = true;
                        }
                        if (FetchManager.IsFetchablePickup(p, chore, destination)) return p;
                    }
                }
            }

            return null;
        }

        private static Pickupable FindMatchingBaggedCreatureAcrossAllWorlds(int excludeWorldId, FetchChore chore, Storage destination)
        {
            var allBagged = BaggedCreatureRegistry.GetAllBaggedCreatures();
            if (allBagged != null && allBagged.Length > 0)
            {
                for (int i = 0; i < allBagged.Length; i++)
                {
                    Baggable bag = allBagged[i];
                    if (bag == null || bag.gameObject == null || !bag.wrangled) continue;
                    if (bag.GetMyWorldId() == excludeWorldId) continue;
                    if (!bag.HasTag(GameTags.Creatures.Deliverable))
                    {
                        bag.gameObject.AddTag(GameTags.Creatures.Deliverable);
                    }
                    Pickupable p = bag.GetComponent<Pickupable>();
                    if (SolidTransferArmRemotePickup.IsRemoteSource(p) && p.storage != destination && p.UnreservedFetchAmount > 0f)
                    {
                        if (p.storage != null)
                        {
                            p.storage.ignoreSourcePriority = true;
                        }
                        if (FetchManager.IsFetchablePickup(p, chore, destination)) return p;
                    }
                }
            }

            return null;
        }
    }
}
