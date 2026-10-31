using HarmonyLib;

namespace OmniMod.Buildings
{
    /// <summary>仅隐藏世界列表行，保留真实世界、库存和舱内外视角切换所需的登记。</summary>
    [HarmonyPatch(typeof(WorldSelector), "RefreshToggles")]
    internal static class PocketHabitatWorldSelectorPatch
    {
        private static void Postfix(WorldSelector __instance)
        {
            if (__instance.worldRows == null || ClusterManager.Instance == null) return;
            foreach (var row in __instance.worldRows)
            {
                WorldContainer world = ClusterManager.Instance.GetWorld(row.Key);
                if (world != null && world.GetComponent<OmniPocketHabitat>() != null && row.Value != null)
                    row.Value.gameObject.SetActive(false);
            }
        }
    }
}
