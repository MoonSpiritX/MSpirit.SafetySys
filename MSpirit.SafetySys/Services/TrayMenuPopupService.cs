// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Forms;
using MS_SafetyManagementSystem.Views;
using WpfApp = System.Windows.Application;

namespace MS_SafetyManagementSystem.Services
{
    public enum TaskbarLocation { Bottom, Top, Left, Right }

    public class TrayMenuPopupService
    {
        private Popup? _activePopup;
        private IntPtr _mouseHookId = IntPtr.Zero;
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelMouseProc? _mouseProc;
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;

        public void ShowMenu(Window owner, ICommand showWindowCommand, ICommand exitCommand, IReadOnlyList<TrayMenuGroup> groups)
        {
            if (_activePopup != null)
            {
                _activePopup.IsOpen = false;
                _activePopup = null;
                UnhookMouse();
            }

            var menu = new TrayMenuControl();
            menu.ShowWindowCommand = showWindowCommand;
            menu.ExitCommand = exitCommand;
            menu.Groups = groups; // 一级分组菜单（办公工具/信息官网/外部AI/快捷网址），构建后自动重建分组项
            menu.RequestClose += () =>
            {
                if (_activePopup != null)
                    _activePopup.IsOpen = false;
            };

            menu.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var menuSize = menu.DesiredSize;

            var physicalMouse = new Point(System.Windows.Forms.Cursor.Position.X, System.Windows.Forms.Cursor.Position.Y);
            var source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget == null) return;
            var transform = source.CompositionTarget.TransformFromDevice;
            var wpfMouse = transform.Transform(physicalMouse);

            var screen = Screen.FromPoint(new System.Drawing.Point((int)physicalMouse.X, (int)physicalMouse.Y));
            var physicalWorkArea = new Rect(screen.WorkingArea.X, screen.WorkingArea.Y, screen.WorkingArea.Width, screen.WorkingArea.Height);
            var wpfWorkArea = new Rect(
                transform.Transform(new Point(physicalWorkArea.Left, physicalWorkArea.Top)),
                transform.Transform(new Point(physicalWorkArea.Right, physicalWorkArea.Bottom)));

            var taskbarLocation = GetTaskbarLocation();

            const int gap = 5;
            double left = 0, top = 0;

            switch (taskbarLocation)
            {
                case TaskbarLocation.Bottom:
                    var taskbarTop = wpfWorkArea.Bottom;
                    top = taskbarTop - gap - menuSize.Height;
                    left = wpfMouse.X - menuSize.Width / 2;
                    break;
                case TaskbarLocation.Top:
                    var taskbarBottom = wpfWorkArea.Top;
                    top = taskbarBottom + gap;
                    left = wpfMouse.X - menuSize.Width / 2;
                    break;
                case TaskbarLocation.Left:
                    var taskbarRight = wpfWorkArea.Left;
                    left = taskbarRight + gap;
                    top = wpfMouse.Y - menuSize.Height / 2;
                    break;
                case TaskbarLocation.Right:
                    var taskbarLeft = wpfWorkArea.Right;
                    left = taskbarLeft - gap - menuSize.Width;
                    top = wpfMouse.Y - menuSize.Height / 2;
                    break;
            }

            if (left + menuSize.Width > wpfWorkArea.Right)
                left = wpfWorkArea.Right - menuSize.Width;
            if (left < wpfWorkArea.Left)
                left = wpfWorkArea.Left;
            if (top + menuSize.Height > wpfWorkArea.Bottom)
                top = wpfWorkArea.Bottom - menuSize.Height;
            if (top < wpfWorkArea.Top)
                top = wpfWorkArea.Top;

            _activePopup = new Popup
            {
                Child = menu,
                Placement = PlacementMode.AbsolutePoint,
                AllowsTransparency = true,
                StaysOpen = true,
                PopupAnimation = PopupAnimation.Slide,
                HorizontalOffset = left,
                VerticalOffset = top,
            };
            _activePopup.IsOpen = true;
            InstallMouseHook();

            // 可选：也注册到主窗口，作为备用（但钩子已足够）
            if (owner is MainWindow mainWindow)
            {
                mainWindow.TrayPopup = _activePopup;
            }

            var popup = _activePopup;
            popup.Closed += (s, e) =>
            {
                if (owner is MainWindow mainWin && mainWin.TrayPopup == popup)
                    mainWin.TrayPopup = null;
                // 主菜单关闭时一并关闭二级菜单（独立 Popup，不会自动跟随关闭）
                if (popup.Child is TrayMenuControl trayMenu && trayMenu.CurrentSubMenuPopup is Popup sub)
                    sub.IsOpen = false;
                if (_activePopup == popup)
                    _activePopup = null;
                UnhookMouse();
            };

