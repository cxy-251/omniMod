using HarmonyLib;
using OmniMod.Core.Safety;

namespace OmniMod.Machinery.Refineries
{
    /// <summary>
    /// 脱盐器（Desalinator）满盐全自动排空：
    /// 盐分积满或 SaltStorageLeft 归零时，自动掉落所有固体盐并重置容量，取消清空任务，小人免跑腿。
    /// </summary>
    [HarmonyPatch(typeof(Desalinator), "CheckCanConvert")]
    internal static class Desalinator_CheckCanConvert_AutoEmpty_Patch
    {
        private static void Prefix(Desalinator __instance)
        {
            AutoEmptyIfNeeded(__instance);
        }

        internal static void AutoEmptyIfNeeded(Desalinator desalinator)
        {
            if (desalinator == null) return;

            ExceptionShield.Execute("Desalinator.AutoEmptyIfNeeded", () =>
            {
                Storage st = desalinator.GetComponent<Storage>();
                if (st == null) return;

                if (desalinator.SaltStorageLeft <= 0f || st.MassStored() >= desalinator.maxSalt)
                {
                    if (st.items.Count > 0)
                    {
                        st.DropAll(false, false, default, true);
                    }
                    desalinator.SaltStorageLeft = desalinator.maxSalt;
                    if (desalinator.smi != null)
                    {
                        desalinator.smi.CancelEmptyChore();
                    }
                }
            });
        }
    }

    [HarmonyPatch(typeof(Desalinator), "SaltStorageLeft", MethodType.Setter)]
    internal static class Desalinator_set_SaltStorageLeft_Patch
    {
        private static void Prefix(Desalinator __instance, ref float value)
        {
            if (__instance == null) return;

            if (value <= 0f)
            {
                ExceptionShield.Execute("Desalinator.SaltStorageLeft.Setter", () =>
                {
                    Storage st = __instance.GetComponent<Storage>();
                    if (st != null && st.items.Count > 0)
                    {
                        st.DropAll(false, false, default, true);
                    }
                    if (__instance.smi != null)
                    {
                        __instance.smi.CancelEmptyChore();
                    }
                });
                value = __instance.maxSalt;
            }
        }
    }

    [HarmonyPatch(typeof(Desalinator), "OnSpawn")]
    internal static class Desalinator_OnSpawn_AutoEmpty_Patch
    {
        private static void Postfix(Desalinator __instance)
        {
            Desalinator_CheckCanConvert_AutoEmpty_Patch.AutoEmptyIfNeeded(__instance);
        }
    }
}
