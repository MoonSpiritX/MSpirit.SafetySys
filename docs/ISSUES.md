# 已知问题清单（ISSUES）

> 整理日期：2026-08-04
> 最近更新：2026-08-17（新增第 3 条「标注颜色/字号下拉选中后弹回」，2026-08-15 的更新内容保留在下方说明行）
> 说明：本文档只记录已发现或可能存在但**未解决**的问题；已修复内容统一记入 `CHANGELOG.md`。问题列表编号 1-2 为 2026-08-15 重编号所得（原 1-3，移除「图片剪裁占位」后重排），2026-08-17 追加第 3 条。
> 适用范围：斜月安全管理系统（MS_SafetyManagementSystem，个人版 V1.2）

## 范围外（明确暂不处理）

- **收藏夹分类管理**：30 个分类由开发期依据规范条文固定，无需用户增删改；`FavoriteLinksService` 的 `AddCategory/RenameCategory/DeleteCategory` 未接线不视为问题。用户侧“常用/不常用”选择为后续设计项（当前 `LinkCategory` 模型尚无该字段）。
- **日历弹窗季节背景**：2026-08-05 决定日历弹窗不启用季节背景卡片，保持现有灰色背景（`#E5AAAAAA`）；`App.xaml` 的 `Calendar*Bg` 资源与 `SolarTermHelper.GetCalendarSeasonBackgroundKey()` 暂不接线，不再处理。
- **版本号文档同步**：开发期间 csproj 版本号（`Version` / `AssemblyVersion` / `FileVersion`）随时可能调整，定档发布时也可能直接跳版本号发布；README / ISSUES / 开发约定等文档中的版本号**不随开发逐次同步**，一律以 csproj 为准，仅在定档发布时统一更新（2026-08-12 确认此约定，避免将"文档版本号滞后"误判为问题）。

## 问题列表

### 1. 巨型文件（可维护性差）
- **等级**：中高
- **现状**：多个文件超过千行，已到难以维护的规模（行数为 **2026-10-15 实测**）：
  - `ImageViewer\ImageViewerWindow.xaml.cs`：1415 行
  - `ViewModels\GroupTreeViewModel.cs`：1402 行
  - `Hazard\RecordCardView.Photos.cs`：1116 行
  - `Services\ProjectDbContext.cs`：1105 行
  - `Analysis\AnalysisViewModel.cs`：1104 行
  - `Peoplemanagement\PersonnelRecordCardView.Photos.cs`：1057 行
  - `Export\PersonnelExportViewModel.cs`：941 行
  - `Hazard\RecordCardView.Interactions.cs`：848 行
  - `Helpers\OcrService.cs`：820 行
  - `Tools\ImageProcessCropViewModel.cs`：818 行
- **已改善**：`MainWindow.xaml.cs` 原 1495 行已于 2026-08-13 拆分为 10 个 partial（核心 398 + Drag/MenuActions/Navigation/OverlayActions/Panels/Popups/Tray/TreeView/ViewModel），单文件最大 398 行，已移出本清单。
- **建议**：按职责拆分（如 partial 类已用于 ImageViewer 与 MainWindow，可继续推广）；`MainWindow` 中的命令与事件处理迁移到 ViewModel/独立服务。

### 2. 人员导入暂不支持照片列
- **等级**：低
- **现状**：`PersonnelDataWriter` 已具备照片写入逻辑（`IDCardFront`/`IDCardBack` 身份证槽位 + `Cert1~Cert5` 证书槽位），但 `MappingDictionaryService` 人员字段中**没有照片字段映射**，且 `ExcelSmartImporter`/`WordSmartImporter` 仅对 `HazardPhoto`/`RemedyPhoto` 特殊处理图片 → 人员文档中的照片列（如「身份证正面/背面」「证书1-5」）无法被识别导入（2026-08-14 由「人员导入功能测试文档 Word04 / Word05.docx」验证：证件照片列被导入器忽略，仅作文档展示）。
- **暂缓原因（2026-08-14 评估）**：人员身份证/证书照片在实务中均为**单独存档附件**，极少放进表格批量导入，场景概率低；程序现有「手机扫码 OCR 录入 + 手动添加照片」已覆盖主要路径。暂不实施，后续如需再排期。
- **实施要点（已明确，后续直接可用）**：① `MappingDictionaryService` 补 7 个照片字段（`IDCardFront`/`IDCardBack`/`Cert1`~`Cert5`）并扩展 `PersonnelDataFields` 与 `AppendPersonnelMappings` 迁移（Version 1.1→1.2）；② 新增 `IsPhotoField(field)`（Hazard={HazardPhoto,RemedyPhoto}，Personnel={7 个照片字段}），替换两个导入器中的硬编码 `field == "HazardPhoto" || field == "RemedyPhoto"` 判断；③ `PersonnelDataWriter` 无需改动（已就绪）。
- **状态**：暂缓（后续评估）。

