using System;
using HarmonyLib;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 核废料再处理机主控逻辑：
    /// - 支持三种供料模式：智能双模（默认液态优先）、仅液态核废料（纯管道）、仅固态核废料（搬运装填）；
    /// - 当提供液态核废料（管道连接或机内有储液）时，自动中止并暂停固态核废料搬运任务，绝不打扰复制人；
    /// - 支持侧边栏一键切换【供料模式】（智能/纯液态/纯固态）；
    /// - 配合伴生组件 NuclearReprocessorEjector 支持侧边栏一键切换【自动落料出库】与【机内留存】模式；
    /// - 自动同步 DiscoveredResources，确保浓缩铀被原版与聚合箱及时捕获并共享。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class NuclearReprocessor : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim200ms
    {
        public enum FeedMode
        {
            Auto = 0,       // 智能双模（默认）：液态优先。有液态供给/管道接入时彻底关闭固态搬运，无液态时才允许固态后备补充
            LiquidOnly = 1, // 仅液态核废料：纯管道模式，彻底禁用固态核废料搬运，小人绝不会收到搬运任务
            SolidOnly = 2,  // 仅固态核废料：从小人/机械臂搬运装填固态核废料，关闭管道抽取
        }

        public enum EjectMode
        {
            AutoDrop = 0,      // 累积满 20kg 自动弹出至地面（供聚合箱全图瞬时入库或机械臂拾取）
            KeepInStorage = 1, // 留存在机器内（供贴身机械臂/小人直接提取）
        }

        // 保留旧版枚举定义以保证旧存档反序列化完全安全
        public enum WasteSource
        {
            Liquid = 0,
            Solid = 1,
        }

        [Serialize]
        public FeedMode SelectedFeedMode = FeedMode.Auto;

        [Serialize]
        public EjectMode SelectedEjectMode = EjectMode.AutoDrop;

#pragma warning disable CS0414
        [Serialize]
        private WasteSource _currentSource = WasteSource.Liquid;
#pragma warning restore CS0414

        private Storage _storage;
        private ElementConverter _converter;
        private ConduitConsumer _liquidConsumer;
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
            _liquidConsumer = GetComponent<ConduitConsumer>();
            _solidDelivery = GetComponent<ManualDeliveryKG>();
            _radiationEmitter = GetComponent<RadiationEmitter>();
            _operational = GetComponent<Operational>();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            GetComponent<KPrefabID>()?.AddTag(GameTags.CorrosionProof);
            BuildingHP hp = GetComponent<BuildingHP>();
            if (hp != null)
            {
                hp.invincible = true;
                hp.SetHitPoints(hp.MaxHitPoints); // 自动回满生命值，消除历史腐蚀掉血
            }
            EnsureDiscovered();
            EnsureConverterConfigured();
            gameObject.AddOrGet<NuclearReprocessorEjector>();
            ApplyFeedMode();
        }

        private void EnsureConverterConfigured()
        {
            if (_converter == null) return;
            // 容灾检查：如果旧存档中 converter.consumedElements 只有 1 项，补齐双通道并初始化累加器
            if (_converter.consumedElements == null || _converter.consumedElements.Length < 2)
            {
                var elements = new[]
                {
                    new ElementConverter.ConsumedElement(LiquidWasteTag, WasteConsumptionPerSecond, true),
                    new ElementConverter.ConsumedElement(SolidWasteTag, WasteConsumptionPerSecond, false),
                };
                if (Game.Instance != null && Game.Instance.accumulators != null)
                {
                    for (int i = 0; i < elements.Length; i++)
                    {
                        elements[i].Accumulator = Game.Instance.accumulators.Add("ConsumedElement", this);
                    }
                }
                _converter.consumedElements = elements;
            }
        }

        private void OnCopySettings(object data)
        {
            if (data is GameObject go)
            {
                NuclearReprocessor other = go.GetComponent<NuclearReprocessor>();
                if (other != null)
                {
                    SelectedFeedMode = other.SelectedFeedMode;
                    SelectedEjectMode = other.SelectedEjectMode;
                    ApplyFeedMode();
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

        public void Sim200ms(float dt)
        {
            if (_storage == null) return;

            // 0. 兜底排出机内误入的非核废料液体（避免杂质占据储罐容量）
            for (int i = _storage.items.Count - 1; i >= 0; i--)
            {
                GameObject item = _storage.items[i];
                if (item == null) continue;
                PrimaryElement pe = item.GetComponent<PrimaryElement>();
                if (pe != null && pe.Element.IsLiquid && pe.ElementID != SimHashes.NuclearWaste)
                {
                    _storage.Drop(item, true);
                }
            }

            // 1. 动态自适应进料管理
            if (SelectedFeedMode == FeedMode.Auto)
            {
                float liquidMass = _storage.GetMassAvailable(LiquidWasteTag);
                float solidMass = _storage.GetMassAvailable(SolidWasteTag);
                UpdateAutoFeed(liquidMass, solidMass);
            }

            // 2. 状态机驱动：根据转化能力动态启闭工作状态（驱动 480W 功耗、离心机全套转动动画与同位素重富集）
            if (_operational != null && _converter != null)
            {
                if (_operational.IsOperational)
                {
                    bool canConvert = _operational.IsActive ? _converter.CanConvertAtAll() : _converter.HasEnoughMassToStartConverting(false);
                    if (_operational.IsActive != canConvert)
                    {
                        _operational.SetActive(canConvert);
                    }
                }
                else if (_operational.IsActive)
                {
                    _operational.SetActive(false);
                }
            }

            // 3. 辐射发射器与工作状态联动（运转时才发射中度辐射）
            if (_radiationEmitter != null && _operational != null)
            {
                _radiationEmitter.SetEmitting(_operational.IsActive);
            }

            // 4. 产物弹出处理
            if (SelectedEjectMode == EjectMode.AutoDrop)
            {
                CheckDropEnrichedUranium();
            }
        }

        public void SetFeedMode(FeedMode mode)
        {
            SelectedFeedMode = mode;
            ApplyFeedMode();
        }

        public void ApplyFeedMode()
        {
            if (_storage == null || _converter == null) return;
            EnsureConverterConfigured();

            float liquidMass = _storage.GetMassAvailable(LiquidWasteTag);
            float solidMass = _storage.GetMassAvailable(SolidWasteTag);

            switch (SelectedFeedMode)
            {
                case FeedMode.LiquidOnly:
                    if (_liquidConsumer != null) _liquidConsumer.enabled = true;
                    if (_solidDelivery != null)
                    {
                        _solidDelivery.AbortDelivery("FeedMode is LiquidOnly");
                        _solidDelivery.Pause(true, "FeedMode is LiquidOnly");
                    }
                    _converter.SetConsumedElementActive(LiquidWasteTag, true);
                    _converter.SetConsumedElementActive(SolidWasteTag, false);
                    _currentSource = WasteSource.Liquid;
                    break;

                case FeedMode.SolidOnly:
                    if (_liquidConsumer != null) _liquidConsumer.enabled = false;
                    if (_solidDelivery != null)
                    {
                        _solidDelivery.Pause(false, "FeedMode is SolidOnly");
                    }
                    _converter.SetConsumedElementActive(LiquidWasteTag, false);
                    _converter.SetConsumedElementActive(SolidWasteTag, true);
                    _currentSource = WasteSource.Solid;
                    break;

                case FeedMode.Auto:
                    if (_liquidConsumer != null) _liquidConsumer.enabled = true;
                    UpdateAutoFeed(liquidMass, solidMass);
                    break;
            }
        }

        private bool HasLiquidWasteSupply(float liquidMass)
        {
            // 1. 机器内部已有可用液态核废料
            if (liquidMass >= WasteConsumptionPerSecond)
            {
                return true;
            }

            // 2. 管道已接入液体入口
            if (_liquidConsumer != null && _liquidConsumer.IsConnected)
            {
                Building building = GetComponent<Building>();
                int inputCell = building != null ? building.GetUtilityInputCell() : -1;
                if (inputCell >= 0 && Game.Instance != null && Game.Instance.liquidConduitFlow != null)
                {
                    ConduitFlow.ConduitContents contents = Game.Instance.liquidConduitFlow.GetContents(inputCell);
                    // 管道入口处有液态核废料流经
                    if (contents.mass > 0.01f && contents.element == SimHashes.NuclearWaste)
                    {
                        return true;
                    }
                }
                // 只要玩家已接入液体管道，说明意图是管道送液，优先锁定管道供料，避免小人跑来送固废
                return true;
            }

            return false;
        }

        private void UpdateAutoFeed(float liquidMass, float solidMass)
        {
            bool hasLiquid = HasLiquidWasteSupply(liquidMass);

            if (hasLiquid)
            {
                // 液态供给充足或已连接排废管道：彻底暂停固态核废料搬运，取消任务
                if (_solidDelivery != null && !_solidDelivery.IsPaused)
                {
                    _solidDelivery.AbortDelivery("Liquid nuclear waste supplied");
                    _solidDelivery.Pause(true, "Liquid nuclear waste supplied");
                }

                // 转换器消耗设定：有液态存量则消耗液态；若箱内恰有此前遗留的固态且液态断续，优先清空固态
                if (liquidMass >= WasteConsumptionPerSecond)
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, true);
                    _converter.SetConsumedElementActive(SolidWasteTag, false);
                    _currentSource = WasteSource.Liquid;
                }
                else if (solidMass >= WasteConsumptionPerSecond)
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, false);
                    _converter.SetConsumedElementActive(SolidWasteTag, true);
                    _currentSource = WasteSource.Solid;
                }
                else
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, true);
                    _converter.SetConsumedElementActive(SolidWasteTag, false);
                    _currentSource = WasteSource.Liquid;
                }
            }
            else
            {
                // 无液态供给且未连接管道：启用固态搬运作为后备
                if (_solidDelivery != null && _solidDelivery.IsPaused)
                {
                    _solidDelivery.Pause(false, "No liquid nuclear waste supply");
                }

                if (solidMass >= WasteConsumptionPerSecond)
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, false);
                    _converter.SetConsumedElementActive(SolidWasteTag, true);
                    _currentSource = WasteSource.Solid;
                }
                else if (liquidMass >= WasteConsumptionPerSecond)
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, true);
                    _converter.SetConsumedElementActive(SolidWasteTag, false);
                    _currentSource = WasteSource.Liquid;
                }
                else
                {
                    _converter.SetConsumedElementActive(LiquidWasteTag, false);
                    _converter.SetConsumedElementActive(SolidWasteTag, true);
                    _currentSource = WasteSource.Solid;
                }
            }
        }

        public void CheckDropEnrichedUranium()
        {
            if (_storage == null) return;
            float uraniumMass = _storage.GetMassAvailable(EnrichedUraniumTag);
            if (uraniumMass >= DropThresholdKg)
            {
                DropEnrichedUranium();
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

        // ================= ISidescreenButtonControl (供料模式) =================

        public string SidescreenTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR_FEED.TITLE";
        public string SidescreenStatusUrl => "";

        public string SidescreenTitle => "核废料供料模式";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                switch (SelectedFeedMode)
                {
                    case FeedMode.Auto:
                        return "供料：智能双模 (液态优先) [点击切换]";
                    case FeedMode.LiquidOnly:
                        return "供料：仅液态核废料 (纯管道) [点击切换]";
                    case FeedMode.SolidOnly:
                        return "供料：仅固态核废料 (搬运装填) [点击切换]";
                    default:
                        return "供料模式 [点击切换]";
                }
            }
        }

        public string SidescreenButtonTooltip
        {
            get
            {
                switch (SelectedFeedMode)
                {
                    case FeedMode.Auto:
                        return "【当前模式：智能双模 (液态优先)】\n检测到液态管道接入或机内存有液态核废料时，自动彻底关闭固态核废料搬运请求，避免复制人无端运送固废。\n若未接排液管道，则允许复制人/机械臂搬运固态核废料补充。\n点击切换为【仅液态核废料 (纯管道)】。";
                    case FeedMode.LiquidOnly:
                        return "【当前模式：仅液态核废料 (纯管道)】\n仅接受液态核废料管道连续通入。彻底禁用固态核废料搬运任务，复制人与机械臂绝不会收到运送请求。\n点击切换为【仅固态核废料 (搬运装填)】。";
                    case FeedMode.SolidOnly:
                        return "【当前模式：仅固态核废料 (搬运装填)】\n仅接受固态核废料块，由复制人或机械臂按需搬运装填。\n点击切换为【智能双模 (液态优先)】。";
                    default:
                        return "点击切换核废料供料模式。";
                }
            }
        }

        public int ButtonSideScreenSortOrder() => 20;

        public void OnSidescreenButtonPressed()
        {
            switch (SelectedFeedMode)
            {
                case FeedMode.Auto:
                    SetFeedMode(FeedMode.LiquidOnly);
                    break;
                case FeedMode.LiquidOnly:
                    SetFeedMode(FeedMode.SolidOnly);
                    break;
                case FeedMode.SolidOnly:
                    SetFeedMode(FeedMode.Auto);
                    break;
            }

            ModLog.Info($"[核废料再处理机] 供料模式已切换为: {SelectedFeedMode}");
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public bool SidescreenButtonInteractable() => true;

        public bool SidescreenEnabled() => true;
    }

    /// <summary>
    /// 核废料再处理机出料控制伴生组件：
    /// 独立实现 ISidescreenButtonControl，使得建筑侧边栏能同时呈现【供料模式】与【出料配置】两个控制按钮。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class NuclearReprocessorEjector : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        private NuclearReprocessor _reprocessor;

        public NuclearReprocessor Reprocessor
        {
            get
            {
                if (_reprocessor == null)
                {
                    _reprocessor = GetComponent<NuclearReprocessor>();
                }
                return _reprocessor;
            }
            set => _reprocessor = value;
        }

        public string SidescreenTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_REPROCESSOR_EJECT.TITLE";
        public string SidescreenStatusUrl => "";

        public string SidescreenTitle => "浓缩铀出料配置";

        public int HorizontalGroupID() => -1;

        public string SidescreenButtonText
        {
            get
            {
                NuclearReprocessor repo = Reprocessor;
                if (repo == null) return "出料模式 [点击切换]";
                switch (repo.SelectedEjectMode)
                {
                    case NuclearReprocessor.EjectMode.AutoDrop:
                        return "出料：满 20kg 自动弹出地面 [点击切换]";
                    case NuclearReprocessor.EjectMode.KeepInStorage:
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
                NuclearReprocessor repo = Reprocessor;
                if (repo == null) return "点击切换浓缩铀出料处理模式。";
                switch (repo.SelectedEjectMode)
                {
                    case NuclearReprocessor.EjectMode.AutoDrop:
                        return "【当前模式：自动弹出地面】\n产物浓缩铀满 20kg 后自动掉落至地面，可被杂物聚合箱自动秒收纳进虚拟资源池并供全图机械臂隔空取物。\n点击切换为【留存在机器内部】。";
                    case NuclearReprocessor.EjectMode.KeepInStorage:
                        return "【当前模式：留存在机器内部】\n浓缩铀保存在机器内部存储中，供贴身机械臂或复制人直接取出，不掉落到地面。\n点击切换为【自动弹出地面】。";
                    default:
                        return "点击切换浓缩铀出料处理模式。";
                }
            }
        }

        public int ButtonSideScreenSortOrder() => 21;

        public void OnSidescreenButtonPressed()
        {
            NuclearReprocessor repo = Reprocessor;
            if (repo == null) return;

            if (repo.SelectedEjectMode == NuclearReprocessor.EjectMode.AutoDrop)
            {
                repo.SelectedEjectMode = NuclearReprocessor.EjectMode.KeepInStorage;
            }
            else
            {
                repo.SelectedEjectMode = NuclearReprocessor.EjectMode.AutoDrop;
                repo.CheckDropEnrichedUranium();
            }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }

        public bool SidescreenButtonInteractable() => true;

        public bool SidescreenEnabled() => true;
    }

    /// <summary>
    /// 核废料再处理机与生态舱流体进料保护补丁：
    /// 1. 拦截 ConduitConsumer.Consume：核废料处理机进料口若流经非核废料液体，直接放行不予抽取，
    ///    从根源上杜绝触发原版 BAD_INPUT_ELEMENT（元素错误）损坏与机内储料污染；
    /// 2. 拦截 BuildingHP.OnDoBuildingDamage：彻底阻断核废料处理机与生态舱因错误元素（BAD_INPUT_ELEMENT）而承受建筑损坏。
    /// </summary>
    [HarmonyPatch(typeof(ConduitConsumer), "Consume")]
    public static class NuclearReprocessor_ConduitConsumer_Consume_Patch
    {
        private static readonly AccessTools.FieldRef<ConduitConsumer, int> UtilityCellRef =
            AccessTools.FieldRefAccess<ConduitConsumer, int>("utilityCell");

        public static bool Prefix(ConduitConsumer __instance)
        {
            if (__instance == null) return true;
            NuclearReprocessor reprocessor = __instance.GetComponent<NuclearReprocessor>();
            if (reprocessor != null)
            {
                ConduitFlow flow = Conduit.GetFlowManager(__instance.conduitType);
                if (flow != null)
                {
                    int cell = UtilityCellRef != null ? UtilityCellRef(__instance) : (__instance.GetComponent<Building>()?.GetUtilityInputCell() ?? -1);
                    if (cell >= 0)
                    {
                        ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                        // 管道入口处流体若存在且不是液态核废料，不予抽取，杜绝 BAD_INPUT_ELEMENT 损坏，让其他液体直接顺管流走
                        if (contents.mass > 0f && contents.element != SimHashes.NuclearWaste)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(BuildingHP), "OnDoBuildingDamage")]
    public static class NuclearReprocessor_BuildingHP_Damage_Patch
    {
        public static bool Prefix(BuildingHP __instance, object data)
        {
            if (__instance != null)
            {
                // 核废料再处理机彻底免疫酸液/核废料腐蚀损坏(CORROSIVE_ELEMENT)、进料错误(BAD_INPUT_ELEMENT)及其他所有机内介质伤害
                if (__instance.GetComponent<NuclearReprocessor>() != null)
                {
                    return false;
                }

                if (data is BuildingHP.DamageSourceInfo info && info.source == STRINGS.BUILDINGS.DAMAGESOURCES.BAD_INPUT_ELEMENT)
                {
                    if (__instance.GetComponent<OmniPocketHabitat>() != null)
                    {
                        return false; // 阻断生态舱元素错误损坏
                    }
                }
            }
            return true;
        }
    }
}
