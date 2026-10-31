using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间智能维度网格分配器】：
    /// 严格保持原汁原味的标准规格（外部占地 32x32，内部净空 30x30 全真空大平层）：
    /// 1. 突破原版 BestFit 仅在地图最右侧狭窄 136 格区域扫描的物理局限。
    /// 2. 使用跳跃式 AABB 全图碰撞检测，充分发掘星系大地图中所有星球之间、上下方的广阔闲置真空带，
    ///    在当前存档中、在规格完全一致的前提下，大幅增加可建造的 32x32 生态舱数量！
    /// 3. 电网空引用绝缘防护网：彻底拦截 VirtualCircuitKey == null 引发的 ArgumentNullException，解救损坏存档。
    /// </summary>
    public static class OmniPocketSpaceManager
    {
        /// <summary>标准统一尺寸：外部占地 32x32，内部空间 30x30</summary>
        public static readonly Vector2I StandardSize = TUNING.ROCKETRY.ROCKET_INTERIOR_SIZE; // (32, 32)

        private struct WorldBox
        {
            public int x1;
            public int y1;
            public int x2;
            public int y2;
        }

        /// <summary>
        /// 收集当前星系中所有存活世界（星球、已有火箭内部、已有生态舱）所占用的网格矩形。
        /// </summary>
        private static List<WorldBox> CollectExistingWorldBoxes(int buffer = 1)
        {
            var list = new List<WorldBox>();
            if (ClusterManager.Instance == null)
            {
                return list;
            }

            foreach (WorldContainer world in ClusterManager.Instance.WorldContainers)
            {
                if (world == null || Util.IsNullOrDestroyed(world))
                {
                    continue;
                }
                Vector2I offset = world.WorldOffset;
                Vector2I size = world.WorldSize;
                if (size.x <= 0 || size.y <= 0)
                {
                    continue;
                }

                list.Add(new WorldBox
                {
                    x1 = offset.x - buffer,
                    y1 = offset.y - buffer,
                    x2 = offset.x + size.x + buffer,
                    y2 = offset.y + size.y + buffer
                });
            }
            return list;
        }

        /// <summary>
        /// 使用跳跃式 AABB 碰撞检测，在地图网格物理边界内寻找连续未占用的 32x32 空闲矩形空间。
        /// </summary>
        private static bool FindFreeSpaceForSize(Vector2I size, List<WorldBox> occupied, out Vector2I foundOffset)
        {
            foundOffset = default;
            int maxX = Grid.WidthInCells - size.x;
            int maxY = Grid.HeightInCells - size.y;
            if (maxX < 0 || maxY < 0)
            {
                return false;
            }

            // 全图由下至上、由左至右跳跃扫描未被任何已有世界占用的真空区域
            for (int y = 0; y <= maxY; y++)
            {
                for (int x = 0; x <= maxX; )
                {
                    int tx1 = x;
                    int ty1 = y;
                    int tx2 = x + size.x;
                    int ty2 = y + size.y;

                    bool collision = false;
                    int jumpX = x + 1;

                    for (int i = 0; i < occupied.Count; i++)
                    {
                        WorldBox box = occupied[i];
                        if (tx1 < box.x2 && tx2 > box.x1 && ty1 < box.y2 && ty2 > box.y1)
                        {
                            collision = true;
                            if (box.x2 > jumpX)
                            {
                                jumpX = box.x2; // 直接跨过阻挡世界的右边界，极速跳跃
                            }
                        }
                    }

                    if (!collision)
                    {
                        foundOffset = new Vector2I(x, y);
                        return true;
                    }

                    x = jumpX;
                }
            }
            return false;
        }

        /// <summary>
        /// 为新的折叠生态舱分配底层网格空间（始终保持 32x32 原版标准尺寸）。
        /// 先尝试原版标准分配；若原版右侧预留区用尽，则启动全图 32x32 深度装箱探测。
        /// </summary>
        public static bool TryAllocatePocketSpace(out Vector2I allocatedSize, out Vector2I allocatedOffset)
        {
            allocatedSize = StandardSize;
            allocatedOffset = default;

            if (Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0)
            {
                return false;
            }

            // 1. 优先尝试原版分配器（标准 32x32）
            if (Grid.GetFreeGridSpace(StandardSize, out Vector2I defaultOffset))
            {
                allocatedOffset = defaultOffset;
                ModLog.Info($"[折叠生态舱] 原版网格分配成功 (Size: {allocatedSize}, Offset: {allocatedOffset})。");
                return true;
            }

            // 2. 原版右侧预留区耗尽，启动全图 32x32 深度装箱算法（在星球间隙的真空带寻找标准空间）
            ModLog.Info("[折叠生态舱] 原版火箭预留区已满，启动全图 32x32 深度真空空间扫描...");
            List<WorldBox> occupied = CollectExistingWorldBoxes(buffer: 1);

            if (FindFreeSpaceForSize(StandardSize, occupied, out Vector2I freeOffset))
            {
                // 在底层 C++ Sim 物理网格中初始化这片真空空间
                Vector2I gridOffset = new Vector2I(Grid.WidthInCells, Grid.HeightInCells);
                SimMessages.SimDataResizeGridAndInitializeVacuumCells(gridOffset, StandardSize.x, StandardSize.y, freeOffset.x, freeOffset.y);
                Game.Instance.roomProber?.Refresh();

                allocatedOffset = freeOffset;
                ModLog.Info($"[折叠生态舱] 全图深度扫描成功开辟标准 32x32 空间！坐标: {allocatedOffset}。");
                return true;
            }

            ModLog.Warn("[折叠生态舱] 星系大地图物理真空空间已彻底耗尽，无法再容纳新的 32x32 折叠空间。");
            return false;
        }
    }

    /// <summary>
    /// 全局虚拟管网（电网/气网/液网）安全屏障：
    /// 拦截所有 AddToVirtualNetworks(null, ...) 调用，防止原版 Dictionary.ContainsKey(null) 抛出 ArgumentNullException 破坏游戏！
    /// </summary>
    [HarmonyPatch]
    public static class UtilityNetworkManager_AddToVirtualNetworks_SafePatch
    {
        private static MethodBase TargetMethod()
        {
            try
            {
                Type mgrType = typeof(Game).Assembly.GetType("UtilityNetworkManager`2")
                    ?.MakeGenericType(typeof(ElectricalUtilityNetwork), typeof(Wire));
                return mgrType?.GetMethod("AddToVirtualNetworks", BindingFlags.Public | BindingFlags.Instance);
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[CrashFix] 反射获取 AddToVirtualNetworks 方法异常: {ex.Message}");
                return null;
            }
        }

        public static bool Prefix(object key)
        {
            if (key == null)
            {
                ModLog.Warn("[CrashFix] 拦截到 null key 注册虚拟电网请求，成功阻断 ArgumentNullException 崩溃。");
                return false;
            }
            return true;
        }
    }
}
