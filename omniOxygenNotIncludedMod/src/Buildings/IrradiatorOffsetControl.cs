using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace OmniMod.Buildings
{
    /// <summary>设置栏中的左右三角按钮与数值框，复用原版控件样式。</summary>
    [SkipSaveFileSerialization]
    public sealed class IrradiatorOffsetControl : KMonoBehaviour, IRender200ms
    {
        private RadboltIrradiator target;
        private GameObject row;
        private KNumberInputField input;
        private KButton minus;
        private KButton plus;
        private ColorStyleSetting arrowStyle;
        private bool editing;
        private bool refreshing;
        private int shown = int.MinValue;

        private static readonly AccessTools.FieldRef<DetailsScreen, List<DetailsScreen.SideScreenRef>> Screens =
            AccessTools.FieldRefAccess<DetailsScreen, List<DetailsScreen.SideScreenRef>>("sideScreens");

        internal void SetTarget(RadboltIrradiator next, MultiSliderSideScreen screen)
        {
            if (target != next)
            {
                refreshing = true;
                if (input != null)
                {
                    input.SetIsEditing(false);
                    input.field.DeactivateInputField();
                }
                editing = false;
                target = next;
                refreshing = false;
                shown = int.MinValue;
            }
            if (target == null)
            {
                if (row != null) row.SetActive(false);
                return;
            }
            if (row == null && !CreateRow(screen)) return;
            row.SetActive(true);
            RefreshValue();
        }

        private bool CreateRow(MultiSliderSideScreen screen)
        {
            if (DetailsScreen.Instance == null || screen.sliderPrefab == null) return false;
            CounterSideScreen counter = null;
            foreach (DetailsScreen.SideScreenRef reference in Screens(DetailsScreen.Instance))
                if (reference.screenPrefab is CounterSideScreen found) { counter = found; break; }
            var refs = screen.sliderPrefab.GetComponent<HierarchyReferences>();
            LocText labelPrefab = refs != null ? refs.TryGetReference<LocText>("TargetLabel") : null;
            KNumberInputField inputPrefab = refs != null ? refs.TryGetReference<KNumberInputField>("NumberInputField") : null;
            if (counter == null || counter.incrementMaxButton == null || counter.decrementMaxButton == null ||
                labelPrefab == null || inputPrefab == null) return false;

            row = new GameObject("OmniIrradiatorOffset", typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
            row.SetActive(false);
            row.transform.SetParent(screen.sliderContainer, false);
            row.transform.SetSiblingIndex(2);
            var layout = row.GetComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 4, 4);
            layout.spacing = 4f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            Size(row, -1f, 36f);

            LocText label = CloneLabel(labelPrefab, row, ModStrings.T("上下偏移", "Vertical offset").Resolve());
            Size(label.gameObject, 64f, 28f).flexibleWidth = 1f;
            arrowStyle = ScriptableObject.CreateInstance<ColorStyleSetting>();
            arrowStyle.Init(labelPrefab.color);
            arrowStyle.hoverColor = arrowStyle.activeColor = Color.white;
            Color disabled = labelPrefab.color;
            disabled.a *= 0.35f;
            arrowStyle.disabledColor = arrowStyle.disabledActiveColor = arrowStyle.disabledhoverColor = disabled;
            minus = CreateButton(counter.decrementMaxButton, row, true, arrowStyle);
            minus.onClick += Decrease;
            input = Util.KInstantiateUI(inputPrefab.gameObject, row, force_active: true).GetComponent<KNumberInputField>();
            Size(input.gameObject, 52f, 28f);
            input.minValue = -8f;
            input.maxValue = 8f;
            input.decimalPlaces = 0;
            input.field.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.field.characterLimit = 3;
            if (input.field.textComponent != null) input.field.textComponent.alignment = TextAlignmentOptions.Center;
            input.onStartEdit += BeginEditing;
            input.onEndEdit += Commit;
            input.Activate();
            plus = CreateButton(counter.incrementMaxButton, row, false, arrowStyle);
            plus.onClick += Increase;
            LocText units = CloneLabel(labelPrefab, row, ModStrings.T("格", "tiles").Resolve());
            Size(units.gameObject, 20f, 28f);
            row.AddOrGet<ToolTip>().SetSimpleTooltip(ModStrings.T(
                "点击左右三角按钮，每次减／加一格；也可直接输入 −8～+8。正值上移，负值下移；粒子入口保持原位。",
                "Use the left/right arrow buttons to decrease/increase the offset by one, or enter -8 through +8. The radbolt input stays put.").Resolve());
            return true;
        }

        private static LayoutElement Size(GameObject go, float width, float height)
        {
            var size = go.AddOrGet<LayoutElement>();
            size.minWidth = size.preferredWidth = width;
            size.minHeight = size.preferredHeight = height;
            size.flexibleWidth = size.flexibleHeight = 0f;
            return size;
        }

        private static LocText CloneLabel(LocText prefab, GameObject parent, string text)
        {
            var label = Util.KInstantiateUI(prefab.gameObject, parent, force_active: true).GetComponent<LocText>();
            label.key = "";
            label.text = text;
            label.raycastTarget = false;
            return label;
        }

        private static KButton CreateButton(KButton prefab, GameObject parent, bool pointsLeft, ColorStyleSetting style)
        {
            GameObject go = Util.KInstantiateUI(prefab.gameObject, parent, force_active: true);
            KButton button = go.GetComponent<KButton>();
            button.ClearOnClick();
            button.ClearOnPointerEvents();
            button.enabled = true;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            Size(go, 28f, 28f);
            foreach (Graphic graphic in go.GetComponentsInChildren<Graphic>(true))
                if (graphic.gameObject != go && graphic != button.bgImage) graphic.enabled = false;
            if (button.fgImage != null && button.fgImage != button.bgImage) button.fgImage.enabled = false;
            // 原按钮的可点击图形可能正是上面隐藏的文字／图标；单独保留完整矩形命中面。
            var hitObject = new GameObject("ClickTarget", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            hitObject.transform.SetParent(go.transform, false);
            var hit = hitObject.GetComponent<Image>();
            hit.color = Color.clear;
            hit.raycastTarget = true;
            hit.canvasRenderer.cullTransparentMesh = false;
            hitObject.AddOrGet<LayoutElement>().ignoreLayout = true;
            RectTransform hitRect = hit.rectTransform;
            hitRect.anchorMin = Vector2.zero;
            hitRect.anchorMax = Vector2.one;
            hitRect.offsetMin = hitRect.offsetMax = Vector2.zero;
            var iconObject = new GameObject("Arrow", typeof(RectTransform), typeof(CanvasRenderer), typeof(IrradiatorArrowImage));
            iconObject.transform.SetParent(go.transform, false);
            var icon = iconObject.GetComponent<IrradiatorArrowImage>();
            icon.PointsLeft = pointsLeft;
            icon.raycastTarget = false;
            icon.colorStyleSetting = style;
            icon.ColorState = KImage.ColorSelector.Inactive;
            iconObject.AddOrGet<LayoutElement>().ignoreLayout = true;
            RectTransform rect = icon.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(9f, 12f);
            var nativeImages = button.additionalKImages;
            var images = new KImage[(nativeImages?.Length ?? 0) + 1];
            if (nativeImages != null) nativeImages.CopyTo(images, 0);
            images[images.Length - 1] = icon;
            button.additionalKImages = images;
            button.fgImage = icon;
            go.AddOrGet<ToolTip>().SetSimpleTooltip(pointsLeft
                ? ModStrings.T("减一格：辐照区域下移", "Decrease offset: move the field down one tile").Resolve()
                : ModStrings.T("加一格：辐照区域上移", "Increase offset: move the field up one tile").Resolve());
            return button;
        }

        private void BeginEditing() => editing = true;
        private void Commit()
        {
            editing = false;
            if (refreshing || target == null || input == null) return;
            if (!float.IsNaN(input.currentValue) && !float.IsInfinity(input.currentValue))
                target.SetVerticalOffset(Mathf.RoundToInt(input.currentValue));
            shown = int.MinValue;
            RefreshValue();
        }
        private void Decrease() => Adjust(-1);
        private void Increase() => Adjust(1);
        private void Adjust(int delta)
        {
            if (target == null) return;
            int value = target.VerticalOffset;
            if (input != null && editing)
            {
                // 先完成文本解析，再应用箭头；失焦回调及延迟回调均不会提交旧值覆盖本次调整。
                refreshing = true;
                input.SetIsEditing(false);
                input.field.DeactivateInputField();
                editing = false;
                refreshing = false;
                if (!float.IsNaN(input.currentValue) && !float.IsInfinity(input.currentValue))
                    value = Mathf.RoundToInt(input.currentValue);
            }
            target.SetVerticalOffset(value + delta);
            shown = int.MinValue;
            RefreshValue();
        }

        private void RefreshValue()
        {
            if (target == null || input == null || editing || shown == target.VerticalOffset) return;
            refreshing = true;
            shown = target.VerticalOffset;
            input.SetAmount(shown);
            minus.isInteractable = shown > -8;
            plus.isInteractable = shown < 8;
            refreshing = false;
        }

        // 只有选中侧栏这一份 UI 定期刷新；复制设置后也能同步显示，不轮询所有建筑。
        public void Render200ms(float dt)
        {
            if (isActiveAndEnabled && row != null && row.activeSelf) RefreshValue();
        }

        protected override void OnCleanUp()
        {
            target = null;
            if (input != null) { input.onStartEdit -= BeginEditing; input.onEndEdit -= Commit; }
            if (minus != null) minus.onClick -= Decrease;
            if (plus != null) plus.onClick -= Increase;
            if (arrowStyle != null) Destroy(arrowStyle);
            base.OnCleanUp();
        }
    }

    /// <summary>直接绘制三角图形，不依赖字体字符；颜色由原版按钮的悬停／按下／禁用状态控制。</summary>
    public sealed class IrradiatorArrowImage : KImage
    {
        internal bool PointsLeft;

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Rect bounds = GetPixelAdjustedRect();
            float left = bounds.xMin;
            float right = bounds.xMax;
            float bottom = bounds.yMin;
            float top = bounds.yMax;
            if (PointsLeft)
            {
                vertices.AddVert(new Vector3(left, bounds.center.y), color, Vector2.zero);
                vertices.AddVert(new Vector3(right, top), color, Vector2.zero);
                vertices.AddVert(new Vector3(right, bottom), color, Vector2.zero);
            }
            else
            {
                vertices.AddVert(new Vector3(left, bottom), color, Vector2.zero);
                vertices.AddVert(new Vector3(left, top), color, Vector2.zero);
                vertices.AddVert(new Vector3(right, bounds.center.y), color, Vector2.zero);
            }
            vertices.AddTriangle(0, 1, 2);
        }
    }

    [HarmonyPatch(typeof(MultiSliderSideScreen), "Refresh")]
    internal static class IrradiatorOffset_Row_Patch
    {
        private static void Postfix(MultiSliderSideScreen __instance, IMultiSliderControl ___target)
        {
            var target = ___target as RadboltIrradiator;
            var control = __instance.GetComponent<IrradiatorOffsetControl>();
            if (target != null && control == null) control = __instance.gameObject.AddOrGet<IrradiatorOffsetControl>();
            if (control != null) control.SetTarget(target, __instance);
        }
    }
}
