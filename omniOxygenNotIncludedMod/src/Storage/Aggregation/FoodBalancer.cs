using System.Collections.Generic;
using OmniMod.Core.Safety;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>
    /// 全星系食物跨星均衡调度器：
    /// 协调所有生效的食物箱，将可拆分食物适度均衡调配到各个星球的食物箱中，确保跨星球食物箱不空竭。
    /// </summary>
    public static class FoodBalancer
    {
        public static void RebalanceFoodAcrossBoxes()
        {
            // 箱子只是全星系共享池的入口，所有食物箱 100% 共享食物与卡路里。
            // 绝不均分，无需后台物理切片或跨箱搬运，彻底消除冗余切片与遍历开销。
            return;
        }
    }
}
