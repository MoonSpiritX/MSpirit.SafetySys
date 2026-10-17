// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Models
{
    public class AppSettings
    {
        public string ImageViewerType { get; set; } = "Internal";
        public string ImageViewerStartMode { get; set; } = "FullScreen";
        public string CurrentModule { get; set; } = "Hazard";
        // 新增：隐患类型选择器模式 ("FullWindow" / "DropdownMenu")
        public string HazardTypeSelectorMode { get; set; } = "FullWindow";
    }
}