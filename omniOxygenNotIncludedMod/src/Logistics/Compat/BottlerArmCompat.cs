using HarmonyLib;

namespace OmniMod.Logistics.Compat
{
    // 装瓶器把库存 Pickupable 的操作目标设为 Bottler，供复制人执行装瓶动画。
    // 机械臂直接按预约量拆取库存，沿用 Pickupable 的原版 Take/Store 和完成回调，
    // 避免占用或覆盖装瓶器的单个 worker，也不改动复制人的工作路径。
    [HarmonyPatch(typeof(StandardWorker), nameof(StandardWorker.StartWork))]
    internal static class StandardWorker_StartWork_BottlerArm_Patch
    {
        private static void Prefix(StandardWorker __instance, ref WorkerBase.StartWorkInfo start_work_info)
        {
            if (!BottlerArmCompat.IsArmWorker(__instance) ||
                !(start_work_info is Pickupable.PickupableStartWorkInfo info) ||
                !(info.workable is Bottler bottler)) return;
            Pickupable source = info.originalPickupable;
            if (source == null || source.storage == null || source.storage != bottler.storage) return;

            var directPickup = new Pickupable.PickupableStartWorkInfo(source, info.amount, result =>
            {
                if (result != null)
                {
                    Pickupable pickup = result.GetComponent<Pickupable>();
                    if (pickup != null)
                    {
                        pickup.targetWorkable = pickup;
                        pickup.RemoveTag(GameTags.GasSource);
                        pickup.RemoveTag(GameTags.LiquidSource);
                        pickup.GetSMI<FetchableMonitor.Instance>()?.SetForceUnfetchable(false);
                    }
                }
                info.setResultCb(result);
            });
            directPickup.workable = source;
            start_work_info = directPickup;
        }
    }

    /// <summary>
    /// 装瓶器与充罐器机械臂动画兼容：
    /// 机械臂没有复制人的 "snapto_chest" 符号与手持动画，跳过代理瓶创建避免动画错乱与 DevLogError。
    /// </summary>
    public static class BottlerArmCompat
    {
        public static bool IsArmWorker(WorkerBase worker)
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
