using System;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>世界矩形与逐格归属的共同约束；任何破坏性操作都先取得验证过的范围。</summary>
    internal readonly struct PocketWorldBounds
    {
        internal readonly int WorldId;
        internal readonly Vector2I Offset;
        internal readonly Vector2I Size;
        internal int MaxX => Offset.x + Size.x;
        internal int MaxY => Offset.y + Size.y;

        private PocketWorldBounds(WorldContainer world)
        {
            WorldId = world.id;
            Offset = world.WorldOffset;
            Size = world.WorldSize;
        }

        internal static bool FitsGrid(Vector2I offset, Vector2I size)
        {
            return size.x > 0 && size.y > 0 && offset.x >= 0 && offset.y >= 0 &&
                (long)offset.x + size.x <= Grid.WidthInCells && (long)offset.y + size.y <= Grid.HeightInCells &&
                Grid.WorldIdx != null && Grid.WorldIdx.Length >= (long)Grid.WidthInCells * Grid.HeightInCells;
        }

        internal static bool TryGet(WorldContainer world, out PocketWorldBounds bounds)
        {
            bounds = default;
            if (world == null || world.id < 0 || world.id >= ClusterManager.INVALID_WORLD_IDX ||
                !FitsGrid(world.WorldOffset, world.WorldSize)) return false;
            bounds = new PocketWorldBounds(world);
            return true;
        }

        internal bool Contains(int cell)
        {
            if (!Grid.IsValidCell(cell)) return false;
            int x = cell % Grid.WidthInCells;
            int y = cell / Grid.WidthInCells;
            return x >= Offset.x && x < MaxX && y >= Offset.y && y < MaxY;
        }

        internal bool Owns(int cell) => Contains(cell) && Grid.WorldIdx[cell] == WorldId;

        internal bool Overlaps(WorldContainer other)
        {
            Vector2I offset = other.WorldOffset;
            Vector2I size = other.WorldSize;
            return (long)Offset.x < (long)offset.x + size.x && (long)MaxX > offset.x &&
                (long)Offset.y < (long)offset.y + size.y && (long)MaxY > offset.y;
        }

        internal static bool TryValidate(OmniPocketHabitat owner, WorldContainer world,
            out PocketWorldBounds bounds, out string reason)
        {
            bounds = default;
            reason = null;
            ClusterManager manager = ClusterManager.Instance;
            if (owner == null || world == null || manager == null ||
                world.GetComponent<OmniPocketHabitat>() != owner || manager.GetWorld(world.id) != world ||
                owner.targetWorldId != world.id)
            {
                reason = "世界编号与舱体组件不属于同一个实例";
                return false;
            }
            if (!TryGet(world, out bounds) || bounds.Size.x < 3 || bounds.Size.y < 3 ||
                bounds.Size.x > OmniPocketSpaceManager.StandardSize.x || bounds.Size.y > OmniPocketSpaceManager.StandardSize.y)
            {
                reason = "舱室尺寸或坐标超出可用网格";
                return false;
            }
            if (bounds.Contains(Grid.PosToCell(owner)))
            {
                reason = "内部矩形覆盖了舱外入口";
                return false;
            }
            foreach (WorldContainer other in manager.WorldContainers)
            {
                if (other == null || other == world) continue;
                if (other.id == world.id)
                {
                    reason = $"World {world.id} 的编号被多个世界占用";
                    return false;
                }
                if (other.id < 0 && other.WorldSize.x == 0 && other.WorldSize.y == 0) continue;
                if (!FitsGrid(other.WorldOffset, other.WorldSize))
                {
                    reason = $"World {other.id} 的范围尚未就绪";
                    return false;
                }
                if (bounds.Overlaps(other))
                {
                    reason = $"内部矩形与 World {other.id} 重叠";
                    return false;
                }
            }
            for (int y = bounds.Offset.y; y < bounds.MaxY; y++)
            {
                for (int x = bounds.Offset.x; x < bounds.MaxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (Grid.WorldIdx[cell] != world.id)
                    {
                        reason = $"格子 ({x},{y}) 实际属于 World {Grid.WorldIdx[cell]}";
                        return false;
                    }
                }
            }
            return true;
        }

        internal bool ContainsFootprint(OccupyArea area)
        {
            if (area == null) return false;
            int[] cells = area.GetOccupiedGridCells();
            if (cells == null || cells.Length == 0) return false;
            for (int i = 0; i < cells.Length; i++) if (!Owns(cells[i])) return false;
            return true;
        }

        internal void Release()
        {
            // 原版 Grid.FreeGridSpace 还会写入矩形外侧的 Element；这里只释放已经验证的自有范围。
            SimMessages.SimDataFreeCells(Size.x, Size.y, Offset.x, Offset.y);
            Element vacuum = ElementLoader.FindElementByHash(SimHashes.Vacuum);
            for (int y = Offset.y; y < MaxY; y++)
            {
                for (int x = Offset.x; x < MaxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    if (!Owns(cell)) continue;
                    Grid.Element[cell] = vacuum;
                    Grid.WorldIdx[cell] = ClusterManager.INVALID_WORLD_IDX;
                    Pathfinding.Instance?.AddDirtyNavGridCell(cell);
                }
            }
            Game.Instance?.roomProber?.Refresh();
        }
    }
}
