using System;
using HarmonyLib;
using OmniMod.Core.Logging;
using UnityEngine;

namespace OmniMod.Fixes
{
    /// <summary>
    /// 全局输入看门狗与紧急解锁管理器：
    /// 彻底防止与解决以下导致玩家键鼠失灵或无法操作画面的极端异常：
    /// 1. 偶发主线程长卡顿（如读写盘或GC）导致鼠标按键抬起事件被吞噬，DragTool 永久滞留在 dragging 状态，
    ///    导致 EventSystem 被禁用、全部 UI 与快捷键无法响应；
    /// 2. macOS 下窗口被系统短暂剥离焦点导致 KInputManager.hasFocus 停留在 false，导致游戏忽略全部键盘与鼠标分发；
    /// 3. 全景缩略图截图（Timelapser）异常导致 freezeCamera 残留或 DebugHandler.TimelapseMode 使得 UI 画布 Alpha 置 0；
    /// 4. 模态窗口或全屏遮罩异常关闭导致 KScreenManager.inputDisabled 永久为 true；
    /// 5. 提供紧急救援全局快捷键（Ctrl + Shift + F12 或 Cmd + Shift + F12），一键强制复位所有输入、工具与 UI 状态。
    /// </summary>
    public static class InputUnfreezeManager
    {
        private static float _lastWatchdogCheck = 0f;
        private static int _stuckDragTicks = 0;
        private static float _timelapseTimer = 0f;

        /// <summary>
        /// 强制恢复全部键鼠分发、EventSystem、活动工具与 UI 状态
        /// </summary>
        public static void ForceRestoreInputState(bool notifyUser = false)
        {
            try
            {
                // 1. 恢复底层输入分发焦点
                Traverse.Create(typeof(KInputManager)).Field<bool>("hasFocus").Value = true;

                // 2. 恢复 KScreenManager 输入与 EventSystem
                if (KScreenManager.Instance != null)
                {
                    KScreenManager.Instance.DisableInput(false);
                    KScreenManager.Instance.SetEventSystemEnabled(true);
                }

                if (UnityEngine.EventSystems.EventSystem.current != null)
                {
                    UnityEngine.EventSystems.EventSystem.current.enabled = true;
                }

                // 3. 解除可能滞留的拖拽工具，复位至选择工具
                if (PlayerController.Instance != null)
                {
                    if (PlayerController.Instance.ActiveTool is DragTool dt && dt.Dragging)
                    {
                        dt.CancelDragging();
                    }
                    if (SelectTool.Instance != null && PlayerController.Instance.ActiveTool != SelectTool.Instance)
                    {
                        SelectTool.Instance.Activate();
                    }
                }

                // 4. 解除可能冻结的摄像机与缩略图遮罩（仅在真正处于缩略图模式时解除，严禁盲目调用覆写 activeWorld 导致生态舱黑屏）
                if (DebugHandler.TimelapseMode)
                {
                    DebugHandler.SetTimelapseMode(false);
                }
                DebugHandler.ScreenshotMode = false;

                if (Game.Instance != null && Game.Instance.timelapser != null)
                {
                    Camera fc = Traverse.Create(Game.Instance.timelapser).Field<Camera>("freezeCamera")?.Value;
                    if (fc != null && fc.enabled)
                    {
                        fc.enabled = false;
                    }
                }

                if (CameraController.Instance != null)
                {
                    CameraController.Instance.DisableUserCameraControl = false;
                }

                // 5. 确保主界面所有 UI 画布 Alpha 正常可见
                RestoreUiCanvasAlphas();

                if (notifyUser)
                {
                    ModLog.Info("[输入救援] 玩家已通过快捷键 (Ctrl+Shift+F12) 强制复位键鼠焦点、EventSystem 与界面交互！");
                    ShowOnScreenMessage("OmniMod: 键鼠控制与界面交互已强制恢复！");
                }
            }
            catch (Exception ex)
            {
                ModLog.Warn($"[输入救援] 执行复位异常: {ex.Message}");
            }
        }

        private static void RestoreUiCanvasAlphas()
        {
            if (GameScreenManager.Instance == null) return;
            ResetCanvasGroup(GameScreenManager.Instance.ssHoverTextCanvas);
            ResetCanvasGroup(GameScreenManager.Instance.ssCameraCanvas);
            ResetCanvasGroup(GameScreenManager.Instance.ssOverlayCanvas);
            ResetCanvasGroup(GameScreenManager.Instance.worldSpaceCanvas);
        }

