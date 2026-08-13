// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class WindowHelper
    {
        // 自定义消息，用于通知主窗口自行恢复（必须与 MainWindow 中的值一致）
        private const int WM_SHOWMAINWINDOW = 0x8000 + 1;

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        private const int SW_RESTORE = 9;
        private const int SW_SHOW = 5;

        /// <summary>
        /// 通过标题查找已有窗口，并发送自定义消息通知其自行恢复显示。
        /// </summary>
        /// <param name="title">窗口标题</param>
        public static void BringExistingToFront(string title)
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (GetWindowText(hWnd) == title)
                {
                    // 发送自定义消息，让主窗口自己调用 ShowWindow()
                    SendMessage(hWnd, WM_SHOWMAINWINDOW, IntPtr.Zero, IntPtr.Zero);
                    SetForegroundWindow(hWnd);
                    return false; // 停止枚举
                }
                return true;
            }, IntPtr.Zero);
        }

        private static string GetWindowText(IntPtr hWnd)
        {
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, sb.Capacity);
            return sb.ToString();
        }
    }
}