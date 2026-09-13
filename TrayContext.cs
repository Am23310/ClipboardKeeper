using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace ClipboardKeeper
{
    // 隐藏窗口：接收系统剪贴板更新消息
    public class ClipboardWindow : NativeWindow
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public event Action ClipboardChanged;

        public ClipboardWindow()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE && ClipboardChanged != null) ClipboardChanged();
            base.WndProc(ref m);
        }
    }

    // 历史记录窗口
    public class HistoryForm : Form
    {
        private ListBox list;
        private Button btnCopy, btnDel;
        public Action<string> OnCopyRequested;

        public HistoryForm()
        {
            Text = "ClipboardKeeper — 剪贴板历史";
            Size = new Size(640, 480);
            StartPosition = FormStartPosition.CenterScreen;
            Icon = TrayContext.AppIcon();
            Font = new Font("Microsoft YaHei UI", 9f);

            list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
            list.DoubleClick += delegate { CopySelected(); };

            var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 44, Padding = new Padding(8), FlowDirection = FlowDirection.LeftToRight };
            btnCopy = new Button { Text = "重新复制 (双击)", Width = 140 };
            btnDel = new Button { Text = "删除选中条目", Width = 120 };
            btnCopy.Click += delegate { CopySelected(); };
            btnDel.Click += delegate { DeleteSelected(); };
            bottom.Controls.Add(btnCopy);
            bottom.Controls.Add(btnDel);

            Controls.Add(list);
            Controls.Add(bottom);
        }

        public void Reload(IList<HistoryItem> items)
        {
            list.BeginUpdate();
            list.Items.Clear();
            foreach (var it in items)
            {
                string text = (it.Text == null) ? "" : it.Text.Replace("\r", "").Replace("\n", " ⏎ ");
                if (text.Length > 120) text = text.Substring(0, 120) + "…";
                list.Items.Add(string.Format("{0:MM-dd HH:mm}  {1}", it.At, text));
            }
            list.EndUpdate();
        }

        void CopySelected()
        {
            if (list.SelectedIndex < 0 || OnCopyRequested == null) return;
            OnCopyRequested(list.SelectedIndex.ToString());
        }

        void DeleteSelected()
        {
            if (list.SelectedIndex < 0 || OnDelete == null) return;
            OnDelete(list.SelectedIndex);
        }

        public Action<int> OnDelete;

        // 点 X 只是隐藏：窗口若被销毁，后续剪贴板事件刷新它会崩溃
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }

    // 托盘常驻主体
    public class TrayContext : ApplicationContext
    {
        private HistoryStore store;
        private NotifyIcon tray;
        private ClipboardWindow clipWin;
        private HistoryForm historyForm;
        private ToolStripMenuItem tsPause;
        private ToolStripMenuItem tsAutostart;
        private bool paused = false;
        private string storePath;

        private static string DataDir()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClipboardKeeper");
        }

        private static string ExePath()
        {
            return System.Reflection.Assembly.GetExecutingAssembly().Location;
        }

        public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string RunValue = "ClipboardKeeper";

        public static bool IsAutostartEnabled()
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, false))
                return key != null && key.GetValue(RunValue) != null;
        }

        public static void SetAutostart(bool enabled)
        {
            using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (key == null) return;
                if (enabled) key.SetValue(RunValue, "\"" + ExePath() + "\"");
                else if (key.GetValue(RunValue) != null) key.DeleteValue(RunValue);
            }
        }

        // 取 exe 里内嵌的图标（空则退回系统默认）
        internal static Icon AppIcon()
        {
            try
            {
                Icon ic = Icon.ExtractAssociatedIcon(ExePath());
                if (ic != null) return ic;
            }
            catch (Exception) { }
            return SystemIcons.Application;
        }

        public TrayContext()
        {
            string dir = DataDir();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            storePath = Path.Combine(dir, "history.json");
            store = new HistoryStore(storePath);
            store.Load(); // 开机/重启后恢复上一次的记录

            clipWin = new ClipboardWindow();
            clipWin.ClipboardChanged += OnClipboardChanged;

            historyForm = new HistoryForm();
            historyForm.OnCopyRequested += CopyAt;
            historyForm.OnDelete += DeleteAt;

            tray = new NotifyIcon();
            tray.Icon = AppIcon();
            tray.Text = "ClipboardKeeper 剪贴板记录";
            tray.Visible = true;

            var menu = new ContextMenuStrip();
            menu.Items.Add("查看历史 (&H)", null, delegate { ShowHistory(); });
            tsPause = new ToolStripMenuItem("暂停记录 (&P)");
            tsPause.Click += delegate { TogglePause(); };
            menu.Items.Add(tsPause);
            tsAutostart = new ToolStripMenuItem("开机自启");
            tsAutostart.Click += delegate { ToggleAutostart(); };
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("清空所有记录", null, delegate { ClearAll(); });
            menu.Items.Add("退出", null, delegate { ExitApp(); });

            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { ShowHistory(); };

            // 首次运行默认开启自启（用户要求开机自启）
            if (!IsAutostartEnabled()) SetAutostart(true);
            tsAutostart.Checked = IsAutostartEnabled();

            NativeMethods.AddClipboardFormatListener(clipWin.Handle);
        }

        void ToggleAutostart()
        {
            SetAutostart(!IsAutostartEnabled());
            tsAutostart.Checked = IsAutostartEnabled();
        }

        void TogglePause()
        {
            paused = !paused;
            tsPause.Checked = paused;
            tray.Text = paused ? "ClipboardKeeper（已暂停）" : "ClipboardKeeper 剪贴板记录";
        }

        void OnClipboardChanged()
        {
            if (paused) return;
            try
            {
                if (!Clipboard.ContainsText()) return;
                string text = Clipboard.GetText();
                if (string.IsNullOrEmpty(text)) return;
                store.Add(text);
                store.Save();
                if (historyForm.Visible && !historyForm.IsDisposed) historyForm.Reload(store.Items);
            }
            catch (Exception) { /* 剪贴板可能被其他进程短暂锁定，忽略这一次 */ }
        }

        void CopyAt(object indexObj)
        {
            int idx;
            if (!int.TryParse(indexObj as string, out idx)) return;
            if (idx < 0 || idx >= store.Items.Count) return;
            Clipboard.SetText(store.Items[idx].Text);
            tray.ShowBalloonTip(800, "ClipboardKeeper", "已复制到剪贴板", ToolTipIcon.Info);
        }

        void DeleteAt(int idx)
        {
            if (idx < 0 || idx >= store.Items.Count) return;
            store.Items.RemoveAt(idx);
            store.Save();
            historyForm.Reload(store.Items);
        }

        void ShowHistory()
        {
            historyForm.Reload(store.Items);
            historyForm.Show();
            historyForm.Activate();
        }

        void ClearAll()
        {
            var r = MessageBox.Show("确定要清空所有剪贴板历史记录吗？", "ClipboardKeeper",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;
            store.Clear();
            store.Save();
            historyForm.Reload(store.Items);
        }

        void ExitApp()
        {
            NativeMethods.RemoveClipboardFormatListener(clipWin.Handle);
            tray.Visible = false;
            tray.Dispose();
            Application.Exit();
        }
    }

    internal static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    }
}
