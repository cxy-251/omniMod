using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 太空生态舱内部空间模板生成器：
    /// 遵循极简净空设计，在火箭内部网格中提供全真空纯净空间：
    /// - 不预置任何火箭壁板（RocketWallTile）、不塞中子物质、不预充气体、不预放其他建筑。
    /// - 仅在左下角（minX, minY + 1）放置 1 个原版【太空舱内门】(ClustercraftInteriorDoor)，底部留出 (minX, minY) 供玩家自由铺设玻璃砖或地板。
    /// - 玩家可自由在四周铺设玻璃砖、在顶部建造光伏发电板，并随时在舱内建造原版【电源插座】(RocketInteriorPowerPlug) 与舱外电网直连。
    /// - 支持自适应网格装箱尺寸（首选 32x32，当全图标准空间用尽时平滑适配 32x24 等真空大平层，维持 30 格超宽跨度）。
    /// </summary>
    public static class OmniPocketTemplate
    {
        public const string TemplatePrefix = "expansion1::interiors/omni_hyper_habitat";

        private static readonly Dictionary<Vector2I, TemplateContainer> CachedTemplates = new Dictionary<Vector2I, TemplateContainer>();

        public static string GetTemplateNameForSize(Vector2I size)
        {
            if (size.x == 32 && size.y == 32)
            {
                return TemplatePrefix;
            }
            return $"{TemplatePrefix}_{size.x}x{size.y}";
        }

        public static TemplateContainer GetOrCreateTemplate(Vector2I size)
        {
            if (!CachedTemplates.TryGetValue(size, out var template))
            {
                template = BuildTemplate(size);
                CachedTemplates[size] = template;
            }
            return template;
        }

        private static TemplateContainer BuildTemplate(Vector2I size)
        {
            // 内部可用净空（四周各留 1 格净空外沿，与原版 32x32 对应 30x30 一致）
            int interiorW = Mathf.Max(2, size.x - 2);
            int interiorH = Mathf.Max(2, size.y - 2);

            int minX = -(interiorW / 2);
            int maxX = minX + interiorW - 1;
            int minY = -(interiorH / 2);
            int maxY = minY + interiorH - 1;

            int area = interiorW * interiorH;

            var template = new TemplateContainer
            {
                name = $"omni_hyper_habitat_{size.x}x{size.y}",
                priority = 100,
                info = new TemplateContainer.Info
                {
                    size = new Vector2f(interiorW, interiorH),
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

        [HarmonyPatch(typeof(TemplateCache), nameof(TemplateCache.GetTemplate))]
        public static class TemplateCache_GetTemplate_Patch
        {
            public static bool Prefix(string templatePath, ref TemplateContainer __result)
            {
                if (templatePath != null && templatePath.StartsWith(TemplatePrefix))
                {
                    Vector2I size = new Vector2I(32, 32);
                    int idx = templatePath.LastIndexOf('_');
                    if (idx >= 0 && idx < templatePath.Length - 1)
                    {
                        string sub = templatePath.Substring(idx + 1);
                        string[] parts = sub.Split('x');
                        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
                        {
                            size = new Vector2I(w, h);
                        }
                    }
                    __result = GetOrCreateTemplate(size);
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
                if (templatePath != null && templatePath.StartsWith(TemplatePrefix))
                {
                    __result = true;
                    return false;
                }
                return true;
            }
        }
    }
}
