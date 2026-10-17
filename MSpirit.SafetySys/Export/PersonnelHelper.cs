// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

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

        /// <summary>
        /// 解析有效期区间文本为起止两个日期（导入方向，与 FormatValidPeriod 互逆）。
        /// 支持："2021-01-01 至 2041-01-01"、"2021.01.01-2041.01.01"、"20210101-20410101"、"… 至 长期" 等。
        /// 返回 false 表示无法解析（调用方应保守处理，如原样保存）。
        /// </summary>
        public static bool TryParseValidPeriod(string? period, out string start, out string end)
        {
            start = "";
            end = "";
            if (string.IsNullOrWhiteSpace(period)) return false;
            string text = period.Trim();

            // 1) 文字/符号分隔符：至、～、~、—、－（全角减号）——优先于此，避免日期内部连字符误拆
            string[] textParts = Regex.Split(text, @"\s*(?:至|～|~|—|－)\s*");
            if (textParts.Length == 2)
            {
                if (TryNormalizeDateSegment(textParts[0], out start))
                {
                    if (textParts[1].Trim() == "长期")
                    {
                        end = "长期";
                        return true;
                    }
                    if (TryNormalizeDateSegment(textParts[1], out end))
                        return true;
                }
            }

            // 2) 半角减号分隔：仅当两段均可独立解析为日期时才接受（"20210101-20410101"、"2021.01.01-2041.01.01"）
            string[] dashParts = Regex.Split(text, @"\s*-\s*");
            if (dashParts.Length == 2 &&
                TryNormalizeDateSegment(dashParts[0], out start) &&
                TryNormalizeDateSegment(dashParts[1], out end))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 将单个日期段归一化为 yyyy-MM-dd。支持 yyyy-MM-dd / yyyy/MM/dd / yyyy.MM.dd / yyyy年M月d日 / yyyyMMdd。
        /// </summary>
        private static bool TryNormalizeDateSegment(string seg, out string normalized)
        {
            normalized = "";
            string s = seg.Trim();
            if (string.IsNullOrEmpty(s)) return false;

            if (s.Length == 8 && int.TryParse(s, out _) &&
                DateTime.TryParseExact(s, "yyyyMMdd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var compact))
            {
                normalized = compact.ToString("yyyy-MM-dd");
                return true;
            }

            string[] formats = { "yyyy-MM-dd", "yyyy/MM/dd", "yyyy.MM.dd", "yyyy年M月d日", "yyyy年MM月dd日" };
            if (DateTime.TryParseExact(s, formats, CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed))
            {
                normalized = parsed.ToString("yyyy-MM-dd");
                return true;
            }
            return false;
        }
    }
}
