using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
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
    /// - 额定功率：20kW（20,000W，支持供电调谐提升至 30,000W）。
    /// - 存储可调：存储容量默认 4000 粒子，侧边栏自由调节（0 ~ 4000 粒子）。
    /// - 穿透平分：每次射入粒子光球时按比例分流（默认 50% 穿透，如 50 粒子射入则留 25 穿透 25 给后方机器，两台完美平分）。
    /// - 大档位消耗速率：侧边栏大档位步进调节（5 ~ 100 粒子/秒，步进 5，默认 10 粒子/秒）。
    /// - 设置复制：全面支持 Copy Settings（快捷键 C），一键同步所有参数。
    /// - 自动化端口：带自动化输入端（可连智能电池，充满自动停机并全额穿透放行）与粒子储量满信号输出端。
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

            def.GeneratorWattageRating = 20000f;
            def.GeneratorBaseCapacity = 20000f;
            def.RequiresPowerOutput = true;
            def.PowerOutputOffset = new CellOffset(1, 0);
            def.ExhaustKilowattsWhenActive = 1f;
            def.SelfHeatKilowattsWhenActive = 4f;
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
            storage.autoStore = false; // 由 RadboltGenerator 自行接管穿透与接收判定
            storage.capacity = RadboltGenerator.DEFAULT_STORAGE_CAPACITY;
            storage.PORT_ID = PORT_ID;
            storage.showCapacityStatusItem = true;

            go.AddOrGet<CopyBuildingSettings>();

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
    public class RadboltGenerator : Generator, IMultiSliderControl, ISidescreenButtonControl
    {
        public const float DEFAULT_STORAGE_CAPACITY = 4000f; // 默认存储上限 4000 粒子
        public const float DEFAULT_PASSTHROUGH_PERCENT = 50f; // 默认穿透放行比例 50%（两台平分）
        public const float DEFAULT_CONSUMPTION_RATE = 10f; // 默认消耗速率 10 粒子/秒（大档位 5 10 15 20...）

        [MyCmpReq]
        private HighEnergyParticleStorage _particleStorage;

        [MyCmpReq]
        private KBatchedAnimController _kbac;

        [Serialize]
        [SerializeField]
        public float targetStorageCapacity = DEFAULT_STORAGE_CAPACITY; // 存储上限（默认 4000 粒子，可调 0 ~ 4000）

        [Serialize]
        [SerializeField]
        public float passThroughPercent = DEFAULT_PASSTHROUGH_PERCENT; // 穿透比例（默认 50%，可调 0% ~ 100%）

        [Serialize]
        [SerializeField]
        public float particlesPerSecond = DEFAULT_CONSUMPTION_RATE; // 消耗速率（默认 10 粒子/秒，大档位 5 ~ 100）

        private bool _wasActive;
        private ISliderControl[] _sliderControls;

        private static readonly EventSystem.IntraObjectHandler<RadboltGenerator> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<RadboltGenerator>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);

            _sliderControls = new ISliderControl[3]
            {
                new CapacityController(this),
                new PassThroughPercentController(this),
                new ConsumptionRateController(this)
            };

            BindParticlePort();
        }

        private void BindParticlePort()
        {
            HighEnergyParticlePort port = GetComponent<HighEnergyParticlePort>();
            if (port != null)
            {
                port.onParticleCapture = OnCustomParticleCapture;
                port.onParticleCaptureAllowed = OnCustomParticleCaptureAllowed;
            }
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            RadboltGenerator sourceGen = source.GetComponent<RadboltGenerator>();
            if (sourceGen == null) return;

            targetStorageCapacity = sourceGen.targetStorageCapacity;
            passThroughPercent = sourceGen.passThroughPercent;
            particlesPerSecond = sourceGen.particlesPerSecond;

            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

            ModLog.Info($"[辐射粒子发电机] 已从 {source.name} 复制全部设置：存储上限={targetStorageCapacity:0}, 穿透比例={passThroughPercent:0}%, 消耗速率={particlesPerSecond:0}/s");
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            _particleStorage = GetComponent<HighEnergyParticleStorage>();
            _kbac = GetComponent<KBatchedAnimController>();

            // 升级与容错保护
            if (targetStorageCapacity <= 0f && targetStorageCapacity != 0f)
            {
                targetStorageCapacity = DEFAULT_STORAGE_CAPACITY;
            }

            if (particlesPerSecond < 5f)
            {
                particlesPerSecond = DEFAULT_CONSUMPTION_RATE;
            }

            if (passThroughPercent < 0f || passThroughPercent > 100f)
            {
                passThroughPercent = DEFAULT_PASSTHROUGH_PERCENT;
            }

            if (_particleStorage != null)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

            BindParticlePort();
        }

        public override void EnergySim200ms(float dt)
        {
            base.EnergySim200ms(dt);

            if (_particleStorage != null && _particleStorage.capacity != targetStorageCapacity)
            {
                _particleStorage.capacity = targetStorageCapacity;
            }

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
                    Machinery.BuildingAnimHelper.PlayWorkingAnim(_kbac);
                }
                else
                {
                    Machinery.BuildingAnimHelper.StopWorkingAnim(_kbac, operational);
                }
            }
        }

        // ================= 粒子穿透平分与捕获逻辑 =================

        private bool OnCustomParticleCaptureAllowed(HighEnergyParticle particle)
        {
            // 始终允许接收，并在 OnCustomParticleCapture 内部精确计算留存与穿透传导
            return true;
        }

        private static readonly System.Reflection.FieldInfo DirectionField =
            typeof(HighEnergyParticle).GetField("direction", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        private static EightDirection GetParticleDirection(HighEnergyParticle particle)
        {
            if (particle != null && DirectionField != null)
            {
                return (EightDirection)DirectionField.GetValue(particle);
            }
            return EightDirection.Right;
        }

        private void OnCustomParticleCapture(HighEnergyParticle particle)
        {
            if (_particleStorage == null || particle == null) return;

            float incoming = particle.payload;
            if (incoming <= 0f) return;

            // 自动化红信号停机时，本机不储存，全额放行
            bool canStore = (operational == null || operational.IsOperational);
            float currentStored = _particleStorage.Particles;
            float capacity = targetStorageCapacity;
            float availableSpace = canStore ? Mathf.Max(capacity - currentStored, 0f) : 0f;

            // 计算本机计划截留量（根据穿透比例：如 50% 穿透则留 50%）
            float keepFraction = Mathf.Clamp01(1f - (passThroughPercent / 100f));
            float desiredKeep = incoming * keepFraction;

            // 实际可存量受可用存储空间限制
            float toStore = Mathf.Min(desiredKeep, availableSpace);
            // 穿透放行给后方的粒子数
            float toPass = incoming - toStore;

            if (toStore > 0f)
            {
                _particleStorage.Store(toStore);
            }

            if (toPass > 0.01f)
            {
                EightDirection dir = GetParticleDirection(particle);
                LaunchPassThroughParticle(toPass, dir, particle.transform.position);
            }
        }

        private void LaunchPassThroughParticle(float payload, EightDirection direction, Vector3 incomingPos)
        {
            GameObject prefab = Assets.GetPrefab((Tag)"HighEnergyParticle");
            if (prefab == null) return;

            Vector2 norm = EightDirectionUtil.GetNormal(direction);
            // 向飞行方向前移 1 格，脱离当前接收端口的检测格，沿原方向无缝穿透飞行
            Vector3 spawnPos = incomingPos + new Vector3(norm.x, norm.y, 0f) * 1.0f;

            GameObject go = GameUtil.KInstantiate(prefab, spawnPos, Grid.SceneLayer.FXFront2);
            if (go == null) return;

            go.SetActive(true);
            HighEnergyParticle newParticle = go.GetComponent<HighEnergyParticle>();
            if (newParticle != null)
            {
                newParticle.payload = payload;
                newParticle.capturedBy = GetComponent<HighEnergyParticlePort>();
                newParticle.SetDirection(direction);
            }
        }

        // ================= IMultiSliderControl (三组参数控制滑块) =================

        string IMultiSliderControl.SidescreenTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PANEL_TITLE";

        bool IMultiSliderControl.SidescreenEnabled() => true;

        ISliderControl[] IMultiSliderControl.sliderControls => _sliderControls;

        // 1. 存储上限控制器（0 ~ 4000 粒子，默认 4000）
        protected class CapacityController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public CapacityController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TITLE";
            public string SliderUnits => " 粒子";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => 0f;
            public float GetSliderMax(int index) => 4000f;
            public float GetSliderValue(int index) => _target.targetStorageCapacity;
            public void SetSliderValue(float val, int index)
            {
                _target.targetStorageCapacity = Mathf.Round(Mathf.Clamp(val, 0f, 4000f) / 100f) * 100f;
                if (_target._particleStorage != null)
                {
                    _target._particleStorage.capacity = _target.targetStorageCapacity;
                }
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CAPACITY_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"粒子存储上限：{_target.targetStorageCapacity:0} 粒子（默认 4000 粒子）。\n" +
                $"发电机允许储存的最大辐射粒子总量。可随时按需自由调节。";
        }

        // 2. 穿透放行比例控制器（0% ~ 100%，步进 5%，默认 50% 两台平分）
        protected class PassThroughPercentController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public PassThroughPercentController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TITLE";
            public string SliderUnits => " %";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => 0f;
            public float GetSliderMax(int index) => 100f;
            public float GetSliderValue(int index) => _target.passThroughPercent;
            public void SetSliderValue(float val, int index)
            {
                _target.passThroughPercent = Mathf.Round(Mathf.Clamp(val, 0f, 100f) / 5f) * 5f;
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.PASSTHROUGH_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"穿透放行比例：{_target.passThroughPercent:0}%（默认 50% 两台平分）。\n" +
                $"每次射入粒子光球时，按此比例将部分粒子穿透放行给后方的发电机。\n" +
                $" • 设为 50%：射来 50 粒子，本机留 25 粒子，穿透 25 粒子给下一台发电机，两台完美平分！\n" +
                $" • 设为 100%：全额直通穿透，全部让给后方机器。\n" +
                $" • 设为 0%：全部自身拦截，尽量充满本机。\n" +
                $"（若本机存量已满或自动化红信号停机，多余粒子将自动全额放行，绝不浪费）。";
        }

        // 3. 消耗速率控制器（5 ~ 100 粒子/秒，大档位步进 5，默认 10 粒子/秒）
        protected class ConsumptionRateController : ISingleSliderControl, ISliderControl
        {
            private readonly RadboltGenerator _target;

            public ConsumptionRateController(RadboltGenerator target) => _target = target;

            public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TITLE";
            public string SliderUnits => " 粒子/秒";
            public int SliderDecimalPlaces(int index) => 0;
            public float GetSliderMin(int index) => 5f;
            public float GetSliderMax(int index) => 100f;
            public float GetSliderValue(int index) => _target.particlesPerSecond;
            public void SetSliderValue(float val, int index)
            {
                _target.particlesPerSecond = Mathf.Round(Mathf.Clamp(val, 5f, 100f) / 5f) * 5f;
            }
            public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_RADBOLTGENERATOR.CONSUMPTION_RATE_TOOLTIP";
            public string GetSliderTooltip(int index) =>
                $"辐射粒子消耗速率：{_target.particlesPerSecond:0} 粒子/秒（每周期 {_target.particlesPerSecond * 600f:0} 粒子）。\n" +
                $"满载发电功率：20,000 W。\n" +
                $"大档位步进调节（5、10、15、20、25... 粒子/秒），便于根据辐射粒子发生器的产出节奏精准匹配。";
        }

        // ================= ISidescreenButtonControl (穿透模式预设快捷切换按钮) =================

        public string SidescreenTitle => "穿透模式预设";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                if (Mathf.Approximately(passThroughPercent, 50f))
                    return "穿透模式：两台平分 (50%) [点击切换]";
                if (Mathf.Approximately(passThroughPercent, 100f))
                    return "穿透模式：全额直通 (100%) [点击切换]";
                if (Mathf.Approximately(passThroughPercent, 0f))
                    return "穿透模式：完全拦截 (0%) [点击切换]";
                return $"穿透模式：自定义 ({passThroughPercent:0}%) [点击切换]";
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                return $"【当前穿透比例：{passThroughPercent:0}%】\n" +
                       "点击在常用预设档位之间快速轮换：\n" +
                       " ▶ 两台平分 (50%)：射来 50 留 25 穿 25，两台发电机平分\n" +
                       " ▶ 全额直通 (100%)：本机不存，全额让给下游机器\n" +
                       " ▶ 完全拦截 (0%)：全部拦截并充饱本机\n" +
                       "亦可直接拖动上方滑块自定义任意比例。";
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            if (Mathf.Approximately(passThroughPercent, 50f))
            {
                passThroughPercent = 100f;
            }
            else if (Mathf.Approximately(passThroughPercent, 100f))
            {
                passThroughPercent = 0f;
            }
            else
            {
                passThroughPercent = 50f;
            }

            KFMOD.PlayUISound(GlobalAssets.GetSound("HUD_Click"));
            ModLog.Info($"[辐射粒子发电机] 穿透比例快捷切换为: {passThroughPercent:0}%");
        }
    }
}
