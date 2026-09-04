using System.Collections.Generic;
using OmniMod.Collection;
using STRINGS;
using TUNING;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 食物箱（Food Box）—— 阶段二。
    ///
    /// 结构与杂物箱几乎一样，区别：
    /// - 只收食物（storageFilters = FOOD），并挂 RationBox 组件（香草"口粮箱"的行为组件，
    ///   无需供电）。
    /// - 靠 StandardSealedStorage 里的 Preserve 修饰实现"绝对防腐"：箱内食物打上 Preserved 标签，
    ///   腐烂状态机直接进入 Preserved 状态、停止计时。
    /// - 收集器工作在 Edibles 模式，把本星球散落的可食用物自动吸进来。
    /// 视觉复用香草"口粮箱(RationBox)"的 2x2 动画。
    /// </summary>
    public class FoodBoxConfig : IBuildingConfig
    {
        public const string Id = "OmniMod_FoodBox";

        private const float NearInfiniteCapacityKg = 1_000_000_000f;

        public override BuildingDef CreateBuildingDef()
        {
            BuildingDef def = BuildingTemplates.CreateBuildingDef(
                id: Id,
                width: 2,
                height: 2,
                anim: "rationbox_kanim",
                hitpoints: 10,
                construction_time: 10f,
                construction_mass: TUNING.BUILDINGS.CONSTRUCTION_MASS_KG.TIER4,
                construction_materials: MATERIALS.RAW_MINERALS,
                melting_point: 1600f,
                build_location_rule: BuildLocationRule.OnFloor,
                decor: TUNING.BUILDINGS.DECOR.BONUS.TIER0,
                noise: NOISE_POLLUTION.NONE);

            def.Floodable = false;
            def.Overheatable = false;
            def.AudioCategory = "Metal";
            return def;
        }

        public override void ConfigureBuildingTemplate(GameObject go, Tag prefab_tag)
        {
            Prioritizable.AddRef(go);

            Storage storage = go.AddOrGet<Storage>();
            storage.showInUI = true;
            storage.allowItemRemoval = true;
            storage.showDescriptor = true;
            storage.storageFilters = STORAGEFILTERS.FOOD;
            storage.storageFullMargin = STORAGE.STORAGE_LOCKER_FILLED_MARGIN;
            storage.fetchCategory = Storage.FetchCategory.GeneralStorage;
            storage.showCapacityStatusItem = true;
            storage.showCapacityAsMainStatus = true;
            storage.capacityKg = NearInfiniteCapacityKg;
            storage.allowSettingOnlyFetchMarkedItems = false;   // 隐藏"仅限清扫"开关
            // { Hide, Preserve }：隐藏渲染 + 打 Preserved 标签让食物停止腐烂（绝对防腐）。
            // 不用 Seal —— GameTags.Sealed 会让状态栏里的卡路里显示为 0。
            storage.SetDefaultStoredItemModifiers(new List<Storage.StoredItemModifier>
            {
                Storage.StoredItemModifier.Hide,
                Storage.StoredItemModifier.Preserve,
            });

            TreeFilterable foodFilter = go.AddOrGet<TreeFilterable>();
            foodFilter.allResourceFilterLabelString = UI.UISIDESCREENS.TREEFILTERABLESIDESCREEN.ALLBUTTON_EDIBLES;
            foodFilter.dropIncorrectOnFilterChange = false;
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = GameTags.StorageLocker;
            go.AddOrGet<RationBox>();
            go.AddOrGet<UserNameable>();
            go.AddOrGetDef<RocketUsageRestriction.Def>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGetDef<StorageController.Def>();
            go.AddOrGet<GlobalItemCollector>().mode = GlobalItemCollector.CollectMode.Edibles;
        }
    }
}
