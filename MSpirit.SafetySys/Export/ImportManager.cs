// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using MS_SafetyManagementSystem.Services;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MS_SafetyManagementSystem.Export
{
    /// <summary>
    /// 导入编排管理器：协调文件解析→校验→预览→写入全流程
    /// 与具体业务模块解耦，通过 IDataWriter 注入模块特定的写入逻辑
    /// </summary>
    public class ImportManager
    {
        private readonly ExcelSmartImporter _excelImporter;
        private readonly WordSmartImporter _wordImporter;

        public ImportManager(MappingDictionaryService mappingService, string module = "Hazard")
        {
            _excelImporter = new ExcelSmartImporter(mappingService, module);
            _wordImporter = new WordSmartImporter(mappingService, module);
        }

        /// <summary>
        /// 根据文件扩展名自动分派解析器
        /// </summary>
        public ImportParseResult ParseFile(string filePath, int sheetIndex = 1)
        {
            string ext = Path.GetExtension(filePath).ToLowerInvariant();

            switch (ext)
            {
                case ".xlsx":
                    return _excelImporter.Parse(filePath, sheetIndex);
                case ".docx":
                    return _wordImporter.Parse(filePath);
                default:
                    var result = new ImportParseResult { SourceFilePath = filePath };
                    result.Warnings.Add($"不支持的文件格式: {ext}（仅支持 .xlsx 和 .docx）");
                    return result;
            }
        }

        /// <summary>
        /// 校验解析结果：行级有效性检查，生成警告
        /// </summary>
        public ImportParseResult Validate(ImportParseResult result, bool autoAssignByDate)
        {
            if (result.Rows.Count == 0)
            {
                result.Warnings.Add("没有可导入的数据行");
                return result;
            }

            int validCount = 0;
            Log.Debug("[导入调试] Validate 开始: 总行 {Total}, autoAssignByDate={AutoAssign}", result.TotalRowCount, autoAssignByDate);

            foreach (var row in result.Rows)
            {
                if (!row.IsValid)
                {
                    Log.Debug("[导入调试] 行 {RowIdx} 已在解析阶段标为无效: {Msg}", row.RowIndex, row.ValidationMessage);
                    result.InvalidRowIndices.Add(row.RowIndex);
                    continue;
                }

                bool hasText = row.TextValues.Values.Any(v => !string.IsNullOrWhiteSpace(v));
                bool hasPhotos = row.PhotoPaths.Any(kv => kv.Value != null && kv.Value.Count > 0);
                if (!hasText && !hasPhotos)
                {
                    Log.Debug("[导入调试] 行 {RowIdx} 因无文本且无图片字段被标为无效", row.RowIndex);
                    row.IsValid = false;
                    row.ValidationMessage = "无有效文本数据或图片";
                    result.InvalidRowIndices.Add(row.RowIndex);
                    continue;
                }

                if (autoAssignByDate)
                {
                    string? dateValue = row.TextValues.TryGetValue("RecordDate", out var dv) ? dv : null;
                    if (string.IsNullOrEmpty(dateValue))
                    {
                        Log.Debug("[导入调试] 行 {RowIdx} 缺少 RecordDate 字段 (已有字段: {Fields})",
                            row.RowIndex, string.Join(", ", row.TextValues.Keys));
                        row.IsValid = false;
                        row.ValidationMessage = "缺少日期字段（RecordDate），无法自动分配分组";
                        result.InvalidRowIndices.Add(row.RowIndex);
                        continue;
                    }

                    if (!DateTime.TryParse(dateValue, out _))
                    {
                        Log.Debug("[导入调试] 行 {RowIdx} 日期无法解析: \"{Date}\"", row.RowIndex, dateValue);
                        result.Warnings.Add($"第 {row.RowIndex + 1} 行的日期值 \"{dateValue}\" 无法解析，已跳过");
                        row.IsValid = false;
                        row.ValidationMessage = $"日期 \"{dateValue}\" 格式无法识别";
                        result.InvalidRowIndices.Add(row.RowIndex);
                        continue;
                    }
                }

                validCount++;
            }

            if (validCount == 0)
            {
                result.Warnings.Add("所有行均无效，无数据可导入");
            }

            Log.Debug("[导入调试] Validate 完成: validCount={Valid}", validCount);

            return result;
        }

        /// <summary>
        /// 构建导入确认消息（用于预览弹窗）
        /// </summary>
        public string BuildPreviewMessage(ImportParseResult result, bool autoAssignByDate, string? targetDateDescription)
        {
            var lines = new List<string>();

            lines.Add($"源文件：{Path.GetFileName(result.SourceFilePath)}");
            lines.Add($"文件类型：{result.SourceFileType}");
            lines.Add($"检测文档内容：识别到 {result.MatchedFieldCount} 个字段（{string.Join("、", result.FieldColumnCount.Keys)}）");
            lines.Add($"有效数据：{result.ValidRowCount} 条");
            lines.Add($"无效数据：{result.InvalidRowIndices.Count} 条");

            if (result.Warnings.Count > 0)
            {
                lines.Add($"警告：{result.Warnings.Count} 条");
                foreach (var w in result.Warnings.Take(3))
                    lines.Add($"  - {w}");
                if (result.Warnings.Count > 3)
                    lines.Add($"  ... 及其他 {result.Warnings.Count - 3} 条");
            }

            if (autoAssignByDate)
                lines.Add($"分配方式：{targetDateDescription ?? "按文件中日期自动分配分组"}");
            else
                lines.Add($"目标位置：{targetDateDescription ?? "指定日期分组"}");

            lines.Add("");
            lines.Add($"确认导入 {result.ValidRowCount} 条记录？");

            return string.Join("\n", lines);
        }

        /// <summary>
        /// 执行导入（委托给 IDataWriter）
        /// </summary>
        public (int success, int failed, List<string> errors) Import(
            ImportParseResult result,
            IDataWriter writer,
            ProjectDataService dataService,
            string targetYearMonth,
            string targetDateGroupId,
            bool autoAssignByDate)
        {
            return writer.Import(result, dataService, targetYearMonth, targetDateGroupId, autoAssignByDate);
        }
    }
}
