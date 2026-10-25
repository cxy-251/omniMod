using System;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 核废料再处理机主控逻辑：
    /// - 自动感知当前箱内是液态核废料还是固态核废料，无缝切换 ElementConverter 消耗源；
    /// - 支持侧边栏一键切换【自动落料出库】与【机内留存】模式；
    /// - 自动同步 DiscoveredResources，确保浓缩铀被原版与聚合箱及时捕获并共享。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class NuclearReprocessor : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim1000ms
    {
        public enum WasteSource
        {
            Liquid = 0,
            Solid = 1,
        }

        public enum EjectMode
        {
            AutoDrop = 0,      // 累积满 20kg 自动弹出至地面（供聚合箱全图瞬时入库或机械臂拾取）
            KeepInStorage = 1, // 留存在机器内（供贴身机械臂/小人直接提取）
        }

        [Serialize]
        public EjectMode SelectedEjectMode = EjectMode.AutoDrop;

        [Serialize]
        private WasteSource _currentSource = WasteSource.Liquid;

        private Storage _storage;
        private ElementConverter _converter;
        private ManualDeliveryKG _solidDelivery;
        private RadiationEmitter _radiationEmitter;
        private Operational _operational;

        public static readonly Tag LiquidWasteTag = SimHashes.NuclearWaste.CreateTag();
        public static readonly Tag SolidWasteTag = SimHashes.SolidNuclearWaste.CreateTag();
        public static readonly Tag EnrichedUraniumTag = SimHashes.EnrichedUranium.CreateTag();

        public const float WasteConsumptionPerSecond = 5.0f;
        public const float UraniumProductionPerSecond = 0.5f;
        public const float DropThresholdKg = 20.0f;

        public static readonly ElementConverter.ConsumedElement[] ConsumedLiquidWaste = new[]
        {
            new ElementConverter.ConsumedElement(LiquidWasteTag, WasteConsumptionPerSecond),
        };

        public static readonly ElementConverter.ConsumedElement[] ConsumedSolidWaste = new[]
        {
            new ElementConverter.ConsumedElement(SolidWasteTag, WasteConsumptionPerSecond),
        };

        public static readonly ElementConverter.OutputElement[] OutputEnrichedUranium = new[]
        {
            new ElementConverter.OutputElement(UraniumProductionPerSecond, SimHashes.EnrichedUranium, 313.15f, useEntityTemperature: false, storeOutput: true),
        };

        private static readonly EventSystem.IntraObjectHandler<NuclearReprocessor> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<NuclearReprocessor>((component, data) => component.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            _converter = GetComponent<ElementConverter>();
            _solidDelivery = GetComponent<ManualDeliveryKG>();
            _radiationEmitter = GetComponent<RadiationEmitter>();
            _operational = GetComponent<Operational>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            EnsureDiscovered();
            ApplySourceMode(_currentSource);
        }

        private void OnCopySettings(object data)
        {
            if (data is GameObject go)
            {
                NuclearReprocessor other = go.GetComponent<NuclearReprocessor>();
                if (other != null)
                {
                    SelectedEjectMode = other.SelectedEjectMode;
                }
            }
        }

        private void EnsureDiscovered()
        {
            if (DiscoveredResources.Instance != null && !DiscoveredResources.Instance.IsDiscovered(EnrichedUraniumTag))
            {
                DiscoveredResources.Instance.Discover(EnrichedUraniumTag, GameTags.ManufacturedMaterial);
            }
        }

        public void Sim1000ms(float dt)
        {
            if (_storage == null) return;

            // 1. 动态智能源检测：根据箱内存量在液态核废料与固态核废料间平滑切换
            float liquidMass = _storage.GetMassAvailable(LiquidWasteTag);
            float solidMass = _storage.GetMassAvailable(SolidWasteTag);

            if (_currentSource == WasteSource.Liquid && liquidMass < WasteConsumptionPerSecond && solidMass >= WasteConsumptionPerSecond)
            {
                ApplySourceMode(WasteSource.Solid);
            }
            else if (_currentSource == WasteSource.Solid && solidMass < WasteConsumptionPerSecond && liquidMass >= WasteConsumptionPerSecond)
            {
                ApplySourceMode(WasteSource.Liquid);
            }

            // 2. 辐射发射器与工作状态联动（运转时才发射中度辐射）
            if (_radiationEmitter != null && _operational != null)
            {
                _radiationEmitter.SetEmitting(_operational.IsActive);
            }

            // 3. 产物弹出处理
            if (SelectedEjectMode == EjectMode.AutoDrop)
            {
                float uraniumMass = _storage.GetMassAvailable(EnrichedUraniumTag);
                if (uraniumMass >= DropThresholdKg)
                {
                    DropEnrichedUranium();
                }
            }
        }

        private void ApplySourceMode(WasteSource source)
        {
            _currentSource = source;
            if (_converter == null) return;

            if (source == WasteSource.Liquid)
            {
                _converter.consumedElements = ConsumedLiquidWaste;
            }
            else
            {
                _converter.consumedElements = ConsumedSolidWaste;
            }
        }

        private void DropEnrichedUranium()
        {
            if (_storage == null || _storage.items == null) return;

            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject item = _storage.items[i];
                if (item == null) continue;

                KPrefabID kpid = item.GetComponent<KPrefabID>();
                if (kpid != null && kpid.PrefabTag == EnrichedUraniumTag)
                {
                    GameObject dropped = _storage.Drop(item);
                    if (dropped != null)
                    {
                        // 弹出到建筑右侧偏下地面
                        dropped.transform.SetPosition(transform.GetPosition() + new Vector3(1.5f, 0.5f, 0f));
                        EnsureDiscovered();
                    }
                }
            }
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR.TITLE";
        public string SidescreenStatusUrl => "";

        public string SidescreenTitle => "浓缩铀出料配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedEjectMode)
                {
                    case EjectMode.AutoDrop:
                        return "出料：满 20kg 自动弹出地面 [点击切换]";
                    case EjectMode.KeepInStorage:
                        return "出料：留存在机器内部 [点击切换]";
                    default:
                        return "出料模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedEjectMode)
                {
                    case EjectMode.AutoDrop:
                        return "【当前模式：自动弹出地面】\n产物浓缩铀满 20kg 后自动掉落至地面，可被杂物聚合箱自动秒收纳进虚拟资源池并供全图机械臂隔空取物。\n点击切换为【留存在机器内部】。";
                    case EjectMode.KeepInStorage:
                        return "【当前模式：留存在机器内部】\n浓缩铀保存在机器内部存储中，供贴身机械臂或复制人直接取出，不掉落到地面。\n点击切换为【自动弹出地面】。";
                    default:
                        return "点击切换浓缩铀出料处理模式。";
                }
            }
        }

        public int ButtonSideScreenSortOrder() => 20;

        public void OnSidescreenButtonPressed()
        {
            if (SelectedEjectMode == EjectMode.AutoDrop)
            {
                SelectedEjectMode = EjectMode.KeepInStorage;
            }
            else
            {
                SelectedEjectMode = EjectMode.AutoDrop;
                if (_storage != null && _storage.GetMassAvailable(EnrichedUraniumTag) >= DropThresholdKg)
                {
                    DropEnrichedUranium();
                }
            }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public bool SidescreenButtonInteractable() => true;

        public bool SidescreenEnabled() => true;
    }
}
