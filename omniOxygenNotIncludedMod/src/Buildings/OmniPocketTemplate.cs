using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 太空生态舱内部空间模板生成器：
    /// 遵循极简净空设计，支持多级自适应规格（标准 32x32，自适应 28x28、24x24、20x20、16x16 等）：
    /// - 全真空纯净空间，四周无柱无墙，不塞中子物质、不预充气体、不预放其他建筑。
    /// - 仅在左下角 (minX, minY + 1) 放置 1 个原版【太空舱内门】(ClustercraftInteriorDoor)，底部留出 (minX, minY) 供玩家自由铺设玻璃砖或地板。
    /// - 玩家可自由在四周铺设玻璃砖、在顶部建造光伏发电板，并随时在舱内建造原版【电源插座】(RocketInteriorPowerPlug) 与舱外电网直连。
    /// </summary>
    public static class OmniPocketTemplate
    {
        public const string TemplateName = "expansion1::interiors/omni_hyper_habitat";
        public const string TemplatePrefix = "expansion1::interiors/omni_hyper_habitat_";

        public const int MinX = -15;
        public const int MaxX = 14;
        public const int MinY = -15;
        public const int MaxY = 14;

        private static readonly Dictionary<string, TemplateContainer> CachedTemplates = new Dictionary<string, TemplateContainer>();

        public static string GetTemplateNameForSize(Vector2I worldSize)
        {
            if (worldSize.x == 32 && worldSize.y == 32)
            {
                return TemplateName;
            }
            return $"{TemplatePrefix}{worldSize.x}x{worldSize.y}";
        }

        public static TemplateContainer GetOrCreateTemplate(int worldWidth = 32, int worldHeight = 32)
        {
            string key = $"{worldWidth}x{worldHeight}";
            if (!CachedTemplates.TryGetValue(key, out TemplateContainer template) || template == null)
            {
                template = BuildTemplate(worldWidth, worldHeight);
                CachedTemplates[key] = template;
            }
            return template;
        }

        private static TemplateContainer BuildTemplate(int worldWidth, int worldHeight)
        {
            int innerW = Math.Max(worldWidth - 2, 8);
            int innerH = Math.Max(worldHeight - 2, 8);
            int minX = -(innerW / 2);
            int maxX = minX + innerW - 1;
            int minY = -(innerH / 2);
            int maxY = minY + innerH - 1;
            int area = innerW * innerH;

            var template = new TemplateContainer
            {
                name = $"omni_hyper_habitat_{worldWidth}x{worldHeight}",
                priority = 100,
                info = new TemplateContainer.Info
                {
                    size = new Vector2f(innerW, innerH),
                    area = area
                },
                cells = new List<TemplateClasses.Cell>(area),
                buildings = new List<TemplateClasses.Prefab>(),
                otherEntities = new List<TemplateClasses.Prefab>()
            };

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
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
            // 位于 (minX, minY + 1)，下方 (minX, minY) 留空供玩家自行铺设玻璃砖或地板
            template.otherEntities.Add(new TemplateClasses.Prefab(
                "ClustercraftInteriorDoor",
                TemplateClasses.Prefab.Type.Other,
                minX,
                minY + 1,
                SimHashes.Steel,
                294.15f,
                1f,
                null,
                0,
                Orientation.Neutral
            ));

            return template;
        }

        private static bool TryParseSizeFromTemplatePath(string templatePath, out int width, out int height)
        {
            width = 32;
            height = 32;
            if (string.IsNullOrEmpty(templatePath))
            {
                return false;
            }

            if (templatePath == TemplateName)
            {
                return true;
            }

            if (templatePath.StartsWith(TemplatePrefix))
            {
                string suffix = templatePath.Substring(TemplatePrefix.Length);
                string[] parts = suffix.Split('x');
                if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                {
                    width = w;
                    height = h;
                    return true;
                }
            }
            return false;
        }

        [HarmonyPatch(typeof(TemplateCache), nameof(TemplateCache.GetTemplate))]
        public static class TemplateCache_GetTemplate_Patch
        {
            public static bool Prefix(string templatePath, ref TemplateContainer __result)
            {
                if (TryParseSizeFromTemplatePath(templatePath, out int w, out int h))
                {
                    __result = GetOrCreateTemplate(w, h);
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
                if (TryParseSizeFromTemplatePath(templatePath, out _, out _))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }
    }
}
