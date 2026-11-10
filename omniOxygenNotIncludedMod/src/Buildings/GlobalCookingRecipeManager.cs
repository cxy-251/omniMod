using System;
using System.Collections.Generic;
using KSerialization;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 全局烹饪菜单模板与同类机台同步管理器：
    /// 1. 挂载于 SaveGame，随殖民地存档自动序列化持久化保存；
    /// 2. 按建筑 PrefabID（如 "CookingStation", "GourmetCookingStation"）独立维护菜单模板；
    /// 3. 当玩家在任意一台同类烹饪台上修改菜品配置时，自动存入全局模板，并瞬间同步给全宇宙所有开启全局同步的同类烹饪台；
    /// 4. 新建造的烹饪台自动继承当前的全局菜单模板，杜绝“新盖一台就擅自制作禁做菜品”。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class GlobalCookingRecipeManager : KMonoBehaviour, ISaveLoadable
    {
        public static GlobalCookingRecipeManager Instance { get; private set; }

        public static readonly HashSet<SmartCookingStation> AllStations = new HashSet<SmartCookingStation>();

        [Serialize]
        private Dictionary<string, Dictionary<string, int>> _templates = new Dictionary<string, Dictionary<string, int>>();

        private static bool _isSyncing = false;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;
        }

        protected override void OnCleanUp()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            base.OnCleanUp();
        }

        /// <summary>
        /// 是否已存在该建筑类型的全局菜单模板
        /// </summary>
        public bool HasTemplate(string prefabId)
        {
            return _templates != null && _templates.ContainsKey(prefabId) && _templates[prefabId].Count > 0;
        }

        /// <summary>
        /// 获取指定机台类型的全局菜单模板
        /// </summary>
        public Dictionary<string, int> GetTemplate(string prefabId)
        {
            if (_templates != null && _templates.TryGetValue(prefabId, out var dict))
            {
                return dict;
            }
            return null;
        }

        /// <summary>
        /// 更新单个菜品的排队配置并存入全局模板
        /// </summary>
        public void SetRecipeCount(string prefabId, string recipeId, int count)
        {
            if (string.IsNullOrEmpty(prefabId) || string.IsNullOrEmpty(recipeId)) return;
            if (_templates == null) _templates = new Dictionary<string, Dictionary<string, int>>();
            if (!_templates.TryGetValue(prefabId, out var dict))
            {
                dict = new Dictionary<string, int>();
                _templates[prefabId] = dict;
            }
            dict[recipeId] = count;
        }

        /// <summary>
        /// 从指定烹饪台完整提取并保存为全局模板
        /// </summary>
        public void SaveTemplateFrom(ComplexFabricator fab)
        {
            if (fab == null) return;
            string prefabId = fab.PrefabID().Name;
            if (_templates == null) _templates = new Dictionary<string, Dictionary<string, int>>();
            var dict = new Dictionary<string, int>();
            ComplexRecipe[] recipes = fab.GetRecipes();
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Length; i++)
                {
                    if (recipes[i] != null)
                    {
                        dict[recipes[i].id] = fab.GetRecipeQueueCount(recipes[i]);
                    }
                }
            }
            _templates[prefabId] = dict;
        }

        /// <summary>
        /// 将全局模板完整应用到指定烹饪机台
        /// </summary>
        public void ApplyTemplateTo(ComplexFabricator fab)
        {
            if (fab == null) return;
            string prefabId = fab.PrefabID().Name;
            var dict = GetTemplate(prefabId);
            if (dict == null || dict.Count == 0) return;

            ComplexRecipe[] recipes = fab.GetRecipes();
            if (recipes != null)
            {
                for (int i = 0; i < recipes.Length; i++)
                {
                    if (recipes[i] != null)
                    {
                        if (dict.TryGetValue(recipes[i].id, out int count))
                        {
                            fab.SetRecipeQueueCount(recipes[i], count);
                        }
                    }
                }
                fab.SetQueueDirty();
            }
        }

        /// <summary>
        /// 当玩家在 UI 中手动修改菜品排队设置时触发广播同步
        /// </summary>
        public static void OnUserRecipeChanged(ComplexFabricator fab, ComplexRecipe recipe, int newCount)
        {
            if (_isSyncing) return;
            if (fab == null || recipe == null) return;

            var syncCtrl = fab.GetComponent<SmartCookingGlobalSync>();
            if (syncCtrl != null && !syncCtrl.GlobalSyncEnabled)
            {
                // 该机台已开启单台独立模式，不广播也不更新全局模板
                return;
            }

            var smart = fab.GetComponent<SmartCookingStation>();
            if (smart == null || !smart.SmartModeEnabled)
            {
                return;
            }

            string prefabId = fab.PrefabID().Name;

            // 首次修改时若全局模板尚未初始化，先完整快照当前烹饪机台的所有排队数据
            if (Instance != null)
            {
                if (!Instance.HasTemplate(prefabId))
                {
                    Instance.SaveTemplateFrom(fab);
                }
                Instance.SetRecipeCount(prefabId, recipe.id, newCount);
            }

            try
            {
                _isSyncing = true;
                int syncedCount = 0;
                foreach (var station in AllStations)
                {
                    if (station == null || station.gameObject == fab.gameObject) continue;
                    var targetSync = station.GetComponent<SmartCookingGlobalSync>();
                    if (targetSync != null && !targetSync.GlobalSyncEnabled) continue;
                    if (!station.SmartModeEnabled) continue;

                    var otherFab = station.GetComponent<ComplexFabricator>();
                    if (otherFab == null || otherFab.PrefabID().Name != prefabId) continue;

                    otherFab.SetRecipeQueueCount(recipe, newCount);
                    otherFab.SetQueueDirty();
                    syncedCount++;
                }

                if (syncedCount > 0)
                {
                    string actionDesc = newCount == ComplexFabricator.QUEUE_INFINITE ? "【无限制作】" : (newCount == 0 ? "【停止制作】" : $"【排队 {newCount}】");
                    ModLog.Info($"[烹饪菜单同步] 已将【{fab.GetProperName()}】的菜品「{recipe.GetUIName(false)}」设为 {actionDesc}，自动同步至全殖民地 {syncedCount} 台同类烹饪机。");

                    try
                    {
                        if (PopFXManager.Instance != null)
                        {
                            PopFXManager.Instance.SpawnFX(
                                PopFXManager.Instance.sprite_Plus,
                                $"已自动同步 {syncedCount} 台同类烹饪机",
                                null,
                                fab.transform.position + Vector3.up * 1.5f,
                                2.5f);
                        }
                    }
                    catch {}
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[烹饪菜单同步] 同步异常: {ex.Message}");
            }
            finally
            {
                _isSyncing = false;
            }
        }
    }
}
