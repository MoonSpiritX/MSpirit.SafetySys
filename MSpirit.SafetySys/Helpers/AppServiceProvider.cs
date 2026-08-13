// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;

namespace MS_SafetyManagementSystem.Helpers
{
    public static class AppServiceProvider
    {
        public static CommonHazardTypeService? CommonHazardTypeService { get; set; }
        public static SettingsService? SettingsService { get; set; }
    }
}