using System;
using System.Collections.Generic;
using Delaunay.Geo;
using Klei;
using OmniMod.Core.Logging;
using ProcGen;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>内部区域、旧模板迁移及舱门修复。修门只生成门，不再覆盖整个世界模板。</summary>
    internal static class PocketWorldLayout
    {
        internal static bool Configure(OmniPocketHabitat owner, WorldContainer world)
        {
            if (!PocketWorldBounds.TryValidate(owner, world, out PocketWorldBounds bounds, out string reason))
            {
                ModLog.Throttle("pocket_zone_" + (world != null ? world.id : -1), "[生态舱] 跳过区域配置：" + reason, 10f);
                return false;
            }
            // 1. 赋予顶部充足阳光（80,000 Lux，满足原版光伏板满功率发电需求）
            world.sunlight = TUNING.FIXEDTRAITS.SUNLIGHT.VERY_HIGH;
            world.sunlightFixedTrait = TUNING.FIXEDTRAITS.SUNLIGHT.NAME.VERY_HIGH;
            OmniPocketHabitatPatches.RefreshTimeOfDaySunlight();

            // 2. 构建覆盖内部 30x30 可建造区域的 OverworldCell 多边形（避免因去除 RocketWallTile 导致全图变成太空暴露）
            Vector2I offset = world.WorldOffset;
            Vector2I size = world.WorldSize;
            var corners = new List<Vector2>(4)
            {
                new Vector2(offset.x + 1f, offset.y + 1f),
                new Vector2(offset.x + size.x - 1f, offset.y + 1f),
                new Vector2(offset.x + size.x - 1f, offset.y + size.y - 1f),
                new Vector2(offset.x + 1f, offset.y + size.y - 1f)
            };
            var poly = new Polygon(corners);

            ref WorldDetailSave.OverworldCell overworldCell = ref OmniPocketHabitatPatches.OverworldCellRef(world);
            DetachForeignZoneReference(world, bounds);
            if (overworldCell == null)
            {
                overworldCell = new WorldDetailSave.OverworldCell();
            }

            overworldCell.poly = poly;
            overworldCell.zoneType = SubWorld.ZoneType.RocketInterior;
            if (overworldCell.tags == null)
            {
                overworldCell.tags = new TagSet { WorldGenTags.RocketInterior };
            }
            else if (!overworldCell.tags.Contains(WorldGenTags.RocketInterior))
            {
                overworldCell.tags.Add(WorldGenTags.RocketInterior);
            }

            var overworldCells = SaveLoader.Instance?.clusterDetailSave?.overworldCells;
            if (overworldCells != null && !overworldCells.Contains(overworldCell))
            {
                overworldCells.Add(overworldCell);
            }

            // 3. 将内部 30x30 格设为 RocketInterior，外圈 1 格边界保持 Space
            for (int y = 0; y < size.y; y++)
            {
                for (int x = 0; x < size.x; x++)
                {
                    int cell = Grid.XYToCell(offset.x + x, offset.y + y);
                    if (!bounds.Owns(cell))
                    {
                        continue;
                    }

                    bool isInterior = x >= 1 && x < size.x - 1 && y >= 1 && y < size.y - 1;
                    byte simZone = isInterior ? (byte)SubWorld.ZoneType.RocketInterior : byte.MaxValue;
                    SubWorld.ZoneType renderZone = isInterior ? SubWorld.ZoneType.RocketInterior : SubWorld.ZoneType.Space;

                    SimMessages.ModifyCellWorldZone(cell, simZone);
                    if (World.Instance?.zoneRenderData?.worldZoneTypes != null && cell < World.Instance.zoneRenderData.worldZoneTypes.Length)
                    {
                        World.Instance.zoneRenderData.worldZoneTypes[cell] = renderZone;
                    }
                }
            }
            for (int y = bounds.Offset.y; y < bounds.MaxY; y++)
                for (int x = bounds.Offset.x; x < bounds.MaxX; x++)
                    Grid.Reveal(Grid.XYToCell(x, y), byte.MaxValue);
            return true;
        }

        internal static void DetachForeignZoneReference(WorldContainer world, PocketWorldBounds bounds)
        {
            ref WorldDetailSave.OverworldCell cell = ref OmniPocketHabitatPatches.OverworldCellRef(world);
            if (cell == null) return;
            bool owned = cell.poly != null && cell.zoneType == SubWorld.ZoneType.RocketInterior;
            if (owned)
            {
                Rect rectangle = cell.poly.bounds;
                owned = rectangle.xMin >= bounds.Offset.x && rectangle.yMin >= bounds.Offset.y &&
                    rectangle.xMax <= bounds.MaxX && rectangle.yMax <= bounds.MaxY;
            }
            if (owned && ClusterManager.Instance != null)
            {
                foreach (WorldContainer other in ClusterManager.Instance.WorldContainers)
                {
                    if (other != null && other != world &&
                        ReferenceEquals(OmniPocketHabitatPatches.OverworldCellRef(other), cell)) { owned = false; break; }
                }
            }
            // 损坏或共享的引用只从本组件解除，保留它在其它世界及全局列表中的内容。
            if (!owned) cell = null;
        }

        internal static ClustercraftInteriorDoor FindDoor(WorldContainer world)
        {
            if (!PocketWorldBounds.TryGet(world, out PocketWorldBounds bounds)) return null;
            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
                if (door != null && bounds.Owns(Grid.PosToCell(door)) &&
                    bounds.ContainsFootprint(door.GetComponent<OccupyArea>())) return door;
            return null;
        }

        internal static ClustercraftInteriorDoor EnsureDoor(OmniPocketHabitat owner, WorldContainer world)
        {
            if (!PocketWorldBounds.TryValidate(owner, world, out PocketWorldBounds bounds, out string reason))
            {
                ModLog.Throttle("pocket_door_" + owner.targetWorldId, "[生态舱] 跳过舱门修复：" + reason, 10f);
                return null;
            }
            ClustercraftInteriorDoor found = FindDoor(world);
            if (found != null) return found;
            GameObject prefab = Assets.GetPrefab("ClustercraftInteriorDoor");
            OccupyArea area = prefab != null ? prefab.GetComponent<OccupyArea>() : null;
            if (area == null || area.OccupiedCellsOffsets == null) return null;
            int anchor = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 2);
            if (!CanPlaceDoor(bounds, area, anchor))
            {
                anchor = Grid.InvalidCell;
                for (int y = bounds.Offset.y + 1; y < bounds.MaxY - 1 && anchor == Grid.InvalidCell; y++)
                    for (int x = bounds.Offset.x + 1; x < bounds.MaxX - 1; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (CanPlaceDoor(bounds, area, cell)) { anchor = cell; break; }
                    }
            }
            if (anchor == Grid.InvalidCell)
            {
                ModLog.Throttle("pocket_door_full_" + world.id, "[生态舱] 舱内没有可安全补门的空位，保留现有建筑和地形。", 10f);
                return null;
            }
            GameObject instance = GameUtil.KInstantiate(prefab, Grid.CellToPosCBC(anchor, Grid.SceneLayer.Building), Grid.SceneLayer.Building);
            PrimaryElement primary = instance.GetComponent<PrimaryElement>();
            if (primary != null)
            {
                primary.SetElement(SimHashes.Steel);
                primary.Temperature = 294.15f;
                primary.Units = 1f;
            }
            instance.SetActive(true);
            ModLog.Info($"[生态舱] 已补齐 World {world.id} 的舱门，未重新盖舱内模板。");
            return instance.GetComponent<ClustercraftInteriorDoor>();
        }

        private static bool CanPlaceDoor(PocketWorldBounds bounds, OccupyArea prefabArea, int anchor)
        {
            if (!bounds.Owns(anchor) || !bounds.Owns(Grid.OffsetCell(anchor, new CellOffset(1, 0))) ||
                !bounds.Owns(Grid.OffsetCell(anchor, new CellOffset(0, -1))) ||
                !bounds.Owns(Grid.OffsetCell(anchor, new CellOffset(1, -1)))) return false;
            foreach (CellOffset offset in prefabArea.OccupiedCellsOffsets)
            {
                int cell = Grid.OffsetCell(anchor, offset);
                if (!bounds.Owns(cell) || Grid.Solid[cell] || Grid.Objects[cell, (int)ObjectLayer.Building] != null) return false;
            }
            int exit = Grid.OffsetCell(anchor, new CellOffset(1, 0));
            return !Grid.Solid[exit] && Grid.Objects[exit, (int)ObjectLayer.Building] == null;
        }

        internal static bool CleanupLegacy(OmniPocketHabitat owner, WorldContainer world)
        {
            if (!PocketWorldBounds.TryValidate(owner, world, out PocketWorldBounds bounds, out string reason))
            {
                ModLog.Throttle("pocket_legacy_" + owner.targetWorldId, "[生态舱] 跳过旧模板清理：" + reason, 10f);
                return false;
            }
            var buildings = new List<ScenePartitionerEntry>();
            GameScenePartitioner.Instance.GatherEntries(bounds.Offset.x, bounds.Offset.y, bounds.Size.x, bounds.Size.y,
                GameScenePartitioner.Instance.completeBuildings, buildings);
            foreach (ScenePartitionerEntry entry in buildings)
            {
                BuildingComplete building = entry.obj as BuildingComplete;
                if (building == null || building.PrefabID() != "RocketWallTile" ||
                    !bounds.ContainsFootprint(building.GetComponent<OccupyArea>())) continue;
                int cell = Grid.PosToCell(building);
                Util.KDestroyGameObject(building.gameObject);
                if (bounds.Owns(cell)) SimMessages.ReplaceElement(cell, SimHashes.Vacuum, CellEventLogger.Instance.Scenario, 0f);
            }
            ClustercraftInteriorDoor primaryDoor = FindDoor(world);
            if (primaryDoor != null)
            {
                var extras = new List<ClustercraftInteriorDoor>();
                foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
                    if (door != null && door != primaryDoor && bounds.Owns(Grid.PosToCell(door)) &&
                        bounds.ContainsFootprint(door.GetComponent<OccupyArea>())) extras.Add(door);
                foreach (ClustercraftInteriorDoor extra in extras) Util.KDestroyGameObject(extra.gameObject);
            }
            return true;
        }

    }
}
