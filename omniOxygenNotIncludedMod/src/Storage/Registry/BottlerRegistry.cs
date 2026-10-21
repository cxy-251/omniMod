using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星系装瓶器（液体装瓶器 / 气体装瓶器）登记表。
    /// 供机械手（SolidTransferArm）无视距离隔空直接从装瓶器中抓取流体（如水、浓盐水等），实现免小人搬运。
    /// </summary>
    internal static class BottlerRegistry
    {
        private static readonly List<Bottler> Bottlers = new List<Bottler>();
        private static readonly Dictionary<int, Bottler> ActiveBottlerCellToBottler = new Dictionary<int, Bottler>();
        private static readonly Dictionary<int, List<Bottler>> WorldBottlers = new Dictionary<int, List<Bottler>>();

        internal static void Register(Bottler bottler)
        {
            if (bottler == null)
            {
                return;
            }
            if (!Bottlers.Contains(bottler))
            {
                Bottlers.Add(bottler);
            }
            // 确保装瓶器 Storage 允许机械臂取物，且不因优先级被拦截
            if (bottler.storage != null)
            {
                bottler.storage.ignoreSourcePriority = true;
                bottler.storage.allowItemRemoval = true;
            }
            Rebuild();
        }

        internal static void Unregister(Bottler bottler)
        {
            if (bottler == null)
            {
                return;
            }
            Bottlers.Remove(bottler);
            Rebuild();
        }

        private static void Rebuild()
        {
            ActiveBottlerCellToBottler.Clear();
            WorldBottlers.Clear();

            for (int i = 0; i < Bottlers.Count; i++)
            {
                Bottler b = Bottlers[i];
                if (b == null || b.gameObject == null)
                {
                    continue;
                }
                int cell = Grid.PosToCell(b.transform.GetPosition());
                if (Grid.IsValidCell(cell))
                {
                    ActiveBottlerCellToBottler[cell] = b;
                }
                int worldId = b.GetMyWorldId();
                if (worldId >= 0 && worldId != 255)
                {
                    if (!WorldBottlers.TryGetValue(worldId, out List<Bottler> list))
                    {
                        list = new List<Bottler>();
                        WorldBottlers[worldId] = list;
                    }
                    list.Add(b);
                }
            }
        }

        internal static bool IsSameWorldActiveBottlerCell(int cell, int workerCell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsValidCell(workerCell))
            {
                return false;
            }
            if (!ActiveBottlerCellToBottler.TryGetValue(cell, out Bottler b) || b == null)
            {
                return false;
            }
            return Grid.WorldIdx[cell] == Grid.WorldIdx[workerCell];
        }

        internal static List<Bottler> GetWorldBottlers(int worldId)
        {
            if (WorldBottlers.TryGetValue(worldId, out List<Bottler> list))
            {
                return list;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(Bottler), "OnSpawn")]
    internal static class Bottler_OnSpawn_Patch
    {
        private static void Postfix(Bottler __instance)
        {
            if (__instance != null)
            {
                BottlerRegistry.Register(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Bottler), "OnCleanUp")]
    internal static class Bottler_OnCleanUp_Patch
    {
        private static void Prefix(Bottler __instance)
        {
            if (__instance != null)
            {
                BottlerRegistry.Unregister(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(Bottler), "UpdateStoredItemState")]
    internal static class Bottler_UpdateStoredItemState_Patch
    {
        private static void Postfix(Bottler __instance)
        {
            if (__instance != null && __instance.storage != null)
            {
                // 只要装瓶器内有流体，始终允许机械臂取物并忽略来源优先级
                if (__instance.storage.MassStored() > 0f)
                {
                    __instance.storage.allowItemRemoval = true;
                    __instance.storage.ignoreSourcePriority = true;
                }
            }
        }
    }
}
