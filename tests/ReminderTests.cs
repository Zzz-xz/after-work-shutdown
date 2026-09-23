using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace AfterWork
{
    /** @brief 使用可观察的替代关机操作，验证交互状态及取消路径。 */
    internal static class ReminderTests
    {
        private static int passed;

        /** @brief 执行交互流程检查，不调用 WindowsShutdown.Request。 */
        [STAThread]
        private static int Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                Run("Unconfirmed click never requests shutdown", Unconfirmed);
                Run("Countdown requests shutdown exactly once", ConfirmedCountdown);
                Run("Cancel discards pending shutdown", CancelCountdown);
                Run("Window close discards pending shutdown", CloseCountdown);
                Run("Escape discards pending shutdown", EscapeCountdown);
                Run("Preview never calls the shutdown boundary", PreviewCountdown);
                Run("Failure is visible and requires reconfirmation", FailureRecovery);
                Run("System command uses /s /t 0 without force", CommandArguments);
                Run("Real timer advances and is disposed on close", RealTimer);
                Console.WriteLine("PASS: " + passed + " checks. No system shutdown was executed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        /** @brief 执行一项检查，仅成功后累计通过数。 */
        private static void Run(string name, Action test)
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }

        /** @brief 创建透明测试窗口，以便执行原生控件交互且不干扰桌面。 */
        private static ReminderForm Open(Action request, bool preview = false)
        {
            ReminderForm form = new ReminderForm(request, preview);
            form.Opacity = 0;
            form.ShowInTaskbar = false;
            form.Show();
            Application.DoEvents();
            return form;
        }

        /** @brief 按稳定控件名称读取实际界面控件。 */
        private static T Control<T>(ReminderForm form, string name) where T : System.Windows.Forms.Control
        {
            System.Windows.Forms.Control[] found = form.Controls.Find(name, true);
            Assert(found.Length == 1, "Missing or duplicate control: " + name);
            return (T)found[0];
        }

        /** @brief 将倒计时事件推进指定次数，以覆盖边界而无需实际等待。 */
        private static void Tick(ReminderForm form, int count)
        {
            MethodInfo method = typeof(ReminderForm).GetMethod("AdvanceCountdown", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int index = 0; index < count; index++)
                method.Invoke(form, new object[] { null, EventArgs.Empty });
        }

        /** @brief 完成界面上的打卡勾选并点击确认。 */
        private static void Begin(ReminderForm form)
        {
            Control<CheckBox>(form, "clockedOut").Checked = true;
            Control<Button>(form, "confirm").PerformClick();
        }

        /** @brief 验证默认未勾选、按钮禁用且不能发起请求。 */
        private static void Unconfirmed()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }))
            {
                Assert(!Control<CheckBox>(form, "clockedOut").Checked, "Confirmation must start unchecked.");
                Assert(!Control<Button>(form, "confirm").Enabled, "Confirmation button must start disabled.");
                Assert(form.AcceptButton == null, "Enter must not implicitly start shutdown.");
                Control<Button>(form, "confirm").PerformClick();
                Tick(form, 11);
                Assert(calls == 0, "Unchecked form requested shutdown.");
            }
        }

        /** @brief 验证最后一秒前不请求关机，完成后仅提交一次。 */
        private static void ConfirmedCountdown()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }))
            {
                Begin(form);
                Assert(calls == 0, "Confirmation must not immediately shut down.");
                Assert(!Control<Button>(form, "confirm").Enabled, "Countdown must disable repeated clicks.");
                Tick(form, 9);
                Assert(calls == 0, "Shutdown ran before countdown completed.");
                Tick(form, 1);
                Assert(calls == 1, "Completed countdown did not request shutdown once.");
                Tick(form, 3);
                Assert(calls == 1, "Late timer event requested shutdown again.");
                Assert(form.IsDisposed, "Successful request must exit the reminder.");
            }
        }

        /** @brief 验证取消按钮关闭窗口并使迟到的倒计时事件失效。 */
        private static void CancelCountdown()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }))
            {
                Begin(form);
                Tick(form, 9);
                Control<Button>(form, "cancel").PerformClick();
                Tick(form, 2);
                Assert(form.IsDisposed && calls == 0, "Cancel did not discard pending shutdown.");
            }
        }

        /** @brief 验证标题栏关闭操作等效于取消。 */
        private static void CloseCountdown()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }))
            {
                Begin(form);
                form.Close();
                Tick(form, 10);
                Assert(form.IsDisposed && calls == 0, "Closing the form left shutdown pending.");
            }
        }

        /** @brief 通过原生窗体键盘处理验证 Esc 取消。 */
        private static void EscapeCountdown()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }))
            {
                Begin(form);
                MethodInfo processKey = typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic);
                bool handled = (bool)processKey.Invoke(form, new object[] { Keys.Escape });
                Tick(form, 10);
                Assert(handled && form.IsDisposed && calls == 0, "Escape did not cancel shutdown.");
            }
        }

        /** @brief 即使误传真实边界，预览窗体也不调用该委托。 */
        private static void PreviewCountdown()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }, true))
            {
                Begin(form);
                Tick(form, 12);
                Assert(calls == 0, "Preview invoked the shutdown boundary.");
                Assert(!form.IsDisposed, "Preview should remain visible after completion.");
                Assert(Control<Label>(form, "heading").Text == "预览完成。", "Preview completion was not shown.");
            }
        }

        /** @brief 验证异常可见、无自动重试，并在重新勾选后允许主动重试。 */
        private static void FailureRecovery()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate
            {
                calls++;
                if (calls == 1)
                    throw new InvalidOperationException("测试：系统拒绝了关机请求。");
            }))
            {
                Begin(form);
                Tick(form, 10);
                Assert(!form.IsDisposed, "Failure must remain visible.");
                Assert(Control<Label>(form, "status").Text.Contains("系统拒绝"), "Failure detail is missing.");
                Assert(!Control<CheckBox>(form, "clockedOut").Checked, "Failure must reset confirmation.");
                Assert(!Control<Button>(form, "confirm").Enabled, "Failure must require reconfirmation.");
                Tick(form, 10);
                Assert(calls == 1, "Failure must not auto-retry.");
                Begin(form);
                Tick(form, 10);
                Assert(calls == 2 && form.IsDisposed, "Explicit retry did not recover.");
            }
        }

        /** @brief 只读取命令配置，确认无强制关闭或系统倒计时参数。 */
        private static void CommandArguments()
        {
            ProcessStartInfo info = WindowsShutdown.CreateStartInfo();
            Assert(info.Arguments == "/s /t 0", "Unsafe shutdown arguments.");
            Assert(info.FileName == Path.Combine(Environment.SystemDirectory, "shutdown.exe"), "Command must use the system directory.");
            Assert(File.Exists(info.FileName), "System shutdown command is unavailable.");
            Assert(!info.UseShellExecute && info.CreateNoWindow, "Command must not open a console or use shell resolution.");
        }

        /** @brief 验证真实消息循环驱动计时器，关闭后不再提交关机。 */
        private static void RealTimer()
        {
            int calls = 0;
            using (ReminderForm form = Open(delegate { calls++; }, true))
            {
                Begin(form);
                string initial = Control<Label>(form, "status").Text;
                Stopwatch wait = Stopwatch.StartNew();
                while (Control<Label>(form, "status").Text == initial && wait.ElapsedMilliseconds < 4000)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }
                Assert(Control<Label>(form, "status").Text != initial, "Actual timer did not advance.");
                form.Close();
                Tick(form, 12);
                Assert(calls == 0, "Closed preview invoked shutdown.");
            }
        }

        /** @brief 失败时立即终止检查并保留清晰原因。 */
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
