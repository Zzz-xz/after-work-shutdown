using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace AfterWork
{
    /** @brief 封装 Windows 关机请求；此模块是唯一访问系统关机命令的位置。 */
    internal static class WindowsShutdown
    {
        /**
         * @brief 生成不强制关闭应用的即时关机命令。
         * @return 使用系统目录中的 shutdown.exe，避免依赖工作目录或 PATH。
         * @details 倒计时由界面管理；系统 /t 大于零会隐含 /f，因此固定使用 /t 0。
         */
        internal static ProcessStartInfo CreateStartInfo()
        {
            return new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "shutdown.exe"),
                Arguments = "/s /t 0",
                UseShellExecute = false,
                CreateNoWindow = true
            };
        }

        /**
         * @brief 提交关机请求；只在用户确认且倒计时结束后调用。
         * @throws InvalidOperationException 命令启动失败、返回错误或超时。
         * @throws Win32Exception 系统拒绝启动关机命令。
         * @details 最多等待三秒。成功返回仅表示请求被接受，不表示电脑已经断电。
         */
        internal static void Request()
        {
            using (Process process = Process.Start(CreateStartInfo()))
            {
                if (process == null)
                    throw new InvalidOperationException("无法启动系统关机命令，请使用开始菜单关机。");

                if (!process.WaitForExit(3000))
                    throw new InvalidOperationException(
                        "系统尚未返回关机结果，请先观察电脑状态，不要立即重复操作或拔电源。");

                if (process.ExitCode != 0)
                    throw new InvalidOperationException(
                        "Windows 未接受关机请求（错误码 " + process.ExitCode +
                        "）。请保存文件后使用开始菜单关机。");
            }
        }
    }
}
