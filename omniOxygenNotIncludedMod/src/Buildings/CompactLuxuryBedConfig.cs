using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 紧凑型舒适床铺（Compact Luxury Bed）：
    /// 仅占用 2 格横向空间（2x2，普通行军床大小），却拥有与 4 格豪华床（Luxury Bed）完全一致的
    /// 顶级舒适度与体力恢复效果（LuxuryBedStamina, BedHealth, +40% 舒适度, 装饰度 +20）。
    /// 具有 LuxuryBedType 标签，完美适配集体卧室与私人卧室要求。
    /// </summary>
    public class CompactLuxuryBedConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_CompactLuxuryBed";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 2,
                height: 2,
                anim: "bedlg_kanim",
                hitpoints: 10,
                construction_time: 20f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.RAW_MINERALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.BONUS.TIER2,
                noise: NOISE_POLLUTION.NONE);

            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";
            def.PermittedRotations = PermittedRotations.FlipH;

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.AddOrGet<LoopingSounds>();
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.BedType);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.LuxuryBedType);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.GetComponent<KAnimControllerBase>().initialAnim = "off";

            // 赋予淡金浅紫的高雅光泽微调，在视觉上区别于普通行军床
            var animController = go.GetComponent<KBatchedAnimController>();
            if (animController != null)
            {
                animController.TintColour = new Color(0.98f, 0.92f, 1.0f);
            }

            Bed bed = go.AddOrGet<Bed>();
            bed.effects = new string[] { "LuxuryBedStamina", "BedHealth" };

            Sleepable sleepable = go.AddOrGet<Sleepable>();
            sleepable.overrideAnims = new KAnimFile[] { Assets.GetAnim("anim_sleep_bed_kanim") };
            sleepable.workLayer = Grid.SceneLayer.BuildingFront;

            if (DlcManager.IsContentSubscribed("DLC3_ID"))
            {
                DefragmentationZone defrag = go.AddOrGet<DefragmentationZone>();
                defrag.overrideAnims = new KAnimFile[] { Assets.GetAnim("anim_bionic_kanim") };
            }

            Ownable ownable = go.AddOrGet<Ownable>();
            ownable.slotID = Db.Get().AssignableSlots.Bed.Id;

            go.AddOrGetDef<RocketUsageRestriction.Def>();
        }
    }
}
