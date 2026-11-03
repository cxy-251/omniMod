using System;
using System.Collections.Generic;
using OmniMod.Collection;
using OmniMod.Pool;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>实体池的分配兼容入口；箱子不再独立持有星球库存。</summary>
    internal static class SharedItemDispatcher
    {
        /// <summary>解析 IAssignableIdentity 所属小人/目标所在的 WorldId。</summary>
        internal static int GetAssigneeWorldId(IAssignableIdentity assignee)
        {
            if (assignee == null || assignee.IsNull())
            {
                return -1;
            }
            if (assignee is MinionAssignablesProxy proxy)
            {
                GameObject targetGo = proxy.GetTargetGameObject();
                if (targetGo != null)
                {
                    return targetGo.GetMyWorldId();
                }
            }
            if (assignee is KMonoBehaviour kmb && kmb != null)
            {
                return kmb.GetMyWorldId();
            }
            List<Ownables> owners = assignee.GetOwners();
            if (owners != null && owners.Count > 0 && owners[0] != null)
            {
                MinionAssignablesProxy p2 = owners[0].GetComponent<MinionAssignablesProxy>();
                if (p2 != null && p2.GetTargetGameObject() != null)
                {
                    return p2.GetTargetGameObject().GetMyWorldId();
                }
                return owners[0].GetMyWorldId();
            }
            return -1;
        }

        /// <summary>为具体分配请求从实体池经目标入口出库；不再寻找其它星球的“库存箱”。</summary>
        internal static bool TransferItemToWorldBox(GameObject itemGo, int targetWorldId, GlobalItemCollector.CollectMode mode)
        {
            return itemGo != null && EntityPoolAccess.ForAssignment(itemGo.GetComponent<Pickupable>(), targetWorldId);
        }
        internal static bool TransferItemToWorldJunkBox(GameObject itemGo, int targetWorldId) =>
            TransferItemToWorldBox(itemGo, targetWorldId, GlobalItemCollector.CollectMode.Debris);
    }
}
