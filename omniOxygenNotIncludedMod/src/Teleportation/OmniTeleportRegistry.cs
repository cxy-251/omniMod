using System.Collections.Generic;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Teleportation
{
    /// <summary>
    /// 时空传送网络注册中心：
    /// 管理全星系所有已建造的时空发射器与接收器，
    /// 提供按星球/火箭的每星限建一台（1 per world/rocket）检查，以及接收端跨星球检索与调度。
    /// </summary>
    public static class OmniTeleportRegistry
    {
        private static readonly List<OmniTeleportReceiver> _receivers = new List<OmniTeleportReceiver>();
        private static readonly List<OmniTeleportTransmitter> _transmitters = new List<OmniTeleportTransmitter>();

        public static void RegisterReceiver(OmniTeleportReceiver r)
        {
            if (r != null && !_receivers.Contains(r))
            {
                _receivers.Add(r);
                ModLog.Info($"[时空传送网络] 注册接收器: {GetWorldDisplayName(r.GetMyWorldId())}");
            }
        }

        public static void UnregisterReceiver(OmniTeleportReceiver r)
        {
            if (r != null)
            {
                _receivers.Remove(r);
                ModLog.Info($"[时空传送网络] 注销接收器: {GetWorldDisplayName(r.GetMyWorldId())}");
            }
        }

        public static void RegisterTransmitter(OmniTeleportTransmitter t)
        {
            if (t != null && !_transmitters.Contains(t))
            {
                _transmitters.Add(t);
                ModLog.Info($"[时空传送网络] 注册发射器: {GetWorldDisplayName(t.GetMyWorldId())}");
            }
        }

        public static void UnregisterTransmitter(OmniTeleportTransmitter t)
        {
            if (t != null)
            {
                _transmitters.Remove(t);
                ModLog.Info($"[时空传送网络] 注销发射器: {GetWorldDisplayName(t.GetMyWorldId())}");
            }
        }

        public static List<OmniTeleportReceiver> GetAvailableReceivers()
        {
            _receivers.RemoveAll(r => r == null || r.gameObject == null);
            var list = new List<OmniTeleportReceiver>(_receivers.Count);
            for (int i = 0; i < _receivers.Count; i++)
            {
                var r = _receivers[i];
                if (r != null && r.IsOperational)
                {
                    list.Add(r);
                }
            }
            return list;
        }

        /// <summary>
        /// 检查指定星球或火箭内部是否已存在此建筑（已建成或正在建造中）。
        /// 支持每星球/火箭限建一台（1 per world/rocket）。
        /// </summary>
        public static bool WorldHasBuilding(int worldId, string prefabId, GameObject exclude = null)
        {
            if (Components.BuildingCompletes != null)
            {
                for (int i = 0; i < Components.BuildingCompletes.Count; i++)
                {
                    BuildingComplete b = Components.BuildingCompletes[i];
                    if (b != null && b.GetMyWorldId() == worldId && b.Def != null && b.Def.PrefabID == prefabId)
                    {
                        if (exclude != null && (b.gameObject == exclude || b.gameObject == exclude.transform.root.gameObject))
                        {
                            continue;
                        }
                        return true;
                    }
                }
            }

            BuildingUnderConstruction[] underConstructions = Object.FindObjectsByType<BuildingUnderConstruction>(FindObjectsSortMode.None);
            if (underConstructions != null)
            {
                for (int i = 0; i < underConstructions.Length; i++)
                {
                    BuildingUnderConstruction b = underConstructions[i];
                    if (b != null && b.GetMyWorldId() == worldId && b.Def != null && b.Def.PrefabID == prefabId)
                    {
                        if (exclude != null && (b.gameObject == exclude || b.gameObject == exclude.transform.root.gameObject))
                        {
                            continue;
                        }
                        return true;
                    }
                }
            }

            return false;
        }

        public static string GetWorldDisplayName(int worldId)
        {
            if (ClusterManager.Instance == null) return $"星体 {worldId}";
            WorldContainer wc = ClusterManager.Instance.GetWorld(worldId);
            if (wc == null) return $"星体 {worldId}";

            string name = wc.GetProperName();
            if (wc.IsModuleInterior)
            {
                return $"🚀 {name} (火箭舱内)";
            }
            return $"{name} (星体 {worldId})";
        }
    }
}
