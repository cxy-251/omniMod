using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>离散物品分类兼容入口；实际入库、出库与归还由 OmniEntityPool 管理。</summary>
    public static class DiscreteDemandManager
    {
        public static bool IsDiscreteManaged(GameObject go)
        {
            if (go == null || LivingThingsSafety.IsLivingThing(go)) return false;
            Pickupable pickup = go.GetComponent<Pickupable>();
            return pickup != null && !BulkBufferManager.IsBulkSolidElementChunk(pickup);
        }
    }
}
