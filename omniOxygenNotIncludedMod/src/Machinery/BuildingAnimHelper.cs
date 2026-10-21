namespace OmniMod.Machinery
{
    /// <summary>
    /// 建筑通用工作循环与停机动画控制器
    /// </summary>
    public static class BuildingAnimHelper
    {
        public static void PlayWorkingAnim(KBatchedAnimController kbac)
        {
            if (kbac == null) return;
            if (kbac.HasAnimation("working_loop"))
            {
                if (kbac.currentAnim != "working_loop") kbac.Play("working_loop", KAnim.PlayMode.Loop);
            }
            else if (kbac.HasAnimation("working"))
            {
                if (kbac.currentAnim != "working") kbac.Play("working", KAnim.PlayMode.Loop);
            }
        }

        public static void StopWorkingAnim(KBatchedAnimController kbac, Operational op)
        {
            if (kbac == null) return;
            string anim = (op != null && op.IsOperational) ? "on" : "off";
            if (kbac.HasAnimation(anim))
            {
                kbac.Play(anim, KAnim.PlayMode.Once);
            }
        }
    }
}
