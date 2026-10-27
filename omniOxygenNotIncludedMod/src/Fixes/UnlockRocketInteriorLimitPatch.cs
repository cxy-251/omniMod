using HarmonyLib;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 解除太空舱 / 乘员舱的数量上限限制（原版受限于 MAX_ROCKET_INTERIOR_COUNT）。
    /// 强制允许无限建造生态巨舱与火箭乘员舱。
    /// </summary>
    public static class UnlockRocketInteriorLimitPatch
    {
        [HarmonyPatch(typeof(NoFreeRocketInterior), nameof(NoFreeRocketInterior.EvaluateCondition))]
        public static class NoFreeRocketInterior_EvaluateCondition_Patch
        {
            public static bool Prefix(ref bool __result)
            {
                // 恒定返回 true，彻底抹除已达上限禁止建造的红字拦截
                __result = true;
                return false;
            }
        }

        [HarmonyPatch(typeof(ClusterManager), "OnPrefabInit")]
        public static class ClusterManager_OnPrefabInit_Patch
        {
            public static void Postfix()
            {
                // 将底层世界舱槽位上限提升至 250（接近 byte 上限 255）
                ClusterManager.MAX_ROCKET_INTERIOR_COUNT = 250;
            }
        }
    }
}
