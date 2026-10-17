// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

namespace MS_SafetyManagementSystem.Models
{
    public class Group
    {
        public string Id { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public string Module { get; set; } = "";   // "Hazard" 或 "Personnel"
        public string? ParentId { get; set; }
        public string Name { get; set; } = "";
        public int Order { get; set; }
        public string? CreatedDate { get; set; }   // 可选
        public string? Tag { get; set; }   // 标签，可为 null
    }
}