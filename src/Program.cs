using System;
using System.Windows.Forms;

namespace AfterWork
{
    /** @brief 初始化按需运行的下班提醒；窗口关闭后进程退出。 */
    internal static class Program
    {
        /** @brief 启动提醒窗口；仅接受 --preview 参数，预览时不调用系统关机。 */
        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool preview = args.Length == 1 && args[0] == "--preview";
            if (args.Length > 0 && !preview)
            {
                MessageBox.Show("参数无效。请直接打开程序，或使用 --preview 预览提醒。",
                    "下班关机", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (ReminderForm form = new ReminderForm(
                preview ? (Action)(delegate { }) : WindowsShutdown.Request, preview))
            {
                Application.Run(form);
            }
        }
    }
}
