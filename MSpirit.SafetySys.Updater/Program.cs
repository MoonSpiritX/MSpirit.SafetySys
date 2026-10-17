// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSpirit.SafetySys.Updater;

/// <summary>
/// 斜月安全管理系统 · 绿色版更新器（Updater.exe）
///
/// 用法：
///  1. 更新模式（默认，双击运行）：自动查找同目录（或同目录子文件夹）内的 .msupd 更新包，
///     定位程序文件夹（含 MSpirit.SafetySys.exe 的目录），校验并替换程序文件。
///  2. 打包模式：Updater.exe --pack &lt;程序文件夹&gt; &lt;输出.msupd&gt; —— 把程序文件夹打成更新包
///     （全量文件清单 + SHA256），供分发使用。
///
/// 设计要点：
///  - NativeAOT 单文件、零依赖；提示用 Win32 MessageBox，无 WPF 依赖。
///  - 替换前等待主程序进程退出（轮询 ≤10 秒），避免文件占用。
///  - 先解压到临时目录并校验，再备份旧文件 → 复制新文件 → 对账删除，失败自动回滚。
///  - 更新完成可选择重新启动主程序。
/// </summary>
internal static class Program
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;
    private const uint MB_ICONINFORMATION = 0x00000040;
    private const uint MB_ICONQUESTION = 0x00000020;
    private const uint MB_YESNO = 0x00000004;
    private const uint MB_DEFBUTTON2 = 0x00000100;
    private const uint IDYES = 6;

    [STAThread]
    private static int Main(string[] args)
    {
        // ---------- 打包模式 ----------
        if (args.Length >= 1 && args[0] is "--pack" or "-pack")
        {
            if (args.Length < 3)
            {
                MessageBox("用法：Updater.exe --pack <程序文件夹> <输出.msupd>", "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                return 1;
            }
            return RunPack(args[1], args[2]);
        }

        // ---------- 更新模式（--silent 跳过确认与结果弹窗，用于自动化/主程序触发） ----------
        bool silent = args.Length >= 1 && args[0] is "--silent" or "-silent";
        return RunUpdate(silent);
    }

    /// <summary>打包模式：程序文件夹 → .msupd 更新包。</summary>
    private static int RunPack(string sourceFolder, string outPath)
    {
        try
        {
            UpdateEngine.Pack(sourceFolder, outPath);
            MessageBox($"更新包已生成：\n{Path.GetFullPath(outPath)}\n\n" +
                       "分发时请将 Updater.exe 与该 .msupd 包放在同一目录（或程序文件夹内）。",
                "斜月安全管理系统更新器", MB_OK | MB_ICONINFORMATION);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox($"打包失败：{ex.Message}", "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
            return 1;
        }
    }

    /// <summary>更新模式：找包 → 定位程序文件夹 → 等主进程退出 → 校验替换 → 完成。</summary>
    private static int RunUpdate(bool silent = false)
    {
        string baseDir = AppContext.BaseDirectory;

        try
        {
            // 1. 定位程序文件夹
            string? mainExePath = UpdateEngine.FindMainExe(baseDir);
            if (mainExePath == null)
            {
                if (!silent)
                    MessageBox($"未找到主程序 {UpdateEngine.MainExeName}。\n\n" +
                               "请将更新器放在主程序同目录，或其子文件夹内。",
                        "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                return 1;
            }
            string appFolder = Path.GetDirectoryName(mainExePath)!;

            // 2. 查找更新包
            string? packagePath = UpdateEngine.FindUpdatePackage(baseDir, appFolder);
            if (packagePath == null)
            {
                if (!silent)
                    MessageBox("未找到 .msupd 更新包。\n\n" +
                               "请将更新包与更新器放在同一目录，或程序文件夹内。",
                        "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                return 1;
            }

            // 3. 检测包类型（有 manifest = 全量对账；无 = 增量覆盖），用于提示与后续分流
            bool isFullMode = UpdateEngine.HasManifest(packagePath);

            // 4. 提示确认（静默模式跳过）
            if (!silent)
            {
                string pkgName = Path.GetFileName(packagePath);
                string modeDesc = isFullMode
                    ? "全量更新：将按清单对账替换程序文件（含删除多余旧文件）"
                    : "增量更新：将覆盖程序文件夹中的同名文件（不删除其他文件）";
                int ask = MessageBox($"检测到更新包：{pkgName}\n\n" +
                                     modeDesc + "\n\n" +
                                     "更新目标：\n" + appFolder + "\n\n是否开始更新？",
                    "斜月安全管理系统更新器", MB_YESNO | MB_DEFBUTTON2 | MB_ICONQUESTION);
                if (ask != IDYES) return 0;
            }

            // 6. 等待主程序退出：
            //    若仍在运行，先通过命名管道请求主程序保存数据并退出，再等待；
            //    仍不退则提示用户手动关闭（静默模式直接失败）。
            if (!UpdateEngine.WaitMainProcessExit(10))
            {
                UpdateEngine.RequestMainProcessExit(); // 请求保存数据并退出（主程序已开 IPC 监听）
                if (!UpdateEngine.WaitMainProcessExit(15))
                {
                    if (silent) return 2; // 主程序未退出，静默模式直接失败
                    int force = MessageBox("主程序仍在运行，更新前需要先关闭它。\n\n" +
                                           "已请求主程序自动保存并退出，若仍未退出请手动关闭后点击「重试」。",
                        "斜月安全管理系统更新器", MB_YESNO | MB_DEFBUTTON2 | MB_ICONERROR);
                    if (force != IDYES) return 1;

                    // 再等一轮
                    if (!UpdateEngine.WaitMainProcessExit(10))
                    {
                        MessageBox("主程序仍未退出，已取消更新。", "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                        return 1;
                    }
                }
            }

            // 7. 解压（模式已在第 3 步确定）：
            //    全量模式 → 解压 + SHA256 校验；增量模式 → 仅解压（不校验）
            UpdateManifest? manifest = isFullMode ? UpdateEngine.ReadManifest(packagePath) : null;
            string tempDir = isFullMode
                ? UpdateEngine.ExtractAndVerify(packagePath, manifest!)
                : UpdateEngine.ExtractOnly(packagePath);

            // 8. 备份目录（程序文件夹父目录下，保证同卷可移动）
            string backupDir = Path.Combine(
                Path.GetDirectoryName(appFolder.TrimEnd(Path.DirectorySeparatorChar)) ?? baseDir,
                UpdateEngine.BackupDirPrefixPublic + Guid.NewGuid().ToString("N"));

            try
            {
                if (isFullMode)
                    UpdateEngine.Apply(appFolder, tempDir, manifest!, backupDir);
                else
                    UpdateEngine.ApplyIncremental(appFolder, tempDir, backupDir);
            }
            catch (Exception ex)
            {
                // 失败回滚
                bool rolledBack = UpdateEngine.Rollback(appFolder, backupDir);
                if (!silent)
                    MessageBox($"更新失败：{ex.Message}\n\n" +
                               (rolledBack ? "已自动回滚到更新前状态。" : "回滚失败！程序文件夹可能不完整，请勿运行主程序！"),
                        "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                TryDeleteDirectory(backupDir);
                return 1;
            }
            finally
            {
                TryDeleteDirectory(tempDir);
            }

            // 9. 成功，清理备份
            TryDeleteDirectory(backupDir);

            // 10. 询问是否启动主程序（静默模式不启动）
            if (!silent)
            {
                string doneMsg = isFullMode
                    ? $"更新完成！\n当前版本：{manifest!.AppVersion}\n\n是否立即启动主程序？"
                    : "更新完成！（增量覆盖模式，已覆盖同名文件）\n\n是否立即启动主程序？";
                int launch = MessageBox(doneMsg,
                    "斜月安全管理系统更新器", MB_YESNO | MB_ICONINFORMATION);
                if (launch == IDYES)
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = mainExePath,
                            WorkingDirectory = appFolder,
                            UseShellExecute = false,
                        });
                    }
                    catch (Exception ex)
                    {
                        MessageBox($"启动主程序失败：{ex.Message}", "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
                    }
                }
            }
            return 0;
        }
        catch (Exception ex)
        {
            if (!silent)
                MessageBox($"更新失败：{ex.Message}", "斜月安全管理系统更新器", MB_OK | MB_ICONERROR);
            return 1;
        }
    }

    private static void TryDeleteDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 清理失败不致命（备份目录留着不影响主程序）
        }
    }

    private static int MessageBox(string text, string caption, uint type)
    {
        return MessageBoxW(IntPtr.Zero, text, caption, type);
    }
}
