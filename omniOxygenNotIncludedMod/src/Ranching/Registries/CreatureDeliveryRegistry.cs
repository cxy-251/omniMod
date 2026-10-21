using System.Collections.Generic;

namespace OmniMod.Ranching.Registries
{
    /// <summary>
    /// 放生点全局注册表：记录全星系所有放生点及其有效释放格子，实现 O(1) 判定
    /// </summary>
    public static class CreatureDeliveryRegistry
    {
        private static readonly HashSet<int> _deliveryCells = new HashSet<int>();

        public static void Register(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int baseCell = Grid.PosToCell(point.transform.GetPosition());
            if (Grid.IsValidCell(baseCell))
            {
                _deliveryCells.Add(baseCell);
            }
            if (point.deliveryOffsets != null)
            {
                for (int i = 0; i < point.deliveryOffsets.Length; i++)
                {
                    int offsetCell = Grid.OffsetCell(baseCell, point.deliveryOffsets[i]);
                    if (Grid.IsValidCell(offsetCell))
                    {
                        _deliveryCells.Add(offsetCell);
                    }
                }
            }
        }

        public static void Unregister(CreatureDeliveryPoint point)
        {
            if (point == null) return;
            int baseCell = Grid.PosToCell(point.transform.GetPosition());
            _deliveryCells.Remove(baseCell);
            if (point.deliveryOffsets != null)
            {
                for (int i = 0; i < point.deliveryOffsets.Length; i++)
                {
                    int offsetCell = Grid.OffsetCell(baseCell, point.deliveryOffsets[i]);
                    _deliveryCells.Remove(offsetCell);
                }
            }
        }

        public static bool IsDeliveryCell(int cell)
        {
            return _deliveryCells.Contains(cell);
        }

        public static void Clear()
        {
            _deliveryCells.Clear();
        }
    }
}
