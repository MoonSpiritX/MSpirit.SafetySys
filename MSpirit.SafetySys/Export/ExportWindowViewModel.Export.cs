// Copyright (C) 2026 AXin（斜月Spirit）
// SPDX-License-Identifier: AGPL-3.0-or-later

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using MS_SafetyManagementSystem.Common;
using MS_SafetyManagementSystem.Models;
using MS_SafetyManagementSystem.Peoplemanagement;
using MS_SafetyManagementSystem.Services;
using MS_SafetyManagementSystem.ViewModels;
using MS_SafetyManagementSystem.Views;
using Serilog;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace MS_SafetyManagementSystem.Export
{
    public partial class ExportWindowViewModel : ObservableObject
    {
        #region 文件操作命令

        private void ExecuteOpenFile()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择导出模板文件",
                Filter = "模板文件|*.xlsx;*.docx|Excel 文件|*.xlsx|Word 文件|*.docx",
                FilterIndex = 1,
                RestoreDirectory = true,
                InitialDirectory = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}"
            };
            if (dialog.ShowDialog() == true)
                SelectedTemplatePath = dialog.FileName;
        }

        private void ExecuteRemoveFile() => SelectedTemplatePath = string.Empty;

        private void ExecutePasteFile()
        {
            var allowedExtensions = new[] { ".xlsx", ".docx" };

            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList();
                if (files.Count > 0)
                {
                    string filePath = files[0]!;
                    string ext = System.IO.Path.GetExtension(filePath).ToLower();
                    if (allowedExtensions.Contains(ext))
                    {
                        SelectedTemplatePath = filePath;
                        return;
                    }
                    else
                    {
                        MessageDialog.Show(_ownerWindow, "文件类型不支持。\n导出目标文件类型仅限于 .xlsx 和 .docx 格式", "提示");
                        return;
                    }
                }
            }

            if (Clipboard.ContainsText())
            {
                string text = Clipboard.GetText().Trim();
                text = text.Trim('"', '\'');
                if ((text.Contains(":\\") || text.StartsWith("\\\\")))
                {
                    string ext = System.IO.Path.GetExtension(text).ToLower();
                    if (allowedExtensions.Contains(ext))
                    {
                        SelectedTemplatePath = text;
                        return;
                    }
                    else
                    {
                        MessageDialog.Show(_ownerWindow, "文件类型不支持。\n导出目标文件类型仅限于 .xlsx 和 .docx 格式", "提示");
                        return;
                    }
                }
            }

            MessageDialog.Show(_ownerWindow, "剪贴板中没有有效的文件路径。\n请先复制文件或文件路径。", "提示");
        }

        #endregion

        #region 清空命令

        private void ExecuteClearYearMonth()
        {
            SelectedYearMonthText = "请选择年月";
            SelectedDateGroupText = "请选择日期";
            UpdateRangeDescription();
        }

        private bool CanClearDateGroup()
        {
            return !string.IsNullOrEmpty(SelectedDateGroupText) && SelectedDateGroupText != "请选择日期";
        }

        private void ExecuteClearDateGroup()
        {
            SelectedDateGroupText = "请选择日期";
            UpdateRangeDescription();
        }

        private void ExecuteClearCustomRange()
        {
            CustomStartDateStr = "";
            CustomEndDateStr = "";
            StartDateTag = "";
            EndDateTag = "";
            UpdateRangeDescription();
            OnPropertyChanged(nameof(StartDateDisplay));
            OnPropertyChanged(nameof(EndDateDisplay));
            OnPropertyChanged(nameof(StartDateToolTip));
            OnPropertyChanged(nameof(EndDateToolTip));
        }

        #endregion

        #region 导出入口

        private async Task ExecuteStartExportAsync()
        {
            if (IsExporting) return;

            // 界面打开期间可能切换过项目：刷新分组缓存，保证下拉与数据来自同一项目
            RefreshGroupsForCurrentProject();

            if (IsImportMode)
            {
                await ExecuteStartImportAsync();
                return;
            }

            if (IsFullMode)
                await ExecuteExportWithModeAsync("Full");
            else if (IsNoImageMode)
                await ExecuteExportWithModeAsync("NoImage");
            else if (IsPlaceholderMode)
                await ExecuteExportWithModeAsync("OnlyImage");
            else
                MessageDialog.Show(_ownerWindow, "请先选择导出模式", "提示");
        }

        private async Task ExecuteExportWithModeAsync(string mode)
        {
            if (IsPersonnel)
            {
                await ExecutePersonnelExportAsync(mode);
                return;
            }
            var dataRows = GetDataRowsForExport();
            if (dataRows == null || dataRows.Count == 0)
            {
                MessageDialog.Show(_ownerWindow, "没有符合条件的数据", "提示");
                return;
            }

            string templatePath = SelectedTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !global::System.IO.File.Exists(templatePath))
            {
                MessageDialog.Show(_ownerWindow, "请先选择有效的模板文件", "提示");
                return;
            }

            string ext = Path.GetExtension(templatePath).ToLower();
            if (ext == ".docx")
            {
                var mainWindow = Application.Current.MainWindow as MainWindow;
                var projectDataService = mainWindow?.ViewModel.GetProjectDataService();
                if (projectDataService == null)
                {
                    MessageDialog.Show(_ownerWindow, "无法获取项目数据服务", "错误");
                    return;
                }

                var dataPathService = new DataPathService();
                var mappingService = new MappingDictionaryService(dataPathService.RootPath);
                string attachmentsRoot = GetAttachmentsRootPath();
                var exporter = new WordSmartExporter(mappingService, attachmentsRoot, "Hazard");
                var provider = new HazardExportDataProvider(dataRows, projectDataService);

                string outputFileName = Path.GetFileNameWithoutExtension(templatePath) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".docx";
                string outputPath = Path.Combine(Path.GetDirectoryName(templatePath) ?? "", outputFileName);

                IsExporting = true;
                try
                {
                    var result = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode));
                    IsExporting = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (result.Success)
                            MessageDialog.Show(_ownerWindow, $"导出成功！文件已保存至：{outputPath}", "完成");
                        else
                            MessageDialog.Show(_ownerWindow, $"导出失败：{result.ErrorMessage}", "错误");
                    });
                }
                catch (Exception ex)
                {
                    IsExporting = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageDialog.Show(_ownerWindow, $"导出异常：{ex.Message}", "错误"));
                }
            }
            else if (ext == ".xlsx")
            {
                var dataPathService = new DataPathService();
                var mappingService = new MappingDictionaryService(dataPathService.RootPath);
                var mainWindowForExcel = Application.Current.MainWindow as MainWindow;
                var projectDataService = mainWindowForExcel?.ViewModel.GetProjectDataService();
                string attachmentsRoot = GetAttachmentsRootPath();
                var exporter = new ExcelSmartExporter(mappingService, attachmentsRoot, "Hazard");
                var provider = new HazardExportDataProvider(dataRows, projectDataService!);

                string outputFileName = Path.GetFileNameWithoutExtension(templatePath) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ".xlsx";
                string outputPath = Path.Combine(Path.GetDirectoryName(templatePath) ?? "", outputFileName);

                string sheetName = ExcelSheetIndex;
                if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "1";

                IsExporting = true;
                try
                {
                    var result = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode, sheetName));
                    IsExporting = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (result.Success)
                            MessageDialog.Show(_ownerWindow, $"导出成功！文件已保存至：{outputPath}", "完成");
                        else
                            MessageDialog.Show(_ownerWindow, $"导出失败：{result.ErrorMessage}", "错误");
                    });
                }
                catch (Exception ex)
                {
                    IsExporting = false;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        MessageDialog.Show(_ownerWindow, $"导出异常：{ex.Message}", "错误"));
                }
            }
            else
            {
                MessageDialog.Show(_ownerWindow, "不支持的文件格式", "提示");
            }
        }

        #endregion

        #region 人员导出

        private async Task ExecutePersonnelExportAsync(string mode)
        {
            var dataRows = GetPersonnelRowsForExport();
            if (dataRows == null || dataRows.Count == 0)
            {
                MessageDialog.Show(_ownerWindow, "没有符合条件的数据", "提示");
                return;
            }

            string templatePath = SelectedTemplatePath;
            if (string.IsNullOrEmpty(templatePath) || !File.Exists(templatePath))
            {
                MessageDialog.Show(_ownerWindow, "请先选择有效的模板文件", "提示");
                return;
            }

            string ext = Path.GetExtension(templatePath).ToLower();
            if (ext != ".docx" && ext != ".xlsx")
            {
                MessageDialog.Show(_ownerWindow, "不支持的文件格式", "提示");
                return;
            }

            string contextName = SelectedYearMonthText ?? "人员统计";
            if (!string.IsNullOrEmpty(SelectedDateGroupText) && SelectedDateGroupText != "请选择日期")
                contextName += "-" + SelectedDateGroupText.Split(' ')[0];

            var dataPathService = new DataPathService();
            var mappingService = new MappingDictionaryService(dataPathService.RootPath);
            var provider = new PersonnelExportDataProvider(dataRows, contextName);

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var projectDataService = mainWindow?.ViewModel.GetProjectDataService();

            string outputFileName = Path.GetFileNameWithoutExtension(templatePath) + "_" + DateTime.Now.ToString("yyyyMMddHHmmss") + ext;
            string outputPath = Path.Combine(Path.GetDirectoryName(templatePath) ?? "", outputFileName);

            IsExporting = true;
            try
            {
                (bool success, string? errorMessage) result2;
                if (ext == ".docx")
                {
                    string attachmentsRoot = GetAttachmentsRootPath();
                    var exporter = new WordSmartExporter(mappingService, attachmentsRoot, "Personnel");
                    result2 = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode));
                }
                else
                {
                    string sheetName = ExcelSheetIndex;
                    if (string.IsNullOrWhiteSpace(sheetName)) sheetName = "1";
                    var exporter = new ExcelSmartExporter(mappingService, "", "Personnel");
                    result2 = await Task.Run(() => exporter.Export(templatePath, outputPath, provider, mode, sheetName));
                }

                IsExporting = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (result2.success)
                        MessageDialog.Show(_ownerWindow, $"导出成功！文件已保存至：{outputPath}", "完成");
                    else
                        MessageDialog.Show(_ownerWindow, $"导出失败：{result2.errorMessage}", "错误");
                });
            }
            catch (Exception ex)
            {
                IsExporting = false;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageDialog.Show(_ownerWindow, $"导出异常：{ex.Message}", "错误"));
            }
        }

        #endregion

        #region 数据行收集

        private List<MonthlyRecordRow> GetDataRowsForExport()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null) return new List<MonthlyRecordRow>();

            if (IsMonthlyRangeSelected)
            {
                if (string.IsNullOrEmpty(SelectedYearMonthText) || SelectedYearMonthText == "请选择年月")
                    return new List<MonthlyRecordRow>();

                string yearMonthName = SelectedYearMonthText;
                var yearMonthGroups = dataService.GetGroups(ModuleNames.Hazard, null);
                var yearMonthGroup = yearMonthGroups.FirstOrDefault(g => g.Name == yearMonthName);
                if (yearMonthGroup == null) return new List<MonthlyRecordRow>();

                var allDateGroups = dataService.GetGroups(ModuleNames.Hazard, yearMonthGroup.Id)
                    .OrderBy(g => ParseDateValue(g.Name))
                    .ToList();

                List<Group> dateGroupsToProcess;
                if (!string.IsNullOrEmpty(SelectedDateGroupText) && SelectedDateGroupText != "请选择日期")
                {
                    string selectedName = SelectedDateGroupText.Split(' ')[0];
                    var targetGroup = allDateGroups.FirstOrDefault(g => g.Name == selectedName);
                    if (targetGroup == null) return new List<MonthlyRecordRow>();
                    dateGroupsToProcess = new List<Group> { targetGroup };
                }
                else
                {
                    dateGroupsToProcess = allDateGroups;
                }

                var allRows = new List<MonthlyRecordRow>();
                foreach (var dateGroup in dateGroupsToProcess)
                {
                    var records = dataService.GetRecordsByGroup(dateGroup.Id);
                    if (records.Count == 0) continue;

                    var recordIds = records.Select(r => r.Id).ToList();
                    var allAttachments = dataService.GetAttachmentsByRecordIds(recordIds);
                    DateTime? groupDate = ParseDateFromGroupName(dateGroup.Name, yearMonthName);

                    foreach (var record in records)
                    {
                        int hazardPhotoCount = allAttachments.Count(a => a.RecordId == record.Id && a.AttachmentType == "Hazard");
                        int remedyPhotoCount = allAttachments.Count(a => a.RecordId == record.Id && a.AttachmentType == "Remedy");

                        var row = new MonthlyRecordRow
                        {
                            RecordId = record.Id,
                            DateGroupId = dateGroup.Id,
                            DateName = dateGroup.Name,
                            RecordDate = groupDate,
                            ResponsibleUnit = record.ResponsibleUnit,
                            ResponsiblePerson = record.ResponsiblePerson,
                            Location = record.Location,
                            HazardType = record.HazardType,
                            HazardLevel = record.HazardLevel,
                            Deadline = record.Deadline,
                            CompletedDate = record.CompletedDate,
                            Reviewer = record.Reviewer,
                            Description = record.Description,
                            Measure = record.Measure,
                            HazardPhotoCount = hazardPhotoCount,
                            RemedyPhotoCount = remedyPhotoCount,
                            Status = ComputeStatus(record, hazardPhotoCount, groupDate)
                        };
                        row.RowNumber = allRows.Count + 1;
                        allRows.Add(row);
                    }
                }
                return allRows;
            }
            else if (IsCustomRangeSelected)
            {
                if (!ParseDateFromString(CustomStartDateStr, out DateTime startDate) ||
                    !ParseDateFromString(CustomEndDateStr, out DateTime endDate))
                    return new List<MonthlyRecordRow>();

                var yearMonthGroups = dataService.GetGroups(ModuleNames.Hazard, null);
                var allRows = new List<MonthlyRecordRow>();

                foreach (var ymGroup in yearMonthGroups)
                {
                    var ymMatch = System.Text.RegularExpressions.Regex.Match(ymGroup.Name, @"(\d{4})年(\d{1,2})月");
                    if (!ymMatch.Success) continue;
                    int year = int.Parse(ymMatch.Groups[1].Value);
                    int month = int.Parse(ymMatch.Groups[2].Value);

                    var dateGroups = dataService.GetGroups(ModuleNames.Hazard, ymGroup.Id);
                    foreach (var dateGroup in dateGroups)
                    {
                        var dayMatch = System.Text.RegularExpressions.Regex.Match(dateGroup.Name, @"(\d{1,2})月(\d{1,2})日");
                        if (!dayMatch.Success) continue;
                        int day = int.Parse(dayMatch.Groups[2].Value);

                        DateTime groupDate;
                        try { groupDate = new DateTime(year, month, day); }
                        catch { continue; }

                        if (groupDate < startDate || groupDate > endDate) continue;

                        var records = dataService.GetRecordsByGroup(dateGroup.Id);
                        if (records.Count == 0) continue;

                        var recordIds = records.Select(r => r.Id).ToList();
                        var allAttachments = dataService.GetAttachmentsByRecordIds(recordIds);

                        foreach (var record in records)
                        {
                            int hazardPhotoCount = allAttachments.Count(a => a.RecordId == record.Id && a.AttachmentType == "Hazard");
                            int remedyPhotoCount = allAttachments.Count(a => a.RecordId == record.Id && a.AttachmentType == "Remedy");

                            var row = new MonthlyRecordRow
                            {
                                RecordId = record.Id,
                                DateGroupId = dateGroup.Id,
                                DateName = dateGroup.Name,
                                RecordDate = groupDate,
                                ResponsibleUnit = record.ResponsibleUnit,
                                ResponsiblePerson = record.ResponsiblePerson,
                                Location = record.Location,
                                HazardType = record.HazardType,
                                HazardLevel = record.HazardLevel,
                                Deadline = record.Deadline,
                                CompletedDate = record.CompletedDate,
                                Reviewer = record.Reviewer,
                                Description = record.Description,
                                Measure = record.Measure,
                                HazardPhotoCount = hazardPhotoCount,
                                RemedyPhotoCount = remedyPhotoCount,
                                Status = ComputeStatus(record, hazardPhotoCount, groupDate)
                            };
                            allRows.Add(row);
                        }
                    }
                }

                allRows = allRows.OrderBy(r => r.RecordDate).ToList();
                for (int i = 0; i < allRows.Count; i++)
                    allRows[i].RowNumber = i + 1;
                return allRows;
            }

            return new List<MonthlyRecordRow>();
        }

        private List<PersonnelStatisticsRow> GetPersonnelRowsForExport()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null) return new List<PersonnelStatisticsRow>();

            if (string.IsNullOrEmpty(SelectedYearMonthText) || SelectedYearMonthText == "请选择年月")
                return new List<PersonnelStatisticsRow>();

            string unitName = SelectedYearMonthText;
            var units = dataService.GetGroups(ModuleNames.Personnel, null);
            var unitGroup = units.FirstOrDefault(g => g.Name == unitName);
            if (unitGroup == null) return new List<PersonnelStatisticsRow>();

            string? selectedDeptName = null;
            if (!string.IsNullOrEmpty(SelectedDateGroupText) && SelectedDateGroupText != "请选择日期")
                selectedDeptName = SelectedDateGroupText.Split(' ')[0];

            var rows = new List<PersonnelStatisticsRow>();
            int index = 1;

            var departments = dataService.GetGroups(ModuleNames.Personnel, unitGroup.Id);
            foreach (var dept in departments)
            {
                if (!string.IsNullOrEmpty(selectedDeptName) && dept.Name != selectedDeptName) continue;

                var monthGroups = dataService.GetGroups(ModuleNames.Personnel, dept.Id);
                foreach (var mg in monthGroups)
                {
                    var records = dataService.GetPersonnelRecordsByGroup(mg.Id);
                    foreach (var r in records)
                        rows.Add(PersonnelHelper.BuildPersonnelRow(r, index++, unitName, dept.Name, dataService));
                }

                var deptRecords = dataService.GetPersonnelRecordsByGroup(dept.Id);
                foreach (var r in deptRecords)
                    rows.Add(PersonnelHelper.BuildPersonnelRow(r, index++, unitName, dept.Name, dataService));
            }

            if (string.IsNullOrEmpty(selectedDeptName))
            {
                var unitRecords = dataService.GetPersonnelRecordsByGroup(unitGroup.Id);
                foreach (var r in unitRecords)
                    rows.Add(PersonnelHelper.BuildPersonnelRow(r, index++, "", "", dataService));
            }

            return rows;
        }

        private string ComputeStatus(Record record, int hazardPhotoCount, DateTime? recordDate)
        {
            bool isValidRecord = !string.IsNullOrEmpty(record.Description) || hazardPhotoCount > 0;
            bool hasBasicInfo = !string.IsNullOrEmpty(record.ResponsibleUnit) ||
                                !string.IsNullOrEmpty(record.ResponsiblePerson) ||
                                !string.IsNullOrEmpty(record.Location) ||
                                !string.IsNullOrEmpty(record.HazardType) ||
                                !string.IsNullOrEmpty(record.HazardLevel) ||
                                record.Deadline.HasValue;

            if (isValidRecord)
            {
                bool isCompleted = record.CompletedDate.HasValue && !string.IsNullOrEmpty(record.Measure);
                if (isCompleted) return "完成";

                DateTime? deadlineToCheck = record.Deadline;
                if (!deadlineToCheck.HasValue && recordDate.HasValue)
                    deadlineToCheck = recordDate.Value.AddDays(3);

                if (deadlineToCheck.HasValue)
                {
                    if (DateTime.Now.Date > deadlineToCheck.Value.Date) return "超期";
                    else return "进行";
                }
                else return "进行";
            }
            else if (hasBasicInfo) return "无效";
            else return "";
        }

        private string GetAttachmentsRootPath()
        {
            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel?.GetProjectDataService();
            if (dataService == null)
            {
                Log.Error("GetAttachmentsRootPath: 无法获取 ProjectDataService");
                return "";
            }
            try
            {
                string path = dataService.GetProjectsDateRoot();
                Log.Debug("GetAttachmentsRootPath: 返回 {Path}", path);
                return path;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "GetAttachmentsRootPath: 获取路径失败");
                return "";
            }
        }

        #endregion

        #region 导入

        private async Task ExecuteStartImportAsync()
        {
            string filePath = SelectedTemplatePath;
            if (string.IsNullOrEmpty(filePath) || !global::System.IO.File.Exists(filePath))
            {
                MessageDialog.Show(_ownerWindow, "请先选择有效的源文件", "提示");
                return;
            }

            var dataPathService = new DataPathService();
            var mappingService = new MappingDictionaryService(dataPathService.RootPath);
            var manager = new ImportManager(mappingService, "Hazard");
            IDataWriter writer = new HazardDataWriter();

            var mainWindow = Application.Current.MainWindow as MainWindow;
            var dataService = mainWindow?.ViewModel.GetProjectDataService();
            if (dataService == null)
            {
                MessageDialog.Show(_ownerWindow, "无法获取项目数据服务", "错误");
                return;
            }

            IsExporting = true;
            ImportParseResult? parseResult = null;

            try
            {
                int sheetIndex = 1;
                if (int.TryParse(ExcelSheetIndex, out int si) && si > 0) sheetIndex = si;

                parseResult = await Task.Run(() => manager.ParseFile(filePath, sheetIndex));
                if (parseResult == null || parseResult.TotalRowCount == 0)
                {
                    IsExporting = false;
                    MessageDialog.Show(_ownerWindow, "未能从文件中解析出有效数据", "提示");
                    return;
                }

                bool autoAssignByDate = !IsMonthlyRangeSelected;
                parseResult = manager.Validate(parseResult, autoAssignByDate);

                if (parseResult.ValidRowCount == 0)
                {
                    IsExporting = false;
                    string warnMsg;
                    if (autoAssignByDate && !parseResult.FieldColumnCount.ContainsKey("RecordDate"))
                    {
                        warnMsg = "按“按文件中日期自动分配”导入时，文档中需包含可识别的隐患日期列（支持：日期、检查日期、记录日期、问题日期、隐患日期、排查时间、排查日期、发现日期、发现时间）。\n" +
                                  "当前文档未解析到日期列，因此没有可导入的数据。\n" +
                                  "如需继续导入，请先在程序内新建要导入的年月及日期分组，并选择“导入到指定的日期分组”后重试。";
                    }
                    else
                    {
                        warnMsg = "所有行均无效";
                        if (parseResult.Warnings.Count > 0)
                            warnMsg += "\n" + string.Join("\n", parseResult.Warnings.Take(5));
                    }
                    MessageDialog.Show(_ownerWindow, warnMsg, "校验失败");
                    return;
                }

                string targetDesc = "";
                if (!autoAssignByDate)
                {
                    targetDesc = string.IsNullOrEmpty(SelectedDateGroupText) || SelectedDateGroupText == "请选择日期"
                        ? (string.IsNullOrEmpty(SelectedYearMonthText) || SelectedYearMonthText == "请选择年月"
                            ? "未选择" : SelectedYearMonthText)
                        : $"{SelectedYearMonthText}{SelectedDateGroupText.Split(' ')[0]}";
                }
                string preview = manager.BuildPreviewMessage(parseResult, autoAssignByDate, targetDesc);

                IsExporting = false;

                bool confirmed = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new ConfirmDialog(preview, "数据导入预览");
                    dialog.Owner = _ownerWindow;
                    return dialog.ShowDialog() == true;
                });

                if (!confirmed) return;

                IsExporting = true;

                string targetYearMonth = "";
                string targetGroupId = "";

                if (!autoAssignByDate)
                {
                    if (string.IsNullOrEmpty(SelectedYearMonthText) || SelectedYearMonthText == "请选择年月" ||
                        string.IsNullOrEmpty(SelectedDateGroupText) || SelectedDateGroupText == "请选择日期")
                    {
                        IsExporting = false;
                        MessageDialog.Show(_ownerWindow, "导入到指定日期分组模式需要同时选择年月和日期", "提示");
                        return;
                    }

                    string selectedDateName = SelectedDateGroupText.Split(' ')[0];
                    var ymMatch = System.Text.RegularExpressions.Regex.Match(SelectedYearMonthText ?? "", @"(\d{4})年(\d{1,2})月");
                    if (ymMatch.Success)
                    {
                        targetYearMonth = $"{ymMatch.Groups[1].Value}-{int.Parse(ymMatch.Groups[2].Value):D2}";
                        if (int.TryParse(ymMatch.Groups[2].Value, out int month))
                        {
                            var dayMatch = System.Text.RegularExpressions.Regex.Match(selectedDateName, @"(\d{1,2})月(\d{1,2})日");
                            if (dayMatch.Success && int.TryParse(dayMatch.Groups[2].Value, out int day))
                                targetGroupId = dataService.GetOrCreateDateGroup(targetYearMonth, day, null);
                        }
                    }

                    if (string.IsNullOrEmpty(targetGroupId))
                    {
                        IsExporting = false;
                        MessageDialog.Show(_ownerWindow, "未找到目标日期分组，请重新选择年月和日期", "提示");
                        return;
                    }
                }

                var resultStats = await Task.Run(() => manager.Import(
                    parseResult, writer, dataService, targetYearMonth, targetGroupId, autoAssignByDate));

                IsExporting = false;

                string resultMsg = $"导入完成！\n成功：{resultStats.success} 条\n失败：{resultStats.failed} 条";
                if (resultStats.errors.Count > 0)
                {
                    resultMsg += "\n\n错误详情：\n" + string.Join("\n", resultStats.errors.Take(10));
                    if (resultStats.errors.Count > 10)
                        resultMsg += $"\n... 及其他 {resultStats.errors.Count - 10} 条";
                }
                MessageDialog.Show(_ownerWindow, resultMsg, "导入结果");
            }
            catch (Exception ex)
            {
                IsExporting = false;
                Log.Error(ex, "导入过程发生异常");
                await Application.Current.Dispatcher.InvokeAsync(() =>
                    MessageDialog.Show(_ownerWindow, $"导入异常：{ex.Message}", "错误"));
            }
        }

        #endregion
    }
}
