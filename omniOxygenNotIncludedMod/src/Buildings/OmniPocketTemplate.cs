using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 太空生态舱内部空间模板生成器：
    /// 遵循极简净空设计，在 32x32 火箭内部网格中提供标准 30x30（x ∈ [-15, 14], y ∈ [-15, 14]）的全真空纯净空间：
    /// - 不预置任何火箭壁板（RocketWallTile）、不塞中子物质、不预充气体、不预放其他建筑。
    /// - 仅在左下角（-15, -14）放置 1 个原版【太空舱内门】(ClustercraftInteriorDoor)，底部留出 (-15, -15) 供玩家自由铺设玻璃砖或地板。
    /// - 玩家可自由在四周铺设玻璃砖、在顶部建造光伏发电板，并随时在舱内建造原版【电源插座】(RocketInteriorPowerPlug) 与舱外电网直连。
    /// </summary>
    public static class OmniPocketTemplate
    {
        public const string TemplateName = "expansion1::interiors/omni_hyper_habitat";

        public const int MinX = -15;
        public const int MaxX = 14;
        public const int MinY = -15;
        public const int MaxY = 14;

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
            int height = MaxY - MinY + 1;  // 30
            int area = width * height;     // 900

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

            // 仅在左下角放置原版【太空舱内门】(ClustercraftInteriorDoor)
            // 位于 (-15, -14)，下方 (-15, -15) 留空供玩家自行铺设玻璃砖或地板
            template.otherEntities.Add(new TemplateClasses.Prefab(
                "ClustercraftInteriorDoor",
                TemplateClasses.Prefab.Type.Other,
                MinX,
                MinY + 1,
                SimHashes.Steel,
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
