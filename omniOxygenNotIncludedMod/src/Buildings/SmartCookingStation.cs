using System;
using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 智能烹饪控制器：
    /// 1. 建造后默认开启【智能全自动烹饪模式】，所有可用食谱自动设为无限循环；
    /// 2. 默认禁止变异种子（避免误将珍稀变异种子当成普通食材烹饪）；
    /// 3. 彻底根除“建筑缺少资源”报警：
    ///    - 原版 FabricatorIngredientStatusManager 会为缺少原料的排队配方挂载 MaterialsUnavailable（“建筑缺少资源”）全局通知报警；
    ///    - 本模组在智能模式下直接拦截该组件的报警下发，并彻底拔除残留警告；
    ///    - 烹饪机台保持全速运转与正常搬运调度，食材到达机台后由免人力系统全速自动烹饪；
    /// 4. 侧边栏按钮（ISidescreenButtonControl）：
    ///    - 状态极其明确，允许玩家随时在【已开启】与【已关闭】之间一键切换。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class SmartCookingStation : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl, ISim1000ms
    {
        [Serialize]
        public bool SmartModeEnabled = true;

        [Serialize]
        private bool _initializedDefaultRecipes = false;

        private static readonly EventSystem.IntraObjectHandler<SmartCookingStation> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<SmartCookingStation>((cmp, data) => cmp.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();

            GlobalCookingRecipeManager.AllStations.Add(this);

            ComplexFabricator fab = GetComponent<ComplexFabricator>();
            if (fab == null) return;

            string prefabId = fab.PrefabID().Name;

            // 首次生成：初始化配置（默认禁止变异物，优先继承已保存的全局菜单模板，否则全配方设为无限循环）
            if (!_initializedDefaultRecipes)
            {
                SmartModeEnabled = true;
                fab.ForbidMutantSeeds = true;

                if (GlobalCookingRecipeManager.Instance != null && GlobalCookingRecipeManager.Instance.HasTemplate(prefabId))
                {
                    GlobalCookingRecipeManager.Instance.ApplyTemplateTo(fab);
                    ModLog.Info($"[烹饪菜单同步] 新建【{fab.GetProperName()}】已自动继承全殖民地菜单模板。");
                }
                else
                {
                    ComplexRecipe[] recipes = fab.GetRecipes();
                    if (recipes != null)
                    {
                        for (int i = 0; i < recipes.Length; i++)
                        {
                            if (recipes[i] != null)
                            {
                                fab.SetRecipeQueueCount(recipes[i], ComplexFabricator.QUEUE_INFINITE);
                            }
                        }
                    }
                    GlobalCookingRecipeManager.Instance?.SaveTemplateFrom(fab);
                }
                _initializedDefaultRecipes = true;
            }

            if (SmartModeEnabled)
            {
                fab.ForbidMutantSeeds = true;
                RemoveAllMaterialsUnavailable(GetComponent<KSelectable>());
                fab.SetQueueDirty();
            }
        }

        protected override void OnCleanUp()
        {
            GlobalCookingRecipeManager.AllStations.Remove(this);
            base.OnCleanUp();
        }

        private void OnCopySettings(object data)
        {
            GameObject source = (GameObject)data;
            if (source == null) return;
            SmartCookingStation other = source.GetComponent<SmartCookingStation>();
            if (other != null)
            {
                SmartModeEnabled = other.SmartModeEnabled;
                ComplexFabricator fab = GetComponent<ComplexFabricator>();
                if (fab != null)
                {
                    if (SmartModeEnabled)
                    {
                        fab.ForbidMutantSeeds = true;
                        RemoveAllMaterialsUnavailable(GetComponent<KSelectable>());
                    }
                    fab.SetQueueDirty();
                }
            }
        }

        public void Sim1000ms(float dt)
        {
            if (SmartModeEnabled)
            {
                RemoveAllMaterialsUnavailable(GetComponent<KSelectable>());
            }
            SanitizeStationAndIngredients();
        }

        private void SanitizeStationAndIngredients()
        {
            try
            {
                var pe = GetComponent<PrimaryElement>();
                if (pe != null && pe.DiseaseCount > 0)
                {
                    pe.ModifyDiseaseCount(-pe.DiseaseCount, "OmniMod_CookCleanse");
                }
                ComplexFabricator fab = GetComponent<ComplexFabricator>();
                if (fab != null)
                {
                    SanitizeStorage(fab.inStorage);
                    SanitizeStorage(fab.buildStorage);
                    SanitizeStorage(fab.outStorage);
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[智能烹饪消杀] 执行异常: {ex.Message}");
            }
        }

        private static void SanitizeStorage(Storage storage)
        {
            if (storage == null || storage.items == null) return;
            int count = storage.items.Count;
            for (int i = 0; i < count; i++)
            {
                if (i >= storage.items.Count) break;
                GameObject item = storage.items[i];
                if (item != null)
                {
                    try
                    {
                        var itemPe = item.GetComponent<PrimaryElement>();
                        if (itemPe != null && itemPe.DiseaseCount > 0)
                        {
                            itemPe.ModifyDiseaseCount(-itemPe.DiseaseCount, "OmniMod_CookCleanse");
                        }
                    }
                    catch {}
                }
            }
        }

        /// <summary>
        /// 从建筑的 KSelectable 彻底移除所有 MaterialsUnavailable（"建筑缺少资源"）报警项
        /// </summary>
        public static void RemoveAllMaterialsUnavailable(KSelectable selectable)
        {
            if (selectable == null) return;
            StatusItemGroup group = selectable.GetStatusItemGroup();
            if (group == null) return;

            List<Guid> guidsToRemove = null;
            foreach (StatusItemGroup.Entry entry in group)
            {
                if (entry.item != null && entry.item.Id == Db.Get().BuildingStatusItems.MaterialsUnavailable.Id)
                {
                    if (guidsToRemove == null) guidsToRemove = new List<Guid>();
                    guidsToRemove.Add(entry.id);
                }
            }

            if (guidsToRemove != null)
            {
                for (int i = 0; i < guidsToRemove.Count; i++)
                {
                    selectable.RemoveStatusItem(guidsToRemove[i], true);
                }
            }
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "智能全自动烹饪";

        public string SidescreenButtonText => SmartModeEnabled
            ? "当前状态：【已开启】(点击关闭)"
            : "当前状态：【已关闭】(点击开启)";

        public string SidescreenButtonTooltip => SmartModeEnabled
            ? "【智能全自动烹饪：已开启】\n- 所有可用菜品自动无限循环制作；\n- 免人工自动加工，食材入库立刻烹饪；\n- 绝不产生“建筑缺少资源”报警；\n- 默认禁止使用变异种子。\n\n点击可切换回原版手动排队模式。"
            : "【智能全自动烹饪：已关闭】\n- 恢复游戏原版机制，需手动配置菜品排队数量；\n- 缺料时正常显示“建筑缺少资源”黄色报警。\n\n点击可重新开启智能全自动烹饪。";

        public void OnSidescreenButtonPressed()
        {
            SmartModeEnabled = !SmartModeEnabled;
            ComplexFabricator fab = GetComponent<ComplexFabricator>();
            if (fab != null)
            {
                if (SmartModeEnabled)
                {
                    fab.ForbidMutantSeeds = true;
                    string prefabId = fab.PrefabID().Name;
                    if (GlobalCookingRecipeManager.Instance != null && GlobalCookingRecipeManager.Instance.HasTemplate(prefabId))
                    {
                        GlobalCookingRecipeManager.Instance.ApplyTemplateTo(fab);
                    }
                    else
                    {
                        ComplexRecipe[] recipes = fab.GetRecipes();
                        if (recipes != null)
                        {
                            for (int i = 0; i < recipes.Length; i++)
                            {
                                if (recipes[i] != null)
                                {
                                    fab.SetRecipeQueueCount(recipes[i], ComplexFabricator.QUEUE_INFINITE);
                                }
                            }
                        }
                        GlobalCookingRecipeManager.Instance?.SaveTemplateFrom(fab);
                    }
                    RemoveAllMaterialsUnavailable(GetComponent<KSelectable>());
                }
                fab.SetQueueDirty();
            }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }
        public bool SidescreenEnabled() => true;
        public bool SidescreenButtonInteractable() => true;
        public int HorizontalGroupID() => -1;
        public int ButtonSideScreenSortOrder() => 20;
    }

    /// <summary>
    /// 烹饪机台全局同步控制组件（提供侧边栏开关与单机独立切换）：
    /// 允许玩家选择将当前机台纳入全殖民地统一菜单网络，或设为独立烹饪台。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class SmartCookingGlobalSync : KMonoBehaviour, ISaveLoadable, ISidescreenButtonControl
    {
        [Serialize]
        public bool GlobalSyncEnabled = true;

        private static readonly EventSystem.IntraObjectHandler<SmartCookingGlobalSync> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<SmartCookingGlobalSync>((cmp, data) => cmp.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        private void OnCopySettings(object data)
        {
            GameObject source = (GameObject)data;
            if (source == null) return;
            SmartCookingGlobalSync other = source.GetComponent<SmartCookingGlobalSync>();
            if (other != null)
            {
                GlobalSyncEnabled = other.GlobalSyncEnabled;
            }
        }

        // ================= ISidescreenButtonControl =================

        public string SidescreenTitle => "全殖民地菜单同步";

        public string SidescreenButtonText => GlobalSyncEnabled
            ? "菜单全局同步：【已开启】(点击单台独立)"
            : "菜单全局同步：【已关闭】(点击加入全局同步)";

        public string SidescreenButtonTooltip => GlobalSyncEnabled
            ? "【全殖民地菜单同步：已开启】\n- 本机台已加入全殖民地烹饪菜单同步网络；\n- 修改任意菜品的排队或停做，会自动应用到全宇宙所有同类烹饪台；\n- 新建的同类烹饪台也将自动继承此菜单设置。\n\n点击可切换为【单机独立模式】。"
            : "【全殖民地菜单同步：已关闭】\n- 当前机台为独立排队菜单，不受其他机台修改影响，也不会影响全局设置。\n\n点击可重新加入全局同步网络并拉取最新全局菜单。";

        public void OnSidescreenButtonPressed()
        {
            GlobalSyncEnabled = !GlobalSyncEnabled;
            if (GlobalSyncEnabled)
            {
                var fab = GetComponent<ComplexFabricator>();
                if (fab != null && GlobalCookingRecipeManager.Instance != null)
                {
                    GlobalCookingRecipeManager.Instance.ApplyTemplateTo(fab);
                }
            }
        }

        public void SetButtonTextOverride(ButtonMenuTextOverride textOverride) { }
        public bool SidescreenEnabled() => true;
        public bool SidescreenButtonInteractable() => true;
        public int HorizontalGroupID() => -1;
        public int ButtonSideScreenSortOrder() => 21;
    }

    /// <summary>
    /// 烹饪机台类型检查辅助工具
    /// </summary>
    internal static class SmartCookingRegistry
    {
        public static bool IsCookingStation(ComplexFabricator fab)
        {
            if (fab == null) return false;
            string id = fab.PrefabID().Name;
            if (id == "CookingStation" ||
                id == "GourmetCookingStation" ||
                id == "MicrobeMusher" ||
                id == "FoodDehydrator")
            {
                return true;
            }

            ComplexRecipe[] recipes = fab.GetRecipes();
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Length; i++)
                {
                    var results = recipes[i]?.results;
                    if (results != null)
                    {
                        for (int j = 0; j < results.Length; j++)
                        {
                            if (EdiblesManager.GetFoodInfo(results[j].material.Name) != null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }
    }

    // ================= Harmony Patches =================

    /// <summary>
    /// 核心拦截：原版 FabricatorIngredientStatusManager 每秒检查所有已排队配方并为缺料配方挂载 MaterialsUnavailable（"建筑缺少资源"）全局报警。
    /// 在智能全自动模式下，拦截该方法并清除所有报警项，彻底杜绝报警噪音并减少 CPU/GC 消耗；
    /// 切换为原版手动模式时自动放行，恢复原版缺料报警逻辑。
    /// </summary>
    [HarmonyPatch(typeof(FabricatorIngredientStatusManager), "RefreshStatusItems")]
    internal static class FabricatorIngredientStatusManager_RefreshStatusItems_SmartCook_Patch
    {
        private static bool Prefix(FabricatorIngredientStatusManager __instance)
        {
            if (__instance == null) return true;
            var smart = __instance.GetComponent<SmartCookingStation>();
            if (smart != null && smart.SmartModeEnabled)
            {
                var selectable = __instance.GetComponent<KSelectable>();
                if (selectable != null)
                {
                    SmartCookingStation.RemoveAllMaterialsUnavailable(selectable);
                }
                return false; // 智能全自动模式下彻底跳过缺料报警计算
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), "OnSpawn")]
    [HarmonyPriority(Priority.High)]
    internal static class ComplexFabricator_OnSpawn_SmartCook_Patch
    {
        private static void Postfix(ComplexFabricator __instance)
        {
            if (__instance != null && SmartCookingRegistry.IsCookingStation(__instance))
            {
                __instance.gameObject.AddOrGet<SmartCookingStation>();
                __instance.gameObject.AddOrGet<SmartCookingGlobalSync>();
            }
        }
    }

    [HarmonyPatch(typeof(CookingStationConfig), nameof(CookingStationConfig.DoPostConfigureComplete))]
    internal static class CookingStationConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            go?.AddOrGet<SmartCookingStation>();
            go?.AddOrGet<SmartCookingGlobalSync>();
        }
    }

    [HarmonyPatch(typeof(GourmetCookingStationConfig), nameof(GourmetCookingStationConfig.DoPostConfigureComplete))]
    internal static class GourmetCookingStationConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            go?.AddOrGet<SmartCookingStation>();
            go?.AddOrGet<SmartCookingGlobalSync>();
        }
    }

    [HarmonyPatch(typeof(MicrobeMusherConfig), nameof(MicrobeMusherConfig.DoPostConfigureComplete))]
    internal static class MicrobeMusherConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            go?.AddOrGet<SmartCookingStation>();
            go?.AddOrGet<SmartCookingGlobalSync>();
        }
    }

    [HarmonyPatch(typeof(FoodDehydratorConfig), nameof(FoodDehydratorConfig.DoPostConfigureComplete))]
    internal static class FoodDehydratorConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            go?.AddOrGet<SmartCookingStation>();
            go?.AddOrGet<SmartCookingGlobalSync>();
        }
    }

    /// <summary>
    /// 菜单全局同步拦截：当玩家在 UI 中设置某个菜品的排队数量时，广播给所有开启全局同步的同类烹饪台
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "SetRecipeQueueCount", new[] { typeof(ComplexRecipe), typeof(int) })]
    internal static class ComplexFabricator_SetRecipeQueueCount_Sync_Patch
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, int count)
        {
            GlobalCookingRecipeManager.OnUserRecipeChanged(__instance, recipe, count);
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), "IncrementRecipeQueueCount", new[] { typeof(ComplexRecipe) })]
    internal static class ComplexFabricator_IncrementRecipeQueueCount_Sync_Patch
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe)
        {
            if (__instance != null && recipe != null)
            {
                int count = __instance.GetRecipeQueueCount(recipe);
                GlobalCookingRecipeManager.OnUserRecipeChanged(__instance, recipe, count);
            }
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), "DecrementRecipeQueueCount", new[] { typeof(ComplexRecipe), typeof(bool) })]
    internal static class ComplexFabricator_DecrementRecipeQueueCount_Sync_Patch
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, bool respectInfinite)
        {
            // respectInfinite 为 true 时表示小人制作完成扣减，为 false 时表示玩家在界面中点击减号按钮
            if (!respectInfinite && __instance != null && recipe != null)
            {
                int count = __instance.GetRecipeQueueCount(recipe);
                GlobalCookingRecipeManager.OnUserRecipeChanged(__instance, recipe, count);
            }
        }
    }

    /// <summary>
    /// 烹饪产物无菌化：烹饪出的食物在生成的一瞬间彻底消杀所有病菌，杜绝因原料带菌导致厨房地带污染
    /// </summary>
    [HarmonyPatch(typeof(ComplexFabricator), "SpawnOrderProduct")]
    internal static class ComplexFabricator_SpawnOrderProduct_Sterilize_Patch
    {
        private static void Postfix(ComplexFabricator __instance, List<GameObject> __result)
        {
            if (__instance == null || __result == null) return;
            if (SmartCookingRegistry.IsCookingStation(__instance))
            {
                for (int i = 0; i < __result.Count; i++)
                {
                    GameObject prod = __result[i];
                    if (prod != null)
                    {
                        try
                        {
                            var pe = prod.GetComponent<PrimaryElement>();
                            if (pe != null && pe.DiseaseCount > 0)
                            {
                                pe.ModifyDiseaseCount(-pe.DiseaseCount, "OmniMod_CookSterilize");
                            }
                        }
                        catch {}
                    }
                }
            }
        }
    }
}
