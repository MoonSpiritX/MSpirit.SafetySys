---
name: wpf-partial-class-split
description: WPF 单文件巨类（超 1000 行）按职责拆分为分部类（partial class）文件的流程与坑清单。适用于 MainWindow.xaml.cs、ImageViewerWindow.xaml.cs 等大文件重构。触发词：拆分大文件、分部类、文件太大、MainWindow 拆分。
agent_created: true
---

# WPF 大文件分部类拆分

把超 1000 行的 WPF code-behind 按职责拆成 `类名.职责.cs` 分部类文件，**纯搬移、零逻辑改动**。

## 适用场景

- `MainWindow.xaml.cs`（1607 行已拆，见项目记忆）—— 拆分样例
- `ImageViewerWindow.xaml.cs`（~1500 行）等历史大文件
- 项目惯例：`Hazard/RecordCardView.*`（AutoSave/Interactions/Photos/Suggestions）同模式

## 流程

1. **读完整文件**：分段 Read（每段 ~600 行），列出全部成员。
2. **按职责划区块**：每个文件只装一类职责（生命周期/面板/菜单命令/托盘/右键菜单/树交互/导航/拖拽…）；依赖 XAML 字段（如 `BasePanel`）的成员必须留在窗口类内但可放单独分部文件；纯逻辑块（菜单构建等）可标为后续抽服务的候选。
3. **建分部文件**：同 namespace、`public partial class MainWindow`（**基类只写一次**，在主声明处）。每个文件带文件头版权注释 + 类注释（注明拆分日期、纯搬移）。
4. **重写主文件**：用 Write 覆盖，只保留核心成员（字段、构造函数、生命周期、窗口规格）。主文件 using 可保留原全集（未使用只是 IDE 提示，不报错）。
5. **验证**：
   - `dotnet build` 0 错误；若程序正在运行锁了输出 exe（MSB3021/MSB3027），改用 `dotnet build -o $TEMP/xxx` 临时目录验证，不动运行中的程序。
   - `dotnet test` 全量回归（本项目 153 用例）。
   - 冒烟：需用户关闭旧实例重启（单实例保护会阻止第二个实例）。

## 坑清单（血泪）

- **using 是文件级作用域，分部类不共享！** 每个分部文件必须自带所需的 using 和别名（如 `using WpfButton = System.Windows.Controls.Button;`），别名不能靠主文件。
- **别猜类型命名空间，用 Grep 确认**：反例——`TrayMenuGroup` 在 `MS_SafetyManagementSystem.Views`（不在 Controls）、`GroupNode` 在 `ViewModels`（不在 Models）。`class Xxx` 定义处向上 10 行内必有 namespace。
- **ToggleButton/Popup/PlacementMode 等控件在 `System.Windows.Controls.Primitives`**，拖拽白名单类代码容易漏。
- XAML 里引用的事件处理器名（如 `BasePanel_PreviewMouseLeftButtonDown`）在任意分部文件都能被编译器解析，搬移无影响。
- 搬移时保留成员原注释；`static` 成员保持 static；字段就近放"主要使用者"文件（分部类成员互访无障碍）。
- 主文件类注释写清楚"本文件保留核心，其余见 MainWindow.*.cs"，方便后人。

## 交付汇报

- 表格列出：文件 / 职责 / 行数
- 验证结果：编译 + 测试 + 冒烟状态
- 告知用户：纯搬移行为无感，但涉及交互（菜单/拖拽/右键）建议实际点一遍
