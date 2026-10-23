using System;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 水热合成油反应釜主控逻辑：
    /// - 支持通过侧边栏按钮在【煤炭模式】、【精炼碳模式】、【智能自适应模式】之间一键无缝切换；
    /// - 统一调度单一 ElementConverter，彻底避免多转换器冲突导致的建筑停工（不自动工作）问题；
    /// - 自动管理供料任务（ManualDeliveryKG）的启闭与任务中止，确保存储箱内仅拉取所需碳源。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class PetroleumSynthesizer : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim200ms
    {
        public enum CarbonMode
        {
            Coal = 0,          // 固定使用 煤炭（0.5 kg/s）
            RefinedCarbon = 1, // 固定使用 精炼碳（0.25 kg/s）
            Auto = 2           // 智能自适应（两者皆可，优先消耗精炼碳）
        }

        [Serialize]
        public CarbonMode SelectedMode = CarbonMode.Coal;

        private Storage _storage;
        private ElementConverter _converter;

        private ManualDeliveryKG _deliveryCoal;
        private ManualDeliveryKG _deliveryRefinedCarbon;

        private static readonly EventSystem.IntraObjectHandler<PetroleumSynthesizer> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<PetroleumSynthesizer>((component, data) => component.OnCopySettings(data));

        public static readonly Tag CoalTag = SimHashes.Carbon.CreateTag();
        public static readonly Tag RefinedCarbonTag = SimHashes.RefinedCarbon.CreateTag();
        public static readonly Tag WaterTag = SimHashes.Water.CreateTag();

        public static readonly ElementConverter.ConsumedElement[] ConsumedCoal = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(CoalTag, 0.5f),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedRefinedCarbon = new[]
        {
            new ElementConverter.ConsumedElement(WaterTag, 2.0f),
            new ElementConverter.ConsumedElement(RefinedCarbonTag, 0.25f),
        };

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _converter = GetComponent<ElementConverter>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        private void OnCopySettings(object data)
        {
            GameObject source = data as GameObject;
            if (source == null) return;

            PetroleumSynthesizer sourceSynth = source.GetComponent<PetroleumSynthesizer>();
            if (sourceSynth == null) return;

            SetMode(sourceSynth.SelectedMode);
            ModLog.Info($"[水热合成油反应釜] 已从 {source.name} 复制碳源设置: {SelectedMode}");
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();

            ManualDeliveryKG[] deliveries = GetComponents<ManualDeliveryKG>();
            for (int i = 0; i < deliveries.Length; i++)
            {
                if (deliveries[i].RequestedItemTag == CoalTag)
                {
                    _deliveryCoal = deliveries[i];
                }
                else if (deliveries[i].RequestedItemTag == RefinedCarbonTag)
                {
                    _deliveryRefinedCarbon = deliveries[i];
                }
            }

            ApplyMode();
        }

        public void Sim200ms(float dt)
        {
            if (SelectedMode == CarbonMode.Auto)
            {
                UpdateAutoConversion();
            }
        }

        public void SetMode(CarbonMode mode)
        {
            SelectedMode = mode;
            ApplyMode();
        }

        public void ApplyMode()
        {
            if (_converter == null || _storage == null) return;

            switch (SelectedMode)
            {
                case CarbonMode.Coal:
                    if (_deliveryCoal != null) _deliveryCoal.Pause(false, "Mode: Coal active");
                    if (_deliveryRefinedCarbon != null)
                    {
                        _deliveryRefinedCarbon.AbortDelivery("Switched to Coal");
                        _deliveryRefinedCarbon.Pause(true, "Mode: Coal active");
                    }
                    _converter.consumedElements = ConsumedCoal;
                    break;

                case CarbonMode.RefinedCarbon:
                    if (_deliveryCoal != null)
                    {
                        _deliveryCoal.AbortDelivery("Switched to Refined Carbon");
                        _deliveryCoal.Pause(true, "Mode: Refined Carbon active");
                    }
                    if (_deliveryRefinedCarbon != null) _deliveryRefinedCarbon.Pause(false, "Mode: Refined Carbon active");
                    _converter.consumedElements = ConsumedRefinedCarbon;
                    break;

                case CarbonMode.Auto:
                    if (_deliveryCoal != null) _deliveryCoal.Pause(false, "Mode: Auto active");
                    if (_deliveryRefinedCarbon != null) _deliveryRefinedCarbon.Pause(false, "Mode: Auto active");
                    UpdateAutoConversion();
                    break;
            }
        }

        private void UpdateAutoConversion()
        {
            if (SelectedMode != CarbonMode.Auto || _converter == null || _storage == null) return;

            float refinedCarbonMass = _storage.GetMassAvailable(RefinedCarbonTag);
            if (refinedCarbonMass >= 0.25f)
            {
                if (_converter.consumedElements != ConsumedRefinedCarbon)
                {
                    _converter.consumedElements = ConsumedRefinedCarbon;
                }
                return;
            }

            float coalMass = _storage.GetMassAvailable(CoalTag);
            if (coalMass >= 0.5f)
            {
                if (_converter.consumedElements != ConsumedCoal)
                {
                    _converter.consumedElements = ConsumedCoal;
                }
                return;
            }

            // 若两者暂都不足，默认设为煤炭配方待机
            if (_converter.consumedElements != ConsumedCoal)
            {
                _converter.consumedElements = ConsumedCoal;
            }
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "水热碳源配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedMode)
                {
                    case CarbonMode.Coal:
                        return "碳源：煤炭 [点击切换]";
                    case CarbonMode.RefinedCarbon:
                        return "碳源：精炼碳 [点击切换]";
                    case CarbonMode.Auto:
                        return "碳源：智能自适应 [点击切换]";
                    default:
                        return "碳源模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedMode)
                {
                    case CarbonMode.Coal:
                        return "【当前模式：煤炭】\n配方：水 2.0 kg/s + 煤炭 0.5 kg/s → 石油 2.0 kg/s。\n点击切换为使用【精炼碳】。";
                    case CarbonMode.RefinedCarbon:
                        return "【当前模式：精炼碳】\n配方：水 2.0 kg/s + 精炼碳 0.25 kg/s → 石油 2.0 kg/s。\n点击切换为【智能自适应】模式。";
                    case CarbonMode.Auto:
                        return "【当前模式：智能自适应】\n自动检测箱内物料，优先消耗精炼碳，若无则自动消耗煤炭。\n点击切换为仅使用【煤炭】。";
                    default:
                        return "点击切换水热合成油碳源配方。";
                }
            }
        }

        public bool SidescreenEnabled() => true;

        public bool SidescreenButtonInteractable() => true;

        public int ButtonSideScreenSortOrder() => 20;

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public void OnSidescreenButtonPressed()
        {
            switch (SelectedMode)
            {
                case CarbonMode.Coal:
                    SetMode(CarbonMode.RefinedCarbon);
                    break;
                case CarbonMode.RefinedCarbon:
                    SetMode(CarbonMode.Auto);
                    break;
                case CarbonMode.Auto:
                    SetMode(CarbonMode.Coal);
                    break;
            }

            ModLog.Info($"[水热合成油反应釜] 碳源模式已切换为: {SelectedMode}");
        }
    }
}
