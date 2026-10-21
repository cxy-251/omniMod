using HarmonyLib;

namespace OmniMod.Logistics.Compat
{
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
