using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Automation
{
    /// <summary>
    /// 1. 复制人绝不隔空取物：复制人使用 100% 原版寻路走去【本星球】的杂物箱/食物箱取物；
    /// 2. 只有机械手（SolidTransferArm）可以无视距离从【本星球】的杂物箱/食物箱隔空取物；
    /// 3. 离散物品（增幅块、种子、便携电池、衣物、蛋等）全星系共享、绝不均分，
    ///    当在某星球被选中分配或下单时，立即将该 1 件离散物品调拨进目标星球的杂物箱，供本星球小人走去箱子拾取。
    /// </summary>
    internal static class RemoteFetch
    {
        private static int _logCount;

        internal static void LogOnce(string msg)
        {
            if (_logCount < 3)
            {
                _logCount++;
                Debug.Log("[OmniMod] " + msg);
            }
        }
    }

    // =========================================================================
    // 0. 允许跨星球选择增幅块（BionicUpgradeComponent）与箱内可装备物品（Assignable），
    //    并在分配或下单瞬间将该离散物品立即调拨到目标星球的杂物箱，让小人走去本星箱子取用！
    // =========================================================================
    [HarmonyPatch(typeof(Assignable), "OnSpawn")]
    internal static class Assignable_OnSpawn_Patch
    {
        private static readonly FieldInfo PreconditionsField = AccessTools.Field(typeof(Assignable), "assignmentPreconditions");

        private static void Postfix(Assignable __instance)
        {
            if (__instance == null || PreconditionsField == null)
            {
                return;
            }

            var list = PreconditionsField.GetValue(__instance) as List<Func<MinionAssignablesProxy, bool>>;
            if (list == null || list.Count == 0)
            {
                return;
            }

            int lastIdx = list.Count - 1;
            Func<MinionAssignablesProxy, bool> originalWorldCheck = list[lastIdx];
            list[lastIdx] = (MinionAssignablesProxy proxy) =>
            {
                if (originalWorldCheck(proxy))
                {
                    return true;
                }
                if (__instance == null || proxy == null)
                {
                    return false;
                }
                GameObject targetGo = proxy.GetTargetGameObject();
                if (targetGo == null)
                {
                    return false;
                }

                // 1. 物品在可访问的聚合箱内 → 允许跨星球分配给建有同类型聚合箱星球上的小人
                Pickupable p = __instance.GetComponent<Pickupable>();
                if (p != null && AggregateBoxRegistry.IsAccessibleBoxStorage(p.storage, Grid.PosToCell(targetGo)))
                {
                    return true;
                }

                // 2. 增幅块即使还在地面（尚未到 4 秒自动收集周期），只要双方星球都建有杂物箱，也直接允许跨星球分配
                if (__instance is BionicUpgradeComponent &&
                    AggregateBoxRegistry.WorldHasActiveJunkBox(__instance.GetMyWorldId()) &&
                    AggregateBoxRegistry.WorldHasActiveJunkBox(targetGo.GetMyWorldId()))
                {
                    return true;
                }

                return false;
            };
        }
    }

    /// <summary>
    /// 当玩家把增幅块或衣物分配给某个星球的小人时，在触发槽位变更事件（UpgradeSlotAssignationChanged / EquipChore）之前，
    /// 立即将该件增幅块或衣物调拨进目标星球的本地杂物箱，让目标星球的小人走去自己星球的杂物箱拾取安装！
    /// </summary>
    [HarmonyPatch(typeof(Assignable), nameof(Assignable.Assign), new[] { typeof(IAssignableIdentity), typeof(AssignableSlotInstance) })]
    internal static class Assignable_Assign_Patch
    {
        private static void Prefix(Assignable __instance, IAssignableIdentity new_assignee)
        {
            if (__instance == null || new_assignee == null || new_assignee == __instance.assignee)
            {
                return;
            }
            if (!(__instance is BionicUpgradeComponent) && !(__instance is Equippable))
            {
                return;
            }

            int targetWorldId = AggregateBoxRegistry.GetAssigneeWorldId(new_assignee);
            if (targetWorldId >= 0 && targetWorldId != 255)
            {
                AggregateBoxRegistry.TransferItemToWorldJunkBox(__instance.gameObject, targetWorldId);
            }
        }
    }

    /// <summary>
    /// 兼容已分配增幅块的存档：当仿生人检查已分配的增幅块是否可拾取时，若该增幅块还在其它星球的箱子或地面上，
    /// 立即将其调拨进仿生人所在星球的杂物箱，随后完全交由原版 navigator.CanReach 检查能否走到本星杂物箱，彻底杜绝任务闪烁与卡死！
    /// </summary>
    [HarmonyPatch(typeof(BionicUpgradesMonitor.Instance), nameof(BionicUpgradesMonitor.Instance.IsBionicUpgradeComponentObjectAbleToBePickedUp))]
    internal static class BionicUpgradesMonitor_IsBionicUpgradeComponentObjectAbleToBePickedUp_Patch
    {
        private static void Prefix(BionicUpgradesMonitor.Instance __instance, BionicUpgradeComponent upgradecComponent)
        {
            if (__instance == null || __instance.gameObject == null || upgradecComponent == null || upgradecComponent.gameObject == null)
            {
                return;
            }

            int dupeWorld = __instance.gameObject.GetMyWorldId();
            if (dupeWorld >= 0 && dupeWorld != 255 && upgradecComponent.GetMyWorldId() != dupeWorld)
            {
                AggregateBoxRegistry.TransferItemToWorldJunkBox(upgradecComponent.gameObject, dupeWorld);
            }
        }
    }

    /// <summary>
    /// 当玩家在种植砖/农田/水培砖/孵化器/展台下单选择种子、蛋或神器时，立即按需将所需的 1 颗（或多颗）离散物品
    /// 从主箱调拨到本星球的杂物箱，供本星球小人走去箱子取用或本星球机械手隔空抓取，剩余库存继续留在主箱不均分。
    /// </summary>
    [HarmonyPatch(typeof(SingleEntityReceptacle), nameof(SingleEntityReceptacle.CreateOrder))]
    internal static class SingleEntityReceptacle_CreateOrder_Patch
    {
        private static void Postfix(SingleEntityReceptacle __instance, Tag entityTag, Tag additionalFilterTag)
        {
            if (__instance == null || !entityTag.IsValid || entityTag == GameTags.Empty)
            {
                return;
            }

            int worldId = __instance.GetMyWorldId();
            if (worldId >= 0 && worldId != 255 && AggregateBoxRegistry.WorldHasActiveJunkBox(worldId))
            {
                float needed = Mathf.Max(1f, MaterialNeeds.GetAmount(entityTag, worldId, includeRelatedWorlds: false));
                AggregateBoxRegistry.EnsureDiscreteTagInWorldJunkBox(entityTag, additionalFilterTag, worldId, needed);
            }
        }
    }

    // =========================================================================
    // 1. 确保聚合箱内的物品始终视为 Reachable（即使箱子被砌在封闭房间里也正常注册进 FetchManager 与 WorldInventory）
    // =========================================================================
    [HarmonyPatch(typeof(ReachabilityMonitor.Instance), nameof(ReachabilityMonitor.Instance.UpdateReachability))]
    internal static class ReachabilityMonitor_UpdateReachability_Patch
    {
        private static bool Prefix(ReachabilityMonitor.Instance __instance)
        {
            if (__instance != null && __instance.master is Pickupable p && AggregateBoxRegistry.IsBoxStorage(p.storage))
            {
                __instance.sm.isReachable.Set(true, __instance);
                return false;
            }
            return true;
        }
    }

    // =========================================================================
    // 2. 当物品跨星球存入 Storage（如收集进主箱、或按需调拨进目标星球杂物箱）时，
    //    同步刷新两侧星球的 WorldInventory，杜绝幽灵引用或漏统计。
    // =========================================================================
    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.OnStore))]
    internal static class Pickupable_OnStore_WorldSync_Patch
    {
        private static void Prefix(Pickupable __instance, out int __state)
        {
            __state = __instance != null ? __instance.cachedCell : -1;
        }

        private static void Postfix(Pickupable __instance, int __state)
        {
            if (__instance == null || !__instance.isSpawned || Game.Instance == null)
            {
                return;
            }
            int newCell = __instance.cachedCell;
            if (!Grid.IsValidCell(__state) || !Grid.IsValidCell(newCell) || Grid.WorldIdx[__state] == Grid.WorldIdx[newCell])
            {
                return;
            }
            if (__instance.KPrefabID != null && __instance.KPrefabID.HasTag(GameTags.Reachable))
            {
                Game.Instance.Trigger(-1491270284, __instance.gameObject);
                Game.Instance.Trigger(-1588644844, __instance.gameObject);
            }
        }
    }

    // =========================================================================
    /// <summary>
    /// 额外给瓶装流体（水、浓盐水、污水、石油等）添加机械臂（SolidTransferArm）可抓取属性，
    /// 让机械手把瓶装水当作可搬运物品抓取。
    /// </summary>
    [HarmonyPatch(typeof(Assets), nameof(Assets.IsTagSolidTransferArmConveyable))]
    internal static class Assets_IsTagSolidTransferArmConveyable_Patch
    {
        private static void Postfix(Tag tag, ref bool __result)
        {
            if (!__result && tag.IsValid)
            {
                Element e = ElementLoader.FindElementByHash(ElementLoader.GetElementID(tag));
                if (e != null && (e.IsLiquid || e.IsGas))
                {
                    __result = true;
                }
            }
        }
    }

    // =========================================================================
    // 3. 仅限机械臂（SolidTransferArm）隔空取物：允许机械臂无视距离抓取【本星球】生效聚合箱与装瓶器内的物品。
    // =========================================================================
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.IsCellReachable))]
    internal static class SolidTransferArm_IsCellReachable_Patch
    {
        private static void Postfix(SolidTransferArm __instance, int cell, ref bool __result)
        {
            if (!__result && __instance != null && Grid.IsValidCell(cell))
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

                // 2. 常规世界：聚合箱格子、同世界常规储物箱格子、装瓶器格子
                if (AggregateBoxRegistry.IsSameWorldActiveBoxCell(cell, myCell) ||
                    AggregateBoxRegistry.IsSameWorldStorageCell(cell, myCell) ||
                    BottlerRegistry.IsSameWorldActiveBottlerCell(cell, myCell))
                {
                    __result = true;
                }
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

            int world = __instance.GetMyWorldId();
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
            if (!isRocket)
            {
                // 普通星球：禁止机械臂从聚合箱/装瓶器取物送去普通储物箱、装瓶器自身、轨道装载机、自动分配器或聚合箱自身，
                // 避免形成无限取出再掉落回收的死循环卡顿。聚合箱与装瓶器物品只服务于实际消耗设备（精炼机/发电机/农田/建造工地/烹饪台/超算等）。
                if (destination.GetComponent<SolidConduitInbox>() != null ||
                    destination.GetComponent<ObjectDispenser>() != null ||
                    destination.GetComponent<StorageLocker>() != null ||
                    destination.GetComponent<Bottler>() != null ||
                    AggregateBoxRegistry.IsBoxStorage(destination))
                {
                    return;
                }
            }
            else
            {
                // 火箭内部：放开对 SolidConduitInbox（轨道装载机/运输需求）以及舱内储物箱/设备的限制！
                // 仅禁止送入聚合箱自身或装瓶器自身以防循环
                if (AggregateBoxRegistry.IsBoxStorage(destination) || destination.GetComponent<Bottler>() != null)
                {
                    return;
                }
            }

            // 1. 本星球聚合箱及火箭舱内储物
            Pickupable[] boxItems = AggregateBoxRegistry.GetWorldBoxItems(world);
            for (int i = 0; i < boxItems.Length; i++)
            {
                Pickupable p = boxItems[i];
                if (p == null || p.KPrefabID == null || p.UnreservedFetchAmount <= 0f)
                {
                    continue;
                }
                // 防止将箱子里的物品搬回同一个箱子
                if (p.storage != null && p.storage == destination)
                {
                    continue;
                }
                if (!Assets.IsTagSolidTransferArmConveyable(p.KPrefabID.PrefabTag))
                {
                    continue;
                }
                if (FetchManager.IsFetchablePickup(p, chore, destination))
                {
                    __result = p;
                    RemoteFetch.LogOnce("隔空取物：机械臂锁定目标: " + p.name);
                    return;
                }
            }

            // 2. 本星球装瓶器（支持直接隔空从装瓶器中抓取管道水）
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
                        if (!Assets.IsTagSolidTransferArmConveyable(p.KPrefabID.PrefabTag))
                        {
                            continue;
                        }
                        if (FetchManager.IsFetchablePickup(p, chore, destination))
                        {
                            __result = p;
                            RemoteFetch.LogOnce("隔空取水：机械臂锁定装瓶器内流体: " + p.name);
                            return;
                        }
                    }
                }
            }
        }
    }

    // =========================================================================
    // 4. 机械臂从装瓶器取水的兼容处理。
    //    装瓶器进入 ready 状态时，会把罐内流体的 Pickupable.targetWorkable 设为装瓶器自身，
    //    因此机械臂抓取时实际执行的是 Bottler 这个 Workable（OnCompleteWork 会把流体质量正确转入机械臂 Storage）。
    //    但 Bottler 默认为复制人设计：会往工作者身上挂 "snapto_chest" 手持瓶子代理动画、并附加 anim_interacts_bottler 覆盖动画。
    //    机械臂没有这些符号，必须跳过，避免浮空瓶子贴图 / 动画错乱 / 清理时报 DevLogError。
    // =========================================================================
    internal static class BottlerArmCompat
    {
        internal static bool IsArmWorker(WorkerBase worker)
        {
            return worker != null && worker.GetComponent<SolidTransferArm>() != null;
        }
    }

    [HarmonyPatch(typeof(Bottler), "CreateBottleProxyObject")]
    internal static class Bottler_CreateBottleProxyObject_ArmPatch
    {
        private static bool Prefix(WorkerBase worker)
        {
            return !BottlerArmCompat.IsArmWorker(worker);
        }
    }

    [HarmonyPatch(typeof(Bottler), "CleanupBottleProxyObject")]
    internal static class Bottler_CleanupBottleProxyObject_ArmPatch
    {
        private static readonly AccessTools.FieldRef<Bottler, MeterController> WorkerMeter =
            AccessTools.FieldRefAccess<Bottler, MeterController>("workerMeter");

        private static bool Prefix(Bottler __instance)
        {
            // 机械臂工作时从未创建代理瓶，workerMeter 为空，直接跳过以免原版报错上报
            return __instance == null || WorkerMeter(__instance) != null;
        }
    }

    [HarmonyPatch(typeof(Workable), nameof(Workable.GetAnim))]
    internal static class Workable_GetAnim_BottlerArmPatch
    {
        private static void Postfix(Workable __instance, WorkerBase worker, ref Workable.AnimInfo __result)
        {
            if (__instance is Bottler && BottlerArmCompat.IsArmWorker(worker))
            {
                __result.overrideAnims = null;
            }
        }
    }
}
