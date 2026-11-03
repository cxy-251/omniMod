using System;
using System.Collections.Generic;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>先只读验证并预留矩形，再由创建流程初始化；不在探测空位时清空游戏网格。</summary>
    public static class OmniPocketSpaceManager
    {
        public static readonly Vector2I StandardSize = TUNING.ROCKETRY.ROCKET_INTERIOR_SIZE;
        public const int TargetGridHeight = 384;
        private const int WorldGap = 2;
        private static readonly List<Reservation> Reservations = new List<Reservation>();
        private static int _failedWorldCount = -1;
        private static int _failedWidth;
        private static int _failedHeight;
        private static float _retryAfter;

        private readonly struct WorldBox
        {
            internal readonly int X1, Y1, X2, Y2;
            internal WorldBox(Vector2I offset, Vector2I size)
            {
                X1 = offset.x - WorldGap;
                Y1 = offset.y - WorldGap;
                X2 = offset.x + size.x + WorldGap;
                Y2 = offset.y + size.y + WorldGap;
            }
            internal bool Intersects(Vector2I offset, Vector2I size) =>
                offset.x < X2 && offset.x + size.x > X1 && offset.y < Y2 && offset.y + size.y > Y1;
        }

        internal sealed class Reservation : IDisposable
        {
            internal readonly Vector2I Offset;
            internal readonly Vector2I Size;
            private bool _active = true;
            private bool _initialized;

            internal Reservation(Vector2I offset, Vector2I size) { Offset = offset; Size = size; }

            internal void Initialize(WorldContainer world)
            {
                if (!_active || _initialized || world == null || world.id < 0 || world.id >= ClusterManager.INVALID_WORLD_IDX ||
                    world.WorldOffset != Offset || world.WorldSize != Size ||
                    !IsVacant(Offset, Size, out _)) throw new InvalidOperationException("生态舱预留区域已失效");
                // 当前宽高保持不变；只在已确认无人占用的矩形初始化真空。
                SimMessages.SimDataResizeGridAndInitializeVacuumCells(
                    new Vector2I(Grid.WidthInCells, Grid.HeightInCells), Size.x, Size.y, Offset.x, Offset.y);
                for (int y = Offset.y; y < Offset.y + Size.y; y++)
                    for (int x = Offset.x; x < Offset.x + Size.x; x++)
                        Grid.WorldIdx[Grid.XYToCell(x, y)] = (byte)world.id;
                _initialized = true;
                for (int y = Offset.y; y < Offset.y + Size.y; y++)
                    for (int x = Offset.x; x < Offset.x + Size.x; x++)
                        Pathfinding.Instance?.AddDirtyNavGridCell(Grid.XYToCell(x, y));
                Game.Instance?.roomProber?.Refresh();
            }

            public void Dispose()
            {
                if (!_active) return;
                _active = false;
                Reservations.Remove(this);
            }
        }

        private static bool IsVacant(Vector2I offset, Vector2I size, out int blockedX)
        {
            blockedX = offset.x;
            if (!PocketWorldBounds.FitsGrid(offset, size)) return false;
            int layers = Grid.ObjectLayers != null ? Grid.ObjectLayers.Length : 0;
            for (int y = offset.y; y < offset.y + size.y; y++)
            {
                for (int x = offset.x; x < offset.x + size.x; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    blockedX = x;
                    if (Grid.WorldIdx[cell] != ClusterManager.INVALID_WORLD_IDX) return false;
                    // 未分配格可能仍带有零质量的元素/边界标记；按实际质量判断是否存在物质。
                    float mass = Grid.Mass[cell];
                    if (mass > 0f || float.IsNaN(mass) || float.IsInfinity(mass)) return false;
                    for (int layer = 0; layer < layers; layer++)
                        if (Grid.ObjectLayers[layer] != null && Grid.Objects[cell, layer] != null) return false;
                }
            }
            return true;
        }

        internal static bool TryReserve(out Reservation reservation, out string reason)
        {
            reservation = null;
            reason = null;
            ClusterManager manager = ClusterManager.Instance;
            if (manager == null || PocketWorldRegistry.IsShuttingDown || GameScenePartitioner.Instance == null ||
                Grid.ObjectLayers == null || Grid.WidthInCells <= 0 || Grid.HeightInCells <= 0)
            {
                reason = "世界尚未就绪";
                return false;
            }
            if (_failedWorldCount == manager.WorldContainers.Count && _failedWidth == Grid.WidthInCells &&
                _failedHeight == Grid.HeightInCells && Time.realtimeSinceStartup < _retryAfter)
            {
                reason = "暂无安全空位，等待世界变化后重试";
                return false;
            }
            var occupied = new List<WorldBox>(manager.WorldContainers.Count + Reservations.Count);
            foreach (WorldContainer world in manager.WorldContainers)
            {
                if (world == null) continue;
                if (world.id < 0 && world.WorldSize.x == 0 && world.WorldSize.y == 0) continue;
                if (!PocketWorldBounds.FitsGrid(world.WorldOffset, world.WorldSize))
                {
                    reason = $"现有 World {world.id} 的范围无效，停止分配";
                    return false;
                }
                occupied.Add(new WorldBox(world.WorldOffset, world.WorldSize));
            }
            foreach (Reservation pending in Reservations) occupied.Add(new WorldBox(pending.Offset, pending.Size));

            int maxX = Grid.WidthInCells - StandardSize.x;
            int maxY = Grid.HeightInCells - StandardSize.y;
            for (int y = 0; y <= maxY; y++)
            {
                for (int x = 0; x <= maxX;)
                {
                    var candidate = new Vector2I(x, y);
                    int nextX = x;
                    foreach (WorldBox box in occupied)
                        if (box.Intersects(candidate, StandardSize)) nextX = Math.Max(nextX, box.X2);
                    if (nextX > x) { x = nextX; continue; }
                    if (IsVacant(candidate, StandardSize, out int blockedX))
                    {
                        reservation = new Reservation(candidate, StandardSize);
                        Reservations.Add(reservation);
                        _failedWorldCount = -1;
                        return true;
                    }
                    x = Math.Max(x + 1, blockedX + 1);
                }
            }
            _failedWorldCount = manager.WorldContainers.Count;
            _failedWidth = Grid.WidthInCells;
            _failedHeight = Grid.HeightInCells;
            _retryAfter = Time.realtimeSinceStartup + 10f;
            reason = "当前物理网格没有可安全容纳标准舱室的空位";
            return false;
        }

        internal static void ClearReservations()
        {
            Reservations.Clear();
            _failedWorldCount = -1;
            _retryAfter = 0f;
        }
    }
}
