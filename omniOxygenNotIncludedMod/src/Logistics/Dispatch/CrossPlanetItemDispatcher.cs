using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Collection;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Logistics.Dispatch
{
    /// <summary>
    /// 跨星球离散物品调拨系统：
    /// 离散物品（增幅块、种子、便携电池、衣物、蛋等）全星系共享、绝不均分，
    /// 当在某星球被选中分配或下单时，立即将该 1 件离散物品调拨进目标星球的杂物箱，供本星球小人走去箱子拾取。
    /// </summary>
    public static class CrossPlanetItemDispatcher
    {
        // 标记与门面方法，预留未来扩展
    }

    /// <summary>
    /// 允许跨星球选择增幅块（BionicUpgradeComponent）与箱内可装备物品（Assignable）。
    /// </summary>
    [HarmonyPatch(typeof(Assignable), "OnSpawn")]
    internal static class Assignable_OnSpawn_Patch
    {
        private static readonly FieldInfo PreconditionsField = AccessTools.Field(typeof(Assignable), "assignmentPreconditions");

        private static void Postfix(Assignable __instance)
        {
            ExceptionShield.Execute("Assignable.OnSpawn", () =>
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
            });
        }
    }

    /// <summary>
    /// 当玩家把增幅块或衣物分配给某个星球的小人时，在触发槽位变更事件之前，
    /// 立即将该件增幅块或衣物调拨进目标星球的本地杂物箱，让目标星球的小人走去自己星球的杂物箱拾取安装。
    /// </summary>
    [HarmonyPatch(typeof(Assignable), nameof(Assignable.Assign), new[] { typeof(IAssignableIdentity), typeof(AssignableSlotInstance) })]
    internal static class Assignable_Assign_Patch
    {
        private static void Prefix(Assignable __instance, IAssignableIdentity new_assignee)
        {
            ExceptionShield.Execute("Assignable.Assign", () =>
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
            });
        }
    }

    /// <summary>
    /// 兼容已分配增幅块的存档：当仿生人检查已分配的增幅块是否可拾取时，若该增幅块还在其它星球的箱子或地面上，
    /// 立即将其调拨进仿生人所在星球的杂物箱，随后交由原版 navigator.CanReach 检查能否走到本星杂物箱，杜绝任务闪烁与卡死。
    /// </summary>
    [HarmonyPatch(typeof(BionicUpgradesMonitor.Instance), nameof(BionicUpgradesMonitor.Instance.IsBionicUpgradeComponentObjectAbleToBePickedUp))]
    internal static class BionicUpgradesMonitor_IsBionicUpgradeComponentObjectAbleToBePickedUp_Patch
    {
        private static void Prefix(BionicUpgradesMonitor.Instance __instance, BionicUpgradeComponent upgradecComponent)
        {
            ExceptionShield.Execute("BionicUpgradesMonitor.IsBionicUpgradeComponentObjectAbleToBePickedUp", () =>
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
            });
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
            ExceptionShield.Execute("SingleEntityReceptacle.CreateOrder", () =>
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
            });
        }
    }

    /// <summary>
    /// 当物品跨星球存入 Storage（如收集进主箱、或按需调拨进目标星球杂物箱）时，
    /// 同步刷新两侧星球的 WorldInventory，杜绝幽灵引用或漏统计。
    /// </summary>
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
}
