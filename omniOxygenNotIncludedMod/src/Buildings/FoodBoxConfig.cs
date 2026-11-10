using System.Collections.Generic;
using HarmonyLib;
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
            storage.ignoreSourcePriority = true;   // 允许机械手无视优先级从食物箱中抓取食物
            storage.showDescriptor = true;
            var foodFilters = new List<Tag>(STORAGEFILTERS.FOOD);
            if (!foodFilters.Contains(GameTags.Dehydrated))
            {
                foodFilters.Add(GameTags.Dehydrated);
            }
            storage.storageFilters = foodFilters;
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
                Storage.StoredItemModifier.Insulate,   // 箱内食物不跟箱子/环境交换热量
            });

            TreeFilterable foodFilter = go.AddOrGet<TreeFilterable>();
            foodFilter.allResourceFilterLabelString = UI.UISIDESCREENS.TREEFILTERABLESIDESCREEN.ALLBUTTON_EDIBLES;
            foodFilter.dropIncorrectOnFilterChange = false;
            go.AddOrGet<CopyBuildingSettings>().copyGroupTag = GameTags.StorageLocker;
            go.AddOrGet<RationBox>();
            go.AddOrGet<UserNameable>();
            go.AddOrGet<FoodBoxSterilizer>();
            go.AddOrGetDef<RocketUsageRestriction.Def>();
        }

        public override void DoPostConfigureComplete(GameObject go)
        {
            go.AddOrGetDef<StorageController.Def>();
            go.AddOrGet<GlobalItemCollector>().mode = GlobalItemCollector.CollectMode.Edibles;
        }
    }

    /// <summary>
    /// 食物箱深度无菌灭菌器：
    /// 确保箱内存放的任何食物附带的病菌（包括食物中毒菌、粘液肺菌等）被瞬间归零，
    /// 食物出箱后达到医用级洁净，小人食用绝不会发生食物中毒。
    /// </summary>
    [SkipSaveFileSerialization]
    public class FoodBoxSterilizer : KMonoBehaviour, ISim1000ms
    {
        private Storage _storage;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _storage = GetComponent<Storage>();
            Subscribe((int)GameHashes.OnStorageChange, OnStorageChange);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_storage == null) _storage = GetComponent<Storage>();
            Sterilize();
        }

        private void OnStorageChange(object data)
        {
            Sterilize();
        }

        public void Sim1000ms(float dt)
        {
            Sterilize();
        }

        private void Sterilize()
        {
            if (_storage == null || _storage.items == null) return;
            for (int i = 0; i < _storage.items.Count; i++)
            {
                GameObject item = _storage.items[i];
                if (item == null) continue;
                PrimaryElement pe = item.GetComponent<PrimaryElement>();
                if (pe != null && pe.DiseaseCount > 0)
                {
                    pe.ModifyDiseaseCount(-pe.DiseaseCount, "OmniMod_FoodBoxSterilization");
                }
            }
        }
    }

    [HarmonyPatch(typeof(RationBox), "OnSpawn")]
    internal static class RationBox_OnSpawn_Sterilizer_Patch
    {
        private static void Postfix(RationBox __instance)
        {
            if (__instance != null && __instance.PrefabID() == FoodBoxConfig.Id)
            {
                __instance.gameObject.AddOrGet<FoodBoxSterilizer>();
            }
        }
    }
}
