using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    public sealed class RadboltIrradiatorConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_RadboltIrradiator";

        public override string[] GetRequiredDlcIds() => DlcManager.EXPANSION1;

        public override BuildingDef CreateBuildingDef()
        {
            var def = BuildingTemplates.CreateBuildingDef(ID, 1, 1, "radiation_sensor_kanim", 100, 10f,
                new[] { 100f }, MATERIALS.REFINED_METALS, 1600f, BuildLocationRule.NotInTiles,
                BUILDINGS.DECOR.NONE, NOISE_POLLUTION.NONE);
            def.ObjectLayer = ObjectLayer.Building;
            def.SceneLayer = Grid.SceneLayer.Building;
            def.RequiresPowerInput = false;
            def.RequiresPowerOutput = false;
            def.EnergyConsumptionWhenActive = 0f;
            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.ViewMode = OverlayModes.Radiation.ID;
            def.UseHighEnergyParticleInputPort = false;
            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(0, 0));
            def.Deprecated = !Sim.IsRadiationEnabled();
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.RadiationIDs, ID);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            BuildingConfigManager.Instance.IgnoreDefaultKComponent(typeof(RequiresFoundation), prefab_tag);

            var storage = go.AddOrGet<Storage>();
            storage.capacityKg = RadboltIrradiator.RequiredEnrichedUranium;
            storage.showInUI = true;
            storage.showDescriptor = true;
            storage.storageFilters = new List<Tag> { SimHashes.EnrichedUranium.CreateTag() };
            storage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            var delivery = go.AddOrGet<ManualDeliveryKG>();
            delivery.SetStorage(storage);
            delivery.RequestedItemTag = SimHashes.EnrichedUranium.CreateTag();
            delivery.capacity = RadboltIrradiator.RequiredEnrichedUranium;
            delivery.refillMass = RadboltIrradiator.RequiredEnrichedUranium;
            delivery.MinimumMass = 0.01f;
            delivery.choreTypeIDHash = Db.Get().ChoreTypes.MachineFetch.IdHash;
            delivery.operationalRequirement = Operational.State.None;
            delivery.allowPause = true;

            Prioritizable.AddRef(go);

            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = ID.ToTag();
            go.AddOrGet<BuildingTint>().tint = new Color32(115, 235, 190, 255);
        }

        private static void ConfigureRange(GameObject go)
        {
            var range = go.AddOrGet<RangeVisualizer>();
            range.OriginOffset = new Vector2I(15, 0);
            range.RangeMin = new Vector2I(-15, -2);
            range.RangeMax = new Vector2I(15, 2);
            range.TestLineOfSight = false;
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go) => ConfigureRange(go);

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
            var emitter = go.AddOrGet<RadiationEmitter>();
            emitter.radiusProportionalToRads = false;
            emitter.emitType = RadiationEmitter.RadiationEmitterType.Constant;
            emitter.emitRadiusX = 1;
            emitter.emitRadiusY = 1;
            emitter.emitRads = 0f;
            emitter.SetEmitting(false);
            ConfigureRange(go);
            go.AddOrGet<RadboltIrradiator>();
        }
    }
}
