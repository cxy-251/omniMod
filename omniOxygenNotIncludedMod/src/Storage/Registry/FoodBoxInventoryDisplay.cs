using System.Runtime.CompilerServices;
using HarmonyLib;
using OmniMod.Pool;
using UnityEngine;

namespace OmniMod.Collection
{
    /// <summary>门户筛选清单显示池总量，储存内容只显示待领取实物。</summary>
    [HarmonyPatch(typeof(TreeFilterableSideScreenElement), nameof(TreeFilterableSideScreenElement.SetTag))]
    internal static class FoodBoxInventoryDisplay
    {
        private static readonly AccessTools.FieldRef<TreeFilterableSideScreenElement, LocText> LabelField =
            AccessTools.FieldRefAccess<TreeFilterableSideScreenElement, LocText>("elementName");
        private static readonly ConditionalWeakTable<TreeFilterableSideScreenElement, ToolTip> OwnedTooltips =
            new ConditionalWeakTable<TreeFilterableSideScreenElement, ToolTip>();

        private static void Postfix(TreeFilterableSideScreenElement __instance, Tag newTag)
        {
            OwnedTooltips.TryGetValue(__instance, out ToolTip tooltip);
            if (tooltip != null)
            {
                tooltip.SetSimpleTooltip("");
                tooltip.enabled = false;
            }
            TreeFilterableSideScreen screen = __instance.Parent;
            Storage storage = TreeFilterableSideScreen_GetAmountInStorage_Patch.GetStorage(screen);
            GlobalItemCollector box = storage != null ? storage.GetComponent<GlobalItemCollector>() : null;
            if (box == null) return;
            LocText label = LabelField(__instance);
            if (label == null) return;
            label.text = ModStrings.T(box.IsRedundant ? "本箱 " : "共享 ", box.IsRedundant ? "Local " : "Shared ").Resolve() + label.text;
            if (tooltip == null)
            {
                // 只管理自己创建的提示，避免覆盖原版或其它模组附加的提示；UI 元素复用时停用。
                if (label.GetComponent<ToolTip>() != null) return;
                tooltip = label.gameObject.AddComponent<ToolTip>();
                OwnedTooltips.Add(__instance, tooltip);
            }
            float local = storage.GetMassAvailable(newTag);
            float shared = screen.GetAmountInStorage(newTag);
            string localText = GameUtil.GetFormattedMass(local);
            string sharedText = GameUtil.GetFormattedMass(shared);
            string details = box.IsRedundant
                ? ModStrings.T($"本箱实物：{localText}\n同星球已有生效的食物箱，此箱不参与共享。",
                    $"Physical contents: {localText}\nAnother food box is active on this world; this box does not participate in sharing.").Resolve()
                : ModStrings.T($"共享池总量：{sharedText}（已包含待领取物品）\n本入口待领取／待入库：{localText}\n箱子是全局池的存取门户。物品归池后统一保存，有取用请求时经入口出库；未领取的物品会归还。复制人需要步行到达入口，禁食和分配设置保持有效。",
                    $"Global pool: {sharedText} (includes pending checkouts)\nAwaiting pickup or deposit here: {localText}\nBoxes are portals to one shared pool. Requested items check out through a portal; unclaimed items return to the pool. Duplicants walk to the portal. Food permissions and assignments still apply.").Resolve();
            tooltip.SetSimpleTooltip(details);
            tooltip.enabled = true;
        }
    }

    [HarmonyPatch(typeof(GlobalItemCollector), "OnSpawn")]
    internal static class FoodBoxSharedInventoryStatus
    {
        private static StatusItem status;

        private static void Postfix(GlobalItemCollector __instance)
        {
            if (__instance.mode != GlobalItemCollector.CollectMode.Edibles) return;
            if (status == null)
            {
                status = new StatusItem("OmniSharedFoodInventory",
                    ModStrings.T("共享食物入口", "Shared Food Access").Resolve(), "", "", StatusItem.IconType.Info,
                    NotificationType.Neutral, false, OverlayModes.None.ID);
                status.resolveTooltipCallback = ResolveTooltip;
                status.resolveStringCallback = (text, data) => data is GlobalItemCollector box && box.IsRedundant
                    ? ModStrings.T("重复食物箱：未参与共享", "Duplicate Food Box: Sharing Inactive").Resolve() : text;
            }
            __instance.GetComponent<KSelectable>()?.AddStatusItem(status, __instance);
        }

        private static string ResolveTooltip(string text, object data)
        {
            GlobalItemCollector box = data as GlobalItemCollector;
            Storage storage = box != null ? box.BoxStorage : null;
            if (storage == null) return "";
            if (box.IsRedundant)
                return ModStrings.T("同星球已有生效的食物箱；此箱仅保留本箱实物，不参与共享供餐。",
                    "Another food box is active on this world. This box keeps its physical contents but does not supply shared meals.").Resolve();
            float calories = OmniEntityPool.Instance?.FoodCalories ?? 0f;
            string amount = GameUtil.GetFormattedCalories(calories);
            return ModStrings.T($"共享池食物热量：{amount}（包含各入口待领取的食物；尚未按个人禁食和可达性扣除，不计零热量原料）。\n箱子是共享池的存取门户，筛选清单显示全局总量，下方仅显示入口待领取／待入库实物。\n复制人缺少允许吃的饭时，从池中经可步行到达的入口领取所需分量；取消或超时未领取的食物归还共享池。",
                $"Shared food calories: {amount} (includes pending checkouts; before personal restrictions and reachability; excludes zero-calorie ingredients).\nBoxes are portals. Filter rows show global totals; storage contents show items pending pickup or deposit.\nAllowed meals check out from the pool through a reachable portal. Unclaimed portions return to the pool.").Resolve();
        }
    }
}