        private static void ResetCanvasGroup(GameObject go)
        {
            if (go == null) return;
            CanvasGroup cg = go.GetComponent<CanvasGroup>();
            if (cg != null && cg.alpha < 0.9f)
            {
                cg.alpha = 1f;
            }
        }

        private static void ShowOnScreenMessage(string message)
        {
            try
            {
                if (PopFXManager.Instance != null && CameraController.Instance != null)
                {
                    PopFXManager.Instance.SpawnFX(
                        PopFXManager.Instance.sprite_Plus,
                        message,
                        null,
                        CameraController.Instance.transform.position,
                        2.5f);
                }
            }
            catch {}
        }

        /// <summary>
        /// 每帧检测全局救援快捷键，并在后台周期性执行自愈检查
        /// </summary>
        public static void UpdateWatchdog()
        {
            // 快捷键检测：Ctrl+Shift+F12 或 Cmd+Shift+F12
            bool ctrlOrCmd = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                             Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (ctrlOrCmd && shift && Input.GetKeyDown(KeyCode.F12))
            {
                ForceRestoreInputState(notifyUser: true);
                return;
            }

            // 轻量看门狗周期检测（约每 0.5 秒）
            float now = Time.unscaledTime;
            if (now - _lastWatchdogCheck < 0.5f) return;
            _lastWatchdogCheck = now;

            // 1. macOS / Unity 焦点脱节自愈
            if (Application.isFocused && !KInputManager.isFocused)
            {
                Traverse.Create(typeof(KInputManager)).Field<bool>("hasFocus").Value = true;
            }

            // 2. 鼠标抬起事件丢失导致的 DragTool 假死自愈
            if (PlayerController.Instance != null && PlayerController.Instance.ActiveTool is DragTool dt && dt.Dragging)
            {
                if (!Input.GetMouseButton(0) && !Input.GetMouseButton(1) && !Input.GetMouseButton(2))
                {
                    _stuckDragTicks++;
                    if (_stuckDragTicks >= 2) // 连续两次检测鼠标未按下却保持 dragging
                    {
                        _stuckDragTicks = 0;
                        dt.CancelDragging();
                        KScreenManager.Instance?.SetEventSystemEnabled(true);
                        ModLog.Warn("[输入看门狗] 检测到物理鼠标未按下但 DragTool 拖拽残留，已自动取消拖拽并恢复 EventSystem。");
                    }
                }
                else
                {
                    _stuckDragTicks = 0;
                }
            }
            else
            {
                _stuckDragTicks = 0;
            }

            // 3. 缩略图模式/冻结相机残留自愈
            bool isTimelapseActive = DebugHandler.TimelapseMode;
            if (!isTimelapseActive && Game.Instance != null && Game.Instance.timelapser != null)
            {
                Camera fc = Traverse.Create(Game.Instance.timelapser).Field<Camera>("freezeCamera")?.Value;
                if (fc != null && fc.enabled)
                {
                    isTimelapseActive = true;
                }
            }

            if (isTimelapseActive)
            {
                _timelapseTimer += 0.5f;
                if (_timelapseTimer >= 2.5f) // 缩略图模式不应超过 2.5 秒
                {
                    _timelapseTimer = 0f;
                    if (DebugHandler.TimelapseMode)
                    {
                        DebugHandler.SetTimelapseMode(false);
                    }
                    DebugHandler.ScreenshotMode = false;
                    if (Game.Instance != null && Game.Instance.timelapser != null)
                    {
                        Camera fc = Traverse.Create(Game.Instance.timelapser).Field<Camera>("freezeCamera")?.Value;
                        if (fc != null) fc.enabled = false;
                    }
                    RestoreUiCanvasAlphas();
                    ModLog.Warn("[输入看门狗] 检测到全景缩略图遮罩超时残留，已自动解除冻结并恢复 UI。");
                }
            }
            else
            {
                _timelapseTimer = 0f;
            }
        }
    }

    /// <summary>
    /// 挂载看门狗与快捷键轮询
    /// </summary>
    [HarmonyPatch(typeof(Global), "Update")]
    internal static class Global_Update_InputWatchdog_Patch
    {
        private static void Postfix()
        {
            InputUnfreezeManager.UpdateWatchdog();
        }
    }
}
