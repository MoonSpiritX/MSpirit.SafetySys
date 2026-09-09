# 斜月安全管理系统（MS_SafetyManagementSystem）

> **English README**：[`README.md`](../README.md)

单机桌面版安全管理系统，面向**实体行业**（建筑、制造、生产等）企业安全管理：日常安全巡查记录与人员信息管理。当前为个人版 **V1.2**（版本号 1.2.0.0）。

## 功能特性

| 模块 | 说明 |
|---|---|
| 隐患管理（Hazard） | 安全巡查隐患记录与追踪：标题/描述/隐患类型/等级/责任单位/责任人/地点/整改措施/期限/状态；每条记录最多 6 张图片（隐患照片 3 + 整改照片 3）；卡片支持移动/复制到其他日期分组 |
| 人员管理（Personnel） | 人员档案管理（姓名/性别/年龄/籍贯/身份证号等），支持身份证/证书证件照附件；**局域网录入**：手机扫码打开页面拍照，PaddleOCR 识别身份证正反面自动填表 |
| 图片查看器（ImageViewer） | 内置看图器：全屏、缩放（Chase 引擎）、旋转/翻转、缩略图、幻灯片 |
| 智能导入导出（Export） | 基于 Word/Excel 模板的字段智能匹配导出与导入，自动生成整改报告书等文档 |
| 统计分析（Analysis） | 按月的隐患/人员统计分析图表（LiveChartsCore） |
| 图片处理（ImageProcess） | 图片方向转换（左转/右转/180°/翻转/EXIF 校正）+ **标注与剪裁**（矩形/圆形/直线/箭头/文字/马赛克六种标注工具，与裁剪一体输出） |
| 其他 | 系统托盘最小化、单实例保护、四季主题背景 + 节气日历、二维码生成、右键菜单快捷操作 |

## 技术栈

- **框架**：.NET 10（`net10.0-windows10.0.17763.0`）+ WPF
- **UI**：WPF-UI 4.3.0（Fluent Design 风格）
- **MVVM**：CommunityToolkit.Mvvm 8.2.2 + Microsoft.Extensions.DependencyInjection
- **数据**：Microsoft.Data.Sqlite 8.0.0 + Dapper（全部参数化查询，WAL 模式）
- **OCR**：Sdcb.PaddleOCR + ONNX Runtime + OpenCvSharp4
- **文档导出**：ClosedXML（Excel）、DocumentFormat.OpenXml（Word）、SkiaSharp（图片压缩）
- **图表**：LiveChartsCore.SkiaSharpView.WPF
- **局域网服务**：Kestrel（ASP.NET Core，内置 HTTP 服务，免 UAC 绑定）
- **日志**：Serilog（文件滚动，保留 7 天）
- **其他**：QRCoder、Hardcodet.NotifyIcon.Wpf、Microsoft.Xaml.Behaviors.Wpf

## 环境要求

- Windows 10 1809（版本 17763）及以上，或 Windows 11
- 开发/构建：Visual Studio 2022（.NET 桌面开发工作负载）或 .NET 10 SDK

## 构建与运行

```bash
# 调试运行
dotnet run --project MS_SafetyManagementSystem.csproj

# 构建
dotnet build MS_SafetyManagementSystem.csproj -c Debug

# 单元测试（MSpirit.SafetySys.Tests，xUnit）
dotnet test MSpirit.SafetySys.Tests/MSpirit.SafetySys.Tests.csproj

# 发布（示例：win-x64 自包含，产物在 bin/Release/net10.0-windows10.0.17763.0/publish/win-x64/）
dotnet publish MS_SafetyManagementSystem.csproj -c Release -r win-x64 --self-contained true
```

也可以直接用 Visual Studio 打开 `MSpirit.SafetySys.slnx` 后 F5 运行。

## 数据存储与备份（重要）

