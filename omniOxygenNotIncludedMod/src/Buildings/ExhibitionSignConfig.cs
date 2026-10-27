using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【展览馆标牌】(Exhibition Sign)：
    /// 类似原版【公园标牌】(ParkSign)，用于显式标识一间房间为【展览馆】。
    /// 只有放置了该标牌的房间才会被判定为展览馆，彻底避免与摆放了艺术品/藏品的豪华私人卧室发生房间冲突。
    /// </summary>
    public class ExhibitionSignConfig : IBuildingConfig
    {
        public const string ID = "OmniExhibitionSign";
        public static readonly Tag ExhibitionSignTag = TagManager.Create(ID);

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 1,
                height: 2,
                anim: "parksign_kanim",
                hitpoints: 10,
                construction_time: 10f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER1,
                construction_materials: MATERIALS.ANY_BUILDABLE,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.BONUS.TIER1,
                noise: NOISE_POLLUTION.NONE);

            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.Rooms.ID;
            def.Floodable = false;
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(ExhibitionSignTag, false);
            go.AddOrGet<BuildingTint>().tint = new Color32(255, 215, 130, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }
    }
}
