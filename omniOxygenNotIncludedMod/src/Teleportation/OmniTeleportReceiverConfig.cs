using TUNING;
using UnityEngine;

namespace OmniMod.Teleportation
{
    /// <summary>
    /// 时空传送接收器配置（Omni Teleport Receiver）：
    /// 用于接收从全星系任意时空传送发射器传送而来的复制人。每个星球或火箭内部限造一台。
    /// </summary>
    public class OmniTeleportReceiverConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_TeleportReceiver";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 3,
                height: 3,
                anim: "warp_portal_receiver_kanim",
                hitpoints: 100,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.RAW_MINERALS_OR_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.BONUS.TIER1,
                noise: NOISE_POLLUTION.NONE);

            def.SceneLayer = Grid.SceneLayer.BuildingBack;
            def.ForegroundLayer = Grid.SceneLayer.BuildingFront;
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "medium";

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.AddTag(GameTags.WarpTech);
            go.AddTag(GameTags.NotRoomAssignable);
            go.AddTag(GameTags.UniquePerWorld);

            Prioritizable.AddRef(go);
            go.AddOrGet<Operational>();
            go.AddOrGet<Notifier>();
            go.AddOrGet<LoopingSounds>();
            go.AddOrGet<OmniTeleportReceiver>();

            // 允许在火箭舱内建造与运行
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
