// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Collections.Generic;

namespace MS_SafetyManagementSystem.Models
{
    public class LinkItem
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Url { get; set; } = "";
        public int Order { get; set; }
        public string? Description { get; set; } 
    }

    public class LinkCategory
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<LinkItem> Links { get; set; } = new();
    }
}