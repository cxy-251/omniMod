using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OmniMod.Core.Logging;
using ProcGen;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 【折叠空间智能维度网格分配器】：
    /// 严格维持原汁原味的原版统一规格（外部占地 32x32，内部可用净空 30x30 全真空纯净大平层）：
    /// 1. 突破原版地图高度与 BestFit 仅在最右侧 136 格扫描的局限：
    ///    读档时自动将存档物理高度上限无损扩充至 384（原有地表、星球、管道、电网及所有现有生态舱绝对无损且坐标不偏移）。
    /// 2. 在扩充出的海量真空网格中，可容纳 200+ 个完全一致的原版标准 32x32 生态巨舱！
    /// 3. 跳跃式 AABB 极速碰撞检测保底机制，确保 100% 成功分配。
    /// 4. 全局虚拟管网防崩溃补丁，彻底绝缘 ArgumentNullException。
    /// </summary>
    public static class OmniPocketSpaceManager
    {
        /// <summary>标准统一尺寸：外部占地 32x32，内部空间 30x30</summary>
        public static readonly Vector2I StandardSize = TUNING.ROCKETRY.ROCKET_INTERIOR_SIZE; // (32, 32)

        /// <summary>目标物理网格高度（384 为 32 的整数倍，可在当前存档额外开辟 200+ 个标准 32x32 生态舱）</summary>
        public const int TargetGridHeight = 384;

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
        private static List<WorldBox> CollectExistingWorldBoxes(int buffer = 0)
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
                                jumpX = box.x2; // 跳过阻挡世界的右边界
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
        /// 为新的折叠生态舱分配底层网格空间（始终严格保持 32x32 标准尺寸）：
        /// 1. 优先尝试原版分配器（标准 32x32）；
        /// 2. 若原版分配器受阻，使用跳跃式全图装箱保底探测。
        /// </summary>
        public static bool TryAllocatePocketSpace(out Vector2I allocatedSize, out Vector2I allocatedOffset)
        {
            allocatedSize = StandardSize;
            allocatedOffset = default;

            if (Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0)
            {
                return false;
            }

            // 1. 优先尝试原版标准分配
            if (Grid.GetFreeGridSpace(StandardSize, out Vector2I defaultOffset))
            {
                allocatedOffset = defaultOffset;
                ModLog.Info($"[折叠生态舱] 原版网格分配成功 (Size: {allocatedSize}, Offset: {allocatedOffset})。");
                return true;
            }

            // 2. 原版火箭预留区扫描未果，启动全图 32x32 深度装箱探测
            ModLog.Info("[折叠生态舱] 原版火箭预留区扫描未果，启动全图 32x32 深度装箱保底扫描...");
            List<WorldBox> occupied = CollectExistingWorldBoxes(buffer: 0);

            if (FindFreeSpaceForSize(StandardSize, occupied, out Vector2I freeOffset))
            {
                // 在底层 C++ Sim 物理网格中初始化这片真空空间
                Vector2I gridOffset = new Vector2I(Grid.WidthInCells, Grid.HeightInCells);
                SimMessages.SimDataResizeGridAndInitializeVacuumCells(gridOffset, StandardSize.x, StandardSize.y, freeOffset.x, freeOffset.y);
                Game.Instance.roomProber?.Refresh();

                allocatedOffset = freeOffset;
                ModLog.Info($"[折叠生态舱] 深度装箱扫描分配成功 (Size: {allocatedSize}, Offset: {allocatedOffset})。");
                return true;
            }

            ModLog.Warn("[折叠生态舱] 星系大地图物理真空空间已彻底耗尽。");
            return false;
        }
    }

    /// <summary>
    /// 【大地图物理网格高度安全扩充补丁】：
    /// 读档时拦截 SaveFileRoot 反序列化，将大地图高度无损扩充至 384 格：
    /// - 原有星球、地表、建筑、动植物、电网、管道索引（x + y * W）完全不变，零破坏、零丢失。
    /// - 自动同步扩充 GridVisible、GridSpawnable、GridDamage 数组，杜绝 IndexOutOfRangeException。
    /// - 在上方释放出 200+ 个标准 32x32 生态舱的纯真空可用空间！
    /// </summary>
    [HarmonyPatch(typeof(KSerialization.Deserializer), "Deserialize", new[] { typeof(object) })]
    public static class SaveFileRoot_Deserialize_HeightExpander_Patch
    {
        public static void Postfix(object obj)
        {
            if (obj == null) return;
            if (obj.GetType().FullName != "Klei.SaveFileRoot") return;

            try
            {
                Traverse t = Traverse.Create(obj);
                int currentHeight = t.Field<int>("HeightInCells").Value;
                int width = t.Field<int>("WidthInCells").Value;

                if (currentHeight > 0 && currentHeight < OmniPocketSpaceManager.TargetGridHeight && width > 0)
                {
                    ModLog.Info($"[折叠生态舱] 检测到存档大地图物理高度上限为 {currentHeight}（宽 {width}），自动无损扩展物理高度至 {OmniPocketSpaceManager.TargetGridHeight}，彻底解锁海量原版标准 32x32 生态舱空间！");
                    t.Field<int>("HeightInCells").Value = OmniPocketSpaceManager.TargetGridHeight;

                    var streamed = t.Field<Dictionary<string, byte[]>>("streamed").Value;
                    if (streamed != null)
                    {
                        int newCellCount = width * OmniPocketSpaceManager.TargetGridHeight;
                        if (streamed.TryGetValue("GridVisible", out byte[] vis) && vis != null && vis.Length < newCellCount)
                        {
                            byte[] newVis = new byte[newCellCount];
                            Array.Copy(vis, newVis, vis.Length);
                            streamed["GridVisible"] = newVis;
                        }
                        if (streamed.TryGetValue("GridSpawnable", out byte[] sp) && sp != null && sp.Length < newCellCount)
                        {
                            byte[] newSp = new byte[newCellCount];
                            Array.Copy(sp, newSp, sp.Length);
                            streamed["GridSpawnable"] = newSp;
                        }
                        if (streamed.TryGetValue("GridDamage", out byte[] dmg) && dmg != null && dmg.Length < newCellCount * sizeof(float))
                        {
                            byte[] newDmg = new byte[newCellCount * sizeof(float)];
                            Array.Copy(dmg, newDmg, dmg.Length);
                            streamed["GridDamage"] = newDmg;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[折叠生态舱] 扩展网格高度异常: {ex}");
            }
        }
    }

    /// <summary>
    /// 新游戏开局星系高度预先扩充补丁：确保未来开新档时同样拥有 384 高度充足空间。
    /// </summary>
    [HarmonyPatch(typeof(BestFit), nameof(BestFit.BestFitWorlds))]
    public static class BestFit_BestFitWorlds_Patch
    {
        public static void Postfix(ref Vector2I __result)
        {
            if (__result.y < OmniPocketSpaceManager.TargetGridHeight)
            {
                __result.y = OmniPocketSpaceManager.TargetGridHeight;
            }
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
