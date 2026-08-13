---
name: dev-conventions
description: 斜月安全管理系统（MS_SafetyManagementSystem）项目开发约定。当执行本项目的功能开发、数据层修改、建表/迁移、Bug 修复、代码审查、发布相关任务时必须先加载本技能。触发词：开发约定、新功能、加功能、改数据、建表、迁移、发布、坑清单、模块开发、代码审查。
agent_created: true
---

# 项目开发约定（斜月安全管理系统）

给本项目（MS_SafetyManagementSystem，.NET 10 + WPF + MVVM + SQLite）写代码前，先对照以下约定。

## 一、技术栈与分层落位

- 技术栈：.NET 10（`net10.0-windows10.0.17763.0`）+ WPF + WPF-UI 4.3 + CommunityToolkit.Mvvm + SQLite（Microsoft.Data.Sqlite + Dapper）
- **新功能落位顺序**：`Models`（实体）→ `Services`（数据/业务）→ `ViewModels`（命令）→ 模块目录（界面）→ `MenuBar`/`MainWindow` 接线
- 业务模块独立目录：`Hazard/`（隐患）、`Peoplemanagement/`（人员）、`ImageViewer/`、`Analysis/`（统计）、`Export/`（导入导出）、`Tools/`（办公工具）

## 二、代码约定

- 代码注释与 UI 文本一律**中文**。
- 模块名等常量定义在 `Common/Constants.cs`（`ModuleNames.Hazard` / `ModuleNames.Personnel`）。
- 实体扩展字段优先放 `Record.ExtraData`（JSON 字符串），避免频繁加列。
- 大文件用分部类拆分（单文件超 1000 行；流程与坑见 `wpf-partial-class-split` 技能）。
- 服务类命名 `XxxService`，数据库上下文 `XxxDbContext`；全局单例服务经 `Helpers/AppServiceProvider` 提供。

## 三、数据层约定（重点）

- **新增列必须同时改两处**：`ProjectDbContext.CreateTablesSql`（新建库建表）**和** `EnsureDatabase()` 的按需 `ALTER TABLE ADD COLUMN`（老库补列）。缺一处 → 全新环境首次建库报 `no column named ...` 必崩。
- 全部使用**参数化查询**（Dapper），禁止字符串拼接 SQL。
- 写操作**短连接**（每次新建连接用完即关）；项目库启用 WAL 模式。
- 附件 `Attachments.RelativePath`/`ThumbnailPath` 以 `Project_{项目ID}` 开头、相对 **ProjectsDate 根目录**；复制卡片用 `RebuildAttachmentPath` 重建路径。
- 分组树命名约定（多处正则/解析依赖）：Hazard 顶级=`yyyy年MM月`、子级=`MM月dd日`（可带 Tag）；Personnel 顶级=单位、子级=部门。

## 四、功能搭建流程（写 → 验 → 报）

1. **实现**：按"一、分层落位"完成一个功能闭环（实体 → 服务 → 命令 → 界面 → 接线）。
2. **验证**（适用哪个跑哪个）：
   - `dotnet build` 0 错误；
   - 涉及数据层/纯逻辑：在 `MSpirit.SafetySys.Tests`（xUnit）补单元测试，`dotnet test` 全绿；
   - **新库路径（建表/迁移）只有全新库测试能暴露**——测试基类 `TestProject` 每个用例新建库，天然守护建表 SQL，改动建表必须跑数据层测试。
3. **汇报**：一句话说明完成内容与验证结果。

## 五、坑清单（重点）

- **成对代码对称修改**：Hazard / Personnel 两套模块代码同构（面板、卡片、导出），改一处必须检查另一侧（曾因漏改一侧致 Bug 复发）。
- **退出流程访问 `Application.Current` 一律 `?.`**（WPF `Shutdown()` 过程中会置 null）。
- **程序退出统一走 `AppExitManager.RequestExit()`**，禁止业务代码直接 `Shutdown()`（窗口 `Close()` 会被拦截转隐藏托盘）；新模块退出清理用 `AppExitManager.RegisterCleanup(...)` 登记。
- **WPF Shape 的 `Fill="None"` 会运行期抛 XamlParseException**，必须写 `Fill="Transparent"`（`Stroke` 同理）。
- **窗口内容区拖拽统一用 `WindowDragHelper.EnableDrag="True"`**（根 Grid 需设 `Background="Transparent"`，null 背景不参与命中测试）。
- **LAN/HTTP 服务 `Start()` 必须同步等待 `StartAsync`**（fire-and-forget 会吞端口绑定失败、探测失效）。
- **自绘菜单（DropdownMenu）点击外部关闭**用 `Mouse.Capture` + `PreviewMouseDownOutsideCapturedElement`。
- **打开文件属性对话框**用 shell32 `SHObjectProperties`，不要用 `Verb = "properties"`。
- **不新增空 catch**（统一记 Serilog 日志 + 必要用户提示）；**不引入未接线的半成品代码**（完成或排除编译并在 `ISSUES.md` 注明）。
- **新增 NuGet 依赖要谨慎**（影响发布体积；**依赖库版本变化后必须发全量更新包**）。
- **测试隔离**：测试一律用临时目录（`TestProject`），绝不触碰真实 `D:\MSpirit.SafetySys.Date`；测试 helper 不要内部 new 自己的环境再跨对象引用（FK 失败）。

## 六、文档与目录约定

- `docs/` = **正式项目文档**（进 git、随仓库分发）。
- `临时文档/` = **非正式/时效性/计划类文档**（功能开发备忘、发布清单、问题分析；**未形成正式 Skill 的文档一律放这里**；已被 .gitignore 排除）。
- `Skills/` = **正式 Skill 归档**（仅放已形成的标准 SKILL.md 格式技能；当前含 dev-conventions / versioning-policy / beta-release-checklist / wpf-partial-class-split）。
- `其他文件/` = 打包脚本（make_*.py / 打包*.bat）与测试图片等工具类。
- **变更同步**：数据表结构/目录调整 → 同步 `docs/数据架构.md`；功能完成/Bug 修复 → 更新 `docs/ISSUES.md`（含修复记录）；新增约定 → 补本文档。

## 七、相关技能

- 版本号定版 → `versioning-policy` 技能
- 内测发布 → `beta-release-checklist` 技能
- 大文件拆分 → `wpf-partial-class-split` 技能
