// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// Models/CalendarDay.cs
namespace MS_SafetyManagementSystem.Models
{
    public class CalendarDay
    {
        public DateTime Date { get; set; }
        public bool IsCurrentMonth { get; set; }
        public bool IsExists { get; set; }
        public string DisplayText => Date.Day.ToString();
        public string ToolTip => IsExists ? "已有记录，点击将创建副本" : "点击创建该日分组";
        public bool IsEnabled => IsCurrentMonth; // 仅当月可点
    }
}