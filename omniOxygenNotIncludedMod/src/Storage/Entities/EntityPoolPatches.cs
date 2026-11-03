using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using HarmonyLib;
using OmniMod.Collection;
using OmniMod.Core.Logging;

namespace OmniMod.Pool
{
    [HarmonyPatch(typeof(Storage), nameof(Storage.Store))]
    internal static class Storage_EntityPortal_Store
    {
        internal struct StoreState
        {
            internal bool Portal;
            internal bool Pool;
            internal Vector3 Position;
        }
        private static void Prefix(Storage __instance, GameObject go, ref bool is_deserializing, out StoreState __state)
        {
            __state = new StoreState { Pool = OmniEntityPool.IsPool(__instance),
                Position = go != null ? go.transform.GetPosition() : Vector3.zero };
            __state.Portal = !is_deserializing && !App.isLoading && OmniEntityPool.Instance != null &&
                AggregateBoxRegistry.TryGetBox(__instance, out _) && go != null &&
                OmniEntityPool.CanTrackAtPortal(go.GetComponent<Pickupable>());
            // 保留真实物品的独立身份，避免入库时合并状态不同的实体。
            if (__state.Portal) is_deserializing = true;
        }
        private static void Postfix(Storage __instance, GameObject __result, StoreState __state)
        {
            if (__result == null) return;
            // SaveGame 只是持久化根，不是世界坐标。池内物品保留最后的有效坐标，
            // 绝不把隐藏实体移到 Grid 原点或另一个星球的边界。
            if (__state.Pool) __result.transform.SetPosition(__state.Position);
            if (!__state.Portal) return;
            OmniEntityPool.ApplyPortalModifiers(__instance, __result);
            OmniEntityPool.Instance?.Observe(__result.GetComponent<Pickupable>());
        }
    }

    [HarmonyPatch(typeof(Storage), "OnQueueDestroyObject")]
    internal static class Storage_EntityPortal_Demolition
    {
        private static bool Prefix(Storage __instance)
        {
            // 保存根不是可拆建筑；退出游戏也不能把全池 DropAll 到根对象坐标。
            if (OmniEntityPool.IsPool(__instance)) return false;
            if (AggregateBoxRegistry.TryGetBox(__instance, out _))
                OmniEntityPool.Instance?.ReturnPortal(__instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetAmountWithoutTag))]
    internal static class WorldInventory_EntityPool_FilteredAmount
    {
        private static void Postfix(WorldInventory __instance, Tag tag, Tag[] forbiddenTags, ref float __result)
        {
            if (forbiddenTags == null || !EntityPoolAccess.Ready || __instance.WorldContainer == null) return;
            int access = SharedBoxInventory.AccessMask(__instance.WorldContainer.id);
            if (access == 0) return;
            if ((access & 1) != 0) __result += SharedMaterialSupply.GetFilteredMass(tag, forbiddenTags);
            // 原版已计入公共出口；仅补上它因 StoredPrivate 排除的中央库实体，避免再加出口一次。
            var candidates = OmniEntityPool.Instance.Find(tag);
            if (candidates == null) return;
            for (int i = 0; i < candidates.Count; i++)
            {
                Pickupable pickup = candidates[i];
                if (pickup != null && OmniEntityPool.IsPool(pickup.storage) &&
                    (OmniEntityPool.AccessFor(pickup.KPrefabID) & access) != 0 && !pickup.KPrefabID.HasAnyTags(forbiddenTags))
                    __result += pickup.TotalAmount;
            }
        }
    }

    [HarmonyPatch(typeof(Storage), nameof(Storage.Remove))]
    internal static class Storage_EntityPool_Remove
    {
        private static void Postfix(Storage __instance, UnityEngine.GameObject go) => OmniEntityPool.Instance?.Removed(__instance, go);
    }

