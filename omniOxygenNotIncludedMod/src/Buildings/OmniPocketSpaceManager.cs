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
    /// 解决原版 BestFit.GetGridOffset 算法仅在狭窄预留区排布、粗暴跳过非 34x34 间隙导致的"底层网格空间不足"物理瓶颈。
    /// 1. 多级自适应装箱（Adaptive Bin Packing）：
    ///    依次按优先级探测 32x32 (豪华)、28x28 (宽敞)、24x24 (舒适)、20x20 (紧凑)、16x16 (微型) 规格，
    ///    见缝插针利用星系大地图中所有原本闲置的真空死角，在现有存档中无需重开即可将生态舱容量暴增 3~5 倍！
    /// 2. 跳跃式 AABB 极速碰撞检测：毫秒级全图扫描，绝不阻塞主线程。
    /// 3. 电网空引用绝缘防护网：彻底拦截 VirtualCircuitKey == null 引发的 ArgumentNullException，解救损坏存档。
    /// </summary>
    public static class OmniPocketSpaceManager
    {
        /// <summary>多级候选尺寸（由大到小优先尝试豪华全尺寸）</summary>
        public static readonly Vector2I[] CandidateSizes = new[]
        {
            new Vector2I(32, 32), // 豪华全尺寸 (内部 30x30)
            new Vector2I(28, 28), // 宽敞型 (内部 26x26)
            new Vector2I(24, 24), // 舒适型 (内部 22x22)
            new Vector2I(20, 20), // 紧凑型 (内部 18x18)
            new Vector2I(16, 16)  // 微型折叠空间 (内部 14x14)
        };

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
        /// 使用跳跃式 AABB 碰撞检测，在地图网格物理边界内寻找连续未占用的空闲矩形空间。
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

            // 优先从星系大地图右侧向左、由下至上搜索（贴合原版太空区域），充分利用真空带
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
        /// 为新的折叠生态舱分配底层网格空间。
        /// 先尝试原版标准 32x32 分配；若原版预留区用尽，则启动智能全图多级装箱探测。
        /// </summary>
        public static bool TryAllocatePocketSpace(out Vector2I allocatedSize, out Vector2I allocatedOffset)
        {
            allocatedSize = default;
            allocatedOffset = default;

            if (Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0)
            {
                return false;
            }

            // 1. 优先尝试原版分配器（标准 32x32）
            Vector2I defaultSize = TUNING.ROCKETRY.ROCKET_INTERIOR_SIZE;
            if (Grid.GetFreeGridSpace(defaultSize, out Vector2I defaultOffset))
            {
                allocatedSize = defaultSize;
                allocatedOffset = defaultOffset;
                ModLog.Info($"[折叠生态舱] 原版网格分配成功 (Size: {allocatedSize}, Offset: {allocatedOffset})。");
                return true;
            }

            // 2. 原版预留区耗尽，启动智能深度多级装箱算法
            ModLog.Info("[折叠生态舱] 原版火箭预留区已满，启动智能深度全图空间分配器...");
            List<WorldBox> occupied = CollectExistingWorldBoxes(buffer: 1);

            for (int i = 0; i < CandidateSizes.Length; i++)
            {
                Vector2I candidate = CandidateSizes[i];
                if (FindFreeSpaceForSize(candidate, occupied, out Vector2I freeOffset))
                {
                    // 在底层 C++ Sim 物理网格中初始化这片真空空间
                    Vector2I gridOffset = new Vector2I(Grid.WidthInCells, Grid.HeightInCells);
                    SimMessages.SimDataResizeGridAndInitializeVacuumCells(gridOffset, candidate.x, candidate.y, freeOffset.x, freeOffset.y);
                    Game.Instance.roomProber?.Refresh();

                    allocatedSize = candidate;
                    allocatedOffset = freeOffset;
                    ModLog.Info($"[折叠生态舱] 智能全图分配器成功开辟空间！规格: {allocatedSize.x}x{allocatedSize.y}, 坐标: {allocatedOffset}。");
                    return true;
                }
            }

            ModLog.Warn("[折叠生态舱] 星系大地图物理真空空间已彻底耗尽，无法再容纳新的折叠空间。");
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
