using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OmniMod.Core.Logging;
using OmniMod.Pool;

namespace OmniMod.Fixes
{
    /// <summary>只保护具体食物统计入口，不补丁会被 Mono 共享的泛型 CountAmount。</summary>
    [HarmonyPatch(typeof(RationTracker), "GetItemData")]
    internal static class RationTrackerInvalidPickup
    {
        private static bool IsUsableFood(Pickupable item)
        {
            if (SharedPickupView.IsUsable(item, GameTags.Edible)) return true;
            ModLog.Throttle("ration_invalid_pickup", "[食物统计] 已跳过失效或已不再可食用的物品引用。", 15f);
            return false;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions,
            ILGenerator generator, MethodBase __originalMethod)
        {
            // ItemData 是受保护的原版结构体；在挂载时解析一次，失效项返回非空 ID 和零统计值。
            Type dataType = ((MethodInfo)__originalMethod).ReturnType;
            FieldInfo id = AccessTools.Field(dataType, "ID");
            if (id == null || id.FieldType != typeof(string)) throw new InvalidOperationException("RationTracker.ItemData.ID 与 744825 不一致");
            LocalBuilder empty = generator.DeclareLocal(dataType);
            Label valid = generator.DefineLabel();
            yield return new CodeInstruction(OpCodes.Ldarg_1);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RationTrackerInvalidPickup), nameof(IsUsableFood)));
            yield return new CodeInstruction(OpCodes.Brtrue, valid);
            yield return new CodeInstruction(OpCodes.Ldloca, empty);
            yield return new CodeInstruction(OpCodes.Initobj, dataType);
            yield return new CodeInstruction(OpCodes.Ldloca, empty);
            yield return new CodeInstruction(OpCodes.Ldstr, string.Empty);
            yield return new CodeInstruction(OpCodes.Stfld, id);
            yield return new CodeInstruction(OpCodes.Ldloc, empty);
            yield return new CodeInstruction(OpCodes.Ret);
            bool first = true;
            foreach (CodeInstruction instruction in instructions)
            {
                if (first) { instruction.labels.Add(valid); first = false; }
                yield return instruction;
            }
        }
    }
}
