// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Common
{
    public static class ModuleNames
    {
        public const string Hazard = "Hazard";
        public const string Personnel = "Personnel";
    }

    public static class ImageViewerType
    {
        public const string Internal = "Internal";
        public const string System = "System";
    }

    public static class ImageViewerStartMode
    {
        public const string FullScreen = "FullScreen";
        public const string Normal = "Normal";
    }

    /// <summary>
    /// LAN 服务端口常量（唯一修改处）：改端口只需改 <see cref="Start"/>，
    /// 探测上限与防火墙放行终点自动跟随。引用方：
    ///  - 三个录入窗口 LanPortProbe.FindAvailable 起点与"端口占用"提示文案（LanPersonnelEntryWindow /
    ///    LanHazardEntryWindow / PhotoTransferWindow）
    ///  - 防火墙引导窗手动步骤的放行端口区间（LanFirewallPromptWindow）
    /// </summary>
    public static class LanPorts
    {
        /// <summary>LAN 服务端口探测起点（三个录入窗口 LanPortProbe 起点）</summary>
        public const int Start = 8000;

        /// <summary>端口探测上限（Start + 99 ，与窗口"均被占用"提示一致）</summary>
        public const int ProbeEnd = Start + 99;

        /// <summary>防火墙放行端口终点（探测上限留 1 位余量 → Start + 100）</summary>
        public const int FirewallEnd = Start + 100;
    }
}