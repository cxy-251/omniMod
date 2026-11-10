using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能微醺特调吧（Omni Liquor Dispenser）：
    /// 管道输入纯净乙醇，固体自动或手动配送蔗糖；
    /// 复制人休息时调配甘冽甜美的特调鸡尾酒，获得【醉意撩人】强效增益（士气+6、抗寒防冻、全属性体质增强、减压）；
    /// 自带娱乐建筑（RecBuilding）标签，完美胜任并完全替代原版饮水机满足高级餐厅与娱乐室的房间要求，彻底消灭饮水机病菌翻车点。
    /// </summary>
    public class LiquorDispenserConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_LiquorDispenser";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 2,
                height: 2,
                anim: "sodamaker_kanim",
                hitpoints: 30,
                construction_time: 15f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NONE,
                decor: BUILDINGS.DECOR.BONUS.TIER2);

            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);

            // 无需连接电网，摆放极其自由，可直接置于任意高级餐厅或娱乐室
            def.RequiresPowerInput = false;

            def.AddSearchTerms(STRINGS.SEARCH_TERMS.MORALE);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            KPrefabID kpid = go.GetComponent<KPrefabID>();
            kpid.AddTag(RoomConstraints.ConstraintTags.RecBuilding);

            Storage storage = go.AddOrGet<Storage>();
            storage.capacityKg = 40f;
            storage.SetDefaultStoredItemModifiers(Storage.StandardFabricatorStorage);

            // 液体管道输入：乙醇
            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.capacityTag = SimHashes.Ethanol.CreateTag();
            consumer.capacityKG = 20f;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            consumer.forceAlwaysSatisfied = true;
            consumer.alwaysConsume = true;

            // 固体材料供应：蔗糖
            ManualDeliveryKG delivery = go.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = SimHashes.Sucrose.CreateTag();
            delivery.capacity = 20f;
            delivery.refillMass = 5f;
            delivery.MinimumMass = 1f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;

            go.AddOrGet<LiquorDispenserWorkable>();
            go.AddOrGet<LiquorDispenser>();

            RoomTracker roomTracker = go.AddOrGet<RoomTracker>();
            roomTracker.requiredRoomType = Db.Get().RoomTypes.RecRoom.Id;
            roomTracker.requirement = RoomTracker.Requirement.Recommended;

            Prioritizable.AddRef(go);

            // 醉意微醺粉晶光泽（Chic Rosy Glow）
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(255, 175, 215, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
        }
    }
}