- **数据根目录**：默认取第一个可用的非系统固定盘（D→E→F…字母序，数据根为 `{盘根}\MSpirit.SafetySys.Date`）；无可用非系统固定盘时回退 `%LOCALAPPDATA%\MSpirit.SafetySys.Date`。启动时程序会记住上次使用的数据根，若发现数据目录丢失（如磁盘未连接、盘符变化）会引导找回（手动选择 / 全盘搜索），详见 `Services/DataRootLocator.cs`。
- **数据库**：根目录下 `master.db`（项目列表）；各项目数据在 `ProjectsDate\Project_{id}\data.db`。
- **附件文件**：`ProjectsDate\Project_{id}\{模块}_{yyyy-MM}\Card_{记录ID}\`，缩略图在 `thumb\` 子目录。
- **运行日志**：程序目录下 `logs\debug*.log`（按天滚动，保留 7 天）。
- **自动备份**：程序在启动和退出时自动备份到 `ProjectsDate.Backup` 文件夹（保留最近 30 天、最多 100 份）；如需长期备份，可在退出程序后整体复制数据根目录（含 `master.db` 与 `ProjectsDate`），或导出 `.msarch` 存档包。

## 项目结构

```
MS_SafetyManagementSystem/
├── LICENSE                 # AGPL-3.0 许可证全文
├── THIRD-PARTY-NOTICES     # 第三方组件许可清单
├── App.xaml(.cs)            # 入口、DI 容器、全局异常处理、Serilog 初始化
├── MainWindow.xaml(.cs)     # 主窗口（布局、命令分发）
├── MainWindowViewModel.cs   # 主视图模型（项目/模块切换）
├── Models/                  # 数据模型（Record、PersonnelRecord、Attachment、Group 等）
├── ViewModels/              # 视图模型（RecordsViewModel 等）
├── Views/                   # 通用窗口（移动/复制、项目管理、链接管理等）
├── Hazard/                  # 隐患模块（记录卡、照片处理、交互）
├── Peoplemanagement/        # 人员模块
├── ImageViewer/             # 内置图片查看器
├── Export/                  # Word/Excel 导入导出
├── Analysis/                # 统计分析
├── Services/                # 数据访问与业务服务（ProjectDbContext、ProjectDataService、LanService 等）
├── Helpers/ Converters/ Controls/ Tools/ Common/   # 工具与公共代码
└── docs/                    # 项目文档（本文件、ISSUES.md 等）
```

## 已知事项

- 半成品代码（`Controls/FluentPopup.cs`、`Controls/WindowMaterial.cs`、备份版 MainWindow）已删除（2026-08-14 清理，csproj 排除项同步移除）。
- 个别文件较大（如 `ImageViewerWindow.xaml.cs` 约 1500 行、`MainWindow.xaml.cs` 约 1495 行），维护时需注意。
- 已有单元测试项目（`MSpirit.SafetySys.Tests`，xUnit，703 个用例覆盖数据层核心逻辑、导入导出链路（Excel/Word）、数据根定位、项目/存档/备份服务、统计/分析视图模型与图片剪裁/标注工具）；历史数据可能存在少量附件文件缺失（详见 `docs/ISSUES.md`）。

## 开源许可与商业授权

> 当前状态：源代码已托管至作者的**私有仓库**（尚未公开发布），对外开源计划待定；以下许可声明自公开发布时生效。

本项目以 **GNU Affero General Public License v3.0（`AGPL-3.0-or-later`）** 开源发布，版权归 AXin（斜月Spirit）所有，许可证全文见根目录 [`LICENSE`](../LICENSE)。

面向非开发者用户的简要说明：

- **可自由使用**：个人或组织可免费下载、安装、使用本软件。
- **需保留署名**：复制、修改、再分发时需保留版权声明与本许可文本。
- **修改需开源**：修改源码后对外分发，或修改后通过网络对外提供服务，须以 AGPL-3.0 开源并提供对应源码。
- **纯内部使用**：仅内部使用、不修改、不对外分发或提供服务时，不触发开源义务。

本程序包含的第三方组件及其许可清单见根目录 [`THIRD-PARTY-NOTICES`](../THIRD-PARTY-NOTICES)。

**商业授权**：公司/组织如需闭源集成、商用分发或免除 AGPL 义务，可联系作者（AXin / 斜月Spirit）洽谈商业授权。

## 相关文档

- [`README.md`](../README.md) — English version of this README
- [`CONTRIBUTING.md`](../CONTRIBUTING.md) — 贡献指南
- `docs/开发约定.md` — 开发约定（分层、代码/数据层约定、功能搭建流程、注意事项）
- `docs/ISSUES.md` — 已知问题清单与改进建议
- `docs/数据架构.md` — 数据库表结构、目录布局与路径规则
- `docs/用户手册.md` — 面向日常使用者的操作说明
- `docs/LAN服务技术文档.md` — LAN 服务（人员/隐患手机录入、手机图片传输、更新 IPC）架构与协议
- `docs/更新器使用说明.md` — 更新器（Updater.exe）与更新包制作说明
- `docs/开源许可中文参考.md` — AGPL-3.0 非官方中文参考译本（法律效力以根目录 `LICENSE` 英文原版为准）
- `docs/PRIVACY_POLICY.md` — 隐私政策（中英双语，商店上架与开源发布用）
