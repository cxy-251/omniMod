using HarmonyLib;
using UnityEngine;

namespace OmniMod.Tweaks
{
    /// <summary>
    /// Falling Sand（自动清扫）：每次挖穿一格后，把该格新掉落的碎料自动标记为待清扫，
    /// 小人会来收（有 OmniMod 杂物箱时，箱子会先一步吸走——两者不冲突）。
    /// 只做"自动挂清扫任务"这一半；PeterHan 原版还有"碎料正确下落"的物理部分，未包含。
    /// </summary>
    [HarmonyPatch(typeof(WorldDamage), nameof(WorldDamage.OnDigComplete))]
    internal static class WorldDamage_OnDigComplete_AutoSweep_Patch
    {
        private static void Postfix(int cell)
        {
            try
            {
                if (!Grid.IsValidCell(cell))
                {
                    return;
                }

                GameObject go = Grid.Objects[cell, (int)ObjectLayer.Pickupables];
                while (go != null)
                {
                    Clearable c = go.GetComponent<Clearable>();
                    if (c != null && c.isClearable)
                    {
                        c.MarkForClear();
                    }

                    Pickupable pk = go.GetComponent<Pickupable>();
                    ObjectLayerListItem next = (pk != null && pk.objectLayerListItem != null) ? pk.objectLayerListItem.nextItem : null;
                    go = next != null ? next.gameObject : null;
                }
            }
            catch
            {
                // 忽略：清扫标记失败不影响挖掘本身
            }
        }
    }

    /// <summary>
    /// Falling Sand（自动挖掘塌落物）：带 <see cref="GameTags.Unstable"/> 标签的元素
    /// （沙、浮岩、风化盐渣等）因失去支撑而塌落、重新在某格凝固成固体后，自动给那一格
    /// 挂挖掘任务，小人来挖走（有 OmniMod 杂物箱时挖出的碎料会被箱子吸进池）。
    ///
    /// 上面 <see cref="WorldDamage_OnDigComplete_AutoSweep_Patch"/> 只覆盖"玩家挖穿后掉的碎料"；
    /// 塌落是 <c>UnstableGroundManager</c> 驱动的、不走 <c>OnDigComplete</c>。这里改订阅
    /// <c>World.OnSolidChanged</c>（每格固体状态变化都回调），筛出"新变成固体的不稳定元素格"标记。
    /// </summary>
    [HarmonyPatch(typeof(World), "OnSpawn")]
    internal static class World_OnSpawn_AutoDigFallenGround_Patch
    {
        private static void Postfix(World __instance)
        {
            __instance.OnSolidChanged -= OnSolidChanged;   // 防止重进/读档重复订阅
            __instance.OnSolidChanged += OnSolidChanged;
        }

        private static void OnSolidChanged(int cell)
        {
            try
            {
                if (!Grid.IsValidCell(cell) || !Grid.Solid[cell] || Grid.Foundation[cell])
                {
                    return;
                }

                Element e = Grid.Element[cell];
                if (e == null || !e.IsUnstable)
                {
                    return;   // 只处理沙 / 浮岩这类不稳定元素
                }

                if (Grid.Objects[cell, (int)ObjectLayer.DigPlacer] != null)
                {
                    return;   // 已经标记过挖掘
                }

                DigTool.PlaceDig(cell, 0);
            }
            catch
            {
                // 标记失败不影响游戏本身
            }
        }
    }

    /// <summary>
    /// Deselect New Materials：储物类建筑的资源筛选树，发现新资源时不再自动勾上
    /// （<c>TreeFilterable.preventAutoAddOnDiscovery = true</c>）。
    /// 例外：OmniMod 自己的聚合箱要靠自动补勾来持续收集新元素，所以跳过它们。
    /// </summary>
    [HarmonyPatch(typeof(TreeFilterable), "OnPrefabInit")]
    internal static class TreeFilterable_NoAutoAdd_Patch
    {
        private static void Postfix(TreeFilterable __instance)
        {
            if (__instance != null && __instance.GetComponent<OmniMod.Collection.GlobalItemCollector>() == null)
            {
                __instance.preventAutoAddOnDiscovery = true;
            }
        }
    }

    /// <summary>
    /// 更大视野：把相机的"最远拉远"上限从 20 提到 48（约 2.4 倍），
    /// 起始缩放不变，只是能继续往外滚滚轮。
    /// </summary>
    [HarmonyPatch(typeof(CameraController), "OnPrefabInit")]
    internal static class CameraController_ZoomOut_Patch
    {
        private const float MaxOrthographicSize = 48f;

        private static void Postfix(CameraController __instance)
        {
            Traverse.Create(__instance).Field("maxOrthographicSize").SetValue(MaxOrthographicSize);
        }
    }

    /// <summary>
    /// 传送管道调整：
    ///  - 建造规则 <c>NotInTiles</c> → <c>Anywhere</c>：可直接铺进墙体 / 铺进构筑好的地砖
    ///    （管子不占地砖层，建造也不要求挖开——TravelTubeConfig 已设 isDiggingRequired = false）。
    ///  - 渲染层 <c>BuildingFront</c> → <c>Building</c>：往后挪一层，视觉上更像背景，
    ///    不再盖在其它建筑前面。
    /// </summary>
    [HarmonyPatch(typeof(TravelTubeConfig), nameof(TravelTubeConfig.CreateBuildingDef))]
    internal static class TravelTube_Tweaks_Patch
    {
        private static void Postfix(BuildingDef __result)
        {
            if (__result != null)
            {
                __result.BuildLocationRule = BuildLocationRule.Anywhere;
                __result.SceneLayer = Grid.SceneLayer.Building;
                // 从"建筑层"挪到传送管专属层：同一格里还能再建普通建筑，管子留在后面
                __result.ObjectLayer = ObjectLayer.TravelTube;
            }
        }
    }
}
