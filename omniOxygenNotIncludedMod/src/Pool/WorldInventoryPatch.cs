using HarmonyLib;
using OmniMod.Collection;

namespace OmniMod.Pool
{
    /// <summary>
    /// 让建造栏"认得"全局杂物池里的材料。
    ///
    /// 建造栏是否灰显、材料选择器显示多少可用量，都走
    /// WorldInventory.GetAmount(tag, includeRelatedWorlds)。我们在它返回之后，
    /// 对"本星球有生效杂物箱"的情况，额外加上池中该元素的质量。
    ///
    /// 只在 includeRelatedWorlds == false 的那一层加（build 菜单传 true 时会内部
    /// 逐个星球用 false 调一遍，从而只加一次）。
    ///
    /// 显示是够了，实际交付靠聚合箱的缓冲区（RestockFromPool 会按需求把池里材料
    /// 物化成真实碎块放进箱子 Storage，小人正常搬运）。
    /// </summary>
    [HarmonyPatch(typeof(WorldInventory), nameof(WorldInventory.GetAmount))]
    internal static class WorldInventory_GetAmount_Patch
    {
        private static void Postfix(WorldInventory __instance, Tag tag, bool includeRelatedWorlds, ref float __result)
        {
            if (includeRelatedWorlds || OmniPool.Instance == null)
            {
                return;
            }

            WorldContainer world = __instance.WorldContainer;
            if (world == null || !AggregateBoxRegistry.WorldHasActiveJunkBox(world.id))
            {
                return;
            }

            float poolMass = OmniPool.Instance.GetMassForTag(tag);
            if (poolMass > 0f)
            {
                __result += poolMass;
            }
        }
    }
}
