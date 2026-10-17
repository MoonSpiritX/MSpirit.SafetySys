// Copyright (C) 2026 斜月Spirit
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MSpirit.SafetySys.Launcher;

/// <summary>
/// 斜月安全管理系统 · 绿色版启动器
/// 
/// 职责：找到并启动主程序（MSpirit.SafetySys.exe），并把命令行参数原样透传。
/// 设计要点：
///  - 查找顺序：① 启动器同目录直放主程序（调试便捷）；② 同目录第一层子文件夹内
///    （发布结构：主程序在「程序文件」子目录，或其他任意子文件夹）。
///  - 找不到主程序时弹出提示（NativeAOT 下用 Win32 MessageBox，无 WPF 依赖）。
///  - 进程启动后立即退出，不驻留内存；主程序自带单实例保护。
/// </summary>
internal static class Program
{
    private const string MainExeName = "MSpirit.SafetySys.exe";

    // 找不到主程序时弹窗提示（user32.dll 自带的 MessageBox，零依赖）
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x00000000;
    private const uint MB_ICONERROR = 0x00000010;

    /// <summary>
    /// 扫描 baseDir 的第一层子文件夹，返回第一个包含主程序的路径；找不到返回空。
    /// </summary>
    private static string FindMainExeInSubDirs(string baseDir)
    {
        try
        {
            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string candidate = Path.Combine(dir, MainExeName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            // 目录枚举失败不致命，交由上层统一报"未找到"
            Console.Error.WriteLine($"扫描子文件夹失败：{ex.Message}");
        }
        return string.Empty;
    }

    [STAThread]
    private static int Main(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;

        // 查找主程序：① 同目录直放（调试便捷）→ ② 同目录第一层子文件夹内（发布结构等）
        string mainExePath = Path.Combine(baseDir, MainExeName);
        if (!File.Exists(mainExePath))
        {
            mainExePath = FindMainExeInSubDirs(baseDir);
        }

        if (string.IsNullOrEmpty(mainExePath) || !File.Exists(mainExePath))
        {
            MessageBoxW(IntPtr.Zero,
                $"未找到主程序：{MainExeName}\n\n" +
                "请确认主程序位于启动器同目录，或其子文件夹内。",
                "斜月安全管理系统", MB_OK | MB_ICONERROR);
            return 1;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = mainExePath,
                // 工作目录设为主程序所在目录，保证 Resources、logs 等相对路径正确
                WorkingDirectory = Path.GetDirectoryName(mainExePath) ?? baseDir,
                UseShellExecute = false,
            };

            // 透传命令行参数（如 --viewer 独立进程看图模式）
            foreach (string arg in args)
            {
                psi.ArgumentList.Add(arg);
            }

            Process.Start(psi);
            return 0;
        }
        catch (Exception ex)
        {
            MessageBoxW(IntPtr.Zero,
                $"启动主程序失败：{ex.Message}",
                "斜月安全管理系统", MB_OK | MB_ICONERROR);
            return 1;
        }
    }
}
