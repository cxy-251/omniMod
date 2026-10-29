using System.Collections.Generic;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间生态巨舱】建筑配置：
    /// - 外部占地 3x3，防水防淹（Floodable = false），可建于基地任意地面或水底。
    /// - 底部正中自带白色电源接口，与舱内的原版【电源插座】(RocketInteriorPowerPlug) 跨维直通，共享同一电网。
    /// - 挂载 AlertStateManager.Def、Notifier 与 SaveLoadRoot 可选组件声明，完美满足 WorldContainer 底层契约。
    /// </summary>
    public class OmniPocketHabitatConfig : IBuildingConfig
    {
        public const string ID = "OmniPocketHabitat";
        public static readonly CellOffset PowerPlugOffset = new CellOffset(0, 0);

        public override string[] GetRequiredDlcIds()
        {
            return DlcManager.EXPANSION1;
        }

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: ID,
                width: 5,
                height: 4,
                anim: "rocket_habitat_medium_module_kanim",
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
            def.DefaultAnimState = "grounded";

            def.Floodable = false;
            def.Entombable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            def.AudioSize = "large";

            // 外部电网直连插口（位于建筑底部中心 0,0），与舱内【电源插座】共享虚拟电路网络
            def.ViewMode = OverlayModes.Power.ID;
            def.PowerInputOffset = PowerPlugOffset;
            def.PowerOutputOffset = PowerPlugOffset;
            def.RequiresPowerOutput = true;
            def.UseWhitePowerOutputConnectorColour = true;

            // 外部液体管网接口：
            // - 输入口（绿口）位于 (1, 0)，支持将外部液体（如咸乳）通入生态舱内部
            // - 输出口（白口）位于 (3, 0)，支持将舱内液体排回外部基地管网
            def.InputConduitType = ConduitType.Liquid;
            def.UtilityInputOffset = new CellOffset(1, 0);
            def.OutputConduitType = ConduitType.Liquid;
            def.UtilityOutputOffset = new CellOffset(3, 0);

            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.WireIDs, ID);
            GeneratedBuildings.RegisterWithOverlay(OverlayScreen.LiquidVentIDs, ID);

            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            KPrefabID kpid = go.GetComponent<KPrefabID>();
            kpid.AddTag(GameTags.Bunker);
            kpid.AddTag(GameTags.NotRoomAssignable);

            // 声明动态挂载的内部世界组件，确保存档/读档序列化完整无损
            SaveLoadRoot saveLoadRoot = go.AddOrGet<SaveLoadRoot>();
            saveLoadRoot.DeclareOptionalComponent<WorldInventory>();
            saveLoadRoot.DeclareOptionalComponent<WorldContainer>();

            // WorldContainer 强制要求同 GameObject 上存在 AlertStateManager.Instance 状态机与 Notifier
            go.AddOrGet<Notifier>();
            go.AddOrGetDef<AlertStateManager.Def>();

            // 为右侧星球列表（WorldSelector）提供图标与名称实体（不显示在星图上，也不修改建筑坐标）
            go.AddOrGet<OmniPocketClusterEntity>();

            Prioritizable.AddRef(go);
            go.AddOrGet<Operational>();
            go.AddOrGet<LoopingSounds>();

            // 液体储罐与管线消费者/分配器（支持舱内外液体互通，如输送咸乳）
            Storage liquidInStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: false);
            liquidInStorage.capacityKg = 1000f;
            liquidInStorage.showInUI = true;
            liquidInStorage.allowItemRemoval = false;
            liquidInStorage.storageFilters = STORAGEFILTERS.LIQUIDS;
            liquidInStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            Storage liquidOutStorage = BuildingTemplates.CreateDefaultStorage(go, forceCreate: true);
            liquidOutStorage.capacityKg = 1000f;
            liquidOutStorage.showInUI = true;
            liquidOutStorage.allowItemRemoval = false;
            liquidOutStorage.storageFilters = STORAGEFILTERS.LIQUIDS;
            liquidOutStorage.SetDefaultStoredItemModifiers(Storage.StandardSealedStorage);

            ConduitConsumer consumer = go.AddOrGet<ConduitConsumer>();
            consumer.conduitType = ConduitType.Liquid;
            consumer.consumptionRate = 10f;
            consumer.capacityKG = 1000f;
            consumer.wrongElementResult = ConduitConsumer.WrongElementResult.Dump;
            consumer.forceAlwaysSatisfied = true;
            consumer.storage = liquidInStorage;

            ConduitDispenser dispenser = go.AddOrGet<ConduitDispenser>();
            dispenser.conduitType = ConduitType.Liquid;
            dispenser.alwaysDispense = true;
            dispenser.storage = liquidOutStorage;

            RequireInputs requireInputs = go.AddOrGet<RequireInputs>();
            requireInputs.SetRequirements(power: false, conduit: false);

            AssignmentGroupController agc = go.AddOrGet<AssignmentGroupController>();
            agc.generateGroupOnStart = true;

            go.AddOrGet<NavTeleporter>();
            go.AddOrGet<AccessControl>();
            go.AddOrGet<UserNameable>();

            Ownable ownable = go.AddOrGet<Ownable>();
            ownable.slotID = Db.Get().AssignableSlots.HabitatModule.Id;
            ownable.canBePublic = false;
            ownable.tintWhenUnassigned = false;

            go.AddOrGet<OmniPocketHabitat>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            WireUtilitySemiVirtualNetworkLink wireLink = go.AddOrGet<WireUtilitySemiVirtualNetworkLink>();
            wireLink.link1 = PowerPlugOffset;
            wireLink.visualizeOnly = false;

            KBatchedAnimController kbac = go.GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.sceneLayer = Grid.SceneLayer.BuildingBack;
                kbac.fgLayer = Grid.SceneLayer.BuildingFront;
            }
        }
    }

    /// <summary>
    /// 供右侧星球切换列表（WorldSelector）读取名称与图标的轻量级星图实体：
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
                            animFile = Assets.GetAnim("rocket_habitat_medium_module_kanim"),
                            initialAnim = "grounded"
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
