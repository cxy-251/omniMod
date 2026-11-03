using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using OmniMod.Core.Logging;
using OmniMod.Logistics.RemoteFetch;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>登记可取货容器、分片建立不可变快照；只索引本地可取货容器；全局库存归 OmniEntityPool 维护。</summary>
    internal static class WorldStorageIndex
    {
        private static readonly HashSet<int> _dirtyWorlds = new HashSet<int>();

        internal static void MarkDirty(int worldId)
        {
            if (worldId < 0 || worldId == 255) return;
            lock (_dirtyWorlds) _dirtyWorlds.Add(worldId);
        }

        private static readonly Dictionary<int, Dictionary<Tag, List<Pickupable>>> LocalItemsByTag = new Dictionary<int, Dictionary<Tag, List<Pickupable>>>();
        private sealed class WorldIndexBuild
        {
            internal int World;
            internal readonly List<GameObject> Items = new List<GameObject>();
            internal readonly HashSet<Storage> Sources = new HashSet<Storage>();
            internal readonly HashSet<Pickupable> Seen = new HashSet<Pickupable>();
            internal readonly Dictionary<Tag, List<Pickupable>> Tags = new Dictionary<Tag, List<Pickupable>>();
            internal int Cursor;
        }

        private static readonly Queue<int> PendingWorlds = new Queue<int>();
        private static readonly HashSet<int> QueuedWorlds = new HashSet<int>();
        private static WorldIndexBuild _indexBuild;
        private static int _indexFrame = -1;

        // 来源按容器登记，库存变更时只遍历这些容器，不扫描整颗星球的 Pickupables。
        private static readonly Dictionary<int, List<Storage>> RemoteStorages = new Dictionary<int, List<Storage>>();
        private static readonly Dictionary<Storage, int> RemoteStorageWorlds = new Dictionary<Storage, int>();
        private static readonly Dictionary<int, float> LastWorldRefresh = new Dictionary<int, float>();
        private static readonly Dictionary<Storage, Action<object>> StorageListeners = new Dictionary<Storage, Action<object>>();

        internal static void Register(Storage storage)
        {
            if (storage == null) return;
            int world = storage.GetMyWorldId();
            if (world < 0 || world == 255) return;
            bool registered = RemoteStorageWorlds.TryGetValue(storage, out int oldWorld);
            if (registered && oldWorld == world) return;
            if (registered)
            {
                if (RemoteStorages.TryGetValue(oldWorld, out List<Storage> oldList)) oldList.Remove(storage);
                MarkDirty(oldWorld);
            }
            RemoteStorageWorlds[storage] = world;
            if (!RemoteStorages.TryGetValue(world, out List<Storage> list))
            {
                list = new List<Storage>();
                RemoteStorages[world] = list;
            }
            list.Add(storage);
            MarkDirty(world);
            if (!StorageListeners.ContainsKey(storage))
            {
                Action<object> listener = _ =>
                {
                    Register(storage);
                    MarkDirty(storage.GetMyWorldId());
                    RemoteFetchFailures.Invalidate();
                };
                StorageListeners.Add(storage, listener);
                storage.Subscribe((int)GameHashes.OnStorageChange, listener);
            }
        }

        internal static void Unregister(Storage storage)
        {
            if (ReferenceEquals(storage, null) || !RemoteStorageWorlds.TryGetValue(storage, out int world)) return;
            RemoteStorageWorlds.Remove(storage);
            if (RemoteStorages.TryGetValue(world, out List<Storage> list)) list.Remove(storage);
            if (StorageListeners.TryGetValue(storage, out Action<object> listener))
            {
                if (storage != null) storage.Unsubscribe((int)GameHashes.OnStorageChange, listener);
                StorageListeners.Remove(storage);
            }
            MarkDirty(world);
        }

        private static void AddTagToIndex(Dictionary<Tag, List<Pickupable>> index, Tag tag, Pickupable pickup)
        {
            if (!index.TryGetValue(tag, out List<Pickupable> list)) index[tag] = list = new List<Pickupable>(4);
            list.Add(pickup);
        }

        internal static void QueueRefresh(int worldId, bool force = false)
        {
            if (worldId < 0 || worldId == 255 || !Game.IsOnMainThread() || QueuedWorlds.Contains(worldId)) return;
            bool exists;
            lock (LocalItemsByTag) exists = LocalItemsByTag.ContainsKey(worldId);
            lock (_dirtyWorlds)
            {
                if (!force && exists && !_dirtyWorlds.Contains(worldId)) return;
            }
            if (!force && exists && LastWorldRefresh.TryGetValue(worldId, out float last) &&
                Time.realtimeSinceStartup - last < 0.5f) return;
            QueuedWorlds.Add(worldId);
            PendingWorlds.Enqueue(worldId);
        }

        private static void CaptureIndexStorage(WorldIndexBuild build, Storage storage)
        {
            if (!SolidTransferArmRemotePickup.IsRemoteStorage(storage) || storage.GetMyWorldId() != build.World ||
                !build.Sources.Add(storage)) return;
            storage.ignoreSourcePriority = true;
            // 只复制引用；GetComponent、去重与逐标签建表分散到后续帧。
            build.Items.AddRange(storage.items);
        }

        internal static void Advance()
        {
            if (_indexFrame == Time.frameCount || !Game.IsOnMainThread()) return;
            _indexFrame = Time.frameCount;
            if (_indexBuild == null && PendingWorlds.Count == 0) return;
            ModLog.CallTimer started = ModLog.StartCall();
            long deadline = started.Started + Stopwatch.Frequency / 500;
            if (_indexBuild == null)
            {
                int world = PendingWorlds.Dequeue();
                _indexBuild = new WorldIndexBuild { World = world };
                lock (_dirtyWorlds) _dirtyWorlds.Remove(world);
                if (RemoteStorages.TryGetValue(world, out List<Storage> storages))
                    for (int i = 0; i < storages.Count; i++) CaptureIndexStorage(_indexBuild, storages[i]);
            }
            WorldIndexBuild build = _indexBuild;
            int examined = 0;
            while (build.Cursor < build.Items.Count && examined++ < 256 && Stopwatch.GetTimestamp() < deadline)
            {
                GameObject item = build.Items[build.Cursor++];
                Pickupable pickup = item != null ? item.GetComponent<Pickupable>() : null;
                if (pickup == null || pickup.storage == null || !build.Sources.Contains(pickup.storage) ||
                    !build.Seen.Add(pickup)) continue;
                KPrefabID id = pickup.KPrefabID;
                if (id == null || id.HasTag(GameTags.StoredPrivate)) continue;
                Tag prefab = id.PrefabTag;
                AddTagToIndex(build.Tags, prefab, pickup);
                foreach (Tag tag in id.Tags)
                    if (tag != prefab) AddTagToIndex(build.Tags, tag, pickup);

            }
            if (build.Cursor >= build.Items.Count)
            {
                // 后台查询继续读上一份完整快照，整批完成后才替换；绝不发布半张表。
                lock (LocalItemsByTag)
                {
                    LocalItemsByTag[build.World] = build.Tags;
                }
                LastWorldRefresh[build.World] = Time.realtimeSinceStartup;
                QueuedWorlds.Remove(build.World);
                _indexBuild = null;
            }
            ModLog.SlowCall("仓储索引分片", started, examined);
        }

        internal static void Abort()
        {
            if (_indexBuild == null) return;
            MarkDirty(_indexBuild.World);
            QueuedWorlds.Remove(_indexBuild.World);
            _indexBuild = null;
        }

        internal static void Clear()
        {
            _indexBuild = null;
            _indexFrame = -1;
            PendingWorlds.Clear();
            QueuedWorlds.Clear();
            LastWorldRefresh.Clear();
            foreach (KeyValuePair<Storage, Action<object>> pair in StorageListeners)
                if (pair.Key != null) pair.Key.Unsubscribe((int)GameHashes.OnStorageChange, pair.Value);
            StorageListeners.Clear();
            RemoteStorages.Clear();
            RemoteStorageWorlds.Clear();
            lock (_dirtyWorlds) _dirtyWorlds.Clear();
            lock (LocalItemsByTag)
            {
                LocalItemsByTag.Clear();
            }
        }

        internal static bool TryGetLocalPickupables(int worldId, Tag tag, out List<Pickupable> list)
        {
            QueueRefresh(worldId);
            Dictionary<Tag, List<Pickupable>> tagDict = null;
            lock (LocalItemsByTag)
            {
                LocalItemsByTag.TryGetValue(worldId, out tagDict);
            }
            if (tagDict != null && tagDict.TryGetValue(tag, out list))
            {
                return true;
            }
            list = null;
            return false;
        }

        internal static Dictionary<Tag, List<Pickupable>> GetLocalPickupIndex(int worldId)
        {
            QueueRefresh(worldId);
            lock (LocalItemsByTag)
                return LocalItemsByTag.TryGetValue(worldId, out var index) ? index : null;
        }

    }
}
