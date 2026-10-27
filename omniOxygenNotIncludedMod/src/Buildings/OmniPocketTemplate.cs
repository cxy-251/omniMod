using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 太空生态舱内部空间模板生成器：
    /// 严格遵循原版太空舱（habitat_medium）的结构规范，并扩展至 32x32 火箭内部网格的最大可用尺寸：
    /// - 外框（x ∈ [-15, 14], y ∈ [-15, 13]，共 30x29）：标准【火箭壁板】(RocketWallTile, 钢材质)，
    ///   由 WorldContainer.PlaceInteriorTemplate 自动识别并生成完整室内背景区域（ZoneType.RocketInterior）。
    /// - 内部净空（x ∈ [-14, 13], y ∈ [-14, 12]，共 28x27 = 756 格）：纯粹的真空空腔（Vacuum），
    ///   不塞任何中子物质、不预充任何气体、不预铺背景板，完全交给玩家自由规划。
    /// - 左下角（-15, -14）：原版【太空舱内门】(ClustercraftInteriorDoor)，复制人可直接从底层地板进出。
    /// - 门旁地面（-14, -14）：预置 1 个原版【电源插座】(RocketInteriorPowerPlug)，与舱外电源接口直通；
    ///   玩家也可随时在舱内任意壁板旁增建更多【电源插座】。
    /// </summary>
    public static class OmniPocketTemplate
    {
        public const string TemplateName = "expansion1::interiors/omni_hyper_habitat";

        private const int MinX = -15;
        private const int MaxX = 14;
        private const int MinY = -15;
        private const int MaxY = 13;

        private static TemplateContainer cachedTemplate;

        public static TemplateContainer GetOrCreateTemplate()
        {
            if (cachedTemplate == null)
            {
                cachedTemplate = BuildTemplate();
            }
            return cachedTemplate;
        }

        private static TemplateContainer BuildTemplate()
        {
            int width = MaxX - MinX + 1;   // 30
            int height = MaxY - MinY + 1;  // 29
            int area = width * height;     // 870

            var template = new TemplateContainer
            {
                name = "omni_hyper_habitat",
                priority = 100,
                info = new TemplateContainer.Info
                {
                    size = new Vector2f(width, height),
                    area = area
                },
                cells = new List<TemplateClasses.Cell>(area),
                buildings = new List<TemplateClasses.Prefab>(),
                otherEntities = new List<TemplateClasses.Prefab>()
            };

            for (int y = MinY; y <= MaxY; y++)
            {
                for (int x = MinX; x <= MaxX; x++)
                {
                    bool isHull = (x == MinX || x == MaxX || y == MinY || y == MaxY);

                    if (isHull)
                    {
                        template.cells.Add(new TemplateClasses.Cell(
                            x,
                            y,
                            SimHashes.Steel,
                            293.15f,
                            100f,
                            null,
                            0
                        ));

                        template.buildings.Add(new TemplateClasses.Prefab(
                            "RocketWallTile",
                            TemplateClasses.Prefab.Type.Building,
                            x,
                            y,
                            SimHashes.Steel,
                            293.15f,
                            100f,
                            null,
                            0,
                            Orientation.Neutral
                        ));
                    }
                    else
                    {
                        template.cells.Add(new TemplateClasses.Cell(
                            x,
                            y,
                            SimHashes.Vacuum,
                            0f,
                            0f,
                            null,
                            0
                        ));
                    }
                }
            }

            // 预置 1 个舱内【电源插座】，紧邻舱门右侧地面 (-13, -14)，与舱外电网直连
            template.buildings.Add(new TemplateClasses.Prefab(
                "RocketInteriorPowerPlug",
                TemplateClasses.Prefab.Type.Building,
                MinX + 2,
                MinY + 1,
                SimHashes.Cuprite,
                293.15f,
                25f,
                null,
                0,
                Orientation.Neutral
            ));

            // 左下角壁板处放置原版【太空舱内门】(ClustercraftInteriorDoor)
            // 门位于 (-15, -14)，复制人进门落脚点为右侧一格 (-14, -14)，脚下即为 (-14, -15) 坚固地板
            template.otherEntities.Add(new TemplateClasses.Prefab(
                "ClustercraftInteriorDoor",
                TemplateClasses.Prefab.Type.Other,
                MinX,
                MinY + 1,
                SimHashes.Unobtanium,
                294.15f,
                1f,
                null,
                0,
                Orientation.Neutral
            ));

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
