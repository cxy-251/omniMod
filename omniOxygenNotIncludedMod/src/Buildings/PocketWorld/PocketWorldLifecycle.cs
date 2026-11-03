using System;
using System.Collections.Generic;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>创建、接管和回收只作用于舱体自身持有的 WorldContainer。</summary>
    internal static class PocketWorldLifecycle
    {
        internal static WorldContainer ResolveOrCreate(OmniPocketHabitat owner)
        {
            ClusterManager manager = ClusterManager.Instance;
            if (manager == null || PocketWorldRegistry.IsShuttingDown) return null;
            WorldContainer ownWorld = owner.GetComponent<WorldContainer>();
            if (ownWorld != null && ownWorld.id >= 0)
            {
                // 旧存档的编号可以修复，但只能接管同一个建筑上的组件，不能按旧编号接管别的星球。
                if (manager.GetWorld(ownWorld.id) != ownWorld || ownWorld.id >= ClusterManager.INVALID_WORLD_IDX) return null;
                int previousId = owner.targetWorldId;
                owner.targetWorldId = ownWorld.id;
                if (!PocketWorldRegistry.TryBind(owner, ownWorld)) { owner.targetWorldId = previousId; return null; }
                if (previousId != ownWorld.id) ModLog.Info($"[生态舱] 已按自身组件修复世界引用：{previousId} → {ownWorld.id}。");
                return ownWorld;
            }
            if (owner.targetWorldId >= 0)
            {
                ModLog.Throttle("pocket_owner_missing_" + owner.GetInstanceID(),
                    "[生态舱] 存档引用缺少自身世界组件，停止自动创建或接管其它世界。", 10f);
                return null;
            }
            // 反序列化尚未完成时，不能把未恢复的编号和矩形当作空闲区域。
            if (App.isLoading || KMonoBehaviour.isLoadingScene) return null;
            if (!PocketWorldRegistry.TryGetAvailableId(out int worldId))
            {
                ModLog.Throttle("pocket_ids_full", "[生态舱] 世界编号已用尽，停止创建内部空间。", 10f);
                return null;
            }
            if (!OmniPocketSpaceManager.TryReserve(out var space, out string reason))
            {
                ModLog.Throttle("pocket_space_full", "[生态舱] 暂不能创建内部空间：" + reason, 10f);
                return null;
            }
            using (space)
            {
                owner.gameObject.AddOrGet<WorldInventory>();
                WorldContainer world = owner.gameObject.AddOrGet<WorldContainer>();
                world.SetRocketInteriorWorldDetails(worldId, space.Size, space.Offset);
                owner.targetWorldId = worldId;
                owner.MarkWorldCreated();
                space.Initialize(world);
                if (!PocketWorldRegistry.TryBind(owner, world)) throw new InvalidOperationException("新世界归属绑定失败");
                ModLog.Info($"[生态舱] 已创建 World {worldId}，Offset {space.Offset}，Size {space.Size}。");
                return world;
            }
        }

        internal static bool TryGetExterior(OmniPocketHabitat owner, WorldContainer interior, out WorldContainer exterior)
        {
            exterior = null;
            int cell = Grid.PosToCell(owner);
            if (!Grid.IsValidCell(cell)) return false;
            int id = Grid.WorldIdx[cell];
            if (id == interior.id || id == ClusterManager.INVALID_WORLD_IDX || ClusterManager.Instance == null) return false;
            exterior = ClusterManager.Instance.GetWorld(id);
            return PocketWorldBounds.TryGet(exterior, out PocketWorldBounds bounds) && bounds.Owns(cell);
        }

        internal static bool Prepare(OmniPocketHabitat owner, WorldContainer world)
        {
            if (!PocketWorldBounds.TryValidate(owner, world, out _, out string reason) ||
                !TryGetExterior(owner, world, out WorldContainer exterior))
            {
                ModLog.Throttle("pocket_prepare_" + owner.GetInstanceID(), "[生态舱] 世界初始化暂停：" + (reason ?? "舱外世界无效"), 10f);
                return false;
            }
            world.SetParentIdx(exterior.id);
            return PocketWorldLayout.Configure(owner, world);
        }

        internal static bool Destroy(OmniPocketHabitat owner, WorldContainer world, NavTeleporter exteriorDoor)
        {
            if (world == null || ClusterManager.Instance == null) return false;
            // 即便区域已损坏，也只注销本建筑的组件，绝不按旧编号注销其它星球。
            bool ownsComponent = world.GetComponent<OmniPocketHabitat>() == owner &&
                ClusterManager.Instance.GetWorld(world.id) == world;
            if (!ownsComponent) return false;
            int worldId = world.id;
            owner.targetWorldId = worldId;
            bool released = false;
            try
            {
                if (!PocketWorldBounds.TryValidate(owner, world, out PocketWorldBounds bounds, out string reason) ||
                    !TryGetExterior(owner, world, out WorldContainer exterior))
                {
                    ModLog.Warn("[生态舱] 拆除时跳过地形及物资清理：" + (reason ?? "舱外世界无效"));
                    return false;
                }
                if (ClusterManager.Instance.activeWorldId == worldId) ClusterManager.Instance.SetActiveWorld(exterior.id);
                exteriorDoor?.Deregister();
                Vector3 eject = owner.transform.position;
                world.EjectAllDupes(eject);
                world.CancelChores();
                PocketWorldLayout.DetachForeignZoneReference(world, bounds);
                world.DestroyWorldBuildings(out HashSet<int> noRefundTiles);
                if (noRefundTiles == null) noRefundTiles = new HashSet<int>();
                // DestroyWorldBuildings 已调用 ClearWorldZones，不重复清理。
                for (int y = bounds.Offset.y; y < bounds.MaxY; y++)
                    for (int x = bounds.Offset.x; x < bounds.MaxX; x++)
                    {
                        int cell = Grid.XYToCell(x, y);
                        if (bounds.Owns(cell) && Grid.Element[cell]?.id == SimHashes.Unobtanium) noRefundTiles.Add(cell);
                    }
                RemoveLegacyNeutronium(bounds);
                world.TransferResourcesToParentWorld(eject + new Vector3(0f, 0.5f, 0f), noRefundTiles);
                if (PocketWorldBounds.TryValidate(owner, world, out bounds, out reason))
                {
                    bounds.Release();
                    released = true;
                }
                else ModLog.Warn("[生态舱] 清理期间格子归属发生变化，保留该区域：" + reason);
                return released;
            }
            finally
            {
                if (ClusterManager.Instance != null && ClusterManager.Instance.GetWorld(worldId) == world)
                    ClusterManager.Instance.UnregisterWorldContainer(world);
                PocketWorldRegistry.Unbind(owner, worldId);
                OmniPocketHabitatPatches.CleanupDestroyedWorldTrackersAndDiagnostics(worldId);
                owner.targetWorldId = -1;
                if (released) ModLog.Info($"[生态舱] 已回收 World {worldId} 的自有区域并退回舱外物资。");
            }
        }

        private static void RemoveLegacyNeutronium(PocketWorldBounds bounds)
        {
            var entries = new List<ScenePartitionerEntry>();
            GameScenePartitioner.Instance.GatherEntries(bounds.Offset.x, bounds.Offset.y, bounds.Size.x, bounds.Size.y,
                GameScenePartitioner.Instance.pickupablesLayer, entries);
            foreach (ScenePartitionerEntry entry in entries)
            {
                Pickupable pickup = entry.obj as Pickupable;
                if (pickup == null || !bounds.Owns(Grid.PosToCell(pickup)) || pickup.PrimaryElement == null ||
                    pickup.PrimaryElement.ElementID != SimHashes.Unobtanium || pickup.KPrefabID == null ||
                    pickup.KPrefabID.PrefabTag != pickup.PrimaryElement.Element.tag) continue;
                Util.KDestroyGameObject(pickup.gameObject);
            }
        }
    }
}
