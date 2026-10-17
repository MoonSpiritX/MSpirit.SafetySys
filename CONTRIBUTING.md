# 贡献指南（CONTRIBUTING）

感谢您对本项目的关注与贡献！请先阅读本指南，再提交 Issue 或 Pull Request。

## 项目简介

斜月安全管理系统（MS_SafetyManagementSystem）是一款基于 **.NET 10 + WPF** 的本地桌面安全管理系统，面向**实体行业**（建筑、制造、生产等）企业安全巡查记录与人员信息管理。主要技术：WPF-UI、CommunityToolkit.Mvvm、Dapper + SQLite、PaddleOCR（本地 OCR）、Kestrel（局域网录入）、Serilog。

## 如何反馈问题

- **Bug 报告**请包含：软件版本、Windows 版本、复现步骤、预期结果与实际结果；如涉及崩溃或异常，请附上用户目录下 `%USERPROFILE%\MSpirit.SafetySys\logs\debug*.log` 中当天日志的相关片段。
- **功能建议**请说明使用场景与期望行为，越具体越好。
- 反馈渠道：请在仓库 [Issue 区](https://github.com/MoonSpiritX/MSpirit.SafetySys/issues) 提交（Bug 报告与功能建议请按模板填写）；其他联系方式见文末「联系作者」。

## 开发环境

- Windows 10 1809（版本 17763）及以上，或 Windows 11
- Visual Studio 2022 或 .NET 10 SDK
- 打开 `MSpirit.SafetySys.slnx`，F5 即可运行

## 构建与运行

```bash
dotnet run --project MS_SafetyManagementSystem.csproj
dotnet build MS_SafetyManagementSystem.csproj -c Debug
dotnet publish MS_SafetyManagementSystem.csproj -c Release -r win-x64 --self-contained true
```

## 代码约定

- **MVVM**：界面遵循 XAML 视图 + ViewModel（CommunityToolkit.Mvvm）的结构，业务逻辑放 `Services/`。
- **命名**：遵循 .NET 惯例（类型与成员 PascalCase、局部变量 camelCase）；代码注释与 UI 文案使用中文。
- **日志**：使用 Serilog 并区分级别（`Information` / `Debug` / `Warning` / `Error`）；高频路径使用 `Debug` 避免日志量过大。
- **SPDX 头**：新增/修改源码文件请保留文件头：
  `// Copyright (C) 2026 斜月Spirit`
  `// SPDX-License-Identifier: AGPL-3.0-or-later`
- **依赖**：不引入未在 `.csproj` 声明的包；第三方依赖须与 AGPL-3.0 兼容（MIT / Apache-2.0 等）。

## 维护模式（重要）

本项目目前由作者**个人维护**，欢迎所有人：

- 通过 **Issue** 提交 Bug 报告、功能建议与使用问题（请按 Issue 模板填写）；
- 在 Issue 中参与讨论、补充复现信息。

**暂不接受外部直接提交的 Pull Request**：如您有代码改进的想法，请先开 Issue 说明方案，由作者评估后决定是否采纳及实现方式。

**联系作者**：
- 邮箱：374532719@qq.com
- 抖音群：659306761979（作者抖音账号：斜月Spirit）

感谢理解与支持！

## 贡献许可（DCO）

> 说明：当前维护模式下暂不接受外部 PR，本节保留供未来开放贡献时使用。

通过提交 Pull Request，您声明：您有权贡献该代码，并同意您的贡献以本项目采用的 **GNU AGPL-3.0-or-later** 许可证授权。若您代表雇主或他人贡献，请确保您已获得相应授权。

## 敏感数据提醒

本项目处理实体行业（建筑、制造、生产等）企业安全管理与人员信息。**请勿**在 Issue、PR、代码、示例或测试数据中提交任何真实信息，包括：真实项目名称、人员姓名、身份证号、手机号、照片、真实整改报告等。涉及示例一律使用占位符（如 `XX公司`、`XX建设`、`XX班组`）。

## 许可与商业授权

- 本项目以 AGPL-3.0 开源发布，全文见根目录 [`LICENSE`](LICENSE)。
- 第三方组件许可清单见根目录 [`THIRD-PARTY-NOTICES`](THIRD-PARTY-NOTICES)。
- 商业授权 / 闭源集成需求请联系作者（斜月Spirit）。
