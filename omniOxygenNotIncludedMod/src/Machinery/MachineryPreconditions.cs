namespace OmniMod.Machinery
{
    /// <summary>
    /// 自动化机械 Chore 前置条件库：提供永不可用前置条件，彻底阻断小人领取无人化设备的派工任务。
    /// </summary>
    public static class MachineryPreconditions
    {
        public static readonly Chore.Precondition NeverAvailable = new Chore.Precondition
        {
            id = "OmniMod_NeverAvailable",
            description = "OmniMod Unmanned Machine",
            fn = (ref Chore.Precondition.Context context, object data) => false,
            canExecuteOnAnyThread = true
        };
    }
}