    [HarmonyPatch(typeof(Pickupable), "OnCleanUp")]
    internal static class Pickupable_EntityPool_Destroyed
    {
        private static void Prefix(Pickupable __instance) => OmniEntityPool.Instance?.ForgetDestroyed(__instance);
    }

    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.OnStore))]
    internal static class Pickupable_EntityPool_Observe
    {
        private static void Postfix(Pickupable __instance) => OmniEntityPool.Instance?.Observe(__instance);
    }


    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.OnStore))]
    internal static class Pickupable_EntityPool_StoredCell
    {
        internal static int ResolveStoredCell(KMonoBehaviour storage, Pickupable pickup) =>
            storage is OmniEntityPool ? Grid.PosToCell(pickup) : Grid.PosToCell(storage);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            MethodInfo resolver = AccessTools.Method(typeof(Pickupable_EntityPool_StoredCell), nameof(ResolveStoredCell));
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Grid) &&
                    method.Name == nameof(Grid.PosToCell) && method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(KMonoBehaviour))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0);
                    load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                    yield return load;
                    yield return new CodeInstruction(OpCodes.Call, resolver);
                    replaced++;
                }
                else yield return instruction;
            }
            if (replaced != 1) throw new InvalidOperationException("Pickupable.OnStore 的储存坐标入口与 744825 不一致");
        }
    }
    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.UpdateCachedCellFromStoragePosition))]
    internal static class Pickupable_EntityPool_RefreshCell
    {
        private static bool Prefix(Pickupable __instance)
        {
            if (!OmniEntityPool.IsPool(__instance.storage)) return true;
            __instance.UpdateCachedCell(Grid.PosToCell(__instance));
            return false;
        }
    }

    [HarmonyPatch(typeof(Pickupable), nameof(Pickupable.Take))]
    internal static class Pickupable_EntityPool_Take
    {
        private static void Postfix(Pickupable __instance) => OmniEntityPool.Instance?.Observe(__instance);
    }

    // 只读库存选择器可以列出池中实体，但原版取货器不得从 SaveGame 的坐标直接取它。
    [HarmonyPatch(typeof(FetchManager), nameof(FetchManager.IsFetchablePickup))]
    internal static class FetchManager_EntityPool_Private
    {
        private static bool Prefix(Pickupable pickup, ref bool __result)
        {
            if (EntityPoolAccess.MatchingPoolItem > 0 || pickup == null || !OmniEntityPool.IsPool(pickup.storage)) return true;
            __result = false; return false;
        }
    }
    [HarmonyPatch(typeof(FetchManager), nameof(FetchManager.IsFetchablePickup_Exclude),
        new[] { typeof(KPrefabID), typeof(Storage), typeof(float), typeof(HashSet<Tag>), typeof(Tag[]), typeof(Storage) })]
    internal static class FetchManager_EntityPool_PrivateConsumption
    {
        private static bool Prefix(Storage source, ref bool __result)
        {
            if (EntityPoolAccess.MatchingPoolItem > 0 || !OmniEntityPool.IsPool(source)) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(FetchChore), "End")]
    internal static class FetchChore_EntityPool_Return
    {
        private static void Postfix(FetchChore __instance) => OmniEntityPool.Instance?.ReleaseRequest(__instance);
    }

    [HarmonyPatch(typeof(FetchChore), nameof(FetchChore.FindFetchTarget))]
    internal static class FetchChore_EntityPool_Source
    {
        private static void Postfix(FetchChore __instance, ChoreConsumerState consumer_state, ref Pickupable __result)
        {
            if (__result != null || consumer_state == null || consumer_state.hasSolidTransferArm || !EntityPoolAccess.Ready) return;
            try { __result = EntityPoolAccess.ForChore(consumer_state.gameObject.GetMyWorldId(), __instance,
                consumer_state.navigator, false); }
            catch (Exception ex) { ModLog.Throttle("entity_pool_fetch", "[实体共享池] 生产取用稍后重试：" + ex, 10f); }
        }
    }

    [HarmonyPatch]
    internal static class PersonalSupply_EntityPool_Source
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            // Mono 可能共享引用类型泛型的实现，两种闭包使用完全相同且可重复调用的后置处理。
            yield return AccessTools.Method(typeof(ClosestPickupableSensor<Electrobank>), "FindClosestPickupable");
            yield return AccessTools.Method(typeof(ClosestPickupableSensor<Pickupable>), "FindClosestPickupable");
        }
        private static void Postfix(Storage destination, HashSet<Tag> exclude_tags, ref int cost, Tag categoryTag,
            Tag[] otherRequiredTags, ref Pickupable __result)
        {
            if (__result != null || !EntityPoolAccess.Ready ||
                (categoryTag != GameTags.ChargedPortableBattery && categoryTag != GameTags.SolidLubricant)) return;
            try
            {
                __result = EntityPoolAccess.ForConsumption(destination, exclude_tags, otherRequiredTags, categoryTag, false);
                if (__result != null) cost = __result.GetNavigationCost(destination.GetComponent<Navigator>(), __result.cachedCell);
            }
            catch (Exception ex) { ModLog.Throttle("entity_pool_personal_supply", "[实体共享池] 个人用品领取稍后重试：" + ex, 10f); }
        }
    }
}
