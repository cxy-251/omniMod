using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Diagnostics
{
    /// <summary>只对整批机械臂计时，记录慢调用期间是否发生完整 GC。</summary>
    [HarmonyPatch(typeof(SolidTransferArm), nameof(SolidTransferArm.BatchUpdate))]
    internal static class SolidTransferArm_BatchUpdate_SlowCall_Patch
    {
        private static void Prefix(out ModLog.CallTimer __state)
        {
            __state = ModLog.StartCall();
        }

        private static void Postfix(ModLog.CallTimer __state)
        {
            ModLog.SlowCall("机械臂批次", __state);
        }
    }
}
