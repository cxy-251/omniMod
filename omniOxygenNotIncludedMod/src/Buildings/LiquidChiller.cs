using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全能恒温流体冷却机主控逻辑：
    /// - 管道输入任意液体（满管 10 kg/s 通量）；
    /// - 侧边栏滑条设定目标温度（-200°C ~ +100°C）；
    /// - 智能防爆管：若设定温度低于流体冰点，自动锁死在冰点 +2°C，永不相变爆管；
    /// - 侧边栏按钮切换【单向快速制冷】与【双向精确恒温】模式；
    /// - 超低自热与功耗，零废热外泄；
    /// - 全面支持【复制设置】（Copy Settings）。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class LiquidChiller : KMonoBehaviour, ISim200ms, ISaveLoadable, ISidescreenButtonControl, ISingleSliderControl
    {
        public enum ChillerMode
        {
            CoolOnly = 0,   // 单向快速制冷（仅对高于目标温度的液体降温，更冷的液体不加热）
            Thermostat = 1  // 双向精确恒温（热则降温，冷则加热，始终锁定目标温度）
        }

        [Serialize]
        public ChillerMode SelectedMode = ChillerMode.CoolOnly;

        [Serialize]
        public float TargetTemperatureCelsius = 20f;

        private Storage _storage;
        private Operational _operational;

        private static readonly EventSystem.IntraObjectHandler<LiquidChiller> OnStorageChangeDelegate =
            new EventSystem.IntraObjectHandler<LiquidChiller>((cmp, data) => cmp.ProcessStoredLiquids());

        private static readonly EventSystem.IntraObjectHandler<LiquidChiller> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<LiquidChiller>((cmp, data) => cmp.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _operational = GetComponent<Operational>();
            Subscribe((int)GameHashes.OnStorageChange, OnStorageChangeDelegate);
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            if (_operational == null) _operational = GetComponent<Operational>();
            ProcessStoredLiquids();
        }

        public void Sim200ms(float dt)
        {
            ProcessStoredLiquids();
        }

        public void ProcessStoredLiquids()
        {
            if (_storage == null || _operational == null || !_operational.IsOperational) return;

            bool isWorking = false;
            for (int i = 0; i < _storage.items.Count; i++)
            {
                GameObject item = _storage.items[i];
                if (item == null) continue;

                PrimaryElement pe = item.GetComponent<PrimaryElement>();
                if (pe == null || !pe.Element.IsLiquid) continue;

                float lowTemp = pe.Element.lowTemp;
                float highTemp = pe.Element.highTemp;

                // 智能防爆管：出液温度最低不能低于冰点+2度，最高不能高于沸点-2度
                float safeMin = lowTemp + 2.0f;
                float safeMax = highTemp - 2.0f;
                float targetKelvin = Mathf.Clamp(TargetTemperatureCelsius + 273.15f, safeMin, safeMax);

                if (SelectedMode == ChillerMode.CoolOnly)
                {
                    if (pe.Temperature > targetKelvin)
                    {
                        pe.Temperature = targetKelvin;
                        isWorking = true;
                    }
                }
                else // Thermostat (双向恒温)
                {
                    if (Mathf.Abs(pe.Temperature - targetKelvin) > 0.05f)
                    {
                        pe.Temperature = targetKelvin;
                        isWorking = true;
                    }
                }
            }

            _operational.SetActive(isWorking || _storage.items.Count > 0);
        }

        public void SetMode(ChillerMode mode)
        {
            SelectedMode = mode;
            ProcessStoredLiquids();
            ModLog.Info($"[全能恒温流体冷却机] 工作模式已切换为: {SelectedMode}");
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            LiquidChiller sourceChiller = source.GetComponent<LiquidChiller>();
            if (sourceChiller == null) return;

            SelectedMode = sourceChiller.SelectedMode;
            TargetTemperatureCelsius = sourceChiller.TargetTemperatureCelsius;
            ProcessStoredLiquids();

            ModLog.Info($"[全能恒温流体冷却机] 已从 {source.name} 复制温控设置：模式={SelectedMode}, 目标出水={TargetTemperatureCelsius:0}°C");
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "温控模式配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case ChillerMode.CoolOnly:
                        return "温控模式：单向快速制冷 [点击切换为双向恒温]";
                    case ChillerMode.Thermostat:
                        return "温控模式：双向精确恒温 [点击切换为单向制冷]";
                    default:
                        return "温控模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case ChillerMode.CoolOnly:
                        return "【当前：单向快速制冷】\n仅对温度高于设定值的液体进行降温处理，已经比设定值更冷的液体原样放行。\n点击切换为【双向精确恒温】。";
                    case ChillerMode.Thermostat:
                        return "【当前：双向精确恒温】\n无论进水过热或过冷，均统一将出液精确锁定在设定温度（过热则冷却，过冷则加热）。\n点击切换为【单向快速制冷】。";
                    default:
                        return "点击切换温控模式。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            SetMode(SelectedMode == ChillerMode.CoolOnly ? ChillerMode.Thermostat : ChillerMode.CoolOnly);
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TITLE";

        public string SliderUnits => "°C";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => -200f;

        public float GetSliderMax(int index) => 100f;

        public float GetSliderValue(int index) => TargetTemperatureCelsius;

        public void SetSliderValue(float value, int index)
        {
            TargetTemperatureCelsius = Mathf.Round(value);
            ProcessStoredLiquids();
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_LIQUIDCHILLER.TARGET_TEMP_TOOLTIP";

        public string GetSliderTooltip(int index) =>
            $"目标出液温度：{TargetTemperatureCelsius:0}°C\n管道流体经过时一步直接调节至此温度出管。\n若设定温度低于流体冰点，系统将自动锁死在冰点+2°C以保护管道永不爆裂。";
    }
}
