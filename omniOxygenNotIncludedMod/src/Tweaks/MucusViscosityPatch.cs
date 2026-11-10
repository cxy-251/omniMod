using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Tweaks
{
    /// <summary>
    /// 将粘蛋白（Mucus）的流动参数（粘度 viscosity、水平流动阻力 minHorizontalFlow、垂直流动阻力 minVerticalFlow 等）
    /// 修改为与乙醇（Ethanol）一致。
    ///
    /// 背景与原理：
    /// 原版中粘蛋白配置为高粘度液体（speed/viscosity: 30，minHorizontalFlow: 25.5 kg），
    /// 导致缓螺分泌的粘蛋白极易在原地堆积成厚厚的粘稠水包，淹没周围建筑（照料站/喂食器/放生点等）。
    /// 而乙醇具有优良的低粘度和极强流动性（speed/viscosity: 125，minHorizontalFlow: 0.01 kg）。
    ///
    /// 本补丁在元素表终结（ElementLoader.FinaliseElementsTable）以及向底层 Sim 物理引擎注册
    /// （SimMessages.CreateSimElementsTable）时，将粘蛋白的流动参数完全对齐乙醇，使其能像乙醇一样
    /// 极其顺畅地快速摊平、自流汇入凹槽/集液池或漏下网格砖，彻底避免小范围密集养殖时局部积液淹没建筑。
    /// </summary>
    internal static class MucusViscosityHelper
    {
        private static bool applied = false;

        public static void Apply(List<Element> elementsList = null)
        {
            try
            {
                Element ethanol = null;
                Element mucus = null;

                if (elementsList != null)
                {
                    for (int i = 0; i < elementsList.Count; i++)
                    {
                        var elem = elementsList[i];
                        if (elem == null) continue;
                        if (elem.id == SimHashes.Ethanol) ethanol = elem;
                        else if (elem.id == SimHashes.Mucus) mucus = elem;
                    }
                }

                if (ethanol == null) ethanol = ElementLoader.FindElementByHash(SimHashes.Ethanol);
                if (mucus == null) mucus = ElementLoader.FindElementByHash(SimHashes.Mucus);

                if (mucus == null)
                {
                    return;
                }

                // 乙醇的标准参数（若读取失败则以 125f / 0.01f 为保底标准）：
                // speed/viscosity: 125f (Mucus 原版为 30f)
                // minHorizontalFlow: 0.01f (Mucus 原版为 25.5f)
                // minVerticalFlow: 0.01f (Mucus 原版为 0.01f)
                // flow: 0f
                float targetViscosity = ethanol != null ? ethanol.viscosity : 125f;
                float targetMinHorizontal = ethanol != null ? ethanol.minHorizontalFlow : 0.01f;
                float targetMinVertical = ethanol != null ? ethanol.minVerticalFlow : 0.01f;
                float targetFlow = ethanol != null ? ethanol.flow : 0f;

                mucus.viscosity = targetViscosity;
                mucus.minHorizontalFlow = targetMinHorizontal;
                mucus.minVerticalFlow = targetMinVertical;
                mucus.flow = targetFlow;

                if (!applied)
                {
                    applied = true;
                    ModLog.Info($"[MucusViscosity] 已将粘蛋白(Mucus)流动粘度修改为乙醇标准: viscosity={targetViscosity}, minHorizontalFlow={targetMinHorizontal}, minVerticalFlow={targetMinVertical}");
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[MucusViscosity] 修改粘蛋白粘度失败: {ex.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(ElementLoader), "FinaliseElementsTable")]
    internal static class ElementLoader_FinaliseElementsTable_MucusPatch
    {
        private static void Postfix()
        {
            MucusViscosityHelper.Apply(ElementLoader.elements);
        }
    }

    [HarmonyPatch(typeof(SimMessages), nameof(SimMessages.CreateSimElementsTable))]
    internal static class SimMessages_CreateSimElementsTable_MucusPatch
    {
        private static void Prefix(List<Element> elements)
        {
            MucusViscosityHelper.Apply(elements);
        }
    }
}
