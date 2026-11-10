using System;
using System.Collections.Generic;
using HarmonyLib;
using KSerialization;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 打蛋机物种蛋防绝种安全控制器：
    /// 允许玩家在打蛋机侧边栏设置物种蛋的保留阈值（0 ~ 100，默认 10）。
    /// 当该物种的全殖民地存蛋总量 ≤ 保留阈值时，自动停止打蛋并取消取蛋任务，彻底杜绝物种意外绝种。
    /// </summary>
    [SerializationConfig(MemberSerialization.OptIn)]
    public class EggCrackerSafetyControl : KMonoBehaviour, ISingleSliderControl, ISliderControl
    {
        [Serialize]
        public int ReserveThreshold = 10;

        private static int _lastCacheFrame = -1;
        private static readonly Dictionary<Tag, float> _colonyEggCache = new Dictionary<Tag, float>();

        private static readonly EventSystem.IntraObjectHandler<EggCrackerSafetyControl> OnCopySettingsDelegate =
            new EventSystem.IntraObjectHandler<EggCrackerSafetyControl>((cmp, data) => cmp.OnCopySettings(data));

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Subscribe((int)GameHashes.CopySettings, OnCopySettingsDelegate);
        }

        private void OnCopySettings(object data)
        {
            GameObject source = (GameObject)data;
            if (source == null) return;
            EggCrackerSafetyControl other = source.GetComponent<EggCrackerSafetyControl>();
            if (other != null)
            {
                ReserveThreshold = other.ReserveThreshold;
            }
        }

        /// <summary>
        /// 获取全殖民地（共享池 + 各世界本地）指定物种蛋的总数量（带帧缓存，单帧多次调用开销为 0）
        /// </summary>
        public static float GetColonyEggCount(Tag eggTag)
        {
            if (!eggTag.IsValid) return 0f;
            int frame = Time.frameCount;
            if (_lastCacheFrame != frame)
            {
                _lastCacheFrame = frame;
                _colonyEggCache.Clear();
            }

            if (_colonyEggCache.TryGetValue(eggTag, out float cached))
            {
                return cached;
            }

            float total = 0f;
            // 1. 实体共享池存量（O(1) 瞬时查询）
            if (OmniEntityPool.Instance != null)
            {
                total += OmniEntityPool.Instance.GetAmount(3, eggTag);
            }

            // 2. 地图或本地箱中非池存量
            if (ClusterManager.Instance != null && ClusterManager.Instance.WorldContainers != null)
            {
                for (int i = 0; i < ClusterManager.Instance.WorldContainers.Count; i++)
                {
                    var world = ClusterManager.Instance.WorldContainers[i];
                    if (world != null && world.worldInventory != null)
                    {
                        var localPickups = world.worldInventory.GetPickupables(eggTag, false);
                        if (localPickups != null)
                        {
                            foreach (var p in localPickups)
                            {
                                if (p != null && !p.wasAbsorbed && (OmniEntityPool.Instance == null || !OmniEntityPool.Instance.Owns(p)))
                                {
                                    total += p.TotalAmount;
                                }
                            }
                        }
                    }
                }
            }

            _colonyEggCache[eggTag] = total;
            return total;
        }

        public bool CanCrackEgg(Tag eggTag)
        {
            if (ReserveThreshold <= 0) return true;
            if (!eggTag.IsValid) return true;
            return GetColonyEggCount(eggTag) > ReserveThreshold;
        }

        public bool CanCrack(ComplexRecipe recipe)
        {
            if (ReserveThreshold <= 0) return true;
            if (recipe == null || recipe.ingredients == null || recipe.ingredients.Length == 0) return true;

            var ing = recipe.ingredients[0];
            if (ing.possibleMaterials != null && ing.possibleMaterials.Length > 0)
            {
                for (int i = 0; i < ing.possibleMaterials.Length; i++)
                {
                    if (CanCrackEgg(ing.possibleMaterials[i])) return true;
                }
                return false;
            }
            return CanCrackEgg(ing.material);
        }

        // ================= ISingleSliderControl / ISliderControl =================

        public string SliderTitleKey => "STRINGS.UI.UISIDESCREENS.OMNIMOD_EGGCRACKER.RESERVE_THRESHOLD_TITLE";

        public string SliderUnits => " 个";

        public int SliderDecimalPlaces(int index) => 0;

        public float GetSliderMin(int index) => 0f;

        public float GetSliderMax(int index) => 100f;

        public float GetSliderValue(int index) => ReserveThreshold;

        public void SetSliderValue(float value, int index)
        {
            ReserveThreshold = Mathf.RoundToInt(value);
            ComplexFabricator fab = GetComponent<ComplexFabricator>();
            if (fab != null)
            {
                // 触发刷新配方取货与生产队列
                fab.Trigger(-1697596308, null);
            }
        }

        public string GetSliderTooltipKey(int index) => "STRINGS.UI.UISIDESCREENS.OMNIMOD_EGGCRACKER.RESERVE_THRESHOLD_TOOLTIP";

        public string GetSliderTooltip(int index) =>
            $"物种蛋保留阈值：{ReserveThreshold} 个\n当该物种的全殖民地存蛋总量 ≤ {ReserveThreshold} 时，打蛋机自动暂停加工该物种的蛋，杜绝动物意外绝种。\n设为 0 则不保留（全部打光）。";
    }

    [HarmonyPatch(typeof(EggCrackerConfig), nameof(EggCrackerConfig.DoPostConfigureComplete))]
    internal static class EggCrackerConfig_DoPostConfigureComplete_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null && go.GetComponent<EggCrackerSafetyControl>() == null)
            {
                go.AddOrGet<EggCrackerSafetyControl>();
            }
        }
    }

    [HarmonyPatch(typeof(EggCracker), "OnSpawn")]
    internal static class EggCracker_OnSpawn_Patch
    {
        private static void Postfix(EggCracker __instance)
        {
            if (__instance != null && __instance.GetComponent<EggCrackerSafetyControl>() == null)
            {
                __instance.gameObject.AddComponent<EggCrackerSafetyControl>();
            }
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), nameof(ComplexFabricator.GetRecipePrefetchCount))]
    internal static class ComplexFabricator_GetRecipePrefetchCount_EggSafety_Patch
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, ref int __result)
        {
            if (__result <= 0 || __instance == null || recipe == null) return;
            var safety = __instance.GetComponent<EggCrackerSafetyControl>();
            if (safety != null && !safety.CanCrack(recipe))
            {
                __result = 0;
            }
        }
    }

    [HarmonyPatch(typeof(ComplexFabricator), "HasIngredients", new[] { typeof(ComplexRecipe), typeof(Storage) })]
    internal static class ComplexFabricator_HasIngredients_EggSafety_Patch
    {
        private static void Postfix(ComplexFabricator __instance, ComplexRecipe recipe, Storage storage, ref bool __result)
        {
            if (!__result || __instance == null || recipe == null) return;
            var safety = __instance.GetComponent<EggCrackerSafetyControl>();
            if (safety != null && !safety.CanCrack(recipe))
            {
                __result = false;
            }
        }
    }
}
