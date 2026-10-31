using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 过滤星图与管理界面，严禁将折叠空间生态舱作为星球列出：
    /// 1. WorldSelector: 隐藏左上角视角切换的世界行；
    /// 2. SkillsScreen: 隐藏技能界面左侧将生态舱当作星球列出的 WorldDivider 分割栏；
    /// 3. TableScreen: 隐藏优先级、消耗品、生理等管理表格中的生态舱分割栏；
    /// 4. ClusterManager.MinionsByWorld: 将身处生态舱内部的小人归入母星分组，避免产生多余的分组列表。
    /// </summary>
    [HarmonyPatch(typeof(WorldSelector), "RefreshToggles")]
    internal static class PocketHabitatWorldSelectorPatch
    {
        private static void Postfix(WorldSelector __instance)
        {
            if (__instance == null || __instance.worldRows == null || ClusterManager.Instance == null) return;
            foreach (var row in __instance.worldRows)
            {
                if (OmniPocketHabitat.GetHabitatForWorld(row.Key) != null && row.Value != null)
                {
                    row.Value.gameObject.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(SkillsScreen), "AddWorldDivider")]
    internal static class SkillsScreen_AddWorldDivider_Patch
    {
        private static readonly AccessTools.FieldRef<SkillsScreen, Dictionary<int, GameObject>> WorldDividersRef =
            AccessTools.FieldRefAccess<SkillsScreen, Dictionary<int, GameObject>>("worldDividers");

        private static void Postfix(SkillsScreen __instance, int worldId)
        {
            if (__instance != null && OmniPocketHabitat.GetHabitatForWorld(worldId) != null)
            {
                var dividers = WorldDividersRef(__instance);
                if (dividers != null && dividers.TryGetValue(worldId, out GameObject divider) && divider != null)
                {
                    divider.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(SkillsScreen), "BuildMinions")]
    internal static class SkillsScreen_BuildMinions_Patch
    {
        private static readonly AccessTools.FieldRef<SkillsScreen, Dictionary<int, GameObject>> WorldDividersRef =
            AccessTools.FieldRefAccess<SkillsScreen, Dictionary<int, GameObject>>("worldDividers");

        private static void Postfix(SkillsScreen __instance)
        {
            if (__instance == null) return;
            var dividers = WorldDividersRef(__instance);
            if (dividers == null) return;
            foreach (var kvp in dividers)
            {
                if (OmniPocketHabitat.GetHabitatForWorld(kvp.Key) != null && kvp.Value != null)
                {
                    if (kvp.Value.activeSelf) kvp.Value.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(SkillsScreen), "SortRows")]
    internal static class SkillsScreen_SortRows_Patch
    {
        private static readonly AccessTools.FieldRef<SkillsScreen, Dictionary<int, GameObject>> WorldDividersRef =
            AccessTools.FieldRefAccess<SkillsScreen, Dictionary<int, GameObject>>("worldDividers");

        private static void Postfix(SkillsScreen __instance)
        {
            if (__instance == null) return;
            var dividers = WorldDividersRef(__instance);
            if (dividers == null) return;
            foreach (var kvp in dividers)
            {
                if (OmniPocketHabitat.GetHabitatForWorld(kvp.Key) != null && kvp.Value != null)
                {
                    if (kvp.Value.activeSelf) kvp.Value.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TableScreen), "RefreshRows")]
    internal static class TableScreen_RefreshRows_Patch
    {
        private static readonly AccessTools.FieldRef<TableScreen, Dictionary<int, GameObject>> WorldDividersRef =
            AccessTools.FieldRefAccess<TableScreen, Dictionary<int, GameObject>>("worldDividers");

        private static void Postfix(TableScreen __instance)
        {
            if (__instance == null) return;
            var dividers = WorldDividersRef(__instance);
            if (dividers == null) return;
            foreach (var kvp in dividers)
            {
                if (OmniPocketHabitat.GetHabitatForWorld(kvp.Key) != null && kvp.Value != null)
                {
                    if (kvp.Value.activeSelf) kvp.Value.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(TableScreen), "SortRows")]
    internal static class TableScreen_SortRows_Patch
    {
        private static readonly AccessTools.FieldRef<TableScreen, Dictionary<int, GameObject>> WorldDividersRef =
            AccessTools.FieldRefAccess<TableScreen, Dictionary<int, GameObject>>("worldDividers");

        private static void Postfix(TableScreen __instance)
        {
            if (__instance == null) return;
            var dividers = WorldDividersRef(__instance);
            if (dividers == null) return;
            foreach (var kvp in dividers)
            {
                if (OmniPocketHabitat.GetHabitatForWorld(kvp.Key) != null && kvp.Value != null)
                {
                    if (kvp.Value.activeSelf) kvp.Value.SetActive(false);
                }
            }
        }
    }

    [HarmonyPatch(typeof(ClusterManager), "get_MinionsByWorld")]
    internal static class ClusterManager_MinionsByWorld_Patch
    {
        private static void Postfix(Dictionary<int, List<IAssignableIdentity>> __result)
        {
            if (__result == null || __result.Count == 0) return;
            List<int> pocketWorlds = null;
            foreach (var kvp in __result)
            {
                int extWorldId = OmniPocketHabitat.GetExteriorWorldId(kvp.Key);
                if (extWorldId >= 0 && extWorldId != kvp.Key)
                {
                    if (pocketWorlds == null) pocketWorlds = new List<int>();
                    pocketWorlds.Add(kvp.Key);
                }
            }

            if (pocketWorlds != null)
            {
                for (int i = 0; i < pocketWorlds.Count; i++)
                {
                    int pocketId = pocketWorlds[i];
                    int extWorldId = OmniPocketHabitat.GetExteriorWorldId(pocketId);
                    if (extWorldId >= 0 && __result.TryGetValue(pocketId, out var minions) && minions != null)
                    {
                        if (!__result.TryGetValue(extWorldId, out var extMinions))
                        {
                            extMinions = new List<IAssignableIdentity>();
                            __result[extWorldId] = extMinions;
                        }
                        extMinions.AddRange(minions);
                        __result.Remove(pocketId);
                    }
                }
            }
        }
    }
}
