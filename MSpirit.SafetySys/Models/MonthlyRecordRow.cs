// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

// Models/MonthlyRecordRow.cs
using System;

namespace MS_SafetyManagementSystem.Models
{
    public class MonthlyRecordRow
    {
        public string Status { get; set; } = "";
        public int RowNumber { get; set; }           
        public string RecordId { get; set; } = "";
        public string DateGroupId { get; set; } = "";
        public string DateName { get; set; } = "";
        public DateTime? RecordDate { get; set; }
        public string ResponsibleUnit { get; set; } = "";
        public string ResponsiblePerson { get; set; } = "";
        public string Location { get; set; } = "";
        public string HazardType { get; set; } = "";
        public string HazardLevel { get; set; } = "";
        public DateTime? Deadline { get; set; }
        public DateTime? CompletedDate { get; set; }
        public string Reviewer { get; set; } = "";
        public string Description { get; set; } = "";
        public string Measure { get; set; } = "";
        public int HazardPhotoCount { get; set; }
        public int RemedyPhotoCount { get; set; }
    }
}