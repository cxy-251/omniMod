using System;
using HarmonyLib;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 修复火箭内部舱门被误删/丢失时的致命闪退崩溃（Crash to Desktop）：
    ///
    /// 崩溃原因：
    /// 玩家在火箭内部装修改建、使用拆除工具、或使用沙盒工具时，不小心将火箭内部自带的
    /// 舱门（ClustercraftInteriorDoor）误删或解构。
    /// 当火箭发射（LaunchableRocketCluster / OnLaunch）、着陆（OnLand）或寻路时，
    /// 外部舱门 ClustercraftExteriorDoor 会调用 GetTargetWorld() 和 TargetCell()，
    /// 直接访问 targetDoor.gameObject，抛出致命 NullReferenceException，导致游戏瞬间崩溃闪退。
    ///
    /// 修复策略：
    /// 1. 自动重构与补救绑定：检测到 targetDoor 为空时，自动在火箭内部原位重新生成并绑定一扇新舱门，
    ///    让小人能继续正常出入火箭舱；
    /// 2. 空引用全流程防御拦截：对 GetTargetWorld、OnLaunch、TargetCell 等关键路径增加空值守卫，
    ///    即使在无门状态下点击发射，也绝不会让游戏崩溃。
    /// </summary>
    internal static class RocketDoorFixHelper
    {
        private static readonly AccessTools.FieldRef<ClustercraftExteriorDoor, ClustercraftInteriorDoor> TargetDoorField =
            AccessTools.FieldRefAccess<ClustercraftExteriorDoor, ClustercraftInteriorDoor>("targetDoor");

        private static readonly AccessTools.FieldRef<ClustercraftExteriorDoor, int> TargetWorldIdField =
            AccessTools.FieldRefAccess<ClustercraftExteriorDoor, int>("targetWorldId");

        internal static ClustercraftInteriorDoor GetTargetDoor(ClustercraftExteriorDoor exteriorDoor)
        {
            return exteriorDoor != null ? TargetDoorField(exteriorDoor) : null;
        }

        internal static int GetTargetWorldId(ClustercraftExteriorDoor exteriorDoor)
        {
            return exteriorDoor != null ? TargetWorldIdField(exteriorDoor) : -1;
        }

        internal static void TryRestoreDoor(ClustercraftExteriorDoor exteriorDoor)
        {
            if (exteriorDoor == null) return;
            if (TargetDoorField(exteriorDoor) != null) return;

            int worldId = TargetWorldIdField(exteriorDoor);
            if (worldId < 0 || ClusterManager.Instance == null) return;

            WorldContainer world = ClusterManager.Instance.GetWorld(worldId);
            if (world == null) return;

            // 1. 先检查该世界是否已经有某个 ClustercraftInteriorDoor（可能只是未匹配绑定）
            ClustercraftInteriorDoor foundDoor = null;
            if (Components.ClusterCraftInteriorDoors != null)
            {
                for (int i = 0; i < Components.ClusterCraftInteriorDoors.Count; i++)
                {
                    var door = Components.ClusterCraftInteriorDoors[i];
                    if (door != null && door.GetMyWorldId() == worldId)
                    {
                        foundDoor = door;
                        break;
                    }
                }
            }

            // 2. 如果世界内真的没有门（被玩家误删）：自动生成一扇新的火箭舱门
            if (foundDoor == null)
            {
                foundDoor = SpawnInteriorDoor(world);
            }

            // 3. 重新建立双向连通绑定
            if (foundDoor != null)
            {
                exteriorDoor.SetTarget(foundDoor);
                OmniMod.Core.Logging.ModLog.Info($"检测到火箭内部舱门缺失，已自动重生并恢复连通（世界ID: {worldId}）！");
            }
        }

        private static ClustercraftInteriorDoor SpawnInteriorDoor(WorldContainer world)
        {
            try
            {
                GameObject prefab = Assets.GetPrefab("ClustercraftInteriorDoor");
                if (prefab == null) return null;

                Vector2I min = world.WorldOffset;
                Vector2I size = world.WorldSize;

                // 默认舱门位置：舱底中央偏左（底边上一格）
                int spawnX = min.x + size.x / 2 - 1;
                int spawnY = min.y + 1;

                int targetCell = Grid.XYToCell(spawnX, spawnY);
                if (!Grid.IsValidCell(targetCell))
                {
                    targetCell = Grid.XYToCell(min.x + 2, min.y + 1);
                }

                Vector3 pos = Grid.CellToPosCBC(targetCell, Grid.SceneLayer.Building);
                GameObject go = GameUtil.KInstantiate(prefab, pos, Grid.SceneLayer.Building);
                go.SetActive(true);

                return go.GetComponent<ClustercraftInteriorDoor>();
            }
            catch (Exception ex)
            {
                OmniMod.Core.Logging.ModLog.Error("生成火箭内部舱门失败", ex);
                return null;
            }
        }
    }

    [HarmonyPatch(typeof(ClustercraftExteriorDoor), "GetTargetWorld")]
    internal static class ClustercraftExteriorDoor_GetTargetWorld_Guard_Patch
    {
        private static bool Prefix(ClustercraftExteriorDoor __instance, ref WorldContainer __result)
        {
            if (__instance == null)
            {
                __result = null;
                return false;
            }

            ClustercraftInteriorDoor targetDoor = RocketDoorFixHelper.GetTargetDoor(__instance);
            if (targetDoor != null)
            {
                return true; // 正常情况放行
            }

            RocketDoorFixHelper.TryRestoreDoor(__instance);
            targetDoor = RocketDoorFixHelper.GetTargetDoor(__instance);

            if (targetDoor != null)
            {
                __result = ClusterUtil.GetMyWorld(targetDoor);
                return false;
            }

            // 兜底保护：直接通过 targetWorldId 返回世界容器，杜绝空引用崩溃
            int targetWorldId = RocketDoorFixHelper.GetTargetWorldId(__instance);
            if (targetWorldId >= 0 && ClusterManager.Instance != null)
            {
                __result = ClusterManager.Instance.GetWorld(targetWorldId);
                return false;
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(ClustercraftExteriorDoor), "OnLaunch")]
    internal static class ClustercraftExteriorDoor_OnLaunch_Guard_Patch
    {
        private static bool Prefix(ClustercraftExteriorDoor __instance)
        {
            if (__instance == null) return false;

            RocketDoorFixHelper.TryRestoreDoor(__instance);
            ClustercraftInteriorDoor targetDoor = RocketDoorFixHelper.GetTargetDoor(__instance);

            // 如果此时仍未恢复门，执行安全无崩溃发射设置
            if (targetDoor == null)
            {
                NavTeleporter teleporter = __instance.GetComponent<NavTeleporter>();
                if (teleporter != null)
                {
                    teleporter.EnableTwoWayTarget(false);
                    teleporter.Deregister();
                }

                WorldContainer targetWorld = __instance.GetTargetWorld();
                if (targetWorld != null)
                {
                    targetWorld.SetParentIdx(targetWorld.id);
                }

                OmniMod.Core.Logging.ModLog.Warn("火箭发射拦截空舱门异常，已安全放行发射！");
                return false; // 跳过原方法，不再抛 NRE 崩溃
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ClustercraftExteriorDoor), "TargetCell")]
    internal static class ClustercraftExteriorDoor_TargetCell_Guard_Patch
    {
        private static bool Prefix(ClustercraftExteriorDoor __instance, ref int __result)
        {
            if (__instance == null)
            {
                __result = Grid.InvalidCell;
                return false;
            }

            ClustercraftInteriorDoor targetDoor = RocketDoorFixHelper.GetTargetDoor(__instance);
            if (targetDoor == null)
            {
                RocketDoorFixHelper.TryRestoreDoor(__instance);
                targetDoor = RocketDoorFixHelper.GetTargetDoor(__instance);
                if (targetDoor == null)
                {
                    __result = Grid.InvalidCell;
                    return false;
                }
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(ClustercraftExteriorDoor), "PairWithInteriorDoor")]
    internal static class ClustercraftExteriorDoor_PairWithInteriorDoor_Guard_Patch
    {
        private static void Postfix(ClustercraftExteriorDoor __instance)
        {
            if (__instance != null && RocketDoorFixHelper.GetTargetDoor(__instance) == null)
            {
                RocketDoorFixHelper.TryRestoreDoor(__instance);
            }
        }
    }

    /// <summary>
    /// 原版 ClustercraftInteriorDoor.OnCleanUp 未对 GetComponent&lt;OccupyArea&gt;() 与 GetOccupiedGridCells() 进行空值校验。
    /// 当生态舱或火箭门在尚未完全 Spawn / 反序列化未就绪或被销毁时，访问 null.Length 会引发 NullReferenceException 导致闪退。
    /// 此补丁提供全流程空值防御与安全注销。
    /// </summary>
    [HarmonyPatch(typeof(ClustercraftInteriorDoor), "OnCleanUp")]
    internal static class ClustercraftInteriorDoor_OnCleanUp_Guard_Patch
    {
        private static bool Prefix(ClustercraftInteriorDoor __instance)
        {
            if (__instance == null) return false;
            try
            {
                if (Components.ClusterCraftInteriorDoors != null)
                {
                    Components.ClusterCraftInteriorDoors.Remove(__instance);
                }
                OccupyArea component = __instance.GetComponent<OccupyArea>();
                if (component != null)
                {
                    int[] cells = component.GetOccupiedGridCells();
                    if (cells != null)
                    {
                        foreach (int cell in cells)
                        {
                            if (Grid.IsValidCell(cell))
                            {
                                Grid.HasDoor[cell] = false;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                OmniMod.Core.Logging.ModLog.Warn($"[ClustercraftInteriorDoor.OnCleanUp] 拦截清理异常: {ex.Message}");
            }
            return false;
        }
    }
}
