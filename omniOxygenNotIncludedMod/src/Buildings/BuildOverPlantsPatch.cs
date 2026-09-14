using HarmonyLib;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 允许在野生植物上直接盖建筑，不用先把植物铲掉 / 挖走。
    ///
    /// 缺氧其实<b>原生支持</b>"盖在植物上"——建造合法性检查最后一步：
    /// <code>IsAreaClear(..., !PreventBuildOverPlants)</code>
    /// 每个 <see cref="BuildingDef"/> 有个 <c>PreventBuildOverPlants</c> 字段（public bool），
    /// 少数建筑（主要是瓷砖 / 地基类）把它设成了 true，于是那些建筑不能盖在植物上。
    ///
    /// 做法：所有 BuildingDef 注册进 <see cref="Assets"/> 时（<c>AddBuildingDef</c>），
    /// 把这个字段统一改回 false。对香草建筑、DLC 建筑、其它 mod 的建筑、以及我们自己的
    /// 聚合箱都生效。植物会在建造完成时自动被连根拔起（香草行为，无需我们处理）。
    /// </summary>
    [HarmonyPatch(typeof(Assets), nameof(Assets.AddBuildingDef))]
    internal static class Assets_AddBuildingDef_BuildOverPlants_Patch
    {
        private static void Postfix(BuildingDef def)
        {
            if (def != null)
            {
                def.PreventBuildOverPlants = false;
            }
        }
    }
}
