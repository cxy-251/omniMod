using System;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 将运载管道（TravelTube）及其穿墙/连接构件归类为“背景建筑”（BackWall）。
    /// 
    /// 背景与需求：
    /// 本 Mod 已优化运载管道允许其穿墙穿建筑（独立于建筑层并允许 Anywhere 建造）。
    /// 当玩家在同一格内既建有普通建筑/地砖，又铺设了运载管道时，若想单独拆除运载管道而不破坏
    /// 前景建筑、砖块、气体或液体，原版与第三方沙盒工具（如 PeterHan's Sandbox Tools）由于缺少
    /// 专用的运载管道过滤层，无法单独选中管道。
    /// 
    /// 解决方案：
    /// 响应玩家需求，在过滤拖拽工具（FilteredDragTool，包括拆除工具 DeconstructTool、取消工具 CancelTool
    /// 以及 PeterHan 等沙盒拆除工具 FilteredDestroyTool）中，将运载管道的对象层（ObjectLayer.TravelTube/Tile/Connection）
    /// 与对应的 GameObject 统一映射为 "BackWall"（背景建筑）。
    /// 
    /// 效果：
    /// 1. 在沙盒过滤销毁工具（Sandbox Filtered Destroy Tool）中，勾选【BackWall / 背景建筑】即可单拆运载管道，绝不伤及前景建筑、地砖与管道线缆！
    /// 2. 在原版拆除工具（快捷键 X）中，过滤选择【背景建筑】亦可精准框选拆除运载管道！
    /// </summary>
    public static class TravelTubeBackwallFilterPatch
    {
        public const string BackWallFilterLayer = "BackWall";

        /// <summary>
        /// 判断一个 GameObject 是否属于运载管道系列（成品、在建、桥接、连接段）
        /// </summary>
        public static bool IsTravelTubePiece(GameObject go)
        {
            if (go == null) return false;

            // 1. 直属组件检测
            if (go.GetComponent<TravelTube>() != null ||
                go.GetComponent<ITravelTubePiece>() != null ||
                go.GetComponent<TravelTubeBridge>() != null ||
                go.GetComponent<TravelTubeUtilityNetworkLink>() != null)
            {
                return true;
            }

            // 2. 标签检测
            if (go.HasTag(GameTags.TravelTubeBridges))
            {
                return true;
            }

            // 3. 检查已建成的建筑定义（排除站台 TravelTubeEntrance）
            BuildingComplete bc = go.GetComponent<BuildingComplete>();
            if (bc != null && bc.Def != null)
            {
                string id = bc.Def.PrefabID;
                if (id == "TravelTube" || id == "TravelTubeWallBridge")
                {
                    return true;
                }
                if (bc.Def.ObjectLayer == ObjectLayer.TravelTube ||
                    bc.Def.TileLayer == ObjectLayer.TravelTubeTile ||
                    bc.Def.ReplacementLayer == ObjectLayer.ReplacementTravelTube)
                {
                    return true;
                }
            }

            // 4. 检查在建施工定义
            BuildingUnderConstruction buc = go.GetComponent<BuildingUnderConstruction>();
            if (buc != null && buc.Def != null)
            {
                string id = buc.Def.PrefabID;
                if (id == "TravelTube" || id == "TravelTubeWallBridge")
                {
                    return true;
                }
                if (buc.Def.ObjectLayer == ObjectLayer.TravelTube ||
                    buc.Def.TileLayer == ObjectLayer.TravelTubeTile ||
                    buc.Def.ReplacementLayer == ObjectLayer.ReplacementTravelTube)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 拦截 FilteredDragTool.GetFilterLayerFromGameObject：
        /// 当目标为运载管道构件时，返回 "BackWall" 过滤层。
        /// </summary>
        [HarmonyPatch(typeof(FilteredDragTool), nameof(FilteredDragTool.GetFilterLayerFromGameObject))]
        public static class FilteredDragTool_GetFilterLayerFromGameObject_Patch
        {
            public static void Postfix(GameObject input, ref string __result)
            {
                if (input != null && IsTravelTubePiece(input))
                {
                    __result = BackWallFilterLayer;
                }
            }
        }

        /// <summary>
        /// 拦截 FilteredDragTool.GetFilterLayerFromObjectLayer：
        /// 补充原版遗漏的 34/35/36/37 运载管道网格层，映射为 "BackWall"。
        /// </summary>
        [HarmonyPatch(typeof(FilteredDragTool), nameof(FilteredDragTool.GetFilterLayerFromObjectLayer))]
        public static class FilteredDragTool_GetFilterLayerFromObjectLayer_Patch
        {
            public static void Postfix(ObjectLayer gamer_layer, ref string __result)
            {
                switch (gamer_layer)
                {
                    case ObjectLayer.TravelTube:
                    case ObjectLayer.TravelTubeTile:
                    case ObjectLayer.ReplacementTravelTube:
                    case ObjectLayer.TravelTubeConnection:
                        __result = BackWallFilterLayer;
                        break;
                }
            }
        }

        /// <summary>
        /// 拦截 FilteredDragTool.IsActiveLayer(ObjectLayer)：
        /// 当用户激活 "BackWall" 过滤项或 "ALL" 时，将管道层视作激活层。
        /// </summary>
        [HarmonyPatch(typeof(FilteredDragTool), nameof(FilteredDragTool.IsActiveLayer), new[] { typeof(ObjectLayer) })]
        public static class FilteredDragTool_IsActiveLayer_ObjectLayer_Patch
        {
            public static void Postfix(FilteredDragTool __instance, ObjectLayer layer, ref bool __result)
            {
                if (__result) return;

                switch (layer)
                {
                    case ObjectLayer.TravelTube:
                    case ObjectLayer.TravelTubeTile:
                    case ObjectLayer.ReplacementTravelTube:
                    case ObjectLayer.TravelTubeConnection:
                        if (__instance.IsActiveLayer(BackWallFilterLayer))
                        {
                            __result = true;
                        }
                        break;
                }
            }
        }
    }
}
