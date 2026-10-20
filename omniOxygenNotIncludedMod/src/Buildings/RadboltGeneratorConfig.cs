using System.Collections.Generic;
using KSerialization;
using STRINGS;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 辐射粒子发电机（Radbolt Generator）：
    /// 复用材料研究终端（辐射研究站 / material_research_centre_kanim）的外观与粒子接口，
    /// 转换为高功率电网发电机。
    ///
    /// - 额定功率：2000W（支持供电调谐提升至 3000W）。
    /// - 粒子消耗：1.0 辐射粒子 / 秒（1 粒子 = 2,000 焦耳清洁能源）。
    /// - 储量容量：2000 辐射粒子（支持高负荷吞吐与缓冲）。
    /// - 自动化端口：带自动化输入端（可连智能电池）与粒子储量信号输出端。
    /// </summary>
    public class RadboltGeneratorConfig : IBuildingConfig
    {
        public const string ID = "OmniMod_RadboltGenerator";
        public const string PORT_ID = "HEP_STORAGE";

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 5,
                height: 3,
                anim: "material_research_centre_kanim",
                hitpoints: 100,
                construction_time: 30f,
                construction_mass: TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                noise: NOISE_POLLUTION.NOISY.TIER1,
                decor: TUNING.BUILDINGS.DECOR.NONE);

            def.GeneratorWattageRating = 2000f;
            def.GeneratorBaseCapacity = 2000f;
            def.RequiresPowerOutput = true;
            def.PowerOutputOffset = new CellOffset(1, 0);
            def.ExhaustKilowattsWhenActive = 0.5f;
            def.SelfHeatKilowattsWhenActive = 2f;
            def.UseHighEnergyParticleInputPort = true;
            def.HighEnergyParticleInputOffset = new CellOffset(-2, 1);
            def.ViewMode = OverlayModes.Power.ID;
            def.AudioCategory = "Metal";
            def.AudioSize = "large";

            def.LogicInputPorts = LogicOperationalController.CreateSingleInputPortList(new CellOffset(-1, 0));
            def.LogicOutputPorts = new List<LogicPorts.Port>
            {
                LogicPorts.Port.OutputPort(
                    PORT_ID,
                    new CellOffset(2, 2),
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE,
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE_ACTIVE,
                    STRINGS.BUILDINGS.PREFABS.HEPENGINE.LOGIC_PORT_STORAGE_INACTIVE)
            };

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.RadiationIDs, ID);
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.IndustrialMachinery);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.PowerBuilding);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.GeneratorType);
            go.GetComponent<KPrefabID>().AddTag(RoomConstraints.ConstraintTags.HeavyDutyGeneratorType);

            Prioritizable.AddRef(go);

            HighEnergyParticleStorage storage = go.AddOrGet<HighEnergyParticleStorage>();
            storage.autoStore = true;
            storage.capacity = 2000f;
            storage.PORT_ID = PORT_ID;
            storage.showCapacityStatusItem = true;

            RadboltGenerator generator = go.AddOrGet<RadboltGenerator>();
            generator.powerDistributionOrder = 8;

            Tinkerable.MakePowerTinkerable(go);
            go.AddOrGetDef<PoweredActiveController.Def>();

            // 给建筑增加科技感淡青色微调，以在视觉上区别于原版材料研究终端
            BuildingTint tint = go.AddOrGet<BuildingTint>();
            tint.tint = new Color32(140, 230, 255, 255);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGet<LogicOperationalController>();
        }
    }

    [SerializationConfig(MemberSerialization.OptIn)]
    public class RadboltGenerator : Generator
    {
        [MyCmpReq]
        private HighEnergyParticleStorage _particleStorage;

        [MyCmpReq]
        private KBatchedAnimController _kbac;

        [SerializeField]
        public float particlesPerSecond = 1.0f; // 1 粒子/秒 = 2000W 满载发电

        private bool _wasActive;

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _particleStorage = GetComponent<HighEnergyParticleStorage>();
            _kbac = GetComponent<KBatchedAnimController>();
        }

        public override void EnergySim200ms(float dt)
        {
            base.EnergySim200ms(dt);

            operational.SetFlag(Generator.wireConnectedFlag, CircuitID != ushort.MaxValue);

            if (!operational.IsOperational)
            {
                SetActiveState(false);
                return;
            }

            if (_particleStorage == null || _particleStorage.IsEmpty() || _particleStorage.Particles <= 0f)
            {
                SetActiveState(false);
                selectable.SetStatusItem(Db.Get().StatusItemCategories.Power, Db.Get().BuildingStatusItems.GeneratorOffline, this);
                selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.WaitingForHighEnergyParticles, true);
                return;
            }

            selectable.ToggleStatusItem(Db.Get().BuildingStatusItems.WaitingForHighEnergyParticles, false);

            float toConsume = Mathf.Min(_particleStorage.Particles, particlesPerSecond * dt);
            if (toConsume > 0f)
            {
                _particleStorage.ConsumeAndGet(toConsume);
                float fraction = toConsume / (particlesPerSecond * dt);
                GenerateJoules(WattageRating * dt * fraction);
                SetActiveState(true);
                selectable.SetStatusItem(Db.Get().StatusItemCategories.Power, Db.Get().BuildingStatusItems.Wattage, this);
            }
            else
            {
                SetActiveState(false);
            }
        }

        private void SetActiveState(bool active)
        {
            if (_wasActive != active)
            {
                _wasActive = active;
                operational.SetActive(active);
                if (active)
                {
                    Automation.ResearchCenter_Sim200ms_AutoResearch_Patch.PlayWorkingAnim(_kbac);
                }
                else
                {
                    Automation.ResearchCenter_Sim200ms_AutoResearch_Patch.StopWorkingAnim(_kbac, operational);
                }
            }
        }
    }
}
