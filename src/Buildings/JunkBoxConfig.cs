using System.Collections.Generic;
using OmniMod.Collection;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 杂物箱（Junk Box）—— 阶段一。
    ///
    /// 缺氧里"一个建筑"由一个继承 <see cref="IBuildingConfig"/> 的配置类描述。
    /// Mod 加载器会自动发现本 DLL 里的所有 IBuildingConfig 并注册，不需要手动 new。
    /// 三个方法的职责：
    ///   CreateBuildingDef        —— 造这个建筑的静态数据：尺寸、动画、血量、造价、材质、熔点等。
    ///   ConfigureBuildingTemplate —— 往建筑的预制体上挂通用组件（这里挂"存储"相关组件）。
    ///   DoPostConfigureComplete   —— 只对"建成后"的实体做的收尾配置。
    ///
    /// 本阶段目标：一个能正常建造、容量近乎无限、内部物品防腐/防挥发/温度隔离的储物箱。
    /// 视觉上直接复用香草"储物柜(StorageLocker)"的动画。
    /// </summary>
    public class JunkBoxConfig : IBuildingConfig
    {
        /// <summary>建筑的预制体 ID，全局唯一。加前缀避免和其它 mod 撞名。</summary>
        public const string Id = "OmniMod_JunkBox";

        /// <summary>
        /// "近乎无限"的容量。用一个很大的有限值而不是 float.MaxValue，
        /// 因为 MaxValue 参与 UI 里的百分比/余量计算时容易算出 NaN 或 Infinity。
        /// 10 亿千克对任何正常存档都等于无限。
        /// </summary>
        private const float NearInfiniteCapacityKg = 1_000_000_000f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: Id,
                width: 1,
                height: 2,
                anim: "storagelocker_kanim",                       // 复用香草储物柜动画
                hitpoints: 30,
                construction_time: 10f,
                construction_mass: BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,      // { 400 } kg
                construction_materials: MATERIALS.RAW_MINERALS_OR_METALS,    // 生矿物或金属
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: BUILDINGS.DECOR.PENALTY.TIER1,
                noise: NOISE_POLLUTION.NONE);

            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            // 让建筑可被设置优先级（小人才会来搬东西 / 整理）
            Prioritizable.AddRef(go);

            Storage storage = go.AddOrGet<Storage>();
            storage.showInUI = true;               // 点击建筑时在侧栏显示内容物
            storage.allowItemRemoval = true;
            storage.showDescriptor = true;
            storage.storageFilters = STORAGEFILTERS.STORAGE_LOCKERS_STANDARD;   // 和普通储物柜一样，接受所有固体
            storage.storageFullMargin = STORAGE.STORAGE_LOCKER_FILLED_MARGIN;
            storage.fetchCategory = Storage.FetchCategory.GeneralStorage;
            storage.showCapacityStatusItem = true;
            storage.showCapacityAsMainStatus = true;
            storage.capacityKg = NearInfiniteCapacityKg;
            storage.allowSettingOnlyFetchMarkedItems = false;   // 隐藏"仅限清扫"开关（我们靠代码收集，该开关无意义）

            // 箱内物品修饰：只用默认的 { Hide }（和普通储物柜/冰箱一样，隐藏渲染）。
            // 不用 Seal —— GameTags.Sealed 会让便携电池的电量、食物卡路里等在状态栏显示为 0。
            // 散装元素在池里是纯数据、不会挥发，所以不需要 Seal；缓冲块很快被消耗，也不需要 Insulate。
            storage.SetDefaultStoredItemModifiers(new List<Storage.StoredItemModifier> { Storage.StoredItemModifier.Hide });

            // 侧栏里可勾选收集哪些类别。取消勾选时不把缓冲区碎块倒在地上
            // （由收集器负责把该类退回全局池），避免刷出大堆碎片。
            go.AddOrGet<TreeFilterable>().dropIncorrectOnFilterChange = false;
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = GameTags.StorageLocker;  // 支持"复制设置"
            go.AddOrGet<StorageLocker>();                                      // 储物柜行为（动画、音效、状态）
            go.AddOrGet<UserNameable>();                                       // 可自定义名字
            go.AddOrGetDef<RocketUsageRestriction.Def>();                      // 火箭内使用限制（香草储物柜也有）
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGetDef<StorageController.Def>();

            // 阶段二：挂上全星球收集器，Debris 模式（自动吸入本星球散落的非食用杂物）
            go.AddOrGet<GlobalItemCollector>().mode = GlobalItemCollector.CollectMode.Debris;
        }
    }
}
