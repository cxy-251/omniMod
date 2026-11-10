using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Farming
{
    /// <summary>
    /// 农耕等温施肥与种植盒隔热组件：
    /// 解决低温农场（如冰霜小麦、释气草）或高温农场（如火椒藤）在施肥/灌溉时，
    /// 因外部送来的肥料（泥土、磷矿等）或灌溉液体具有温差，导致农耕瓷砖/作物温度持续偏移的问题。
    /// 
    /// 机制：
    /// 1. 种植盒内部存储添加 Insulate 隔热修饰符，阻断静置肥料与建筑及外界的气温热传导；
    /// 2. 当肥料或灌溉液体送入时，自动将温度归一化至作物最适温区与瓷砖温度（ΔT = 0），消除热冲击；
    /// 3. 若为液体灌溉，严格保证温度在冰点以上（+1.5℃），既不结冰爆砖又维持作物安全温区；
    /// 4. 彻底兼容既有存档与新建农场，零侵入无须重新铺管或拆砖。
    /// </summary>
    public class IsothermalPlantPlot : KMonoBehaviour
    {
        private PlantablePlot _plot;
        private int _onStorageChangeHandle = -1;
        private int _onPlanterStorageHandle = -1;
        private int _onOccupantChangedHandle = -1;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            _plot = GetComponent<PlantablePlot>();
        }

        protected override void OnSpawn()
        {
            base.OnSpawn();
            if (_plot == null)
            {
                _plot = GetComponent<PlantablePlot>();
            }

            SetupInsulationAndNormalize();

            _onStorageChangeHandle = Subscribe((int)GameHashes.OnStorageChange, OnStorageChanged);
            _onPlanterStorageHandle = Subscribe((int)GameHashes.PlanterStorage, OnStorageChanged);
            _onOccupantChangedHandle = Subscribe((int)GameHashes.OccupantChanged, OnStorageChanged);
        }

        protected override void OnCleanUp()
        {
            if (_onStorageChangeHandle != -1)
            {
                Unsubscribe(_onStorageChangeHandle);
                _onStorageChangeHandle = -1;
            }
            if (_onPlanterStorageHandle != -1)
            {
                Unsubscribe(_onPlanterStorageHandle);
                _onPlanterStorageHandle = -1;
            }
            if (_onOccupantChangedHandle != -1)
            {
                Unsubscribe(_onOccupantChangedHandle);
                _onOccupantChangedHandle = -1;
            }

            base.OnCleanUp();
        }

        private void OnStorageChanged(object data)
        {
            NormalizeItems();
        }

        public void SetupInsulationAndNormalize()
        {
            Storage[] storages = GetComponents<Storage>();
            if (storages != null)
            {
                for (int s = 0; s < storages.Length; s++)
                {
                    Storage st = storages[s];
                    if (st != null)
                    {
                        st.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
                    }
                }
            }

            NormalizeItems();
        }

        public void NormalizeItems()
        {
            Storage[] storages = GetComponents<Storage>();
            if (storages == null) return;

            for (int s = 0; s < storages.Length; s++)
            {
                Storage st = storages[s];
                if (st == null || st.items == null) continue;

                for (int i = 0; i < st.items.Count; i++)
                {
                    GameObject item = st.items[i];
                    if (item == null) continue;

                    // 阻断静置热传导
                    Storage.MakeItemTemperatureInsulated(item, is_stored: true, is_initializing: false);
                    SimTemperatureTransfer stt = item.GetComponent<SimTemperatureTransfer>();
                    if (stt != null && stt.enabled)
                    {
                        stt.enabled = false;
                    }

                    // 等温调整
                    PrimaryElement pe = item.GetComponent<PrimaryElement>();
                    if (pe != null && pe.Mass > 0.001f)
                    {
                        float safeTemp = CalculateSafeTemperature(_plot, pe);
                        if (Mathf.Abs(pe.Temperature - safeTemp) > 0.05f)
                        {
                            pe.Temperature = safeTemp;
                        }
                    }
                }
            }
        }

        public static float CalculateSafeTemperature(PlantablePlot plot, PrimaryElement itemPE)
        {
            if (itemPE == null) return 293.15f;
            Element elem = itemPE.Element;

            // 1. 基准温度：取种植砖自身当前温度
            PrimaryElement plotPE = plot != null ? plot.GetComponent<PrimaryElement>() : null;
            float baseTemp = plotPE != null ? plotPE.Temperature : 293.15f;

            // 2. 状态保持区间：确保液体不结冰不汽化，固体不融化
            float stateMin = 1.0f;
            float stateMax = 9999.0f;
            if (elem != null)
            {
                if (elem.IsLiquid)
                {
                    stateMin = elem.lowTemp + 1.5f;
                    stateMax = elem.highTemp - 2.0f;
                }
                else if (elem.IsSolid)
                {
                    stateMin = elem.lowTemp > 1.0f ? elem.lowTemp + 1.0f : 1.0f;
                    stateMax = elem.highTemp - 2.0f;
                }
            }

            // 3. 作物舒适温区：如果地里种了作物且有温区需求
            float plantMin = 1.0f;
            float plantMax = 9999.0f;
            bool hasPlantReq = false;

            if (plot != null && plot.Occupant != null)
            {
                TemperatureVulnerable tv = plot.Occupant.GetComponent<TemperatureVulnerable>();
                if (tv != null && tv.TemperatureWarningHigh > tv.TemperatureWarningLow)
                {
                    // 在预警线内侧留出 1.5K 安全缓冲
                    plantMin = tv.TemperatureWarningLow + 1.5f;
                    plantMax = tv.TemperatureWarningHigh - 1.5f;
                    hasPlantReq = true;
                }
            }

            // 4. 计算综合有效温区 [effectiveMin, effectiveMax]
            float effectiveMin = Mathf.Max(stateMin, hasPlantReq ? plantMin : stateMin);
            float effectiveMax = Mathf.Min(stateMax, hasPlantReq ? plantMax : stateMax);

            // 若交集反转（例如极寒作物的上限低于液体的凝固点），以保持物理状态为主（防止水结冰破砖）
            if (effectiveMin > effectiveMax)
            {
                effectiveMin = stateMin;
                effectiveMax = Mathf.Max(stateMin, stateMax);
            }

            // 5. 尽量贴近种植砖自身温度（达到 ΔT = 0 的等温状态），但必须被有效安全区间约束
            return Mathf.Clamp(baseTemp, effectiveMin, effectiveMax);
        }
    }

    /// <summary>
    /// 自动为所有生成/读档的种植盒、土培砖、水培砖挂载等温隔热组件
    /// </summary>
    [HarmonyPatch(typeof(PlantablePlot), "OnSpawn")]
    internal static class PlantablePlot_OnSpawn_Isothermal_Patch
    {
        private static void Postfix(PlantablePlot __instance)
        {
            if (__instance != null && __instance.gameObject != null)
            {
                var iso = __instance.gameObject.AddOrGet<IsothermalPlantPlot>();
                iso.SetupInsulationAndNormalize();
            }
        }
    }

    /// <summary>
    /// 作物种植/替换时，立即对内部已存肥料重新进行等温归一化
    /// </summary>
    [HarmonyPatch(typeof(PlantablePlot), "RegisterWithPlant")]
    internal static class PlantablePlot_RegisterWithPlant_Isothermal_Patch
    {
        private static void Postfix(PlantablePlot __instance)
        {
            if (__instance != null && __instance.gameObject != null)
            {
                var iso = __instance.GetComponent<IsothermalPlantPlot>();
                if (iso != null)
                {
                    iso.NormalizeItems();
                }
            }
        }
    }

    /// <summary>
    /// 土培砖模板配置：默认加上 Insulate 隔热与等温组件
    /// </summary>
    [HarmonyPatch(typeof(FarmTileConfig), "ConfigureBuildingTemplate")]
    internal static class FarmTileConfig_ConfigureBuildingTemplate_Isothermal_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null)
            {
                go.AddOrGet<IsothermalPlantPlot>();
                Storage st = go.GetComponent<Storage>();
                if (st != null)
                {
                    st.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
                }
            }
        }
    }

    /// <summary>
    /// 水培砖模板配置：默认加上 Insulate 隔热与等温组件
    /// </summary>
    [HarmonyPatch(typeof(HydroponicFarmConfig), "ConfigureBuildingTemplate")]
    internal static class HydroponicFarmConfig_ConfigureBuildingTemplate_Isothermal_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null)
            {
                go.AddOrGet<IsothermalPlantPlot>();
                Storage st = go.GetComponent<Storage>();
                if (st != null)
                {
                    st.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
                }
            }
        }
    }

    /// <summary>
    /// 种植箱模板配置：默认加上 Insulate 隔热与等温组件
    /// </summary>
    [HarmonyPatch(typeof(PlanterBoxConfig), "ConfigureBuildingTemplate")]
    internal static class PlanterBoxConfig_ConfigureBuildingTemplate_Isothermal_Patch
    {
        private static void Postfix(GameObject go)
        {
            if (go != null)
            {
                go.AddOrGet<IsothermalPlantPlot>();
                Storage st = go.GetComponent<Storage>();
                if (st != null)
                {
                    st.SetDefaultStoredItemModifiers(Storage.StandardInsulatedStorage);
                }
            }
        }
    }
}
