using System.Reflection;
using HarmonyLib;
using OmniMod.Collection;
using UnityEngine;

namespace OmniMod.Logistics.RemoteFetch
{
    /// <summary>
    /// 只放宽机械臂对 Pickupable 来源的可达性。Storage 目的地与格子查询始终走原版，
    /// 避免同一格有箱子或动物时连带放宽投放范围，也不依赖选单时最后一次查询的临时目标。
    /// </summary>
    internal static class SolidTransferArmRemotePickup
    {
        internal static bool IsRemoteStorage(Storage storage)
        {
            if (storage == null || !storage.allowItemRemoval) return false;

            GlobalItemCollector box = storage.GetComponent<GlobalItemCollector>();
            if (box != null) return !box.IsRedundant;

            return storage.GetComponent<StorageLocker>() != null ||
                   storage.GetComponent<RationBox>() != null ||
                   storage.GetComponent<Refrigerator>() != null ||
                   BottlerRegistry.IsBottlerStorage(storage);
        }

        internal static bool IsRemoteSource(Pickupable pickup)
        {
            if (pickup == null || pickup.KPrefabID == null || pickup.IsEntombed) return false;
            KPrefabID id = pickup.KPrefabID;
            if (id.HasTag(GameTags.BaseMinion) || pickup.GetComponent<MinionIdentity>() != null ||
                id.HasTag(GameTags.StoredPrivate) || id.HasTag(GameTags.MarkedForMove) ||
                id.HasTag(GameTags.Creatures.ReservedByCreature)) return false;

            // 活物允许：已打包/可交付/陷阱捕获的小动物；绝不能从复制人手中抢取
            Baggable bag = pickup.GetComponent<Baggable>();
            if (bag != null || id.HasTag(GameTags.Creature))
            {
                bool isDeliverable = id.HasTag(GameTags.Creatures.Deliverable) ||
                                     id.HasTag(GameTags.Creatures.Bagged) ||
                                     id.HasTag(GameTags.Trapped) ||
                                     (bag != null && bag.wrangled);
                if (!isDeliverable) return false;

                // 绝不能从复制人手上抢取
                if (pickup.storage != null && (pickup.storage.HasTag(GameTags.BaseMinion) || pickup.storage.GetComponent<MinionIdentity>() != null))
                {
                    return false;
                }

                bool validStorage = pickup.storage == null ||
                                    IsRemoteStorage(pickup.storage) ||
                                    pickup.storage.GetComponent<Trap>() != null ||
                                    pickup.storage.GetComponent<ReusableTrap>() != null;
                return validStorage;
            }

            // 已经落地的瓶装液气也是来源；仍排除正在搬运和机器内部的私有库存。
            PrimaryElement element = pickup.PrimaryElement;
            bool looseBottle = pickup.storage == null && !id.HasTag(GameTags.Stored) &&
                               element != null && (element.Element.IsGas || element.Element.IsLiquid);
            return (looseBottle || IsRemoteStorage(pickup.storage)) &&
                   Assets.IsTagSolidTransferArmConveyable(id.PrefabTag);
        }

        internal static bool CanReach(ChoreConsumer consumer, IApproachable approachable)
        {
            if (consumer == null || consumer.consumerState == null || !consumer.consumerState.hasSolidTransferArm ||
                !(approachable is Pickupable pickup) || !IsRemoteSource(pickup)) return false;
            PrimaryElement primary = pickup.PrimaryElement;
            bool crossWorld = pickup.KPrefabID.HasTag(GameTags.Creature) || pickup.GetComponent<Baggable>() != null ||
                (primary != null && (primary.Element.IsGas || primary.Element.IsLiquid));
            return crossWorld || (pickup.storage != null && pickup.storage.GetMyWorldId() == consumer.GetMyWorldId());
        }
    }

    [HarmonyPatch]
    internal static class ChoreConsumer_GetNavigationCost_RemotePickup_Patch
    {
        private static MethodBase TargetMethod() => AccessTools.Method(typeof(ChoreConsumer),
            nameof(ChoreConsumer.GetNavigationCost), new[] { typeof(IApproachable), typeof(int).MakeByRefType() });

        private static void Postfix(ChoreConsumer __instance, IApproachable approachable, ref int cost, ref bool __result)
        {
            if (!__result && SolidTransferArmRemotePickup.CanReach(__instance, approachable))
            {
                cost = 0;
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(ChoreConsumer), nameof(ChoreConsumer.CanReach), typeof(IApproachable))]
    internal static class ChoreConsumer_CanReach_RemotePickup_Patch
    {
        private static void Postfix(ChoreConsumer __instance, IApproachable approachable, ref bool __result)
        {
            if (!__result) __result = SolidTransferArmRemotePickup.CanReach(__instance, approachable);
        }
    }

    [HarmonyPatch(typeof(ChoreConsumer), nameof(ChoreConsumer.IsWithinReach), typeof(IApproachable))]
    internal static class ChoreConsumer_IsWithinReach_RemotePickup_Patch
    {
        private static void Postfix(ChoreConsumer __instance, IApproachable approachable, ref bool __result)
        {
            if (!__result) __result = SolidTransferArmRemotePickup.CanReach(__instance, approachable);
        }
    }

    // 普通储物箱没有 GlobalItemCollector 的脏标记订阅，物品入箱后也需要更新来源索引。
    [HarmonyPatch(typeof(Storage), "OnSpawn")]
    internal static class Storage_OnSpawn_RemotePickupIndex_Patch
    {
        private static void Postfix(Storage __instance)
        {
            if (!SolidTransferArmRemotePickup.IsRemoteStorage(__instance)) return;
            WorldStorageIndex.Register(__instance);
        }
    }
    [HarmonyPatch(typeof(Storage), "OnCleanUp")]
    internal static class Storage_OnCleanUp_RemotePickupIndex_Patch
    {
        private static void Prefix(Storage __instance) => WorldStorageIndex.Unregister(__instance);
    }

}
