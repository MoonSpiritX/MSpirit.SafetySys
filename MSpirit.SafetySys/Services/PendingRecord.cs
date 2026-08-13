// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using System;
using MS_SafetyManagementSystem.Helpers;

namespace MS_SafetyManagementSystem.Services
{
    /// <summary>
    /// LAN 批量录入时的临时待确认记录（OCR 结果确认前暂存）
    /// </summary>
    public class PendingRecord
    {
        public string TempId { get; set; } = "";
        public string GroupId { get; set; } = "";
        public OcrResult? FrontResult { get; set; }
        public OcrResult? BackResult { get; set; }
        public string? FrontImagePath { get; set; }
        public string? BackImagePath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>合并正背面 OCR 结果（背面字段覆盖正面空字段）</summary>
        public OcrResult GetMergedResult()
        {
            var merged = new OcrResult();
            if (FrontResult != null) MergeInto(merged, FrontResult);
            if (BackResult != null) MergeInto(merged, BackResult);
            return merged;
        }

        private static void MergeInto(OcrResult dest, OcrResult src)
        {
            if (!string.IsNullOrEmpty(src.Name) && string.IsNullOrEmpty(dest.Name)) dest.Name = src.Name;
            if (!string.IsNullOrEmpty(src.Gender) && string.IsNullOrEmpty(dest.Gender)) dest.Gender = src.Gender;
            if (!string.IsNullOrEmpty(src.Age) && string.IsNullOrEmpty(dest.Age)) dest.Age = src.Age;
            if (!string.IsNullOrEmpty(src.BirthDate) && string.IsNullOrEmpty(dest.BirthDate)) dest.BirthDate = src.BirthDate;
            if (!string.IsNullOrEmpty(src.IDNumber) && string.IsNullOrEmpty(dest.IDNumber)) dest.IDNumber = src.IDNumber;
            if (!string.IsNullOrEmpty(src.NativePlace) && string.IsNullOrEmpty(dest.NativePlace)) dest.NativePlace = src.NativePlace;
            if (!string.IsNullOrEmpty(src.ValidFrom) && string.IsNullOrEmpty(dest.ValidFrom)) dest.ValidFrom = src.ValidFrom;
            if (!string.IsNullOrEmpty(src.ValidTo) && string.IsNullOrEmpty(dest.ValidTo)) dest.ValidTo = src.ValidTo;
        }
    }
}
