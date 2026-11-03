using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 所有聚合箱共用一次全星系散落物扫描。原版 Cmps.GetWorldItems 每次都会扫描全部 Items，
    /// 不能让每个箱子各扫一次；这里在主线程分片归类，完成后一次发布只读快照。
    /// </summary>
    internal static class CollectionCandidateSnapshot
    {
        private static Dictionary<int, List<Pickupable>> _published;
        private static Dictionary<int, List<Pickupable>> _building;
        private static readonly HashSet<Pickupable> Seen = new HashSet<Pickupable>();
        private static int _version;
        private static int _cursor;
        private static int _frame = -1;
        private static float _nextRefresh;

        internal static bool IsBuilding => _building != null;

        internal static int RequestRefresh()
        {
            if (_building != null) return _version + 1;
            if (_published != null && Time.time < _nextRefresh) return _version;
            _building = new Dictionary<int, List<Pickupable>>();
            Seen.Clear();
            _cursor = Components.Pickupables.Count - 1;
            _nextRefresh = Time.time + 6f;
            return _version + 1;
        }

        internal static bool TryGetWorldItems(int world, int minVersion, out List<Pickupable> items)
        {
            items = null;
            if (_published == null || _version < minVersion) return false;
            _published.TryGetValue(world, out items);
            return true;
        }

        internal static void Advance(long deadline)
        {
            if (_building == null || _frame == Time.frameCount) return;
            _frame = Time.frameCount;
            List<Pickupable> all = Components.Pickupables.Items;
            int examined = 0;
            while (_cursor >= 0 && examined++ < 1024 && Stopwatch.GetTimestamp() < deadline)
            {
                // 原版紧凑列表在收集/销毁物品时会交换尾项；缩小后钳制游标，下一轮补扫遗漏的新项。
                if (_cursor >= all.Count) _cursor = all.Count - 1;
                if (_cursor < 0) break;
                Pickupable pickup = all[_cursor--];
                if (!GlobalItemCollector.IsLooseInertPickupable(pickup) || !Seen.Add(pickup)) continue;
                int world = pickup.GetMyWorldId();
                if (world < 0 || world == 255) continue;
                if (!_building.TryGetValue(world, out List<Pickupable> list))
                {
                    list = new List<Pickupable>();
                    _building[world] = list;
                }
                list.Add(pickup);
            }
            if (_cursor < 0)
            {
                _published = _building;
                _building = null;
                _version++;
                Seen.Clear();
            }
        }

        internal static void Clear()
        {
            _published = null;
            _building = null;
            Seen.Clear();
            _version = 0;
            _frame = -1;
            _cursor = 0;
            _nextRefresh = 0f;
        }
    }
}
