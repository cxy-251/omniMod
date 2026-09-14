using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 背景灯：基于香草挂顶灯（CeilingLight）做的一盏"退到背景层、随处可建"的灯，
    /// 专门用来给毛刺花等需要光照的作物补光，而不占用前景空间、不挡视线。
    ///
    /// - 渲染层挪到 <c>Grid.SceneLayer.Building</c>（在其它建筑后面）。
    /// - 建造规则 <c>Anywhere</c>：可以直接放在作物之间 / 作物后方。
    /// - 光形改成圆形（Circle），半径 6，1800 lux——四周作物都能照到。
    /// </summary>
    public class BackgroundLightConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_BackgroundLight";

        private const int Lux = 1800;
        private const float RangeCells = 6f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 1,
                height: 1,
                anim: "ceilinglight_kanim",
                hitpoints: 10,
                construction_time: 10f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                construction_materials: MATERIALS.ALL_METALS,
                melting_point: 800f,
                build_location_rule: BuildLocationRule.Anywhere,
                decor: BUILDINGS.DECOR.NONE,
                noise: NOISE_POLLUTION.NONE);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 10f;
            def.SelfHeatKilowattsWhenActive = 0.5f;
            def.ViewMode = OverlayModes.Light.ID;
            def.AudioCategory = "Metal";
            def.SceneLayer = Grid.SceneLayer.Building;      // 渲染退到其它建筑后面
            def.ObjectLayer = ObjectLayer.Backwall;         // 占背墙层：同一格里还能再建普通建筑/地砖
            def.Floodable = false;
            def.Overheatable = false;
            return def;
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            LightShapePreview preview = go.AddComponent<LightShapePreview>();
            preview.lux = Lux;
            preview.radius = RangeCells;
            preview.shape = LightShape.Circle;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(GameTags.LightSource);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LoopingSounds>();

            Light2D light = go.AddOrGet<Light2D>();
            light.overlayColour = LIGHT2D.CEILINGLIGHT_OVERLAYCOLOR;
            light.Color = LIGHT2D.CEILINGLIGHT_COLOR;
            light.Range = RangeCells;
            light.shape = LightShape.Circle;
            light.drawOverlay = true;
            light.Lux = Lux;
            light.Offset = new Vector2(0f, 0f);

            go.AddOrGetDef<LightController.Def>();
        }
    }
}
