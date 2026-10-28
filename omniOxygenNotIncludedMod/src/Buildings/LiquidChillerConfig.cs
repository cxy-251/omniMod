using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能恒温流体冷却机（Auto Liquid Chiller）：
    /// 采用先进分子吸收与深冷压缩技术，全流体通用，进管直出，
    /// 侧边栏滑条设定出液温度（-200°C ~ +100°C），智能防爆管，超低功耗与自热，
    /// 支持复制设置，全面替代原版繁琐臃肿的液温调节器。
    /// </summary>
    public class LiquidChillerConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_LiquidChiller";

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 2,
                height: 2,
                anim: "liquidconditioner_kanim",
                hitpoints: 100,
                construction_time: 30f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER3,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NONE,
                decor: BUILDINGS.DECOR.NONE);

            def.RequiresPowerInput = true;
            def.EnergyConsumptionWhenActive = 240f;
            def.ExhaustKilowattsWhenActive = 0f;
            def.SelfHeatKilowattsWhenActive = 0.5f;

            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(0, 0);

            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(1, 0);

            def.PowerInputOffset = new CellOffset(0, 1);

            def.ViewMode = OverlayModes.LiquidConduits.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "medium";

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(1, 1));

            def.Floodable = false;
            def.Overheatable = false;

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);

            Storage storage = BuildingTemplates.CreateDefaultStorage(go);
            storage.capacityKg = 2000f;
            storage.showInUI = true;
            storage.allowItemRemoval = false;
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = 10f;
            consumer.capacityKG = 20f;
            consumer.forceAlwaysSatisfied = true;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Store;
            consumer.OperatingRequirement = Operational.State.Operational;

            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.alwaysDispense = true;

            go.AddOrGet<CopyBuildingSettings>();
            go.AddOrGet<LiquidChiller>();

            Prioritizable.AddRef(go);

            // 深冷浅冰蓝质感微调
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(140, 215, 255, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            go.AddOrGetDef<PoweredActiveController.Def>();
        }
    }
}
