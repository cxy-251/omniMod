using System;
using System.Collections.Generic;
using HarmonyLib;
using OmniMod.Core.Logging;

namespace OmniMod.Tweaks
{
    /// <summary>
    /// 女性复制人优先筛选补丁：
    /// 拦截打印舱门（Telepad / Immigration）与开局随机选小人逻辑，
    /// 确保打印新复制人时刷出的小人均为女性（Female）。
    /// 同时对 DLC2 寒霜行星的仿真复制人（Bionic Minion）同样生效（在女性仿真复制人 Sonyar 和 Steela 中筛选）。
    /// </summary>
    public static class FemaleDuplicantsPatch
    {
        public static bool IsFemale(Personality p)
        {
            if (p == null || string.IsNullOrEmpty(p.genderStringKey)) return false;
            return string.Equals(p.genderStringKey, "Female", StringComparison.OrdinalIgnoreCase);
        }

        [HarmonyPatch(typeof(Database.Personalities), nameof(Database.Personalities.GetRandom),
            new Type[] { typeof(bool), typeof(bool) })]
        public static class Personalities_GetRandom_Patch1
        {
            public static void Postfix(Database.Personalities __instance, bool onlyEnabledMinions, bool onlyStartingMinions, ref Personality __result)
            {
                if (__result != null && !IsFemale(__result))
                {
                    var all = __instance.GetAll(onlyEnabledMinions, onlyStartingMinions);
                    var females = all.FindAll(p => IsFemale(p));
                    if (females.Count > 0)
                    {
                        __result = females.GetRandom();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Database.Personalities), nameof(Database.Personalities.GetRandom),
            new Type[] { typeof(Tag), typeof(bool), typeof(bool) })]
        public static class Personalities_GetRandom_Patch2
        {
            public static void Postfix(Database.Personalities __instance, Tag model, bool onlyEnabledMinions, bool onlyStartingMinions, ref Personality __result)
            {
                if (__result != null && !IsFemale(__result))
                {
                    var all = __instance.GetAll(onlyEnabledMinions, onlyStartingMinions);
                    var females = all.FindAll(p => p.model == model && IsFemale(p));
                    if (females.Count > 0)
                    {
                        __result = females.GetRandom();
                    }
                }
            }
        }

        [HarmonyPatch(typeof(Database.Personalities), nameof(Database.Personalities.GetRandom),
            new Type[] { typeof(List<Tag>), typeof(bool), typeof(bool) })]
        public static class Personalities_GetRandom_Patch3
        {
            public static void Postfix(Database.Personalities __instance, List<Tag> models, bool onlyEnabledMinions, bool onlyStartingMinions, ref Personality __result)
            {
                if (__result != null && !IsFemale(__result))
                {
                    var all = __instance.GetAll(onlyEnabledMinions, onlyStartingMinions);
                    var females = all.FindAll(p => (models == null || models.Count == 0 || models.Contains(p.model)) && IsFemale(p));
                    if (females.Count > 0)
                    {
                        __result = females.GetRandom();
                    }
                }
            }
        }
    }
}
