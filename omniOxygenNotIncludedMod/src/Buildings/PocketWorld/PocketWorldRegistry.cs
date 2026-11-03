using System.Collections.Generic;
using HarmonyLib;

namespace OmniMod.Buildings
{
    /// <summary>舱体与其自身 WorldContainer 一一绑定；编号只用于查找，不构成所有权证明。</summary>
    internal static class PocketWorldRegistry
    {
        private sealed class Binding
        {
            internal OmniPocketHabitat Owner;
            internal WorldContainer World;
        }
        private static readonly Dictionary<int, Binding> Worlds = new Dictionary<int, Binding>();
        internal static bool IsShuttingDown { get; private set; }

        internal static WorldContainer Resolve(OmniPocketHabitat owner)
        {
            if (IsShuttingDown || owner == null || owner.targetWorldId < 0 || owner.targetWorldId >= ClusterManager.INVALID_WORLD_IDX) return null;
            if (Worlds.TryGetValue(owner.targetWorldId, out Binding binding) && IsValid(owner.targetWorldId, binding) && binding.Owner == owner)
                return binding.World;
            WorldContainer world = owner.GetComponent<WorldContainer>();
            return TryBind(owner, world) ? world : null;
        }

        private static bool IsValid(int worldId, Binding binding) => binding.Owner != null && binding.World != null &&
            binding.World.id == worldId && binding.Owner.targetWorldId == worldId &&
            binding.World.GetComponent<OmniPocketHabitat>() == binding.Owner &&
            IsSupportedInterior(binding.World);

        private static bool IsSupportedInterior(WorldContainer world) =>
            PocketWorldBounds.TryGet(world, out var bounds) && bounds.Size.x >= 3 && bounds.Size.y >= 3 &&
            bounds.Size.x <= OmniPocketSpaceManager.StandardSize.x && bounds.Size.y <= OmniPocketSpaceManager.StandardSize.y &&
            world.GetComponent<Clustercraft>() == null;

        internal static bool TryBind(OmniPocketHabitat owner, WorldContainer world)
        {
            if (IsShuttingDown || owner == null || world == null || owner.targetWorldId != world.id ||
                world.GetComponent<OmniPocketHabitat>() != owner || !IsSupportedInterior(world) ||
                ClusterManager.Instance == null || ClusterManager.Instance.GetWorld(world.id) != world) return false;
            foreach (WorldContainer other in ClusterManager.Instance.WorldContainers)
                if (other != null && other != world && other.id == world.id) return false;
            if (Worlds.TryGetValue(world.id, out Binding old) && IsValid(world.id, old) && old.Owner != owner) return false;
            Worlds[world.id] = new Binding { Owner = owner, World = world };
            return true;
        }

        internal static OmniPocketHabitat GetOwner(int worldId)
        {
            if (worldId < 0 || worldId >= ClusterManager.INVALID_WORLD_IDX) return null;
            if (Worlds.TryGetValue(worldId, out Binding binding))
            {
                if (IsValid(worldId, binding)) return binding.Owner;
                Worlds.Remove(worldId);
            }
            WorldContainer world = ClusterManager.Instance != null ? ClusterManager.Instance.GetWorld(worldId) : null;
            OmniPocketHabitat owner = world != null ? world.GetComponent<OmniPocketHabitat>() : null;
            return TryBind(owner, world) ? owner : null;
        }

        internal static int GetExteriorWorldId(int interiorId)
        {
            OmniPocketHabitat owner = GetOwner(interiorId);
            if (owner == null) return -1;
            WorldContainer interior = Resolve(owner);
            return interior != null && PocketWorldLifecycle.TryGetExterior(owner, interior, out WorldContainer exterior) ? exterior.id : -1;
        }

        internal static void CollectInteriors(int exteriorId, List<int> result)
        {
            if (exteriorId < 0 || result == null) return;
            foreach (KeyValuePair<int, Binding> pair in Worlds)
            {
                if (IsValid(pair.Key, pair.Value) && pair.Key != exteriorId &&
                    ClusterUtil.GetMyWorldId(pair.Value.Owner) == exteriorId) result.Add(pair.Key);
            }
        }

        internal static bool TryGetAvailableId(out int worldId)
        {
            worldId = -1;
            ClusterManager manager = ClusterManager.Instance;
            if (!HasAvailableId) return false;
            var used = new bool[ClusterManager.INVALID_WORLD_IDX];
            foreach (WorldContainer world in manager.WorldContainers)
                if (world != null && world.id >= 0 && world.id < used.Length) used[world.id] = true;
            for (int i = 0; i < used.Length; i++) if (!used[i]) { worldId = i; return true; }
            return false;
        }

        internal static bool HasAvailableId => !IsShuttingDown && ClusterManager.Instance != null &&
            ClusterManager.Instance.WorldContainers.Count < ClusterManager.INVALID_WORLD_IDX;

        internal static void Unbind(OmniPocketHabitat owner, int worldId)
        {
            if (Worlds.TryGetValue(worldId, out Binding binding) && binding.Owner == owner) Worlds.Remove(worldId);
        }

        internal static void BeginShutdown() { IsShuttingDown = true; Worlds.Clear(); }
        internal static void Reset() { IsShuttingDown = false; Worlds.Clear(); }
        internal static void InvalidateId(int worldId) => Worlds.Remove(worldId);
        internal static void ClearBindings() => Worlds.Clear();
    }

    [HarmonyPatch(typeof(Game), "OnCleanUp")]
    internal static class Game_OnCleanUp_PocketWorlds_Patch
    {
        private static void Prefix()
        {
            PocketWorldRegistry.BeginShutdown();
            OmniPocketSpaceManager.ClearReservations();
            OmniPocketHabitatPatches.ActiveStorageAccesses.Clear();
            OmniPocketHabitatPatches.ResetPocketDiagnostics();
        }
    }

    [HarmonyPatch(typeof(Game), "OnPrefabInit")]
    internal static class Game_OnPrefabInit_PocketWorlds_Patch
    {
        private static void Prefix() => PocketWorldRegistry.Reset();
    }

    [HarmonyPatch(typeof(WorldContainer), nameof(WorldContainer.SetID))]
    internal static class WorldContainer_SetID_PocketBinding_Patch
    {
        private static void Prefix(WorldContainer __instance, out int __state) => __state = __instance.id;
        private static void Postfix(int __0, int __state)
        {
            PocketWorldRegistry.InvalidateId(__state);
            PocketWorldRegistry.InvalidateId(__0);
        }
    }

    [HarmonyPatch(typeof(ClusterManager), nameof(ClusterManager.RegisterWorldContainer))]
    internal static class ClusterManager_RegisterWorld_PocketBinding_Patch
    {
        private static void Postfix(WorldContainer __0)
        {
            if (__0 != null) PocketWorldRegistry.InvalidateId(__0.id);
        }
    }

    [HarmonyPatch(typeof(ClusterManager), nameof(ClusterManager.UnregisterWorldContainer))]
    internal static class ClusterManager_UnregisterWorld_PocketBinding_Patch
    {
        private static void Postfix(WorldContainer __0)
        {
            if (__0 != null) PocketWorldRegistry.InvalidateId(__0.id);
        }
    }

    [HarmonyPatch(typeof(ClusterManager), nameof(ClusterManager.InitializeWorldGrid))]
    internal static class ClusterManager_InitializeWorldGrid_PocketBinding_Patch
    {
        private static void Postfix() => PocketWorldRegistry.ClearBindings();
    }
}
