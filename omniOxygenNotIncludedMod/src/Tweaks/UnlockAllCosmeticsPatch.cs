using HarmonyLib;

namespace OmniMod.Tweaks
{
    /// <summary>
    /// 一次性解锁所有装扮蓝图（建筑外观 / 小人服饰 / 雕塑绘画 / 气球 / Sweepy 皮肤等）。
    ///
    /// 香草里这些是"许可(Permit)"，正常靠 Klei 账号在线领礼包 / 拆礼包获得。
    /// 游戏已用 gbe_fork 完全离线、没有 Klei 账号，永远领不到礼包，所以直接放开。
    ///
    /// 全游戏所有"是否拥有该装扮"的判定都汇聚到一个方法：
    ///   <c>PermitItems.GetOwnedCount(PermitResource) &gt; 0</c>
    ///   （建筑外观选择走 <c>PermitResource.IsUnlocked()</c> → <c>PermitItems.IsPermitUnlocked</c>
    ///     → <c>GetOwnedCount &gt; 0</c>；小人服饰 / Klei 库存界面直接用 <c>GetOwnedCount &gt; 0</c>）。
    /// 用 Postfix 把返回 0 的一律抬到 1 —— 全部装扮蓝图直接解锁（用户已购买所有 DLC）。
    ///
    /// 本补丁纯运行时，不写 KleiItemData 存档，移除 mod 即恢复真实拥有状态。
    /// （<c>UniversalLocked</c> 那种"敬请期待"占位一般没被任何建筑 / 小人选择器引用，
    ///  即便被抬成"已拥有"也不会出现在选择界面；真出现且缺美术再单独处理。）
    /// </summary>
    [HarmonyPatch(typeof(PermitItems), nameof(PermitItems.GetOwnedCount))]
    internal static class PermitItems_GetOwnedCount_UnlockAll_Patch
    {
        private static void Postfix(ref int __result)
        {
            if (__result <= 0)
            {
                __result = 1;
            }
        }
    }
}