### 3. 标注与剪裁面板：标注颜色/字号下拉"选中后弹回"（偶发）
- **等级**：低
- **现状**：图片处理窗口 → 标注与剪裁面板，「标注颜色」「字号」两个下拉（均为 8 项）**偶发**出现"打开后选中某项，下拉又自动弹开、关不掉"；持续约 20 秒后自行恢复，小概率一直不正常。「线宽」（4 项）及面板其余下拉正常（2026-08-17 现场复现确认）。
- **根因分析（2026-08-17）**：`Tools\ImageProcessWindow.xaml.cs` 的 `AnnotationCombo_DropDownOpened`（2026-08-16 为修复"打开后 ~100ms 自动关闭"而加的 80ms 定时重开）**不区分关闭原因**——用户选中某项后下拉是正常关闭，挂起的定时器检测到已关闭又强行重开，形成"选一次弹一次"；定时器为 Background 优先级，UI 线程排队工作（如预览加载）会饿死它，线程空闲后才触发/耗尽，对应"约 20 秒后恢复"现象。仅 8 项下拉弹出布局耗时更长、最易触发原始"打开即自动关闭"，故只有这两个下拉受影响。
- **已尝试修复（2026-08-17，未生效）**：为三个标注下拉加"用户主动关闭"识别（`SelectionChanged` / Esc / Alt / 窗口点击置位标记），重开逻辑仅在**非用户主动关闭**时生效，并将定时器优先级提至 Input（`DispatcherPriority.Input`）。实测行为与修复前一致，未解决。
- **后续方向**：不再依赖 DropDownOpened 定时重开兜底，考虑直接根治"打开即自动关闭"（如在 Popup `Opened`（已实际呈现）后再重设 `IsDropDownOpen`，或延后打开以避开 MouseUp 命中竞态），并移除 80ms 魔数。
- **状态**：未解决（低影响，暂缓）。

## 附：其他观察项（非阻塞）

- 双模块（Hazard / Personnel）CRUD 存在重复代码，新模块扩展时建议抽象公共基类/服务。
- `Microsoft.ML.OnnxRuntime`、PaddleOCR 等重依赖导致发布体积较大，暂未做裁剪优化。
- **死代码（2026-08-10 复核确认仍存在）**：`ImageViewerWindow.EnableFullscreenDarkBlur` 及配套 `AccentPolicy`/`AccentState`/`WindowCompositionAttributeData`/`WindowCompositionAttribute`/`SetWindowCompositionAttribute` P/Invoke 块（`ImageViewerWindow.xaml.cs:29-92`）全仓无调用；`ImageViewerWindow.Transforms.GetViewerOrientation`（`Transforms.cs:191`）无调用（与已删除的 `ApplyViewerOrientation` 成对）。
- **死逻辑（2026-08-15 测试期复核确认）**：
  - `MappingDictionaryService._commonSynonyms` 已加载/保存但 `MatchField` 从未使用（与 2026-08-15 已移除的「功能未接线/占位」条目同类的未接线代码）。
  - `MappingDictionaryService.MatchField` 的"分隔符回退 + 多段歧义放弃"分支（`SplitHeaderText` 后多段匹配不同字段返回 null）实际不可达：`TryMatchNormalized` 用 contains 匹配，段文本是整体文本的子串，整体必然先命中任一段的别名，回退分支永远执行不到。已用测试守护真实行为（整体包含匹配优先），如需清理回退分支需先确认无历史模板依赖。`ExcelSmartExporter.FindLastDataRow` 已与导入侧同步修复（08-15，见 CHANGELOG）。
  - **`WordSmartExporter.CreateDataRowFromHeader` 不可达（2026-08-15 测试期确认）**：`ProcessTable` 仅在模板存在"结构匹配的示例数据行"（`rowsToDeleteIndices.Count > 0`）时才调用 `ProcessTableBlock`，而 `CreateDataRowFromHeader` 只在该方法内"无示例行"分支使用 → **空模板（仅表头无示例行）永远不会导出数据行**，`CreateDataRowFromHeader` 为死代码。当前行为已被测试守护（`仅表头无示例数据_当前行为_不插入数据行`）；若后续支持空模板导出，需去掉 `ProcessTable` 的 guard 并启用该方法（同时更新守护测试）。
