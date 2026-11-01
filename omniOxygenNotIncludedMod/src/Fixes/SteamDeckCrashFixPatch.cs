using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复在脱机/无 Steam/修改版环境下，点击“载入游戏”时，
    /// Steamworks.SteamUtils.IsSteamRunningOnSteamDeck 抛出 InvalidProgramException 或
    /// InvalidOperationException 导致 LoadScreen 崩溃闪退的问题。
    /// </summary>
    [HarmonyPatch]
    public static class SteamDeckCrashFixPatch
    {
        // 目标方法：Steamworks.SteamUtils.IsSteamRunningOnSteamDeck
        [HarmonyTargetMethod]
        public static MethodBase TargetMethod()
        {
            try
            {
                return AccessTools.Method("Steamworks.SteamUtils:IsSteamRunningOnSteamDeck");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[OmniMod] 查找 SteamUtils.IsSteamRunningOnSteamDeck 失败: {ex.Message}");
                return null;
            }
        }

        // 前置拦截：直接返回 false，彻底绕过底层方法体执行
        [HarmonyPrefix]
        public static bool Prefix(ref bool __result)
        {
            __result = false;
            return false; // 跳过原方法
        }
    }

    /// <summary>
    /// 第二重防线：针对 LoadScreen.OnActivate 等界面中直接调用 IsSteamRunningOnSteamDeck 的指令进行 Transpile 替换，
    /// 确保即使底层方法在 JIT 编译阶段出错，UI 激活流程也绝不调用它。
    /// </summary>
    [HarmonyPatch]
    public static class LoadScreenOnActivateTranspilerPatch
    {
        [HarmonyTargetMethods]
        public static IEnumerable<MethodBase> TargetMethods()
        {
            var targets = new List<MethodBase>();
            TryAdd(targets, typeof(LoadScreen), "OnActivate");
            TryAdd(targets, typeof(LoadScreen), "ShowConvertError");
            TryAdd(targets, typeof(FeedbackScreen), "OnSpawn");
            TryAdd(targets, typeof(GameOptionsScreen), "OnSpawn");
            return targets;
        }

        private static void TryAdd(List<MethodBase> list, Type type, string methodName)
        {
            if (type == null) return;
            try
            {
                var m = AccessTools.Method(type, methodName);
                if (m != null) list.Add(m);
            }
            catch {}
        }

        public static bool SafeIsSteamRunningOnSteamDeck()
        {
            return false;
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            MethodInfo targetMethod = null;
            try
            {
                targetMethod = AccessTools.Method("Steamworks.SteamUtils:IsSteamRunningOnSteamDeck");
            }
            catch {}

            MethodInfo safeReplacement = AccessTools.Method(typeof(LoadScreenOnActivateTranspilerPatch), nameof(SafeIsSteamRunningOnSteamDeck));

            int replacedCount = 0;
            foreach (var instruction in instructions)
            {
                if (targetMethod != null && instruction.Calls(targetMethod))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = safeReplacement;
                    replacedCount++;
                }
                yield return instruction;
            }

            if (replacedCount > 0)
            {
                Debug.Log($"[OmniMod] {original?.DeclaringType?.Name}.{original?.Name} 已成功替换 {replacedCount} 处 IsSteamRunningOnSteamDeck 调用为安全桩。");
            }
        }
    }
}
