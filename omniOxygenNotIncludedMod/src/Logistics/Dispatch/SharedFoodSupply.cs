using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Pool;

namespace OmniMod.Logistics.Dispatch
{
    /// <summary>吃饭的原版入口与生产取货不同；两者现在使用同一实体池。</summary>
    [HarmonyPatch(typeof(FetchManager), nameof(FetchManager.FindEdibleFetchTarget),
        new[] { typeof(Storage), typeof(HashSet<Tag>), typeof(Tag[]) })]
    internal static class FetchManager_FindEdible_SharedFood_Patch
    {
        private static void Postfix(Storage __0, HashSet<Tag> __1, Tag[] __2, ref Pickupable __result)
        {
            if (__result != null || __2 == null || Array.IndexOf(__2, GameTags.Edible) < 0) return;
            try { __result = EntityPoolAccess.ForConsumption(__0, __1, __2, GameTags.Edible, true); }
            catch (Exception ex) { ModLog.Throttle("shared_food_supply_failed", "[共享供餐] 本次出库稍后重试：" + ex, 10f); }
        }
    }
}
