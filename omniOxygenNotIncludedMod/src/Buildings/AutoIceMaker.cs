using System.Collections.Generic;
using Klei;
using KSerialization;
using OmniMod.Core.Logging;
using OmniMod.Machinery;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能冷冻造雪制冰机主控逻辑：
    /// - 进水管道直接通入纯水（2.0 kg/s）；
    /// - 自动免人工将纯水急速恒温冷冻为低温疏松雪花（Snow）或坚硬冰块（Ice）；
    /// - 产物严格锁定在设定的绝对结晶温度（-100°C ~ -1°C），落地永不瞬间融化；
    /// - 侧边栏按钮切换产物形态（雪花 / 冰块）；
    /// - 侧边栏滑条自由调节出料温度；
    /// - 完善的工作/待机循环动画（freezerator_kanim）；
    /// - 全面支持【复制设置】（Copy Settings）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class AutoIceMaker : KMonoBehaviour, ISaveLoadable, ISim200ms, ISidescreenButtonControl, ISingleSliderControl, IGameObjectEffectDescriptor
    {
        public enum OutputMode
        {
            Snow = 0, // 疏松雪花（默认：直接供给糖心树等深冷作物施肥）
            Ice = 1   // 致密冰块（适合工业制冷、降温与固态储水）
        }

        [Serialize]
        public OutputMode SelectedMode = OutputMode.Snow;

        [Serialize]
        public float TargetTemperatureCelsius = -15f;

        [Serialize]
        private float _accumulatedMass;

        [Serialize]
        private byte _diseaseIdx = byte.MaxValue;

        [Serialize]
        private int _diseaseCount;

        private Storage _storage;
        private Operational _operational;
        private KBatchedAnimController _kbac;
        private bool _wasActive;

        public static readonly Tag WaterTag = SimHashes.Water.CreateTag();

        private static readonly EventSystem.IntraObjectHandler<AutoIceMaker> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<AutoIceMaker>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _operational = GetComponent<Operational>();
            _kbac = GetComponent<KBatchedAnimController>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_operational == null) _operational = GetComponent<Operational>();
            if (_kbac == null) _kbac = GetComponent<KBatchedAnimController>();
            UpdateActiveVisuals(false);
        }

        protected override void OnCleanUp()
        {
            Unsubscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
            if (_accumulatedMass > 0.05f)
            {
                SpawnProduct(_accumulatedMass);
                _accumulatedMass = 0f;
            }
            base.OnCleanUp();
        }

        public void Sim200ms(float dt)
        {
            if (_operational == null || !_operational.IsOperational)
            {
                UpdateActiveVisuals(false);
                return;
            }

            if (_storage == null)
            {
                UpdateActiveVisuals(false);
                return;
            }

            float available = _storage.GetMassAvailable(WaterTag);
            float waterNeeded = 2.0f * dt;

            if (available >= 0.01f)
            {
                float toConsume = Mathf.Min(waterNeeded, available);
                _storage.ConsumeAndGetDisease(
                    WaterTag,
                    toConsume,
                    out float consumedMass,
                    out SimUtil.DiseaseInfo diseaseInfo,
                    out float _);

                if (consumedMass > 0f)
                {
                    UpdateActiveVisuals(true);
                    _accumulatedMass += consumedMass;
                    if (diseaseInfo.count > 0)
                    {
                        _diseaseIdx = diseaseInfo.idx;
                        _diseaseCount += diseaseInfo.count;
                    }

                    if (_accumulatedMass >= 2.0f)
                    {
                        SpawnProduct(_accumulatedMass);
                        _accumulatedMass = 0f;
                        _diseaseCount = 0;
                    }
                    return;
                }
            }

            // 无水可冻
            if (_accumulatedMass > 0.05f)
            {
                SpawnProduct(_accumulatedMass);
                _accumulatedMass = 0f;
                _diseaseCount = 0;
            }
            UpdateActiveVisuals(false);
        }

        private void SpawnProduct(float mass)
        {
            SimHashes hash = (SelectedMode == OutputMode.Snow) ? SimHashes.Snow : SimHashes.Ice;
            Element element = ElementLoader.FindElementByHash(hash);
            if (element == null || element.substance == null) return;

            float kelvin = Mathf.Clamp(TargetTemperatureCelsius, -100f, -1f) + 273.15f;
            int spawnCell = Grid.OffsetCell(Grid.PosToCell(this), 1, 0);
            Vector3 spawnPos = Grid.CellToPosCCC(spawnCell, Grid.SceneLayer.Ore);

            element.substance.SpawnResource(
                position: spawnPos,
                mass: mass,
                temperature: kelvin,
                disease_idx: _diseaseIdx,
                disease_count: _diseaseCount,
                prevent_merge: false,
                forceTemperature: true,
                manual_activation: false
            );
        }

        private void UpdateActiveVisuals(bool active)
        {
            if (_wasActive != active)
            {
                _wasActive = active;
                if (_operational != null)
                {
                    _operational.SetActive(active);
                }

                if (_kbac != null)
                {
                    if (active)
                    {
                        BuildingAnimHelper.PlayWorkingAnim(_kbac);
                    }
                    else
                    {
                        BuildingAnimHelper.StopWorkingAnim(_kbac, _operational);
                    }
                }
            }
        }

        public void SetMode(OutputMode mode)
        {
            SelectedMode = mode;
            ModLog.Info($"[全能冷冻造雪机] 产物形态已切换为: {SelectedMode}");
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            AutoIceMaker sourceMaker = source.GetComponent<AutoIceMaker>();
            if (sourceMaker == null) return;

            SelectedMode = sourceMaker.SelectedMode;
            TargetTemperatureCelsius = sourceMaker.TargetTemperatureCelsius;

            ModLog.Info($"[全能冷冻造雪机] 已从 {source.name} 复制冷冻设置：形态={SelectedMode}, 温度={TargetTemperatureCelsius:0}°C");
        }

        // ================= IGameObjectEffectDescriptor =================

        public List<Descriptor> GetDescriptors(GameObject go)
        {
            List<Descriptor> list = new List<Descriptor>();
            string product = (SelectedMode == OutputMode.Snow) ? "疏松雪花" : "致密冰块";
            list.Add(new Descriptor(
                $"冷冻产物：{product}（2 kg/s）",
                $"急速冷冻通入的水，生成{product}直接落于地面",
                Descriptor.DescriptorType.Requirement));
            list.Add(new Descriptor(
                $"出料温度：{TargetTemperatureCelsius:0}°C",
                $"产物严格锁定在设定温度，永不因入水水温过高而瞬间融化",
                Descriptor.DescriptorType.Requirement));
            return list;
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "冷冻产物形态";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case OutputMode.Snow:
                        return "冷冻形态：疏松雪花 [点击切换冰块]";
                    case OutputMode.Ice:
                        return "冷冻形态：致密冰块 [点击切换雪花]";
                    default:
                        return "冷冻形态 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case OutputMode.Snow:
                        return "【当前形态：疏松雪花（Snow）】\n水以 2.0 kg/s 急速冷冻为指定温度的雪花直接吐出到地面！\n完美契合《冰封星球》糖心树施肥需求，小人或机械臂可直接抓取。\n点击切换为【致密冰块】。";
                    case OutputMode.Ice:
                        return "【当前形态：致密冰块（Ice）】\n水以 2.0 kg/s 急速冷冻为指定温度的致密冰块直接吐出到地面！\n适合工业制冷、降温工程或冰块储备。\n点击切换为【疏松雪花】。";
                    default:
                        return "点击切换冷冻产物形态。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            SetMode(SelectedMode == OutputMode.Snow ? OutputMode.Ice : OutputMode.Snow);
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TITLE";

        public string SliderUnits => "°C";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => -100f;

        public float GetSliderMax(int index) => -1f;

        public float GetSliderValue(int index) => TargetTemperatureCelsius;

        public void SetSliderValue(float value, int index)
        {
            TargetTemperatureCelsius = Mathf.Round(value);
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_ICEMAKER.TARGET_TEMP_TOOLTIP";

        public string GetSliderTooltip(int index) => $"出料温度：{TargetTemperatureCelsius:0}°C\n急速冷冻水并在地面直接生成该温度的疏松雪花或坚硬冰块。";
    }
}
