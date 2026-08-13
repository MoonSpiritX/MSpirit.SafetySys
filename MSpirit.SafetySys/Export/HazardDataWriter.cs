// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Helpers;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 隐患模块数据写入器：将通用解析结果写入 Hazard 模块的 Record 和 Attachment 表
    /// </summary>
    public class HazardDataWriter : IDataWriter
    {
        public (int success, int failed, List<string> errors) Import(
            ImportParseResult result,
            ProjectDataService dataService,
            string targetYearMonth,
            string targetDateGroupId,
            bool autoAssignByDate)
        {
            int success = 0;
            int failed = 0;
            var errors = new List<string>();
            string attachmentsRoot = "";

            try
            {
                attachmentsRoot = dataService.GetProjectsDateRoot();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "获取附件根目录失败");
            }

            Log.Debug("[导入调试] HazardDataWriter 开始导入: 总行 {Total}, 有效行 {Valid}, autoAssignByDate={AutoAssign}, targetDateGroupId={GroupId}, targetYearMonth={YearMonth}",
                result.Rows.Count, result.Rows.Count(r => r.IsValid), autoAssignByDate, targetDateGroupId, targetYearMonth);


            // 同一分组内 DisplayIndex 唯一递增（取目标分组当前最大值 + 1）
            var nextIndexByGroup = new Dictionary<string, int>();
            foreach (var row in result.Rows)
            {
                if (!row.IsValid)
                {
                    failed++;
                    continue;
                }

                try
                {
                    // 1. 确定目标日期分组
                    string recordYearMonth;
                    string targetGroupId;

                    if (autoAssignByDate)
                    {
                        string? dateStr = row.TextValues.TryGetValue("RecordDate", out var ds) ? ds : null;
                        if (string.IsNullOrEmpty(dateStr) || !TryParseDate(dateStr, out recordYearMonth, out int day))
                        {
                            errors.Add($"第 {row.RowIndex + 1} 行：日期无效 '{dateStr}'");
                            failed++;
                            continue;
                        }
                        targetGroupId = dataService.GetOrCreateDateGroup(recordYearMonth, day, null);
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(targetDateGroupId))
                        {
                            errors.Add("未指定目标日期分组 ID");
                            failed++;
                            continue;
                        }
                        recordYearMonth = targetYearMonth ?? "";
                        targetGroupId = targetDateGroupId;
                    }

                    // 2. 构建 Record 对象
                    if (!nextIndexByGroup.TryGetValue(targetGroupId, out int nextIndex))
                    {
                        nextIndex = dataService.GetMaxDisplayIndex(targetGroupId) + 1;
                    }
                    nextIndexByGroup[targetGroupId] = nextIndex + 1;
                    var record = new Record
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        GroupId = targetGroupId,
                        YearMonth = recordYearMonth,
                        Title = GenerateTitle(row.TextValues),
                        Description = row.TextValues.TryGetValue("Description", out var desc) ? desc : "",
                        Measure = row.TextValues.TryGetValue("Measure", out var meas) ? meas : "",
                        ResponsibleUnit = row.TextValues.TryGetValue("ResponsibleUnit", out var ru) ? ru : "",
                        ResponsiblePerson = row.TextValues.TryGetValue("ResponsiblePerson", out var rp) ? rp : "",
                        Location = row.TextValues.TryGetValue("Location", out var loc) ? loc : "",
                        HazardType = row.TextValues.TryGetValue("HazardType", out var ht) ? ht : "",
                        HazardLevel = row.TextValues.TryGetValue("HazardLevel", out var hl) ? hl : "",
                        Reviewer = row.TextValues.TryGetValue("Reviewer", out var rev) ? rev : "",
                        Status = row.TextValues.TryGetValue("Status", out var st) ? st : "",
                        DisplayIndex = nextIndex
                    };

                    if (row.TextValues.TryGetValue("Deadline", out var dl) && !string.IsNullOrEmpty(dl) &&
                        DateTime.TryParse(dl, out var deadline))
                    {
                        record.Deadline = deadline;
                    }
                    if (row.TextValues.TryGetValue("CompletedDate", out var cd) && !string.IsNullOrEmpty(cd) &&
                        DateTime.TryParse(cd, out var completed))
                    {
                        record.CompletedDate = completed;
                    }

                    // 3. 写入记录
                    if (!dataService.AddRecord(record))
                    {
                        errors.Add($"第 {row.RowIndex + 1} 行：写入记录失败");
                        failed++;
                        continue;
                    }

                    // 4. 处理图片附件
                    int hazardOrder = 1;
                    int remedyOrder = 1;

                    foreach (var kv in row.PhotoPaths)
                    {
                        string photoField = kv.Key;
                        var photoPaths = kv.Value;

                        for (int i = 0; i < photoPaths.Count; i++)
                        {
                            string photoPath = photoPaths[i];
                            if (!File.Exists(photoPath)) continue;

                            try
                            {
                                string attachmentType = photoField == "HazardPhoto" ? "Hazard" : "Remedy";
                                int displayOrder = photoField == "HazardPhoto" ? hazardOrder++ : remedyOrder++;

                                if (!string.IsNullOrEmpty(attachmentsRoot))
                                {
                                    using var fs = File.OpenRead(photoPath);
                                    // 在后台线程执行异步方法，避免在调用线程有 SynchronizationContext 时死锁
                                    var paths = Task.Run(() => ImageHelper.SaveImageToRecordAsync(
                                        fs,
                                        record.Id,
                                        Path.GetFileName(photoPath),
                                        attachmentsRoot,
                                        dataService.ProjectId,
                                        "Hazard",
                                        recordYearMonth,
                                        attachmentType,
                                        displayOrder
                                    )).GetAwaiter().GetResult();

                                    var attachment = new Attachment
                                    {
                                        Id = Guid.NewGuid().ToString("N"),
                                        RecordId = record.Id,
                                        FileName = Path.GetFileName(photoPath),
                                        RelativePath = paths.originalPath,
                                        ThumbnailPath = paths.thumbPath,
                                        FileSize = new FileInfo(photoPath).Length,
                                        MimeType = "image/jpeg",
                                        AttachmentType = attachmentType,
                                        DisplayOrder = displayOrder
                                    };
                                    dataService.AddAttachment(attachment);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Warning(ex, "导入图片附件失败: {Path}", photoPath);
                                errors.Add($"第 {row.RowIndex + 1} 行：图片 {photoPath} 导入失败: {ex.Message}");
                            }
                        }
                    }

                    success++;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "导入第 {RowIndex} 行时发生异常", row.RowIndex);
                    errors.Add($"第 {row.RowIndex + 1} 行：{ex.Message}");
                    failed++;
                }
            }

            Log.Debug("[导入调试] HazardDataWriter 导入完成: success={Success}, failed={Failed}, errors={ErrCount}",
                success, failed, errors.Count);
            return (success, failed, errors);
        }

        private string GenerateTitle(Dictionary<string, string> textValues)
        {
            string location = textValues.TryGetValue("Location", out var loc) ? loc : "";
            string hazardType = textValues.TryGetValue("HazardType", out var ht) ? ht : "";
            string description = textValues.TryGetValue("Description", out var desc) ? desc : "";

            if (!string.IsNullOrEmpty(location) && !string.IsNullOrEmpty(hazardType))
                return $"{location}-{hazardType}";
            if (!string.IsNullOrEmpty(location))
                return location;
            if (!string.IsNullOrEmpty(hazardType))
                return hazardType;
            if (!string.IsNullOrEmpty(description))
                return description.Length > 50 ? description[..50] + "…" : description;
            return "导入记录";
        }

        private bool TryParseDate(string dateStr, out string yearMonth, out int day)
        {
            yearMonth = "";
            day = 1;
            if (string.IsNullOrWhiteSpace(dateStr)) return false;

            string[] formats = {
                "yyyy-MM-dd", "yyyy/MM/dd", "yyyy-MM-dd HH:mm:ss", "yyyy/MM/dd HH:mm:ss",
                "yyyy年MM月dd日", "yyyy年M月d日",
                "MM/dd/yyyy", "M/d/yyyy",
                "yyyyMMdd"
            };

            if (DateTime.TryParse(dateStr, out DateTime dt))
            {
                yearMonth = dt.ToString("yyyy-MM");
                day = dt.Day;
                return true;
            }

            foreach (var fmt in formats)
            {
                if (DateTime.TryParseExact(dateStr, fmt, null,
                    System.Globalization.DateTimeStyles.None, out dt))
                {
                    yearMonth = dt.ToString("yyyy-MM");
                    day = dt.Day;
                    return true;
                }
            }

            var match = Regex.Match(dateStr, @"(\d{4})\D?(\d{1,2})\D?(\d{1,2})");
            if (match.Success &&
                int.TryParse(match.Groups[1].Value, out int y) &&
                int.TryParse(match.Groups[2].Value, out int m) &&
                int.TryParse(match.Groups[3].Value, out int d))
            {
                try
                {
                    var parsed = new DateTime(y, m, d);
                    yearMonth = parsed.ToString("yyyy-MM");
                    day = parsed.Day;
                    return true;
                }
                catch (Exception ex) { Log.Debug(ex, "导出日期无效，回退处理: {Text}", match.Value); }
            }

            return false;
        }
    }
}
