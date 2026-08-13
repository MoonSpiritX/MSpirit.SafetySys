# 已知问题清单（ISSUES）

> 整理日期：2026-08-04
> 最近更新：2026-08-11（原第 1 条"无单元测试项目"已解决——建立 MSpirit.SafetySys.Tests（xUnit，89 个用例全绿），问题列表重编号为 1-4；原第 5 条"数据无自动备份兜底"已由 BackupService 解决并移除出问题列表；修复「人员模块图片查看器设置无效」；新增修复记录「新库建表 SQL 缺列」；巨型文件行数同步为 2026-08-11 实际值；剩余 1、2、3、4 条复查未解决）
> 最近更新：2026-08-12（「版本号管理约定」升级为正式方案 **`docs/版本号管理方案.md`**：按变更幅度定版、不按日期递增（微调→第4位、修复/界面→第3位、完整功能模块→第2位、重大/不兼容→第1位），历史版本重新归类——1.0.0.1→1.0.1.0、1.0.0.2→1.1.0.0，定版时 csproj + CHANGELOG + AboutWindow 更新日志三处同步；「双模块代码重复」观察项维持附注原状，不升级为问题；**新增修复：托盘退出路径缺"清焦点+保存全部记录"导致最后编辑可能丢失——新建 Services/AppExitManager.cs 统一退出管理器（防重入、钩子异常隔离、任意线程可调用），托盘/更新器/单实例/查看器子进程四个退出入口统一接入，CloseAllLanEntryWindows 补上隐患录入窗口（原漏）；本轮另修复：多 LAN 服务端口都显示 5000（StartAsync fire-and-forget 致端口探测失效）、手机端页面启发式缓存不更新（ServePage 加 no-store）、手机端 Toast 多次选择不弹（批次计数）、方向转换水印 Fill="None" 运行期崩溃、手机图片传输启动按钮不可点击（双按钮重叠）、常用隐患类型 30 项点击被窗口拖拽吞掉（Border 模拟按钮改真 Button）、窗口拖拽空白区失效（根 Grid 透明背景 + 隧道事件 + 交互控件白名单）、DropdownMenu 右键菜单点击外部不关闭（改 Mouse.Capture + PreviewMouseDownOutsideCapturedElement）**）
> 修复记录：2026-08-10 已修复第 7 条（Merge 重写 Groups.ProjectId）、第 8 条（Overwrite 按名称匹配本地目标）与第 9 条（显式列名合并，跨版本表结构差异安全），经临时 console 工程全链路验证通过；存档导入遗留问题 3 条已全部解决。2026-08-11 已修复第 5 条（BackupService：启动/退出自动备份到 ProjectsDate.Backup，保留 30 天 + 最多 100 份轮换）；同日修复「人员模块图片查看器设置无效」——`PersonnelRecordCardView.Photos.cs` 双击照片直接调用系统查看器、未读取 ImageViewerType 设置，已接入设置分支（内置查看器/系统默认）；同日修复「新库建表 SQL 缺列」——`ProjectDbContext.CreateTablesSql`（新建库路径）与 INSERT 列不同步（Groups 缺 Tag、Records 缺 12 个扩展列、Attachments 缺 CertName、PersonnelRecords 缺 Cert1-5），老库走逐列 ALTER 补列未暴露、全新环境首次建库必崩，已补全建表 SQL（由单元测试发现）。2026-08-12 修复详见「最近更新」行（AppExitManager 退出管理器 + 端口探测 + 手机页缓存/Toast + 水印崩溃 + 双按钮重叠 + 模拟按钮点击 + 窗口拖拽空白区）。
> 适用范围：斜月安全管理系统（MS_SafetyManagementSystem，个人版 Beta V1.0）
> 说明：本文档记录当前开发状态下已知的代码质量与工程性问题，作为后续改进的待办清单。

## 范围外（明确暂不处理）

- **Git 版本控制**：按项目决定暂不考虑引入 Git 版本控制（不列入问题清单）。
- **收藏夹分类管理**：30 个分类由开发期依据规范条文固定，无需用户增删改；`FavoriteLinksService` 的 `AddCategory/RenameCategory/DeleteCategory` 未接线不视为问题。用户侧“常用/不常用”选择为后续设计项（当前 `LinkCategory` 模型尚无该字段）。
- **日历弹窗季节背景**：2026-08-05 决定日历弹窗不启用季节背景卡片，保持现有灰色背景（`#E5AAAAAA`）；`App.xaml` 的 `Calendar*Bg` 资源与 `SolarTermHelper.GetCalendarSeasonBackgroundKey()` 暂不接线，不再处理。
- **版本号文档同步**：开发期间 csproj 版本号（`Version` / `AssemblyVersion` / `FileVersion`）随时可能调整，定档发布时也可能直接跳版本号发布；README / ISSUES / 开发约定等文档中的版本号**不随开发逐次同步**，一律以 csproj 为准，仅在定档发布时统一更新（2026-08-12 确认此约定，避免将"文档版本号滞后"误判为问题）。

