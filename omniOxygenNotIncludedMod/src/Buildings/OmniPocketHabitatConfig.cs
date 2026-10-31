using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间生态巨舱】建筑配置：
    /// - 外部占地 1x3，防水防淹（Floodable = false），可建于基地地面或水底。
    /// - 底部正中自带白色电源接口，与舱内的原版【电源插座】(RocketInteriorPowerPlug) 跨维直通，共享同一电网。
    /// - 挂载 AlertStateManager.Def、Notifier 与 SaveLoadRoot 可选组件声明，完美满足 WorldContainer 底层契约。
    /// </summary>
    public class OmniPocketHabitatConfig : IBuildingConfig
    {
        public const string ID = "OmniPocketHabitat";
        public const string ExteriorAnim = "rocket_hatch_door_kanim";
        public const string ExteriorAnimState = "closed";
        public static readonly CellOffset PowerPlugOffset = new CellOffset(0, 0);
        public static readonly CellOffset LiquidInputOffset = new CellOffset(0, 0);
        public static readonly CellOffset LiquidOutputOffset = new CellOffset(0, 2);
        public static readonly ConduitPortInfo GasInputPort = new ConduitPortInfo(ConduitType.Gas, new CellOffset(0, 0));
        public static readonly ConduitPortInfo GasOutputPort = new ConduitPortInfo(ConduitType.Gas, new CellOffset(0, 2));

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 1,
                height: 3,
                anim: ExteriorAnim,
                hitpoints: 1000,
                construction_time: 45f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER5,
                construction_materials: MATERIALS.REFINED_METALS,
                melting_point: 2400f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.BONUS.TIER2,
                noise: NOISE_POLLUTION.NONE);

            def.SceneLayer = Grid.SceneLayer.BuildingBack;
            def.ForegroundLayer = Grid.SceneLayer.BuildingFront;
            // 舱体只占普通建筑层，电线、气液管道与运输轨道仍使用各自的独立占格层。
            def.ObjectLayer = ObjectLayer.Building;
            def.DefaultAnimState = ExteriorAnimState;

            def.Floodable = false;
            def.Entombable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "small";

            // 外部电网直连插口（位于建筑底部中心 0,0），与舱内【电源插座】共享虚拟电路网络
            def.ViewMode = OverlayModes.Power.ID;
            def.PowerInputOffset = PowerPlugOffset;
            def.PowerOutputOffset = PowerPlugOffset;
            def.RequiresPowerOutput = true;
            def.UseWhitePowerOutputConnectorColour = true;

            // 1x3 单列入口：底格为气液输入和电口，顶格为气液输出，各自使用独立线管层。
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = LiquidInputOffset;
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = LiquidOutputOffset;

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.WireIDs, ID);
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, ID);
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.GasVentIDs, ID);

            return def;
        }

        private void AttachPorts(GameObject go)
        {
            go.AddComponent<ConduitSecondaryInput>().portInfo = GasInputPort;
            go.AddComponent<ConduitSecondaryOutput>().portInfo = GasOutputPort;
        }

        // 原版舱门贴图宽 1、高 2，只拉高画面至 3 格；不缩放建筑 Transform、碰撞盒或内部世界坐标。
        internal static void ConfigureExteriorAnimation(GameObject go)
        {
            KBatchedAnimController anim = go.GetComponent<KBatchedAnimController>();
            if (anim == null) return;
            anim.animWidth = 1f;
            anim.animHeight = 1.5f;
        }

        public override void DoPostConfigurePreview(BuildingDef def, GameObject go)
        {
            base.DoPostConfigurePreview(def, go);
            go.AddOrGet<BuildingCellVisualizer>();
            AttachPorts(go);
            ConfigureExteriorAnimation(go);
        }

        public override void DoPostConfigureUnderConstruction(GameObject go)
        {
            base.DoPostConfigureUnderConstruction(go);
            go.AddOrGet<BuildingCellVisualizer>();
            AttachPorts(go);
            ConfigureExteriorAnimation(go);
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            KPrefabID kpid = go.GetComponent<KPrefabID>();
            kpid.AddTag(GameTags.Bunker);
            kpid.AddTag(GameTags.NotRoomAssignable);
            // 管网视图通过原版规则将舱体放在管道后面；动画前景另在运行时同步退层。
            kpid.AddTag(GameTags.OverlayBehindConduits);

            // 声明动态挂载的内部世界组件，确保存档/读档序列化完整无损
            SaveLoadRoot saveLoadRoot = go.AddOrGet<SaveLoadRoot>();
            saveLoadRoot.DeclareOptionalComponent<WorldInventory>();
            saveLoadRoot.DeclareOptionalComponent<WorldContainer>();

            // WorldContainer 强制要求同 GameObject 上存在 AlertStateManager.Instance 状态机与 Notifier
            go.AddOrGet<Notifier>();
            go.AddOrGetDef<AlertStateManager.Def>();

            // 保留内部世界所需的名称/图标实体；星图隐藏，星球列表由专用 UI 补丁隐藏。
            go.AddOrGet<OmniPocketClusterEntity>();

            Prioritizable.AddRef(go);
            go.AddOrGet<Operational>();
            go.AddOrGet<LoopingSounds>();

            // 1. 液体储罐与管线消费者/分配器（对齐原版火箭 10 kg 微容量缓冲，初始 0 直通零截留，舱内建有出水口时动态按需启用）
            Storage liquidInStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: false);
            liquidInStorage.capacityKg = 0f;
            liquidInStorage.showInUI = true;
            liquidInStorage.allowItemRemoval = false;
            liquidInStorage.storageFilters = STORAGEFILTERS.LIQUIDS;
            liquidInStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            Storage liquidOutStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: true);
            liquidOutStorage.capacityKg = 10f;
            liquidOutStorage.showInUI = true;
            liquidOutStorage.allowItemRemoval = false;
            liquidOutStorage.storageFilters = STORAGEFILTERS.LIQUIDS;
            liquidOutStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            ConduitConsumer liquidConsumer = go.AddOrGet<ConduitConsumer>();
            liquidConsumer.conduitType = ConduitType.Liquid;
            liquidConsumer.consumptionRate = 0f;
            liquidConsumer.capacityKG = 0f;
            liquidConsumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            liquidConsumer.forceAlwaysSatisfied = true;
            liquidConsumer.alwaysConsume = true; // 气液接口独立工作，不要求其他输出管全部接好
            liquidConsumer.storage = liquidInStorage;
            liquidConsumer.enabled = false; // 默认禁用，舱内有出水需求时才动态启用，实现 100% 直通零截留

            ConduitDispenser liquidDispenser = go.AddOrGet<ConduitDispenser>();
            liquidDispenser.conduitType = ConduitType.Liquid;
            liquidDispenser.alwaysDispense = true;
            liquidDispenser.storage = liquidOutStorage;

            // 2. 气体储罐与副端口消费者/分配器（对齐原版火箭 1 kg 微容量缓冲，初始 0 直通零截留，舱内建有出气口时动态按需启用）
            Storage gasInStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: true);
            gasInStorage.capacityKg = 0f;
            gasInStorage.showInUI = true;
            gasInStorage.allowItemRemoval = false;
            gasInStorage.storageFilters = STORAGEFILTERS.GASES;
            gasInStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            Storage gasOutStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: true);
            gasOutStorage.capacityKg = 1f;
            gasOutStorage.showInUI = true;
            gasOutStorage.allowItemRemoval = false;
            gasOutStorage.storageFilters = STORAGEFILTERS.GASES;
            gasOutStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            ConduitConsumer gasConsumer = go.AddComponent<ConduitConsumer>();
            gasConsumer.conduitType = ConduitType.Gas;
            gasConsumer.consumptionRate = 0f;
            gasConsumer.capacityKG = 0f;
            gasConsumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            gasConsumer.forceAlwaysSatisfied = true;
            gasConsumer.alwaysConsume = true;
            gasConsumer.storage = gasInStorage;
            gasConsumer.useSecondaryInput = true;
            gasConsumer.enabled = false; // 默认禁用，舱内有出气需求时才动态启用，实现 100% 直通零截留

            ConduitDispenser gasDispenser = go.AddComponent<ConduitDispenser>();
            gasDispenser.conduitType = ConduitType.Gas;
            gasDispenser.alwaysDispense = true;
            gasDispenser.storage = gasOutStorage;
            gasDispenser.useSecondaryOutput = true;

            RequireInputs requireInputs = go.AddOrGet<RequireInputs>();
            requireInputs.SetRequirements(power: false, conduit: false);

            AssignmentGroupController agc = go.AddOrGet<AssignmentGroupController>();
            agc.generateGroupOnStart = true;

            go.AddOrGet<NavTeleporter>().offset = new CellOffset(0, 0);
            go.AddOrGet<AccessControl>();
            go.AddOrGet<UserNameable>();

            Ownable ownable = go.AddOrGet<Ownable>();
            ownable.slotID = Db.Get().AssignableSlots.HabitatModule.Id;
            ownable.canBePublic = false;
            ownable.tintWhenUnassigned = false;

            go.AddOrGet<OmniPocketHabitat>();
            go.AddOrGet<BuildingCellVisualizer>();
            ConfigureExteriorAnimation(go);
            AttachPorts(go);
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            WireUtilitySemiVirtualNetworkLink wireLink = go.AddOrGet<WireUtilitySemiVirtualNetworkLink>();
            wireLink.link1 = PowerPlugOffset;
            wireLink.visualizeOnly = false;

            go.AddOrGet<BuildingCellVisualizer>();
            ConfigureExteriorAnimation(go);

            KBatchedAnimController kbac = go.GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.sceneLayer = Grid.SceneLayer.BuildingBack;
                kbac.fgLayer = Grid.SceneLayer.BuildingFront;
            }
        }
    }

    /// <summary>
    /// 供内部世界读取名称与图标的轻量级实体；不作为独立小行星显示：
    /// - isWorldEntity = true 防止 ClusterGridEntity.OnSpawn 篡改建筑的 transform.localPosition。
    /// - IsVisible = false 确保不会在星图（Starmap）上渲染任何多余图标。
    /// </summary>
    public class OmniPocketClusterEntity : ClusterGridEntity
    {
        private List<AnimConfig> cachedAnimConfigs;

        public override string Name
        {
            get
            {
                UserNameable nameable = GetComponent<UserNameable>();
                if (nameable != null && !string.IsNullOrEmpty(nameable.savedName))
                {
                    return nameable.savedName;
                }
                return ModStrings.T("折叠空间生态舱", "Pocket Habitat").Resolve();
            }
        }

        public override EntityLayer Layer => EntityLayer.None;
        public override bool IsVisible => false;
        public override ClusterRevealLevel IsVisibleInFOW => ClusterRevealLevel.Hidden;

        public override List<AnimConfig> AnimConfigs
        {
            get
            {
                if (cachedAnimConfigs == null)
                {
                    cachedAnimConfigs = new List<AnimConfig>
                    {
                        new AnimConfig
                        {
                            animFile = Assets.GetAnim(OmniPocketHabitatConfig.ExteriorAnim),
                            initialAnim = OmniPocketHabitatConfig.ExteriorAnimState
                        }
                    };
                }
                return cachedAnimConfigs;
            }
        }

        protected override void OnPrefabInit()
        {
            // 关键：标记为 isWorldEntity，阻止基类 OnSpawn 将建筑 localPosition 重置为 (-1, 0, 0)
            isWorldEntity = true;
            base.OnPrefabInit();
        }

        public override Sprite GetUISprite()
        {
            var tuple = Def.GetUISprite(gameObject);
            if (tuple != null && tuple.first != null)
            {
                return tuple.first;
            }
            return Assets.GetSprite("unknown_far");
        }
    }
}
