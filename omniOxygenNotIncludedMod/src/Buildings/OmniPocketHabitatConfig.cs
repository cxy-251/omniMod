using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 折叠空间生态巨舱配置（Omni Pocket Hyper-Habitat）：
    /// 3x3 紧凑占地，直接建造于任何楼层或水底（Floodable = false）。
    /// 内部封装 32x32 极限网格空间（净可用面积 28x28 = 784 格大平层），
    /// 全面预铺背景护墙板，充入纯净常温氧气，支持自由隔断与全自动化养殖/工业/生活。
    /// </summary>
    public class OmniPocketHabitatConfig : IBuildingConfig
    {
        public const string ID = "OmniPocketHabitat";

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 3,
                height: 3,
                anim: "rocket_habitat_medium_module_kanim",
                hitpoints: 1000,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 2400f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.BONUS.TIER2,
                noise: NOISE_POLLUTION.NONE);

            def.SceneLayer = Grid.SceneLayer.BuildingBack;
            def.ForegroundLayer = Grid.SceneLayer.BuildingFront;

            // 核心防水与防破坏特性：支持沉入水底深渊
            def.Floodable = false;
            def.Entombable = false;
            def.Overheatable = false;
            def.Invincible = true;
            def.AudioCategory = "Metal";
            def.AudioSize = "large";

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            KPrefabID kpid = go.GetComponent<KPrefabID>();
            kpid.AddTag(GameTags.Bunker);
            kpid.AddTag(GameTags.NotRoomAssignable);

            Prioritizable.AddRef(go);
            go.AddOrGet<Operational>();
            go.AddOrGet<LoopingSounds>();
            go.AddOrGet<NavTeleporter>();
            go.AddOrGet<AccessControl>();

            Ownable ownable = go.AddOrGet<Ownable>();
            ownable.tintWhenUnassigned = false;

            // 核心控制器
            go.AddOrGet<OmniPocketHabitat>();

            // 允许在火箭内部也可建造（无限套娃折叠）
            go.AddOrGetDef<RocketUsageRestriction.Def>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            KBatchedAnimController kbac = go.GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.sceneLayer = Grid.SceneLayer.BuildingBack;
                kbac.fgLayer = Grid.SceneLayer.BuildingFront;
            }
        }
    }
}
