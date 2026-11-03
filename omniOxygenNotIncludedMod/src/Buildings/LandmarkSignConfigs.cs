using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    internal static class LandmarkSignTemplate
    {
        internal static BuildingDef Create(string id)
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(id, 1, 2, "parksign_kanim", 10, 10f,
                BUILDINGS.CONSTRUCTION_MASS_KG.TIER1, MATERIALS.ANY_BUILDABLE, 1600f,
                BuildLocationRule.OnFloor, BUILDINGS.DECOR.BONUS.TIER1, NOISE_POLLUTION.NONE);
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.Rooms.ID;
            def.Floodable = false;
            return def;
        }

        internal static void Configure(GameObject go, Tag tag, Color32 color)
        {
            go.GetComponent<KPrefabID>().AddTag(tag, false);
            go.AddOrGet<BuildingTint>().tint = color;
        }
    }

    public sealed class NaturalHistorySignConfig : IBuildingConfig
    {
        // 保留首版建筑 ID，已有的绿色标牌直接转为自然馆标牌；孵蛋室不再依赖标牌。
        public const string ID = "OmniHatcherySign";
        public static readonly Tag SignTag = TagManager.Create(ID);
        public override BuildingDef CreateBuildingDef() => LandmarkSignTemplate.Create(ID);
        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag) =>
            LandmarkSignTemplate.Configure(go, SignTag, new Color32(155, 225, 160, 255));
        public override void DoPostConfigureComplete(GameObject go) { }
    }

    public sealed class WorkshopSignConfig : IBuildingConfig
    {
        public const string ID = "OmniWorkshopSign";
        public static readonly Tag SignTag = TagManager.Create(ID);
        public override BuildingDef CreateBuildingDef() => LandmarkSignTemplate.Create(ID);
        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag) =>
            LandmarkSignTemplate.Configure(go, SignTag, new Color32(245, 175, 110, 255));
        public override void DoPostConfigureComplete(GameObject go) { }
    }

    public sealed class FitnessSignConfig : IBuildingConfig
    {
        public const string ID = "OmniFitnessSign";
        public static readonly Tag SignTag = TagManager.Create(ID);
        public override BuildingDef CreateBuildingDef() => LandmarkSignTemplate.Create(ID);
        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag) =>
            LandmarkSignTemplate.Configure(go, SignTag, new Color32(120, 200, 245, 255));
        public override void DoPostConfigureComplete(GameObject go) { }
    }
}
