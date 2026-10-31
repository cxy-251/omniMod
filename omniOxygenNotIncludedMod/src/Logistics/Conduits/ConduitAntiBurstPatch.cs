using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Conduits
{
    /// <summary>
    /// 全能管道相变防爆保护补丁（ConduitAntiBurstPatch）：
    /// 解决 ONI 冷却管网/输气管网在流体相变时炸管并将液体/气体直接排空倾倒到环境中的原版顽疾。
    /// 当流体即将凝固结冰或沸腾气化时，将其安全锁死在临界相变点内侧（+2°C / -2°C），
    /// 既保证管道永不破裂，也彻底避免液体排空到地面后被杂物箱收集或蒸发消失。
    /// </summary>
    public static class ConduitAntiBurstPatch
    {
        [HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.FreezeConduitContents))]
        public static class ConduitFlow_FreezeConduitContents_Patch
        {
            public static bool Prefix(ConduitFlow __instance, int conduit_idx)
            {
                try
                {
                    if (__instance.soaInfo == null) return true;

                    ConduitFlow.Conduit conduit = __instance.soaInfo.GetConduit(conduit_idx);
                    ConduitFlow.ConduitContents contents = conduit.GetContents(__instance);

                    if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                    {
                        Element element = ElementLoader.FindElementByHash(contents.element);
                        if (element != null)
                        {
                            if (__instance.conduitType == ConduitType.Liquid && element.IsLiquid)
                            {
                                // 锁死在冰点以上安全温度，保护液体管道永不爆裂
                                contents.temperature = Mathf.Max(contents.temperature, element.lowTemp + 2.0f);
                                conduit.SetContents(__instance, contents);
                                return false; // 拦截原版管道破裂与内容物倾倒逻辑
                            }
                            else if (__instance.conduitType == ConduitType.Gas && element.IsGas)
                            {
                                // 锁死在液化点以上安全温度，保护气体管道永不冷凝爆管
                                contents.temperature = Mathf.Max(contents.temperature, element.lowTemp + 2.0f);
                                conduit.SetContents(__instance, contents);
                                return false; // 拦截原版管道破裂与内容物倾倒逻辑
                            }
                        }
                    }
                }
                catch
                {
                    // 若发生不可预知异常，降级执行原版逻辑
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(ConduitFlow), nameof(ConduitFlow.MeltConduitContents))]
        public static class ConduitFlow_MeltConduitContents_Patch
        {
            public static bool Prefix(ConduitFlow __instance, int conduit_idx)
            {
                try
                {
                    if (__instance.soaInfo == null) return true;

                    ConduitFlow.Conduit conduit = __instance.soaInfo.GetConduit(conduit_idx);
                    ConduitFlow.ConduitContents contents = conduit.GetContents(__instance);

                    if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                    {
                        Element element = ElementLoader.FindElementByHash(contents.element);
                        if (element != null)
                        {
                            if (__instance.conduitType == ConduitType.Liquid && element.IsLiquid)
                            {
                                // 锁死在沸点以下安全温度，保护液体管道永不沸腾气化爆管
                                contents.temperature = Mathf.Min(contents.temperature, element.highTemp - 2.0f);
                                conduit.SetContents(__instance, contents);
                                return false; // 拦截原版管道破裂与内容物倾倒逻辑
                            }
                        }
                    }
                }
                catch
                {
                    // 若发生不可预知异常，降级执行原版逻辑
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(Conduit), "OnConduitFrozen")]
        public static class Conduit_OnConduitFrozen_Patch
        {
            public static bool Prefix(Conduit __instance)
            {
                try
                {
                    if (__instance == null) return true;
                    int cell = Grid.PosToCell(__instance);
                    ConduitFlow flow = __instance.GetFlowManager();
                    if (flow != null && Grid.IsValidCell(cell))
                    {
                        ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                        if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                        {
                            Element element = ElementLoader.FindElementByHash(contents.element);
                            if (element != null)
                            {
                                float safeTemp = element.lowTemp + 3.0f;
                                contents.temperature = Mathf.Max(contents.temperature, safeTemp);
                                flow.SetContents(cell, contents);
                            }
                        }
                    }
                }
                catch
                {
                }
                return false; // 拦截原版爆管倾倒流体逻辑，流体永不被排空丢失
            }
        }

        [HarmonyPatch(typeof(Conduit), "OnConduitBoiling")]
        public static class Conduit_OnConduitBoiling_Patch
        {
            public static bool Prefix(Conduit __instance)
            {
                try
                {
                    if (__instance == null) return true;
                    int cell = Grid.PosToCell(__instance);
                    ConduitFlow flow = __instance.GetFlowManager();
                    if (flow != null && Grid.IsValidCell(cell))
                    {
                        ConduitFlow.ConduitContents contents = flow.GetContents(cell);
                        if (contents.mass > 0.001f && contents.element != SimHashes.Void)
                        {
                            Element element = ElementLoader.FindElementByHash(contents.element);
                            if (element != null)
                            {
                                float safeTemp = element.highTemp - 3.0f;
                                contents.temperature = Mathf.Min(contents.temperature, safeTemp);
                                flow.SetContents(cell, contents);
                            }
                        }
                    }
                }
                catch
                {
                }
                return false; // 拦截原版沸腾倾倒流体逻辑，流体永不被排空丢失
            }
        }
    }
}
