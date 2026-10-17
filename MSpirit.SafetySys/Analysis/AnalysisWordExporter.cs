// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using MS_SafetyManagementSystem.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace MS_SafetyManagementSystem.Analysis
{
    /// <summary>
    /// 将隐患类型分布数据导出为 .docx 文档（仅表格）
    /// </summary>
    internal class AnalysisWordExporter
    {
        private readonly string _title;
        private readonly string _summaryText;
        private readonly List<TypeStatItem> _typeDistribution;

        public AnalysisWordExporter(
            string title,
            string summaryText,
            List<TypeStatItem> typeDistribution)
        {
            _title = title;
            _summaryText = summaryText;
            _typeDistribution = typeDistribution;
        }

        public void Export(string filePath)
        {
            using var package = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);
            var mainPart = package.AddMainDocumentPart();
            mainPart.Document = new W.Document();
            var body = new W.Body();

            // ===== 标题 =====
            body.Append(new W.Paragraph(
                new W.Run(new W.Text(_title))
                { RunProperties = new W.RunProperties(new W.Bold(), new W.FontSize { Val = "32" }) }
            ));
            body.Append(new W.Paragraph());

            // ===== 摘要 =====
            if (!string.IsNullOrEmpty(_summaryText))
            {
                body.Append(new W.Paragraph(
                    new W.Run(new W.Text(_summaryText))
                    { RunProperties = new W.RunProperties(new W.FontSize { Val = "22" }) }
                ));
                body.Append(new W.Paragraph());
            }

            // ===== 隐患类型分布数据表 =====
            body.Append(new W.Paragraph(
                new W.Run(new W.Text("一、隐患类型分布"))
                { RunProperties = new W.RunProperties(new W.Bold(), new W.FontSize { Val = "26" }) }
            ));

            var table = new W.Table();
            table.Append(new W.TableProperties(
                new W.TableBorders(
                    new W.TopBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.BottomBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.LeftBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.RightBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideHorizontalBorder { Val = W.BorderValues.Single, Size = 4 },
                    new W.InsideVerticalBorder { Val = W.BorderValues.Single, Size = 4 }
                )));

            // 表头行（全部水平居中 + 垂直居中）
            var hRow = new W.TableRow();
            hRow.Append(MakeHeaderCell("隐患类型"));
            hRow.Append(MakeHeaderCell("数量"));
            hRow.Append(MakeHeaderCell("占比"));
            table.Append(hRow);

            // 数据行
            foreach (var item in _typeDistribution)
            {
                var row = new W.TableRow();
                // 隐患类型 → 左对齐
                row.Append(MakeCell(item.HazardType, W.JustificationValues.Left));
                // 数量 → 右对齐
                row.Append(MakeCell(item.Count.ToString(), W.JustificationValues.Right));
                // 占比 → 右对齐
                row.Append(MakeCell(item.PercentageText, W.JustificationValues.Right));
                table.Append(row);
            }
            body.Append(table);
            body.Append(new W.Paragraph());

            // ===== 统计图表 =====
            body.Append(new W.Paragraph(
                new W.Run(new W.Text("二、统计图表（请参考程序界面）"))
                { RunProperties = new W.RunProperties(new W.FontSize { Val = "20" }, new W.Color { Val = "999999" }) }
            ));

            mainPart.Document.Append(body);
            mainPart.Document.Save();
        }

        private static W.TableCell MakeHeaderCell(string text)
        {
            return new W.TableCell(
                new W.TableCellProperties(
                    new W.Shading { Val = W.ShadingPatternValues.Clear, Color = "auto", Fill = "D9E2F3" },
                    new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center },
                    new W.TableCellMargin(
                        new W.StartMargin { Width = "80", Type = W.TableWidthUnitValues.Dxa },
                        new W.EndMargin { Width = "80", Type = W.TableWidthUnitValues.Dxa }
                    )
                ),
                new W.Paragraph(
                    new W.ParagraphProperties(
                        new W.Justification { Val = W.JustificationValues.Center },
                        new W.SpacingBetweenLines { After = "0", Before = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }
                    ),
                    new W.Run(new W.Text(text))
                    { RunProperties = new W.RunProperties(new W.Bold()) }
                )
            );
        }

        private static W.TableCell MakeCell(string text, W.JustificationValues align)
        {
            return new W.TableCell(
                new W.TableCellProperties(
                    new W.TableCellVerticalAlignment { Val = W.TableVerticalAlignmentValues.Center },
                    new W.TableCellMargin(
                        new W.StartMargin { Width = "100", Type = W.TableWidthUnitValues.Dxa },
                        new W.EndMargin { Width = "100", Type = W.TableWidthUnitValues.Dxa }
                    )
                ),
                new W.Paragraph(
                    new W.ParagraphProperties(
                        new W.Justification { Val = align },
                        new W.SpacingBetweenLines { After = "0", Before = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }
                    ),
                    new W.Run(new W.Text(text))
                )
            );
        }
    }
}