            menu.Loaded += (s, ev) =>
            {
                var actualHeight = menu.RenderSize.Height;
                if (actualHeight <= 0) actualHeight = menu.DesiredSize.Height;
                var actualWidth = menu.RenderSize.Width;
                if (actualWidth <= 0) actualWidth = menu.DesiredSize.Width;

                var newTop = top;
                var menuBottom = top + actualHeight;
                if (menuBottom > wpfWorkArea.Bottom)
                {
                    newTop = wpfWorkArea.Bottom - actualHeight;
                    if (newTop < wpfWorkArea.Top) newTop = wpfWorkArea.Top;
                }

                var newLeft = left;
                var menuRight = left + actualWidth;
                if (menuRight > wpfWorkArea.Right)
                {
                    newLeft = wpfWorkArea.Right - actualWidth;
                    if (newLeft < wpfWorkArea.Left) newLeft = wpfWorkArea.Left;
                }

                if (Math.Abs(newTop - top) > 0.1 || Math.Abs(newLeft - left) > 0.1)
                {
                    _activePopup.HorizontalOffset = newLeft;
                    _activePopup.VerticalOffset = newTop;
                    menu.UpdateLayout();
                }
            };
        }

        private TaskbarLocation GetTaskbarLocation()
        {
            var mousePoint = System.Windows.Forms.Cursor.Position;
            var screen = Screen.FromPoint(mousePoint);
            var workingArea = screen.WorkingArea;
            var bounds = screen.Bounds;

            if (workingArea.Height < bounds.Height)
            {
                if (workingArea.Top > 0)
                    return TaskbarLocation.Top;
                else
                    return TaskbarLocation.Bottom;
            }
            else
            {
                if (workingArea.Left > 0)
                    return TaskbarLocation.Left;
                else
                    return TaskbarLocation.Right;
            }
        }

        private void InstallMouseHook()
        {
            if (_mouseHookId != IntPtr.Zero) return;
            _mouseProc = MouseHookCallback;
            _mouseHookId = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle("user32"), 0);
        }

        private void UnhookMouse()
        {
            if (_mouseHookId == IntPtr.Zero) return;
            UnhookWindowsHookEx(_mouseHookId);
            _mouseHookId = IntPtr.Zero;
            _mouseProc = null;
        }

        private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_LBUTTONDOWN || wParam == (IntPtr)WM_RBUTTONDOWN || wParam == (IntPtr)WM_MBUTTONDOWN))
            {
                var hookStruct = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT))!;
                var physicalPoint = new Point(hookStruct.pt.x, hookStruct.pt.y);

                if (_activePopup == null || !_activePopup.IsOpen)
                {
                    UnhookMouse();
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }

                var source = PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow);
                if (source?.CompositionTarget == null)
                {
                    UnhookMouse();
                    return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
                }
                var wpfPoint = source.CompositionTarget.TransformFromDevice.Transform(physicalPoint);

                // 点击是否落在主菜单内
                bool insideMenu = false;
                var menuLeft = _activePopup.HorizontalOffset;
                var menuTop = _activePopup.VerticalOffset;
                if (_activePopup.Child is FrameworkElement menuElement)
                {
                    var menuSize = menuElement.RenderSize;
                    insideMenu = wpfPoint.X >= menuLeft && wpfPoint.X <= menuLeft + menuSize.Width &&
                                 wpfPoint.Y >= menuTop && wpfPoint.Y <= menuTop + menuSize.Height;
                }

                // 不在主菜单内时再查二级菜单（物理坐标直接比较，避免 DPI 转换误差）
                if (!insideMenu &&
                    _activePopup.Child is TrayMenuControl trayMenu &&
                    trayMenu.CurrentSubMenuPopup is Popup subPopup && subPopup.IsOpen &&
                    subPopup.Child is FrameworkElement subChild &&
                    PresentationSource.FromVisual(subChild) is HwndSource subHwnd &&
                    GetWindowRect(subHwnd.Handle, out RECT subRect))
                {
                    var pt = new System.Drawing.Point((int)hookStruct.pt.x, (int)hookStruct.pt.y);
                    if (subRect.Left <= pt.X && pt.X < subRect.Right &&
                        subRect.Top <= pt.Y && pt.Y < subRect.Bottom)
                        insideMenu = true;
                }

                if (!insideMenu)
                {
                    _activePopup.IsOpen = false;
                    UnhookMouse();
                }
            }
            return CallNextHookEx(_mouseHookId, nCode, wParam, lParam);
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }
    }
}