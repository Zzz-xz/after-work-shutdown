using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace AfterWork
{
    /** @brief 展示打卡确认、拔电源提醒和可取消倒计时，不创建后台任务。 */
    internal sealed class ReminderForm : Form
    {
        private const int CountdownSeconds = 10;
        private static readonly Color Ink = Color.FromArgb(26, 47, 73);
        private static readonly Color Muted = Color.FromArgb(81, 98, 119);
        private static readonly Color Accent = Color.FromArgb(28, 90, 139);
        private readonly Action requestShutdown;
        private readonly bool preview;
        private readonly Stream iconStream;
        private readonly Icon applicationIcon;
        private readonly Timer countdown = new Timer();
        private readonly List<Font> ownedFonts = new List<Font>();
        private readonly Label heading;
        private readonly Label description;
        private readonly Label status;
        private readonly CheckBox clockedOut;
        private readonly Button confirm;
        private readonly Button cancel;
        private int remainingSeconds;
        private bool pending;
        private bool requesting;

        /**
         * @brief 创建提醒窗口；构造期间不会调用关机操作。
         * @param requestShutdown 倒计时完成时调用的系统操作，可在测试中替换为计数器。
         * @param preview 是否只演示流程，预览模式不会调用传入的系统操作。
         */
        internal ReminderForm(Action requestShutdown, bool preview)
        {
            if (requestShutdown == null)
                throw new ArgumentNullException("requestShutdown");

            this.requestShutdown = requestShutdown;
            this.preview = preview;
            iconStream = typeof(ReminderForm).Assembly.GetManifestResourceStream("AfterWork.AppIcon.ico");
            if (iconStream == null)
                throw new InvalidOperationException("缺少应用图标资源，请重新构建程序。");
            try
            {
                applicationIcon = new Icon(iconStream);
                Icon = applicationIcon;
            }
            catch
            {
                if (applicationIcon != null)
                    applicationIcon.Dispose();
                iconStream.Dispose();
                throw;
            }
            Text = preview ? "下班关机 · 预览（不会关机）" : "下班关机";
            Font = CreateFont(10F, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(600, 480);
            MinimumSize = new Size(616, 519);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.White;
            ForeColor = Ink;

            TableLayoutPanel layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 22, 28, 20),
                ColumnCount = 1,
                RowCount = 7,
                BackColor = Color.White
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 96F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46F));
            Controls.Add(layout);

            heading = CreateLabel("heading", "下班前，先打卡。", 21F, Ink, FontStyle.Bold);
            description = CreateLabel("description", "打开手机 App，确认下班打卡成功后，再继续。", 10F, Muted);
            layout.Controls.Add(heading, 0, 0);
            layout.Controls.Add(description, 0, 1);

            clockedOut = new CheckBox
            {
                Name = "clockedOut",
                Text = "我已在手机 App 完成下班打卡",
                Dock = DockStyle.Fill,
                AutoSize = false,
                TabIndex = 0,
                Margin = new Padding(0, 0, 0, 12)
            };
            clockedOut.CheckedChanged += delegate { UpdateConfirmButton(); };
            layout.Controls.Add(clockedOut, 0, 2);

            Label unplug = CreateLabel("unplugReminder",
                "最后一步：电脑完全关机后，拔掉电源插头。\n如果正在更新，请等更新和关机结束。",
                11F, Ink, FontStyle.Bold);
            unplug.BackColor = Color.FromArgb(235, 243, 250);
            unplug.Padding = new Padding(15, 8, 12, 8);
            unplug.Margin = new Padding(0, 0, 0, 12);
            layout.Controls.Add(unplug, 0, 3);

            Label save = CreateLabel("saveReminder", "关机前请保存正在编辑的文件。", 9F, Muted);
            layout.Controls.Add(save, 0, 4);

            status = CreateLabel("status", preview
                ? "预览模式：可以体验整个流程，不会关闭电脑。"
                : "确认后有 10 秒倒计时，期间可以取消。", 10F, Muted);
            status.AccessibleRole = AccessibleRole.StaticText;
            layout.Controls.Add(status, 0, 5);

            TableLayoutPanel actions = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1,
                Margin = Padding.Empty
            };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            layout.Controls.Add(actions, 0, 6);

            cancel = new Button
            {
                Name = "cancel",
                Text = "暂不关机",
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 0),
                TabIndex = 2,
                DialogResult = DialogResult.Cancel,
                UseVisualStyleBackColor = true
            };
            cancel.Click += delegate { Close(); };
            actions.Controls.Add(cancel, 0, 0);

            confirm = new Button
            {
                Name = "confirm",
                Text = preview ? "已打卡，预览关机" : "已打卡，准备关机",
                Dock = DockStyle.Fill,
                Margin = Padding.Empty,
                BackColor = Accent,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Enabled = false,
                TabIndex = 1
            };
            confirm.FlatAppearance.BorderSize = 0;
            confirm.Click += BeginCountdown;
            actions.Controls.Add(confirm, 1, 0);
            UpdateConfirmButton();
            CancelButton = cancel;

            countdown.Interval = 1000;
            countdown.Tick += AdvanceCountdown;
            FormClosing += delegate
            {
                pending = false;
                countdown.Stop();
            };
        }

        /** @brief 创建使用统一字体、支持换行的文案控件。 */
        private Label CreateLabel(string name, string text, float size, Color color,
            FontStyle style = FontStyle.Regular)
        {
            return new Label
            {
                Name = name,
                Text = text,
                Font = CreateFont(size, style),
                ForeColor = color,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                AutoEllipsis = false,
                Margin = Padding.Empty
            };
        }

        /** @brief 记录窗口创建的字体，由窗口统一释放其 GDI 资源。 */
        private Font CreateFont(float size, FontStyle style)
        {
            Font font = new Font("Microsoft YaHei UI", size, style);
            ownedFonts.Add(font);
            return font;
        }

        /** @brief 同步确认按钮的可用状态和颜色，区分待确认与可执行操作。 */
        private void UpdateConfirmButton()
        {
            bool ready = clockedOut.Checked && !pending && !requesting;
            confirm.Enabled = ready;
            confirm.BackColor = ready ? Accent : Color.FromArgb(223, 230, 237);
            confirm.ForeColor = ready ? Color.White : Muted;
        }

        /** @brief 仅在已确认打卡时启动界面倒计时，此时尚未提交系统关机请求。 */
        private void BeginCountdown(object sender, EventArgs e)
        {
            if (!clockedOut.Checked || pending || requesting)
                return;

            pending = true;
            remainingSeconds = CountdownSeconds;
            clockedOut.Enabled = false;
            UpdateConfirmButton();
            confirm.Text = "准备关机中…";
            heading.Text = "关机后，记得拔电源。";
            description.Text = "请等电脑完全关闭，再拔掉电源插头。";
            cancel.Text = "取消关机";
            cancel.Focus();
            UpdateCountdownText();
            countdown.Start();
        }

        /** @brief 更新倒计时说明；系统休眠或界面停顿只会延后操作。 */
        private void UpdateCountdownText()
        {
            status.Text = preview
                ? "预览倒计时：" + remainingSeconds + " 秒。不会真正关机。"
                : remainingSeconds + " 秒后请求关机。按 Esc 或关闭窗口可取消。";
        }

        /** @brief 倒计时结束时最多提交一次关机请求，失败后显示错误并要求重新确认。 */
        private void AdvanceCountdown(object sender, EventArgs e)
        {
            if (!pending || requesting || IsDisposed)
                return;

            remainingSeconds--;
            if (remainingSeconds > 0)
            {
                UpdateCountdownText();
                return;
            }

            countdown.Stop();
            pending = false;
            requesting = true;
            cancel.Enabled = false;
            try
            {
                if (preview)
                {
                    heading.Text = "预览完成。";
                    description.Text = "正式使用时，此时会向 Windows 发出关机请求。";
                    status.Text = "电脑没有关机。关机完成后，别忘了拔掉电源插头。";
                    confirm.Text = "预览已完成";
                    cancel.Text = "关闭预览";
                    cancel.Enabled = true;
                    return;
                }

                requestShutdown();
                Close();
            }
            catch (Exception error)
            {
                heading.Text = "请检查关机状态。";
                description.Text = "关机请求未能确认成功，请先保存文件并检查系统提示。";
                status.Text = error.Message;
                status.ForeColor = Color.FromArgb(161, 49, 38);
                clockedOut.Checked = false;
                clockedOut.Enabled = true;
                confirm.Text = "已打卡，准备关机";
                cancel.Text = "关闭提醒";
                cancel.Enabled = true;
                requesting = false;
                UpdateConfirmButton();
            }
        }

        /** @brief 释放计时器、字体及内嵌图标资源；图标流保持打开直到图标释放。 */
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                countdown.Dispose();
            base.Dispose(disposing);
            if (disposing)
            {
                foreach (Font font in ownedFonts)
                    font.Dispose();
                ownedFonts.Clear();
                if (applicationIcon != null)
                    applicationIcon.Dispose();
                if (iconStream != null)
                    iconStream.Dispose();
            }
        }
    }
}
