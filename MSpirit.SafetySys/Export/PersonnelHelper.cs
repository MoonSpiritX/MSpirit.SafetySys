// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 人员模块共享辅助方法（消除两个 ViewModel 之间的代码重复）
    /// </summary>
    internal static class PersonnelHelper
    {
        /// <summary>
        /// 从 PersonnelRecord 构建 PersonnelStatisticsRow（含证书信息）
        /// </summary>
        public static PersonnelStatisticsRow BuildPersonnelRow(
            PersonnelRecord record,
            int rowNumber,
            string unit,
            string department,
            ProjectDataService dataService)
        {
            string certName = "", certNumber = "", certValidPeriod = "";
            try
            {
                var attachments = dataService.GetAttachmentsByRecordAndType(
                    record.Id, PersonnelConstants.AttachmentTypeCertificate);
                var cert = attachments.OrderBy(a => a.DisplayOrder).FirstOrDefault();
                if (cert != null && cert.CertNumber != null)
                {
                    certName = cert.CertName ?? "";
                    certNumber = cert.CertNumber ?? "";
                    certValidPeriod = FormatValidPeriod(cert.CertStart, cert.CertEnd);
                }
            }
            catch (Exception ex) { Log.Warning(ex, "导出人员记录时读取证书信息失败: {RecordId}", record.Id); }

            return new PersonnelStatisticsRow
            {
                RecordId = record.Id,
                GroupId = record.GroupId,
                RowNumber = rowNumber,
                Title = record.Title ?? "",
                Unit = unit,
                Department = department,
                Gender = record.Gender ?? "",
                Position = record.Position ?? "",
                BirthDate = record.BirthDate ?? "",
                Age = record.Age ?? "",
                EmployeeID = record.EmployeeID ?? "",
                NativePlace = record.NativePlace ?? "",
                EducationLevel = record.EducationLevel ?? "",
                PhoneNumber = record.PhoneNumber ?? "",
                EntryTime = record.EntryTime ?? "",
                EduTime = record.EduTime ?? "",
                ExitTime = record.ExitTime ?? "",
                IDCardDocNumber = record.IDCardDocNumber ?? "",
                IDCardValidPeriod = FormatValidPeriod(record.IDCardStart, record.IDCardEnd),
                ThreeCategoryPersonnelDisplay = record.ThreeCategoryPersonnel ? "是" : "否",
                SpecialOperationsPersonnelDisplay = record.SpecialOperationsPersonnel ? "是" : "否",
                CertName = certName,
                CertNumber = certNumber,
                CertValidPeriod = certValidPeriod,
                Remark = record.Remark ?? ""
            };
        }

        /// <summary>
        /// 格式化有效期（"yyyy-MM-dd 至 yyyy-MM-dd" 或 "yyyy-MM-dd 至 长期"）
        /// </summary>
        public static string FormatValidPeriod(string? start, string? end)
        {
            string s = (start ?? "").Trim();
            string e = (end ?? "").Trim();
            if (string.IsNullOrEmpty(s) && string.IsNullOrEmpty(e)) return "";
            if (e == "长期") return string.IsNullOrEmpty(s) ? "长期" : $"{s} 至 长期";
            return $"{s} 至 {e}";
        }
    }
}