## 问题列表

### 1. 巨型文件（可维护性差）
- **等级**：中高
- **现状**：多个文件超过千行，已到难以维护的规模（行数为 2026-08-11 实测）：
  - `MainWindow.xaml.cs`：1495 行
  - `ImageViewer\ImageViewerWindow.xaml.cs`：1411 行
  - `ViewModels\GroupTreeViewModel.cs`：1390 行
  - `Hazard\RecordCardView.Photos.cs`：1116 行
  - `Analysis\AnalysisViewModel.cs`：1104 行
  - `Services\ProjectDbContext.cs`：1086 行
  - `Peoplemanagement\PersonnelRecordCardView.Photos.cs`：1057 行
- **建议**：按职责拆分（如 partial 类已用于 ImageViewer，可继续推广）；`MainWindow` 中的命令与事件处理迁移到 ViewModel/独立服务。

### 2. 异常处理粗糙（空 catch 吞异常）
- **等级**：中
- **现状**：全项目约 121 处 `catch (Exception)`，其中部分为空 catch 或仅记录后继续，出错时静默失败、难以排查。
- **建议**：统一异常处理策略（记录日志 + 用户提示），消灭空 catch；关键路径补充日志上下文（模块、记录 ID 等）。

### 3. 半成品代码被排除编译
- **等级**：中
- **现状**：`Controls\FluentPopup.cs`、`Controls\WindowMaterial.cs`、`MainWindow（备份版本）.xaml` 已在 csproj 中排除编译，属于未完成/未攻克的暂存代码（如 Acrylic 毛玻璃、DWM 阴影）。
- **建议**：明确"保留待攻克"或"删除"；长期保留会带来认知负担和死代码累积。

### 4. 功能"已设定但未执行/未接线"
- **等级**：低
- **现状**：工具窗口占位面板——`Tools\ImageProcessWindow.xaml` 中"格式及压缩面板（待实现）""图片剪裁面板（待实现）""证件照生成（开发中）"为占位界面，属已知未完成功能。
- **建议**：按产品规划另行排期。

> ✅ 已解决（2026-08-11）：原第 1 条「无单元测试项目」——建立 `MSpirit.SafetySys.Tests`（xUnit）并挂入 slnx，第一轮 89 个用例全绿，覆盖分组树/记录/人员 CRUD、附件复制跨年月路径重建、视图状态、DateParser、转换器；`dotnet test` 一键运行，条目已移除出问题列表。扩充方向（未覆盖）：`ConfigManager`、`OneClickExporter`/导入解析、`ImageViewerHelper` 几何计算、OCR 录入链路。2026-08-12 新增图片处理 `ImageProcessViewModel.AddDroppedFiles` 拖放添加 6 例（文件过滤/文件夹递归/去重/格式枚举），当前 95 个用例全绿。
> ✅ 已解决（2026-08-11）：原第 5 条「数据无自动备份兜底」已由 `Services\BackupService.cs` 解决——启动/退出时自动备份到数据根 `ProjectsDate.Backup` 文件夹，保留 30 天 + 最多 100 份双重轮换，条目已移除。

## 附：其他观察项（非阻塞）

- 双模块（Hazard / Personnel）CRUD 存在重复代码，新模块扩展时建议抽象公共基类/服务。
- `Microsoft.ML.OnnxRuntime`、PaddleOCR 等重依赖导致发布体积较大，暂未做裁剪优化。
- **死代码（2026-08-10 复核确认仍存在）**：`ImageViewerWindow.EnableFullscreenDarkBlur` 及配套 `AccentPolicy`/`AccentState`/`WindowCompositionAttributeData`/`WindowCompositionAttribute`/`SetWindowCompositionAttribute` P/Invoke 块（`ImageViewerWindow.xaml.cs:29-92`）全仓无调用；`ImageViewerWindow.Transforms.GetViewerOrientation`（`Transforms.cs:191`）无调用（与已删除的 `ApplyViewerOrientation` 成对）。
