using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Core.Safety;
using OmniMod.Ranching.Registries;
using UnityEngine;

namespace OmniMod.Ranching
{
    /// <summary>
    /// 袋装小动物生命周期监听：确保全星系任何生成的袋装小动物（包括读档、孵化器产出等）实时纳入注册表
    /// </summary>
    [HarmonyPatch(typeof(Baggable), "OnSpawn")]
    internal static class Baggable_OnSpawn_Patch
    {
        private static void Postfix(Baggable __instance)
        {
            if (__instance != null && (__instance.wrangled || __instance.HasTag(GameTags.Creatures.Bagged) || __instance.HasTag(GameTags.Trapped)))
            {
                __instance.gameObject.AddTag(GameTags.Creatures.Deliverable);
                BaggedCreatureRegistry.Register(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Baggable), nameof(Baggable.SetWrangled))]
    internal static class Baggable_SetWrangled_Patch
    {
        private static void Postfix(Baggable __instance)
        {
            if (__instance != null)
            {
                __instance.gameObject.AddTag(GameTags.Creatures.Deliverable);
                BaggedCreatureRegistry.Register(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Baggable), nameof(Baggable.Free))]
    internal static class Baggable_Free_Patch
    {
        private static void Postfix(Baggable __instance)
        {
            if (__instance != null)
            {
                BaggedCreatureRegistry.Unregister(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.UpdateCachedCell))]
    internal static class Pickupable_UpdateCachedCell_BaggedRegistry_Patch
    {
        private static void Prefix(Pickupable __instance, int cell)
        {
            try
            {
                if (__instance != null && __instance.cachedCell != cell && __instance.KPrefabID != null &&
                    (__instance.KPrefabID.HasTag(GameTags.Creatures.Bagged) || __instance.KPrefabID.HasTag(GameTags.Trapped)))
                {
                    var bag = __instance.GetComponent<Baggable>();
                    if (bag != null)
                    {
                        BaggedCreatureRegistry.OnCellChanged(bag, __instance.cachedCell, cell);
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Error("Pickupable_UpdateCachedCell_BaggedRegistry_Patch failed", ex);
            }
        }
    }

    /// <summary>
    /// 放生点全自动解袋与安全释放小动物：
    /// 当机械臂或小人将袋装动物放入放生点时，瞬间自动解开包装并安全释放小动物到目标格子。
    /// </summary>
    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnSpawn")]
    internal static class CreatureDeliveryPoint_OnSpawn_Patch
    {
        private static void Postfix(CreatureDeliveryPoint __instance)
        {
            if (__instance == null) return;
            CreatureDeliveryRegistry.Register(__instance);

            __instance.Subscribe((int)GameHashes.OnStorageChange, (data) =>
            {
                CreatureDeliveryPointHelper.ReleaseAllInStorage(__instance);
            });
            CreatureDeliveryPointHelper.ReleaseAllInStorage(__instance);
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint), "OnCleanUp")]
    internal static class CreatureDeliveryPoint_OnCleanUp_Patch
    {
        private static void Prefix(CreatureDeliveryPoint __instance)
        {
            if (__instance != null)
            {
                CreatureDeliveryRegistry.Unregister(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(CreatureDeliveryPoint.States), nameof(CreatureDeliveryPoint.States.DropAllCreatures))]
    internal static class CreatureDeliveryPoint_DropAllCreatures_Patch
    {
        private static void Postfix(CreatureDeliveryPoint.SMInstance smi)
        {
            if (smi?.master != null)
            {
                CreatureDeliveryPointHelper.ReleaseAllInStorage(smi.master);
            }
        }
    }

    internal static class CreatureDeliveryPointHelper
    {
        private static bool _isReleasing = false;

        internal static void ReleaseAllInStorage(CreatureDeliveryPoint deliveryPoint)
        {
            if (_isReleasing || deliveryPoint == null) return;
            Storage storage = deliveryPoint.GetComponent<Storage>();
            if (storage == null || storage.IsEmpty()) return;

            ExceptionShield.Execute("CreatureDeliveryPoint.ReleaseAll", () =>
            {
                _isReleasing = true;
                try
                {
                    List<GameObject> items = new List<GameObject>(storage.items);
                    int cell = Grid.OffsetCell(Grid.PosToCell(deliveryPoint.transform.GetPosition()), deliveryPoint.spawnOffset);
                    Vector3 spawnPos = Grid.CellToPosCBC(cell, Grid.SceneLayer.Creatures);

                    for (int i = 0; i < items.Count; i++)
                    {
                        GameObject go = items[i];
                        if (go == null) continue;

                        storage.Drop(go, true);
                        go.transform.SetPosition(spawnPos);

                        Baggable baggable = go.GetComponent<Baggable>();
                        if (baggable != null)
                        {
                            BaggedCreatureRegistry.Unregister(baggable);
                            baggable.Free();
                        }

                        KBatchedAnimController anim = go.GetComponent<KBatchedAnimController>();
                        if (anim != null)
                        {
                            anim.SetSceneLayer(Grid.SceneLayer.Creatures);
                        }

                        // 唤醒大脑让小动物立即恢复正常作息
                        CreatureBrain brain = go.GetComponent<CreatureBrain>();
                        if (brain != null)
                        {
                            Game.BrainScheduler?.PrioritizeBrain(brain);
                        }
                    }

                    if (deliveryPoint.critterCapacity != null)
                    {
                        deliveryPoint.critterCapacity.RefreshCreatureCount();
                    }
                }
                finally
                {
                    _isReleasing = false;
                }
            });
        }
    }
}
