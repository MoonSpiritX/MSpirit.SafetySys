# Bug 记录：打开项目及存档管理窗口后退出，弹出 2 个线程异常窗口

> 记录日期：2026-08-10 ｜ 正式入档 docs/：2026-08-11
> 状态：已修复（2026-08-10 下午）
> 适用范围：斜月安全管理系统（MS_SafetyManagementSystem，个人版 Beta V1.0）
> 关联：本文档记录了该 Bug 的复发历史（首次修复 → 复发 → 根治），供后续排查同类问题参考。

---

## 一、Bug 名称

**打开「项目及存档管理」窗口时，右键任务栏图标退出程序，弹出 2 个"线程异常"窗口。**

（本质：WPF 退出流程中的 `NullReferenceException`，由 `Application.Current` 在 Shutdown 期间被置为 null 导致。）

---

## 二、现象描述

1. 主窗口打开「项目及存档管理」窗口（`ShowDialog` 阻塞中）
2. 用户右键系统托盘图标 → 选择"退出"
3. 程序开始退出，但弹出 **2 个**异常窗口（标题为"UI线程异常"），内容为 `NullReferenceException`
4. 需要手动点掉两个窗口后程序才完全退出

### 复现路径（实测确认）

```
主窗口 → 菜单栏「项目列表」→「项目及存档管理」→ 窗口打开
      → 右键任务栏图标 →「退出」
      → 弹 2 个异常窗口
```

---

## 三、产生原因

### 3.1 根因（WPF 框架特性）

WPF 的 `Application.Current` 在 `Application.Shutdown()` **执行过程中会被置为 null**（应用实例被释放）。此时任何 `Application.Current.Xxx` 的访问都会抛 `NullReferenceException`。

代码中大量使用 `Application.Current.MainWindow` 来获取主窗口引用（全项目 80+ 处）。**正常情况下（程序运行中）这些访问是安全的**，但在**退出流程中**被调用的代码路径，就会触发 NRE。

### 3.2 触发链（为什么是 2 个窗口）

```
托盘右键「退出」→ Exit() → Application.Shutdown()
  → Shutdown 关闭所有窗口 → 管理窗口（ShowDialog）被关闭
  → ShowDialog() 返回 → OnManageProject_Click 继续执行
      ├─ ① RefreshCurrentProject()
      │     └─ GroupTree.LoadGroups() → 触发 GroupTreeView_SelectedItemChanged 事件
      │           └─ HazardPanel.UpdateStatusText()   ← 裸访问 Application.Current.MainWindow → NRE（弹窗 1）
      └─ ② UpdateStatusText()
            └─ HazardPanel.UpdateStatusText()          ← 同一处裸访问 → NRE（弹窗 2）
```

即：`HazardPanel.UpdateStatusText()` 在退出流程中被调用了 **两次**，每次 NRE 都由 `App.xaml.cs` 的 `Dispatcher.UnhandledException` 弹一个窗口，所以正好 **2 个**窗口。

### 3.3 复发历史（重要教训）

| 时间 | 情况 |
|---|---|
| 2026-08-10 上午 | 首次发现并修复：`MainWindowViewModel.RestoreViewState` 与 `PersonnelPanel.UpdateStatusText` 两处裸访问 → 改为 `Application.Current?.MainWindow` |
| 2026-08-10 下午 | **复发**：仍会弹 2 个异常窗口。排查发现上午**漏掉了对称的 Hazard 侧**——`HazardPanel.UpdateStatusText` 的 `Application.Current.MainWindow` 仍是裸访问 |

**复发原因**：隐患模块（Hazard）与人员模块（Personnel）的两套面板代码是**同构的**，上次只修了 Personnel 侧，漏掉了 Hazard 侧。这是典型的"成对代码只修一半"导致的复发。

---

## 四、修复方式

### 4.1 修改文件

`MSpirit.SafetySys\Hazard\HazardPanel.xaml.cs`（第 65 行，`UpdateStatusText()` 方法内）

### 4.2 修改内容

```csharp
// 修改前（裸访问，退出时 NRE）
var mainWindow = Application.Current.MainWindow as MainWindow;

// 修改后（防御 Shutdown 中 Application 被置 null）
// 防御：Application.Current 在 WPF Shutdown 过程中会被置为 null，加 ?. 避免退出时的 NRE
var mainWindow = Application.Current?.MainWindow as MainWindow;
```

`?.` 是 C# 空条件运算符：当 `Application.Current` 为 null 时，`Application.Current?.MainWindow` 直接返回 null，不再抛异常。后续 `mainWindow?.ViewModel` 的 `?.` 继续短路，方法安全返回。

### 4.3 同类已确认安全的位置

| 位置 | 状态 |
|---|---|
| `MainWindowViewModel.RestoreViewState`（第 416 行） | ✅ 已防御（上午修复） |
| `PersonnelPanel.UpdateStatusText`（第 93 行） | ✅ 已防御（上午修复） |
| `HazardPanel.UpdateStatusText`（第 65 行） | ✅ **本次修复** |
| `MainWindow.RefreshProjectMenu` | ✅ try/catch 包裹 |
| `MainWindow.ShowProjectSwitchToast`（本轮新增） | ✅ try/catch 包裹 |

### 4.4 验证结果

编译 0 错误；按复现路径实测（打开管理窗口 → 托盘退出），不再弹出异常窗口。

---

## 五、经验教训与后续建议

1. **成对代码要对称修复**：Hazard/Personnel 两套面板、导出/人员导出等对称模块，改一处必须检查另一侧是否有同样的问题。这是本项目"修好又犯"的高发点。

2. **退出流程中的 `Application.Current` 访问必须防御**：只要代码可能在任何窗口 `ShowDialog` 返回后、或 `Shutdown` 过程中执行，访问 `Application.Current.Xxx` 一律用 `?.`。

3. **排查技巧**：遇到弹窗类问题，先看 `App.xaml.cs` 的异常处理器——它显示完整堆栈（排查期特意保留的），异常堆栈比猜测根因快得多。本次就是通过堆栈定位到 `HazardPanel.UpdateStatusText`。

4. **遗留观察**（非紧急）：全项目仍有 80+ 处 `Application.Current.MainWindow` 裸访问，正常操作路径下安全，但**管理窗口 ShowDialog 返回后可达**的路径均属于退出高危区，后续改动涉及这些路径时保持 `?.` 防御习惯。
