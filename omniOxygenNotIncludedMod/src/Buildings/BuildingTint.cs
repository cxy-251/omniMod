using UnityEngine;

namespace OmniMod.Buildings
{
    /// <summary>
    /// 给建筑动画上一个固定色调。必须在建筑生成后（OnSpawn）才能设 TintColour——
    /// 在 IBuildingConfig 里直接设的话 batchInstanceData 还没就绪，会被静默忽略。
    /// </summary>
    public class BuildingTint : KMonoBehaviour
    {
        [SerializeField]
        public Color32 tint = new Color32(255, 255, 255, 255);

        protected override void OnSpawn()
        {
            base.OnSpawn();
            var kbac = GetComponent<KBatchedAnimController>();
            if (kbac != null)
            {
                kbac.TintColour = tint;
            }
        }
    }
}
