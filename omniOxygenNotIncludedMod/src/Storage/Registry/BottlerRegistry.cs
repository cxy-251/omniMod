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
        private static readonly HashSet<Storage> BottlerStorages = new HashSet<Storage>();
        private static short[] _bottlerCellWorld;
        private static readonly Dictionary<int, List<Bottler>> WorldBottlers = new Dictionary<int, List<Bottler>>();

        internal static bool IsBottlerStorage(Storage storage)
        {
            return storage != null && BottlerStorages.Contains(storage);
        }

        private static void EnsureCellArray()
        {
            if (_bottlerCellWorld == null || _bottlerCellWorld.Length < Grid.CellCount)
            {
                int count = Mathf.Max(Grid.CellCount, 1);
                _bottlerCellWorld = new short[count];
                for (int i = 0; i < count; i++) _bottlerCellWorld[i] = -1;
            }
        }

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
            if (bottler.storage != null)
            {
                bottler.storage.ignoreSourcePriority = true;
                bottler.storage.allowItemRemoval = true;
                BottlerStorages.Add(bottler.storage);
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
            if (bottler.storage != null)
            {
                BottlerStorages.Remove(bottler.storage);
            }
            Rebuild();
        }

        private static void Rebuild()
        {
            EnsureCellArray();
            if (_bottlerCellWorld != null)
            {
                for (int i = 0; i < _bottlerCellWorld.Length; i++) _bottlerCellWorld[i] = -1;
            }
            WorldBottlers.Clear();

            for (int i = 0; i < Bottlers.Count; i++)
            {
                Bottler b = Bottlers[i];
                if (b == null || b.gameObject == null)
                {
                    continue;
                }
                int worldId = b.GetMyWorldId();
                short wid = (short)worldId;

                OccupyArea area = b.GetComponent<OccupyArea>();
                int[] cells = area != null ? area.GetOccupiedGridCells() : null;
                if (cells != null && cells.Length > 0)
                {
                    for (int c = 0; c < cells.Length; c++)
                    {
                        int ac = cells[c];
                        if (Grid.IsValidCell(ac) && (uint)ac < (uint)_bottlerCellWorld.Length)
                        {
                            _bottlerCellWorld[ac] = wid;
                        }
                    }
                }
                else
                {
                    int cell = Grid.PosToCell(b.transform.GetPosition());
                    if (Grid.IsValidCell(cell) && (uint)cell < (uint)_bottlerCellWorld.Length)
                    {
                        _bottlerCellWorld[cell] = wid;
                    }
                }

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

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool IsSameWorldActiveBottlerCell(int cell, int workerCell)
        {
            if (!Grid.IsValidCell(cell) || !Grid.IsValidCell(workerCell))
            {
                return false;
            }
            short[] arr = _bottlerCellWorld;
            if (arr == null || (uint)cell >= (uint)arr.Length)
            {
                return false;
            }
            short cellWorld = arr[cell];
            if (cellWorld < 0)
            {
                return false;
            }
            return (uint)workerCell < (uint)Grid.WorldIdx.Length && Grid.WorldIdx[workerCell] == cellWorld;
        }

        /// <summary>
        /// 全星系判断目标格是否为生效装瓶器所在格（供全星系机械臂隔空取水，0 GC、O(1) 极速判定）。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
        internal static bool IsActiveBottlerCell(int cell)
        {
            short[] arr = _bottlerCellWorld;
            return arr != null && (uint)cell < (uint)arr.Length && arr[cell] >= 0;
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
