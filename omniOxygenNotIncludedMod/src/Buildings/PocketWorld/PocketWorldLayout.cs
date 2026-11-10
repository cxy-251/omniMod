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
            {
                for (int x = bounds.Offset.x; x < bounds.MaxX; x++)
                {
                    int cell = Grid.XYToCell(x, y);
                    Grid.PreventFogOfWarReveal[cell] = false;
                    Grid.Reveal(cell, byte.MaxValue, forceReveal: true);
                }
            }
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
            if (world == null) return null;
            if (!PocketWorldBounds.TryGet(world, out PocketWorldBounds bounds)) return null;

            int canonicalCell = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 2);
            var doors = new List<ClustercraftInteriorDoor>();
            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && !Util.IsNullOrDestroyed(door) && ClusterUtil.GetMyWorldId(door) == world.id)
                {
                    doors.Add(door);
                }
            }

            if (doors.Count == 0) return null;
            if (doors.Count == 1) return doors[0];

            doors.Sort((a, b) =>
            {
                int cellA = Grid.PosToCell(a);
                int cellB = Grid.PosToCell(b);
                int distA = Math.Abs(Grid.CellToXY(cellA).x - (bounds.Offset.x + 1)) + Math.Abs(Grid.CellToXY(cellA).y - (bounds.Offset.y + 2));
                int distB = Math.Abs(Grid.CellToXY(cellB).x - (bounds.Offset.x + 1)) + Math.Abs(Grid.CellToXY(cellB).y - (bounds.Offset.y + 2));
                return distA.CompareTo(distB);
            });
            return doors[0];
        }

        internal static ClustercraftInteriorDoor FindAndRestoreDoor(OmniPocketHabitat owner, WorldContainer world)
        {
            if (world == null) return null;
            if (!PocketWorldBounds.TryGet(world, out PocketWorldBounds bounds)) return null;

            int canonicalCell = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 2);
            var doorsInWorld = new List<ClustercraftInteriorDoor>();
            foreach (ClustercraftInteriorDoor door in Components.ClusterCraftInteriorDoors.Items)
            {
                if (door != null && !Util.IsNullOrDestroyed(door))
                {
                    int cell = Grid.PosToCell(door);
                    if (bounds.Owns(cell) || ClusterUtil.GetMyWorldId(door) == world.id)
                    {
                        doorsInWorld.Add(door);
                    }
                }
            }

            // 1. 优先检查标准位置 canonicalCell 是否已经有原版门
            ClustercraftInteriorDoor canonicalDoor = null;
            foreach (var door in doorsInWorld)
            {
                if (Grid.PosToCell(door) == canonicalCell)
                {
                    canonicalDoor = door;
                    break;
                }
            }
            if (canonicalDoor == null && Grid.IsValidCell(canonicalCell))
            {
                GameObject b = Grid.Objects[canonicalCell, (int)ObjectLayer.Building];
                if (b != null)
                {
                    canonicalDoor = b.GetComponent<ClustercraftInteriorDoor>();
                }
            }

            // 2. 运行期清理所有不在标准门位的多余或错位门
            if (!App.isLoading && !KMonoBehaviour.isLoadingScene)
            {
                foreach (var unwanted in doorsInWorld)
                {
                    if (unwanted != null && unwanted != canonicalDoor && !Util.IsNullOrDestroyed(unwanted))
                    {
                        Components.ClusterCraftInteriorDoors.Remove(unwanted);
                        unwanted.gameObject.SetActive(false);
                        Util.KDestroyGameObject(unwanted.gameObject);
                        ModLog.Info($"[生态舱] 已安全清理 World {world.id} 错位舱门 (cell {Grid.PosToCell(unwanted)})。");
                    }
                }
                ClearLeftoverDoorDebris(world, bounds);
            }

            // 3. 若标准门已存在，确保其处于激活状态并注册进系统列表
            if (canonicalDoor != null && !Util.IsNullOrDestroyed(canonicalDoor))
            {
                if (!canonicalDoor.gameObject.activeSelf) canonicalDoor.gameObject.SetActive(true);
                if (Components.ClusterCraftInteriorDoors != null && !Components.ClusterCraftInteriorDoors.Items.Contains(canonicalDoor))
                {
                    Components.ClusterCraftInteriorDoors.Add(canonicalDoor);
                }
                ClearLeftoverDoorDebris(world, bounds);
                return canonicalDoor;
            }

            // 4. 若标准门位尚无门，在游戏运行期强制清除阻碍并原位重建标准舱门
            if (!App.isLoading && !KMonoBehaviour.isLoadingScene)
            {
                ClearCanonicalDoorFootprint(bounds, canonicalCell);

                GameObject prefab = Assets.GetPrefab("ClustercraftInteriorDoor");
                if (prefab != null)
                {
                    GameObject instance = GameUtil.KInstantiate(prefab, Grid.CellToPosCBC(canonicalCell, Grid.SceneLayer.Building), Grid.SceneLayer.Building);
                    PrimaryElement primary = instance.GetComponent<PrimaryElement>();
                    if (primary != null)
                    {
                        primary.SetElement(SimHashes.Steel);
                        primary.Temperature = 294.15f;
                        primary.Units = 1f;
                    }
                    instance.SetActive(true);
                    ClustercraftInteriorDoor restoredDoor = instance.GetComponent<ClustercraftInteriorDoor>();
                    ModLog.Info($"[生态舱] 成功归正舱门！已在 World {world.id} 原始标准门位 (cell {canonicalCell}) 重建原版舱内门。");
                    ClearLeftoverDoorDebris(world, bounds);
                    return restoredDoor;
                }
            }

            ClearLeftoverDoorDebris(world, bounds);
            return doorsInWorld.Count > 0 ? doorsInWorld[0] : null;
        }

        internal static void ClearLeftoverDoorDebris(WorldContainer world, PocketWorldBounds bounds)
        {
            if (world == null || App.isLoading || KMonoBehaviour.isLoadingScene) return;

            // 1. 舱门区域严格仅保留 2 格钢材（位于标准门位 canonicalCell 与 upperCell）
            // 用户明确要求：原来的钢就两格，这两格钢就是在门自身的区域，绝不乱加或外延至其他格子
            int doorCellBottom = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 2);
            int doorCellTop = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 3);

            if (bounds.Owns(doorCellBottom) && (!Grid.Solid[doorCellBottom] || Grid.Element[doorCellBottom].id != SimHashes.Steel))
            {
                SimMessages.ReplaceElement(doorCellBottom, SimHashes.Steel, CellEventLogger.Instance.Scenario, 200f, 293.15f);
                Pathfinding.Instance?.AddDirtyNavGridCell(doorCellBottom);
            }
            if (bounds.Owns(doorCellTop) && (!Grid.Solid[doorCellTop] || Grid.Element[doorCellTop].id != SimHashes.Steel))
            {
                SimMessages.ReplaceElement(doorCellTop, SimHashes.Steel, CellEventLogger.Instance.Scenario, 200f, 293.15f);
                Pathfinding.Instance?.AddDirtyNavGridCell(doorCellTop);
            }

            // 2. 撤销此前误在门口地面 (x+2, y+1) 与门下方 (x+1, y+1) 额外添加的多余钢块，恢复为真空，杜绝 4 格钢
            int outsideCell = Grid.XYToCell(bounds.Offset.x + 2, bounds.Offset.y + 1);
            if (bounds.Owns(outsideCell) && Grid.Solid[outsideCell] && Grid.Element[outsideCell].id == SimHashes.Steel)
            {
                GameObject b = Grid.Objects[outsideCell, (int)ObjectLayer.Building];
                if (b == null)
                {
                    SimMessages.ReplaceElement(outsideCell, SimHashes.Vacuum, CellEventLogger.Instance.Scenario, 0f);
                    Pathfinding.Instance?.AddDirtyNavGridCell(outsideCell);
                }
            }

            int underCell = Grid.XYToCell(bounds.Offset.x + 1, bounds.Offset.y + 1);
            if (bounds.Owns(underCell) && Grid.Solid[underCell] && Grid.Element[underCell].id == SimHashes.Steel)
            {
                GameObject b = Grid.Objects[underCell, (int)ObjectLayer.Building];
                if (b == null)
                {
                    SimMessages.ReplaceElement(underCell, SimHashes.Vacuum, CellEventLogger.Instance.Scenario, 0f);
                    Pathfinding.Instance?.AddDirtyNavGridCell(underCell);
                }
            }

            // 3. 仅在舱门足迹格子内清理散落的钢材掉落物（Pickupables），绝不替换任何固体方块（Grid.Solid）
            int exitCell = Grid.OffsetCell(doorCellBottom, new CellOffset(1, 0));
            int[] doorCells = new[] { doorCellBottom, doorCellTop, exitCell };
            foreach (int c in doorCells)
            {
                if (bounds.Owns(c))
                {
                    CleanCellSteelPickupables(world, c);
                }
            }
        }

        private static void CleanCellSteelPickupables(WorldContainer world, int cell)
        {
            GameObject head = Grid.Objects[cell, (int)ObjectLayer.Pickupables];
            if (head != null)
            {
                Pickupable p = head.GetComponent<Pickupable>();
                if (p != null)
                {
                    ObjectLayerListItem item = p.objectLayerListItem;
                    while (item != null)
                    {
                        var curr = item.gameObject;
                        var next = item.nextItem;
                        if (curr != null)
                        {
                            Pickupable pick = curr.GetComponent<Pickupable>();
                            if (pick != null && IsSteelDebris(pick))
                            {
                                ModLog.Info($"[生态舱] 已自动清理 World {(world != null ? world.id : -1)} 舱内旧门位格子散落钢材 (cell {cell})。");
                                Util.KDestroyGameObject(curr);
                            }
                        }
                        item = next;
                    }
                }
            }
        }

        private static bool IsSteelDebris(Pickupable p)
        {
            if (p == null) return false;
            PrimaryElement pe = p.GetComponent<PrimaryElement>();
            if (pe != null && pe.ElementID == SimHashes.Steel) return true;
            KPrefabID kpid = p.GetComponent<KPrefabID>();
            if (kpid != null && (kpid.PrefabTag == GameTags.Steel || kpid.PrefabTag == "Steel" || kpid.HasTag(SimHashes.Steel.CreateTag()) || kpid.HasTag(GameTags.Steel))) return true;
            if (p.name != null && p.name.IndexOf("Steel", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static void ClearCanonicalDoorFootprint(PocketWorldBounds bounds, int canonicalCell)
        {
            int upperCell = Grid.OffsetCell(canonicalCell, new CellOffset(0, 1));
            int exitCell = Grid.OffsetCell(canonicalCell, new CellOffset(1, 0));

            // 门自身两格（canonicalCell 与 upperCell）：严格作为舱内唯一的 2 格坚固钢块
            int[] doorCells = new[] { canonicalCell, upperCell };
            foreach (int cell in doorCells)
            {
                if (!bounds.Owns(cell)) continue;

                // 若有非门建筑阻挡，清理之
                GameObject b = Grid.Objects[cell, (int)ObjectLayer.Building];
                if (b != null && b.GetComponent<ClustercraftInteriorDoor>() == null)
                {
                    Util.KDestroyGameObject(b);
                }

                if (!Grid.Solid[cell] || Grid.Element[cell].id != SimHashes.Steel)
                {
                    SimMessages.ReplaceElement(cell, SimHashes.Steel, CellEventLogger.Instance.Scenario, 200f, 293.15f);
                    Pathfinding.Instance?.AddDirtyNavGridCell(cell);
                }
            }

            // 门口站立格（exitCell）：小人进出门立足点，必须保持通畅
            if (bounds.Owns(exitCell))
            {
                GameObject b = Grid.Objects[exitCell, (int)ObjectLayer.Building];
                if (b != null && b.GetComponent<ClustercraftInteriorDoor>() == null)
                {
                    Util.KDestroyGameObject(b);
                }

                if (Grid.Solid[exitCell])
                {
                    SimMessages.ReplaceElement(exitCell, SimHashes.Vacuum, CellEventLogger.Instance.Scenario, 0f);
                    Pathfinding.Instance?.AddDirtyNavGridCell(exitCell);
                }
            }
        }

        internal static ClustercraftInteriorDoor EnsureDoor(OmniPocketHabitat owner, WorldContainer world)
        {
            // 场景加载/反序列化期间严禁补门/移动门，防止因内门尚未 Spawn 导致误判
            if (App.isLoading || KMonoBehaviour.isLoadingScene)
            {
                return FindDoor(world);
            }

            return FindAndRestoreDoor(owner, world);
        }

        private static ClustercraftInteriorDoor SpawnDoor(WorldContainer world, PocketWorldBounds bounds, int canonicalCell)
        {
            GameObject prefab = Assets.GetPrefab("ClustercraftInteriorDoor");
            OccupyArea area = prefab != null ? prefab.GetComponent<OccupyArea>() : null;
            if (area == null || area.OccupiedCellsOffsets == null) return null;
            int anchor = canonicalCell;
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
            ModLog.Info($"[生态舱] 容灾保底：已补齐 World {world.id} 的舱门。");
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
                if (!bounds.Owns(cell)) return false;
                if (Grid.Solid[cell] && Grid.Element[cell].id != SimHashes.Steel) return false;
                GameObject b = Grid.Objects[cell, (int)ObjectLayer.Building];
                if (b != null && b.GetComponent<ClustercraftInteriorDoor>() == null) return false;
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
                foreach (ClustercraftInteriorDoor extra in extras)
                {
                    if (extra != null && !Util.IsNullOrDestroyed(extra))
                    {
                        Components.ClusterCraftInteriorDoors.Remove(extra);
                        extra.gameObject.SetActive(false);
                        Util.KDestroyGameObject(extra.gameObject);
                    }
                }
            }
            return true;
        }

    }
}
