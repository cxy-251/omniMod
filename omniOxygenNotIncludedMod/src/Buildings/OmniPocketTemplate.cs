using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 32x32 极限尺寸的纯净毛坯异次元空间模板生成器。
    /// 
    /// 核心参数：
    /// - 物理总网格：32 x 32 (1024 格)
    /// - 外围防护边界：2 格厚度的中子物质（Unobtanium），绝对零导热、零漏气、防辐射、无法被破坏
    /// - 净可用空间：28 格宽 x 28 格高 = 784 格巨型开阔空间（可划分多达 7~8 个满编 96 格大房间！）
    /// - 背景：全域预铺护墙板封底（Granite Drywall）
    /// - 初始环境：充填 20°C、1.2kg/格 常温常压纯净氧气
    /// - 纯净度：零预装垃圾建筑（彻底删除火箭驾驶台、吸顶插座等累赘）
    /// - 仅在左下角保留 1 个进出舱门 (ClustercraftInteriorDoor)
    /// </summary>
    public static class OmniPocketTemplate
    {
        public const string TemplateName = "expansion1::interiors/omni_hyper_habitat";

        private static TemplateContainer cachedTemplate;

        public static TemplateContainer GetOrCreateTemplate()
        {
            if (cachedTemplate != null)
            {
                return cachedTemplate;
            }

            cachedTemplate = CreateMaxInteriorTemplate();
            return cachedTemplate;
        }

        private static TemplateContainer CreateMaxInteriorTemplate()
        {
            var template = new TemplateContainer
            {
                name = "omni_hyper_habitat",
                priority = 100,
                info = new TemplateContainer.Info
                {
                    size = new Vector2f(32, 32),
                    area = 1024
                },
                cells = new List<TemplateClasses.Cell>(1024),
                buildings = new List<TemplateClasses.Prefab>(),
                otherEntities = new List<TemplateClasses.Prefab>()
            };

            // 32x32 空间坐标范围：x ∈ [-16, 15], y ∈ [-16, 15]
            // 外围 2 格保护边界：
            // x <= -15 或 x >= 14 或 y <= -15 或 y >= 14
            for (int x = -16; x <= 15; x++)
            {
                for (int y = -16; y <= 15; y++)
                {
                    bool isBorder = (x <= -15 || x >= 14 || y <= -15 || y >= 14);

                    if (isBorder)
                    {
                        // 2 格厚度的防爆绝热中子物质边界，杜绝任何外部热量与气体干扰
                        var borderCell = new TemplateClasses.Cell(
                            x,
                            y,
                            SimHashes.Unobtanium,
                            293.15f,
                            1000f,
                            null,
                            0,
                            false,
                            SimHashes.Vacuum,
                            0f,
                            0f
                        );
                        template.cells.Add(borderCell);
                    }
                    else
                    {
                        // 28x28 纯净内部可用空间：全域铺设花岗岩护墙板，充入 20°C 纯净氧气
                        var interiorCell = new TemplateClasses.Cell(
                            x,
                            y,
                            SimHashes.Oxygen,
                            293.15f,
                            1.2f,
                            null,
                            0,
                            false,
                            SimHashes.Granite, // 背景板 (Drywall)
                            100f,
                            293.15f
                        );
                        template.cells.Add(interiorCell);
                    }
                }
            }

            // 在左下角地面上放置 1 个进出舱门 (ClustercraftInteriorDoor)
            // 内部可用地板高度为 y = -14，左边界为 x = -14
            var exitDoor = new TemplateClasses.Prefab(
                "ClustercraftInteriorDoor",
                TemplateClasses.Prefab.Type.Other,
                -13,
                -14,
                SimHashes.Unobtanium,
                293.15f,
                1f,
                null,
                0,
                Orientation.Neutral,
                null,
                null,
                0,
                null
            );
            template.otherEntities.Add(exitDoor);

            return template;
        }

        [HarmonyPatch(typeof(TemplateCache), nameof(TemplateCache.GetTemplate))]
        public static class TemplateCache_GetTemplate_Patch
        {
            public static bool Prefix(string templatePath, ref TemplateContainer __result)
            {
                if (templatePath == TemplateName)
                {
                    __result = GetOrCreateTemplate();
                    return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(TemplateCache), nameof(TemplateCache.TemplateExists))]
        public static class TemplateCache_TemplateExists_Patch
        {
            public static bool Prefix(string templatePath, ref bool __result)
            {
                if (templatePath == TemplateName)
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }
    }
}
