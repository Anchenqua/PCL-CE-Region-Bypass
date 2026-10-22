using System;
using System.Drawing;
using System.Windows.Forms;

namespace PclRegionBypass
{
    /// <summary>关闭窗口时询问：最小化到托盘，还是直接退出。</summary>
    internal sealed class CloseChoiceDialog : Form
    {
        private readonly RadioButton _trayOption;
        private readonly RadioButton _exitOption;
        private readonly CheckBox _remember;

        public CloseChoiceDialog()
        {
            Text = "关闭窗口";
            ClientSize = new Size(392, 208);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Microsoft YaHei UI", 9f);

            var title = new Label
            {
                Text = "点击窗口的关闭按钮时，希望：",
                Location = new Point(18, 16),
                AutoSize = true
            };

            _trayOption = new RadioButton
            {
                Text = "最小化到托盘（后台继续工作，双击托盘图标可重新打开）",
                Location = new Point(22, 50),
                Size = new Size(356, 24),
                Checked = true
            };

            _exitOption = new RadioButton
            {
                Text = "直接退出程序（已解锁的启动器不受影响）",
                Location = new Point(22, 80),
                Size = new Size(356, 24)
            };

            _remember = new CheckBox
            {
                Text = "记住我的选择，以后不再询问",
                Location = new Point(22, 116),
                Size = new Size(300, 24)
            };

            var ok = new Button
            {
                Text = "确定",
                DialogResult = DialogResult.OK,
                Location = new Point(196, 158),
                Size = new Size(86, 30)
            };

            var cancel = new Button
            {
                Text = "取消",
                DialogResult = DialogResult.Cancel,
                Location = new Point(290, 158),
                Size = new Size(86, 30)
            };

            Controls.Add(title);
            Controls.Add(_trayOption);
            Controls.Add(_exitOption);
            Controls.Add(_remember);
            Controls.Add(ok);
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        /// <summary>tray / exit</summary>
        public string Choice
        {
            get { return _exitOption.Checked ? Settings.CloseExit : Settings.CloseTray; }
        }

        public bool Remember
        {
            get { return _remember.Checked; }
        }

        /// <summary>设置对话框的初始选中项。</summary>
        public void Preselect(string action)
        {
            if (string.Equals(action, Settings.CloseExit, StringComparison.OrdinalIgnoreCase))
                _exitOption.Checked = true;
            else
                _trayOption.Checked = true;
        }
    }
}
