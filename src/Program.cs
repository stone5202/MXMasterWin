using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace MXMasterWin
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            try
            {
                if (!SetProcessDpiAwarenessContext(new IntPtr(-4))) SetProcessDPIAware();
            }
            catch (EntryPointNotFoundException)
            {
                SetProcessDPIAware();
            }
            // Two instances would fight over the global mouse hook and HID++ requests.
            // A second launch only asks the running one to show its window.
            bool createdNew;
            // The mutex and window message keep the pre-2.0 name so that an old MX Master Portable
            // copy and this one still count as the same app.
            using (var mutex = new Mutex(true, @"Local\MXMasterPortable.SingleInstance", out createdNew))
            {
                if (!createdNew)
                {
                    PostMessage(new IntPtr(0xFFFF), ShowWindowMessage, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-win.ini");
                // Up to 1.1 the app was called MX Master Portable; carry its settings file over once.
                try
                {
                    string oldConfigPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-portable.ini");
                    if (!File.Exists(configPath) && File.Exists(oldConfigPath)) File.Move(oldConfigPath, configPath);
                }
                catch { }
                Lang.Load(configPath);
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
                GC.KeepAlive(mutex);
            }
            // The mutex is gone by now, so the relaunched copy is not mistaken for a second instance.
            if (RestartRequested)
            {
                try { Process.Start(Application.ExecutablePath, "--show"); }
                catch { }
            }
        }

        internal const string Version = "2.5";

        // Set before closing the main window to start the app again (used after a language change).
        internal static bool RestartRequested;

        internal static readonly int ShowWindowMessage = (int)RegisterWindowMessage("MXMasterPortable.ShowWindow");

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint RegisterWindowMessage(string name);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDPIAware();
    }

    // Two-language UI text. The choice is read from the INI before any window is built, so every
    // string is picked once; changing the language restarts the app.
    internal static class Lang
    {
        // "Auto" follows the Windows display language; "zh" and "en" force one.
        public static string Setting = "Auto";
        public static bool English;

        public static void Load(string iniPath)
        {
            try
            {
                if (File.Exists(iniPath))
                {
                    foreach (string line in File.ReadAllLines(iniPath))
                    {
                        string[] parts = line.Trim().Split(new[] { '=' }, 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals("Language", StringComparison.OrdinalIgnoreCase))
                            Setting = Normalize(parts[1]);
                    }
                }
            }
            catch { }
            English = Setting == "en" || (Setting == "Auto" &&
                !System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase));
        }

        public static string Normalize(string value)
        {
            string text = (value ?? "").Trim();
            if (text.Equals("en", StringComparison.OrdinalIgnoreCase)) return "en";
            if (text.Equals("zh", StringComparison.OrdinalIgnoreCase)) return "zh";
            return "Auto";
        }

        public static string T(string chinese, string english)
        {
            return English ? english : chinese;
        }
    }

    internal enum MouseInput
    {
        Middle,
        XButton1,
        XButton2,
        WheelLeft,
        WheelRight,
        GestureButton,
        ActionRingButton,
        WheelModeButton
    }

    internal enum MappedAction
    {
        PassThrough,
        BrowserBack,
        BrowserForward,
        Copy,
        Paste,
        Cut,
        Undo,
        Redo,
        TaskView,
        ShowDesktop,
        DesktopLeft,
        DesktopRight,
        AltTab,
        PlayPause,
        VolumeUp,
        VolumeDown,
        Mute,
        Enter,
        Escape,
        NextTrack,
        PrevTrack,
        FileExplorer,
        Settings,
        Calculator,
        TaskManager,
        Search,
        StartMenu,
        NewTab,
        CloseTab,
        ReopenTab,
        Refresh,
        ZoomIn,
        ZoomOut,
        Screenshot,
        Emoji,
        ClipboardHistory,
        LockPC,
        QuickSettings,
        MinimizeWindow,
        MaximizeWindow,
        CloseWindow,
        SnapLeft,
        SnapRight,
        SelectAll,
        Save,
        Find,
        Delete,
        InsertDate,
        InsertTime,
        OpenSettings,
        PrecisionHold,
        PrecisionToggle,
        WheelModeToggle,
        HostChannel1,
        HostChannel2,
        HostChannel3,
        CustomShortcut,
        LaunchProgram,
        // Only offered for buttons on another mouse (G502).
        AppGesture,
        AppRing,
        AppMouseFunction
    }

    // An action plus its optional argument (shortcut text or program command line).
    // INI form: "Copy", "CustomShortcut|Ctrl+Shift+T", "LaunchProgram|\"C:\\app.exe\" -arg".
    internal sealed class ActionBinding
    {
        public readonly MappedAction Action;
        public readonly string Argument;

        public ActionBinding(MappedAction action, string argument)
        {
            Action = action;
            Argument = argument == null ? "" : argument.Trim();
        }

        public static ActionBinding Of(MappedAction action)
        {
            return new ActionBinding(action, "");
        }

        public bool HasArgument
        {
            get { return Action == MappedAction.CustomShortcut || Action == MappedAction.LaunchProgram; }
        }

        // True when triggering this binding must leave the original mouse event untouched.
        public bool IsPassThrough
        {
            get { return Action == MappedAction.PassThrough || (HasArgument && Argument.Length == 0); }
        }

        public static ActionBinding Parse(string text)
        {
            if (text == null) return null;
            string[] parts = text.Split(new[] { '|' }, 2);
            MappedAction action;
            if (!Enum.TryParse(parts[0].Trim(), true, out action) || !Enum.IsDefined(typeof(MappedAction), action))
                return null;
            return new ActionBinding(action, parts.Length > 1 ? parts[1] : "");
        }

        public override string ToString()
        {
            return HasArgument && Argument.Length > 0 ? Action + "|" + Argument : Action.ToString();
        }

        public string ShortLabel
        {
            get
            {
                if (IsPassThrough) return "—";
                if (Action == MappedAction.CustomShortcut) return Clip(Argument);
                if (Action == MappedAction.LaunchProgram) return Clip(ProgramName(Argument));
                return ActionCatalog.ShortLabel(Action);
            }
        }

        public static string ProgramName(string command)
        {
            string file, arguments;
            ActionRunner.SplitCommand(command, out file, out arguments);
            if (file.IndexOf("://", StringComparison.Ordinal) >= 0) return file;
            try
            {
                string name = Path.GetFileNameWithoutExtension(file);
                if (!String.IsNullOrEmpty(name)) return name;
            }
            catch { }
            return file;
        }

        private static string Clip(string text)
        {
            return text.Length <= 14 ? text : text.Substring(0, 13) + "…";
        }
    }

    internal static class ShortcutText
    {
        public static string Format(Keys key, bool ctrl, bool alt, bool shift, bool win)
        {
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Win");
            if (key != Keys.None) parts.Add(key.ToString());
            return String.Join("+", parts.ToArray());
        }

        public static bool TryParse(string text, out Keys key, out bool ctrl, out bool alt, out bool shift, out bool win)
        {
            key = Keys.None;
            ctrl = alt = shift = win = false;
            if (String.IsNullOrEmpty(text)) return false;
            foreach (string raw in text.Split('+'))
            {
                string token = raw.Trim();
                if (token.Length == 0) return false;
                if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) ctrl = true;
                else if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
                else if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
                else if (token.Equals("Win", StringComparison.OrdinalIgnoreCase)) win = true;
                else
                {
                    Keys parsed;
                    if (key != Keys.None || !Enum.TryParse(token, true, out parsed)) return false;
                    int code = (int)parsed;
                    if (code <= 0 || code > 0xFE) return false;
                    key = parsed;
                }
            }
            return key != Keys.None;
        }
    }

    internal sealed class ActionChoice
    {
        public MappedAction Action { get; private set; }
        public string Label { get; private set; }

        public ActionChoice(MappedAction action, string label)
        {
            Action = action;
            Label = label;
        }
    }

    internal static class ActionCatalog
    {
        public static List<ActionChoice> CreateChoices()
        {
            return new List<ActionChoice>
            {
                new ActionChoice(MappedAction.PassThrough, Lang.T("保留原功能（不攔截）", "Keep original function")),
                new ActionChoice(MappedAction.OpenSettings, Lang.T("MX設定", "MX Settings")),
                new ActionChoice(MappedAction.PrecisionHold, Lang.T("精準模式（按住）", "Precision mode (hold)")),
                new ActionChoice(MappedAction.PrecisionToggle, Lang.T("精準模式（開／關）", "Precision mode (toggle)")),
                new ActionChoice(MappedAction.WheelModeToggle, Lang.T("切換段落／自由滾動", "Toggle ratchet / free spin")),
                new ActionChoice(MappedAction.HostChannel1, Lang.T("切換到頻道 1（Easy-Switch）", "Switch to channel 1 (Easy-Switch)")),
                new ActionChoice(MappedAction.HostChannel2, Lang.T("切換到頻道 2（Easy-Switch）", "Switch to channel 2 (Easy-Switch)")),
                new ActionChoice(MappedAction.HostChannel3, Lang.T("切換到頻道 3（Easy-Switch）", "Switch to channel 3 (Easy-Switch)")),
                new ActionChoice(MappedAction.BrowserBack, Lang.T("上一頁", "Back")),
                new ActionChoice(MappedAction.BrowserForward, Lang.T("下一頁", "Forward")),
                new ActionChoice(MappedAction.Copy, Lang.T("複製", "Copy")),
                new ActionChoice(MappedAction.Paste, Lang.T("貼上", "Paste")),
                new ActionChoice(MappedAction.Cut, Lang.T("剪下", "Cut")),
                new ActionChoice(MappedAction.Undo, Lang.T("復原", "Undo")),
                new ActionChoice(MappedAction.Redo, Lang.T("重做", "Redo")),
                new ActionChoice(MappedAction.TaskView, Lang.T("工作檢視", "Task View")),
                new ActionChoice(MappedAction.ShowDesktop, Lang.T("顯示桌面", "Show desktop")),
                new ActionChoice(MappedAction.DesktopLeft, Lang.T("切換到左側虛擬桌面", "Virtual desktop left")),
                new ActionChoice(MappedAction.DesktopRight, Lang.T("切換到右側虛擬桌面", "Virtual desktop right")),
                new ActionChoice(MappedAction.AltTab, Lang.T("切換視窗（Alt+Tab）", "Switch window (Alt+Tab)")),
                new ActionChoice(MappedAction.PlayPause, Lang.T("播放／暫停", "Play / pause")),
                new ActionChoice(MappedAction.VolumeUp, Lang.T("提高音量", "Volume up")),
                new ActionChoice(MappedAction.VolumeDown, Lang.T("降低音量", "Volume down")),
                new ActionChoice(MappedAction.Mute, Lang.T("靜音／取消靜音", "Mute / unmute")),
                new ActionChoice(MappedAction.NextTrack, Lang.T("下一首", "Next track")),
                new ActionChoice(MappedAction.PrevTrack, Lang.T("上一首", "Previous track")),
                new ActionChoice(MappedAction.FileExplorer, Lang.T("檔案總管", "File Explorer")),
                new ActionChoice(MappedAction.Settings, Lang.T("Windows 設定", "Windows Settings")),
                new ActionChoice(MappedAction.Calculator, Lang.T("小算盤", "Calculator")),
                new ActionChoice(MappedAction.TaskManager, Lang.T("工作管理員", "Task Manager")),
                new ActionChoice(MappedAction.Search, Lang.T("Windows 搜尋", "Windows Search")),
                new ActionChoice(MappedAction.StartMenu, Lang.T("開始功能表", "Start menu")),
                new ActionChoice(MappedAction.NewTab, Lang.T("新分頁", "New tab")),
                new ActionChoice(MappedAction.CloseTab, Lang.T("關閉分頁", "Close tab")),
                new ActionChoice(MappedAction.ReopenTab, Lang.T("重新開啟已關閉的分頁", "Reopen closed tab")),
                new ActionChoice(MappedAction.Refresh, Lang.T("重新整理", "Refresh")),
                new ActionChoice(MappedAction.ZoomIn, Lang.T("放大", "Zoom in")),
                new ActionChoice(MappedAction.ZoomOut, Lang.T("縮小", "Zoom out")),
                new ActionChoice(MappedAction.Screenshot, Lang.T("螢幕截圖（剪取工具）", "Screenshot (Snipping Tool)")),
                new ActionChoice(MappedAction.Emoji, Lang.T("表情符號", "Emoji panel")),
                new ActionChoice(MappedAction.ClipboardHistory, Lang.T("剪貼簿歷程記錄", "Clipboard history")),
                new ActionChoice(MappedAction.LockPC, Lang.T("鎖定電腦", "Lock PC")),
                new ActionChoice(MappedAction.QuickSettings, Lang.T("快速設定（Win+A）", "Quick settings (Win+A)")),
                new ActionChoice(MappedAction.MinimizeWindow, Lang.T("最小化視窗", "Minimize window")),
                new ActionChoice(MappedAction.MaximizeWindow, Lang.T("最大化視窗", "Maximize window")),
                new ActionChoice(MappedAction.CloseWindow, Lang.T("關閉視窗（Alt+F4）", "Close window (Alt+F4)")),
                new ActionChoice(MappedAction.SnapLeft, Lang.T("視窗貼齊左側", "Snap window left")),
                new ActionChoice(MappedAction.SnapRight, Lang.T("視窗貼齊右側", "Snap window right")),
                new ActionChoice(MappedAction.SelectAll, Lang.T("全選", "Select all")),
                new ActionChoice(MappedAction.Save, Lang.T("儲存", "Save")),
                new ActionChoice(MappedAction.Find, Lang.T("尋找", "Find")),
                new ActionChoice(MappedAction.Delete, Lang.T("Delete 鍵", "Delete key")),
                new ActionChoice(MappedAction.InsertDate, Lang.T("輸入今天日期", "Type today's date")),
                new ActionChoice(MappedAction.InsertTime, Lang.T("輸入目前時間", "Type the current time")),
                new ActionChoice(MappedAction.Enter, Lang.T("Enter 鍵", "Enter key")),
                new ActionChoice(MappedAction.Escape, Lang.T("Esc 鍵", "Esc key")),
                new ActionChoice(MappedAction.CustomShortcut, Lang.T("自訂快捷鍵…", "Custom shortcut…")),
                new ActionChoice(MappedAction.LaunchProgram, Lang.T("啟動程式或開啟檔案…", "Launch program / file…"))
            };
        }

        // Actions for a button on another mouse (G502). Actions that drive the MX Master itself are left out.
        public static List<ActionChoice> ForOtherDevice()
        {
            var list = new List<ActionChoice>
            {
                new ActionChoice(MappedAction.PassThrough, Lang.T("不做任何事", "Do nothing")),
                new ActionChoice(MappedAction.AppGesture, Lang.T("四向手勢（按住）", "4-way gesture (hold)")),
                new ActionChoice(MappedAction.AppRing, Lang.T("叫出圓環", "Open Actions Ring"))
            };
            foreach (ActionChoice choice in CreateChoices())
            {
                switch (choice.Action)
                {
                    case MappedAction.PassThrough:
                    case MappedAction.PrecisionHold:
                    case MappedAction.PrecisionToggle:
                    case MappedAction.WheelModeToggle:
                    case MappedAction.HostChannel1:
                    case MappedAction.HostChannel2:
                    case MappedAction.HostChannel3:
                        continue;
                }
                list.Add(choice);
            }
            list.Add(new ActionChoice(MappedAction.AppMouseFunction, Lang.T("改用滑鼠內建功能…", "Use a mouse function…")));
            return list;
        }

        // Compact wording for the Actions Ring, where each slice only fits a few characters.
        public static string ShortLabel(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.PassThrough: return "—";
                case MappedAction.DesktopLeft: return Lang.T("左側桌面", "Left desktop");
                case MappedAction.DesktopRight: return Lang.T("右側桌面", "Right desktop");
                case MappedAction.AltTab: return Lang.T("切換視窗", "Switch window");
                case MappedAction.Mute: return Lang.T("靜音", "Mute");
                case MappedAction.Enter: return "Enter";
                case MappedAction.Escape: return "Esc";
                case MappedAction.Delete: return "Delete";
                case MappedAction.Settings: return Lang.T("設定", "Settings");
                case MappedAction.Search: return Lang.T("搜尋", "Search");
                case MappedAction.ReopenTab: return Lang.T("重開分頁", "Reopen tab");
                case MappedAction.Screenshot: return Lang.T("螢幕截圖", "Screenshot");
                case MappedAction.Emoji: return Lang.T("表情符號", "Emoji");
                case MappedAction.ClipboardHistory: return Lang.T("剪貼簿", "Clipboard");
                case MappedAction.QuickSettings: return Lang.T("快速設定", "Quick settings");
                case MappedAction.MinimizeWindow: return Lang.T("最小化", "Minimize");
                case MappedAction.MaximizeWindow: return Lang.T("最大化", "Maximize");
                case MappedAction.CloseWindow: return Lang.T("關閉視窗", "Close window");
                case MappedAction.SnapLeft: return Lang.T("貼齊左側", "Snap left");
                case MappedAction.SnapRight: return Lang.T("貼齊右側", "Snap right");
                case MappedAction.InsertDate: return Lang.T("今天日期", "Date");
                case MappedAction.InsertTime: return Lang.T("目前時間", "Time");
                case MappedAction.OpenSettings: return Lang.T("MX設定", "MX Settings");
                case MappedAction.PrecisionHold:
                case MappedAction.PrecisionToggle: return Lang.T("精準模式", "Precision");
                case MappedAction.WheelModeToggle: return Lang.T("滾動模式", "Wheel mode");
                case MappedAction.HostChannel1: return Lang.T("頻道 1", "Channel 1");
                case MappedAction.HostChannel2: return Lang.T("頻道 2", "Channel 2");
                case MappedAction.HostChannel3: return Lang.T("頻道 3", "Channel 3");
            }
            foreach (ActionChoice choice in CreateChoices())
                if (choice.Action == action) return choice.Label;
            return action.ToString();
        }
    }

    // A mapping drop-down plus the button that edits a shortcut or program argument.
    internal sealed class ActionPicker
    {
        public readonly ComboBox Combo = new ComboBox();
        public readonly Button Edit;
        private string shortcut = "";
        private string program = "";
        private int committedIndex;
        private readonly int spacing;
        private bool shown = true;

        // Raised only for changes made by the user, not when Binding is assigned from code.
        public event Action Changed;

        public ActionPicker(Control parent, Point location, int comboWidth, int editWidth, int gap)
        {
            spacing = gap;
            Combo.DropDownStyle = ComboBoxStyle.DropDownList;
            Combo.Location = location;
            Combo.Width = comboWidth;
            Combo.FormattingEnabled = true;
            Combo.MaxDropDownItems = 18;
            Combo.Format += delegate(object sender, ListControlConvertEventArgs e)
            {
                ActionChoice choice = e.ListItem as ActionChoice;
                if (choice == null) return;
                if (choice.Action == MappedAction.CustomShortcut && shortcut.Length > 0)
                    e.Value = Lang.T("快捷鍵：", "Shortcut: ") + shortcut;
                else if (choice.Action == MappedAction.LaunchProgram && program.Length > 0)
                    e.Value = Lang.T("啟動：", "Launch: ") + ActionBinding.ProgramName(program);
                else
                    e.Value = choice.Label;
            };
            foreach (ActionChoice choice in ActionCatalog.CreateChoices())
                Combo.Items.Add(choice);
            Combo.SelectedIndex = 0;
            Combo.SelectionChangeCommitted += delegate { OnUserSelected(); };
            Combo.SelectedIndexChanged += delegate { UpdateEditState(); };
            parent.Controls.Add(Combo);

            Edit = Theme.CreateButton(Lang.T("設定", "Edit"), false);
            Edit.Font = new Font(Theme.UiFont, 8.5F);
            Edit.Location = new Point(location.X + comboWidth + gap, location.Y);
            Edit.Size = new Size(editWidth, Combo.Height);
            Edit.Click += delegate { if (EditArgument()) OnChanged(); };
            // The ComboBox only reaches its final height once its font and handle are resolved.
            Combo.SizeChanged += delegate { Edit.Height = Combo.Height; };
            Combo.HandleCreated += delegate { Edit.Height = Combo.Height; };
            parent.Controls.Add(Edit);
            UpdateEditState();
        }

        // Replaces the standard action list.
        public void UseChoices(IEnumerable<ActionChoice> choices)
        {
            Combo.Items.Clear();
            foreach (ActionChoice choice in choices) Combo.Items.Add(choice);
            Combo.SelectedIndex = 0;
            committedIndex = 0;
            UpdateEditState();
        }

        public void SetShown(bool value)
        {
            shown = value;
            Combo.Visible = value;
            UpdateEditState();
        }

        // Moves the picker so it fills this row.
        public void Place(Rectangle row)
        {
            int left = row.Right - Edit.Width;
            if (Combo.Left != row.X || Combo.Top != row.Y) Combo.Location = row.Location;
            if (Edit.Left != left || Edit.Top != row.Y) Edit.Location = new Point(left, row.Y);
            UpdateEditState();
        }

        private MappedAction SelectedAction
        {
            get
            {
                ActionChoice choice = Combo.SelectedItem as ActionChoice;
                return choice == null ? MappedAction.PassThrough : choice.Action;
            }
        }

        public ActionBinding Binding
        {
            get
            {
                MappedAction action = SelectedAction;
                string argument = action == MappedAction.CustomShortcut ? shortcut
                    : action == MappedAction.LaunchProgram ? program : "";
                return new ActionBinding(action, argument);
            }
            set
            {
                ActionBinding binding = value ?? ActionBinding.Of(MappedAction.PassThrough);
                if (binding.Action == MappedAction.CustomShortcut) shortcut = binding.Argument;
                else if (binding.Action == MappedAction.LaunchProgram) program = binding.Argument;
                int index = 0;
                for (int i = 0; i < Combo.Items.Count; i++)
                {
                    ActionChoice choice = Combo.Items[i] as ActionChoice;
                    if (choice != null && choice.Action == binding.Action) { index = i; break; }
                }
                Combo.SelectedIndex = index;
                committedIndex = index;
                RefreshText();
            }
        }

        private void OnUserSelected()
        {
            MappedAction action = SelectedAction;
            bool needsArgument = (action == MappedAction.CustomShortcut && shortcut.Length == 0) ||
                (action == MappedAction.LaunchProgram && program.Length == 0);
            if (!needsArgument)
            {
                committedIndex = Combo.SelectedIndex;
                OnChanged();
                return;
            }
            // Let the drop-down close before the modal editor opens; cancelling restores the old choice.
            Combo.BeginInvoke(new Action(delegate
            {
                if (EditArgument())
                {
                    committedIndex = Combo.SelectedIndex;
                    OnChanged();
                }
                else Combo.SelectedIndex = committedIndex;
            }));
        }

        private void OnChanged()
        {
            Action handler = Changed;
            if (handler != null) handler();
        }

        private bool EditArgument()
        {
            MappedAction action = SelectedAction;
            Form owner = Combo.FindForm();
            if (action == MappedAction.CustomShortcut)
            {
                string result = ShortcutDialog.Prompt(owner, shortcut);
                if (result == null) return false;
                shortcut = result;
            }
            else if (action == MappedAction.LaunchProgram)
            {
                string result = ProgramDialog.Prompt(owner, program);
                if (result == null) return false;
                program = result;
            }
            else return false;
            RefreshText();
            return true;
        }

        private void RefreshText()
        {
            // Toggling formatting makes the ComboBox re-query the Format handler for its items.
            Combo.FormattingEnabled = false;
            Combo.FormattingEnabled = true;
            UpdateEditState();
        }

        private void UpdateEditState()
        {
            if (Edit == null) return;
            MappedAction action = SelectedAction;
            // Edit.Visible reads false while the form is still hidden, so keep the decision in a local.
            bool editable = action == MappedAction.CustomShortcut || action == MappedAction.LaunchProgram;
            Edit.Visible = editable && shown;
            // The drop-down takes the whole row unless the edit button needs its share.
            Combo.Width = editable ? Edit.Left - Combo.Left - spacing : Edit.Right - Combo.Left;
        }
    }

    internal static class DialogStyle
    {
        public static void Apply(Form form, string title, int logicalWidth, int logicalHeight, float scale)
        {
            form.Text = title;
            form.Font = new Font(Theme.UiFont, 9.5F);
            form.BackColor = Theme.Page;
            form.ForeColor = Theme.Text;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.ClientSize = new Size((int)Math.Round(logicalWidth * scale), (int)Math.Round(logicalHeight * scale));
        }
    }

    internal sealed class ShortcutDialog : Form
    {
        private readonly Label preview = new Label();
        private readonly CheckBox winKey = new CheckBox();
        private readonly Button ok;
        private Keys key = Keys.None;
        private bool ctrl, alt, shift;

        private ShortcutDialog(string current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂快捷鍵", "Custom shortcut"), 420, 214, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("請直接按下要送出的組合鍵，例如 Ctrl+Shift+T。", "Press the key combination to send, e.g. Ctrl+Shift+T."),
                AutoSize = true,
                Location = new Point(s(20), s(18))
            });

            preview.Location = new Point(s(20), s(48));
            preview.Size = new Size(s(380), s(52));
            preview.TextAlign = ContentAlignment.MiddleCenter;
            preview.BackColor = Theme.Card;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.Font = new Font("Segoe UI Semibold", 14F);
            Controls.Add(preview);

            winKey.Text = Lang.T("加入 Win 鍵（Windows 會攔截 Win 組合鍵，需在此勾選）", "Add the Win key (Windows intercepts Win combos)");
            winKey.AutoSize = true;
            winKey.Location = new Point(s(20), s(114));
            winKey.CheckedChanged += delegate { UpdatePreview(); };
            Controls.Add(winKey);

            ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(190), s(162));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(300), s(162));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            bool win;
            if (ShortcutText.TryParse(current, out key, out ctrl, out alt, out shift, out win))
                winKey.Checked = win;
            UpdatePreview();
        }

        public static string Prompt(IWin32Window owner, string current)
        {
            using (var dialog = new ShortcutDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK || dialog.key == Keys.None) return null;
                return ShortcutText.Format(dialog.key, dialog.ctrl, dialog.alt, dialog.shift, dialog.winKey.Checked);
            }
        }

        // Every key press is treated as shortcut input, so the dialog is confirmed with the mouse.
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            if (code != Keys.ControlKey && code != Keys.ShiftKey && code != Keys.Menu &&
                code != Keys.LWin && code != Keys.RWin && code != Keys.None)
            {
                key = code;
                ctrl = (keyData & Keys.Control) != 0;
                alt = (keyData & Keys.Alt) != 0;
                shift = (keyData & Keys.Shift) != 0;
                UpdatePreview();
            }
            return true;
        }

        private void UpdatePreview()
        {
            preview.Text = key == Keys.None ? Lang.T("（尚未設定）", "(not set)") : ShortcutText.Format(key, ctrl, alt, shift, winKey.Checked);
            ok.Enabled = key != Keys.None;
        }
    }

    internal sealed class ProgramDialog : Form
    {
        private readonly TextBox command = new TextBox();
        private readonly Button ok;

        private ProgramDialog(string current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("啟動程式或開啟檔案", "Launch a program or open a file"), 520, 190, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("程式、檔案、資料夾或網址：", "Program, file, folder or URL:"),
                AutoSize = true,
                Location = new Point(s(20), s(18))
            });

            command.Location = new Point(s(20), s(46));
            command.Width = s(380);
            command.Text = current ?? "";
            command.TextChanged += delegate { ok.Enabled = command.Text.Trim().Length > 0; };
            Controls.Add(command);

            Button browse = Theme.CreateButton(Lang.T("瀏覽…", "Browse…"), false);
            browse.Location = new Point(s(410), s(44));
            browse.Size = new Size(s(90), s(30));
            browse.Click += delegate
            {
                using (var picker = new OpenFileDialog())
                {
                    picker.Filter = Lang.T("程式 (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|所有檔案 (*.*)|*.*",
                        "Programs (*.exe;*.bat;*.cmd;*.lnk)|*.exe;*.bat;*.cmd;*.lnk|All files (*.*)|*.*");
                    if (picker.ShowDialog(this) == DialogResult.OK)
                        command.Text = "\"" + picker.FileName + "\"";
                }
            };
            Controls.Add(browse);

            Controls.Add(new Label
            {
                Text = Lang.T("需要加參數時，請用引號包住程式路徑，例如 \"C:\\Tools\\app.exe\" -option",
                    "To pass arguments, quote the program path, e.g. \"C:\\Tools\\app.exe\" -option"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(s(20), s(86))
            });

            ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(290), s(138));
            ok.Size = new Size(s(100), s(34));
            ok.Enabled = command.Text.Trim().Length > 0;
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(400), s(138));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        public static string Prompt(IWin32Window owner, string current)
        {
            using (var dialog = new ProgramDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                string text = dialog.command.Text.Trim();
                return text.Length == 0 ? null : text;
            }
        }
    }

    internal sealed class RingDialog : Form
    {
        private readonly ActionPicker[] pickers = new ActionPicker[8];
        private readonly Label[] badges = new Label[8];
        private readonly PictureBox guide = new PictureBox();
        // Never shown: it only draws the numbered picture of the ring being edited.
        private readonly ActionRingOverlay renderer = new ActionRingOverlay();
        private int highlight = -1;

        private RingDialog(ActionBinding[] current)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂 Actions Ring", "Customize Actions Ring"), 756, 430, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("圖上的編號對應右邊各列；點圖上的一格可直接設定該位置。", "Numbers on the picture match the rows; click a slice to set it."),
                AutoSize = true,
                Location = new Point(s(20), s(16))
            });

            guide.SizeMode = PictureBoxSizeMode.Zoom;
            guide.Location = new Point(s(20), s(50));
            guide.Size = new Size(s(308), s(308));
            guide.Cursor = Cursors.Hand;
            guide.MouseMove += delegate(object sender, MouseEventArgs e) { SetHighlight(renderer.SliceAt(e.Location, guide.Size)); };
            guide.MouseLeave += delegate { SetHighlight(-1); };
            guide.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                int slice = renderer.SliceAt(e.Location, guide.Size);
                if (slice < 0) return;
                pickers[slice].Combo.Focus();
                pickers[slice].Combo.DroppedDown = true;
            };
            Controls.Add(guide);

            for (int i = 0; i < 8; i++)
            {
                int index = i;
                int y = 50 + i * 38;
                Label badge = new Label { Location = new Point(s(350), s(y + 2)), Size = new Size(s(22), s(22)) };
                badge.Paint += delegate(object sender, PaintEventArgs e)
                {
                    bool active = highlight == index;
                    e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    var circle = new RectangleF(1F, 1F, badge.Width - 2.5F, badge.Height - 2.5F);
                    using (var fill = new SolidBrush(active ? Theme.Accent : Theme.Card))
                    using (var ring = new Pen(Theme.Accent, 1.5F * scale))
                    using (var ink = new SolidBrush(active ? Color.White : Theme.Accent))
                    using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                    using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        e.Graphics.FillEllipse(fill, circle);
                        e.Graphics.DrawEllipse(ring, circle);
                        e.Graphics.DrawString((index + 1).ToString(), font, ink, circle, center);
                    }
                };
                badge.MouseEnter += delegate { SetHighlight(index); };
                badges[i] = badge;
                Controls.Add(badge);

                pickers[i] = new ActionPicker(this, new Point(s(384), s(y)), s(294), s(52), s(6));
                pickers[i].Binding = current[i];
                pickers[i].Changed += delegate { RedrawGuide(); };
                pickers[i].Combo.Enter += delegate { SetHighlight(index); };
                pickers[i].Combo.MouseEnter += delegate { SetHighlight(index); };
            }

            Button reset = Theme.CreateButton(Lang.T("還原預設", "Reset"), false);
            reset.Location = new Point(s(20), s(378));
            reset.Size = new Size(s(100), s(34));
            reset.Click += delegate
            {
                ActionBinding[] defaults = ActionRingOverlay.DefaultSlots();
                for (int i = 0; i < 8; i++) pickers[i].Binding = defaults[i];
                RedrawGuide();
            };
            Controls.Add(reset);
            Button ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(526), s(378));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(636), s(378));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);

            RedrawGuide();
        }

        // Highlights one position in both the picture and the list.
        private void SetHighlight(int index)
        {
            if (index == highlight) return;
            highlight = index;
            foreach (Label badge in badges) badge.Invalidate();
            RedrawGuide();
        }

        private void RedrawGuide()
        {
            var bindings = new ActionBinding[8];
            for (int i = 0; i < 8; i++) bindings[i] = pickers[i] != null ? pickers[i].Binding : ActionBinding.Of(MappedAction.PassThrough);
            renderer.SetSlots(bindings);
            Image old = guide.Image;
            guide.Image = renderer.RenderGuide(highlight);
            if (old != null) old.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (guide.Image != null) guide.Image.Dispose();
                renderer.Dispose();
            }
            base.Dispose(disposing);
        }

        public static ActionBinding[] Prompt(IWin32Window owner, ActionBinding[] current)
        {
            using (var dialog = new RingDialog(current))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                var result = new ActionBinding[8];
                for (int i = 0; i < 8; i++) result[i] = dialog.pickers[i].Binding;
                return result;
            }
        }
    }

    // Optional start with Windows: a plain shortcut in the user's own Startup folder. It needs no
    // administrator rights, touches no registry key, and the user can see and delete it.
    internal static class StartupShortcut
    {
        private static string LinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MX Master Win.lnk"); }
        }

        // The shortcut written by versions before 2.0 (MX Master Portable).
        private static string OldLinkPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "MX Master Portable.lnk"); }
        }

        public static bool Exists
        {
            get
            {
                try { return File.Exists(LinkPath) || File.Exists(OldLinkPath); }
                catch { return false; }
            }
        }

        // Writes (or rewrites) the shortcut so that it points at the EXE that is running now.
        public static bool Create(out string error)
        {
            error = "";
            object link = null;
            try
            {
                string exe = Application.ExecutablePath;
                link = new ShellLink();
                var shell = (IShellLinkW)link;
                shell.SetPath(exe);
                shell.SetWorkingDirectory(Path.GetDirectoryName(exe));
                shell.SetDescription("MX Master Win");
                shell.SetIconLocation(exe, 0);
                ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Save(LinkPath, false);
                if (File.Exists(OldLinkPath)) File.Delete(OldLinkPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }

        public static bool Remove(out string error)
        {
            error = "";
            try
            {
                if (File.Exists(LinkPath)) File.Delete(LinkPath);
                if (File.Exists(OldLinkPath)) File.Delete(OldLinkPath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        // Only the vtable order matters here; the getters are declared just to keep the slots in place.
        [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int length, IntPtr findData, int flags);
            void GetIDList(out IntPtr idList);
            void SetIDList(IntPtr idList);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int length);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
            void GetHotkey(out short hotkey);
            void SetHotkey(short hotkey);
            void GetShowCmd(out int showCommand);
            void SetShowCmd(int showCommand);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int length, out int icon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int icon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, int reserved);
            void Resolve(IntPtr window, int flags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
        }
    }

    // Lets the user pick the action for each of the four gesture directions.
    internal sealed class GestureDialog : Form
    {
        private readonly ActionPicker[] pickers = new ActionPicker[4];
        private readonly SliderBar sensitivity = new SliderBar();

        // Up, right, down, left.
        public static ActionBinding[] Defaults()
        {
            return new[]
            {
                ActionBinding.Of(MappedAction.TaskView), ActionBinding.Of(MappedAction.BrowserForward),
                ActionBinding.Of(MappedAction.ShowDesktop), ActionBinding.Of(MappedAction.BrowserBack)
            };
        }

        private GestureDialog(ActionBinding[] current, int currentSensitivity)
        {
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };
            DialogStyle.Apply(this, Lang.T("自訂拇指手勢", "Customize gestures"), 470, 330, scale);

            Controls.Add(new Label
            {
                Text = Lang.T("按住拇指手勢鍵並往一個方向滑動時要執行的動作。", "What to run when you hold the gesture button and swipe."),
                AutoSize = true,
                Location = new Point(s(20), s(16))
            });

            string[] names =
            {
                Lang.T("↑  向上", "↑  Up"), Lang.T("→  向右", "→  Right"),
                Lang.T("↓  向下", "↓  Down"), Lang.T("←  向左", "←  Left")
            };
            for (int i = 0; i < 4; i++)
            {
                int y = 52 + i * 38;
                Controls.Add(new Label { Text = names[i], AutoSize = false, Location = new Point(s(20), s(y + 3)), Size = new Size(s(82), s(22)) });
                pickers[i] = new ActionPicker(this, new Point(s(106), s(y)), s(286), s(52), s(6));
                pickers[i].Binding = current[i];
            }

            // How far a swipe must travel before a direction is chosen; 50% is the original distance.
            Controls.Add(new Label { Text = Lang.T("靈敏度", "Sensitivity"), AutoSize = false, Location = new Point(s(20), s(211)), Size = new Size(s(82), s(22)) });
            var readout = new Label { AutoSize = false, TextAlign = ContentAlignment.MiddleRight, Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold), Location = new Point(s(392), s(209)), Size = new Size(s(58), s(26)) };
            sensitivity.Minimum = 0;
            sensitivity.Maximum = 100;
            sensitivity.Increment = 5;
            sensitivity.Location = new Point(s(100), s(208));
            sensitivity.Size = new Size(s(290), s(28));
            EventHandler showSensitivity = delegate { readout.Text = sensitivity.Value + "%"; };
            sensitivity.ValueChanged += showSensitivity;
            sensitivity.Value = currentSensitivity;
            showSensitivity(null, EventArgs.Empty);
            Controls.Add(sensitivity);
            Controls.Add(readout);
            Controls.Add(new Label
            {
                Text = Lang.T("越高，滑動越短的距離就會判定方向", "Higher means a shorter swipe picks a direction"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(s(106), s(240))
            });

            Button reset = Theme.CreateButton(Lang.T("還原預設", "Reset"), false);
            reset.Location = new Point(s(20), s(278));
            reset.Size = new Size(s(100), s(34));
            reset.Click += delegate
            {
                ActionBinding[] defaults = Defaults();
                for (int i = 0; i < 4; i++) pickers[i].Binding = defaults[i];
                sensitivity.Value = 50;
            };
            Controls.Add(reset);
            Button ok = Theme.CreateButton(Lang.T("確定", "OK"), true);
            ok.Location = new Point(s(240), s(278));
            ok.Size = new Size(s(100), s(34));
            ok.Click += delegate { DialogResult = DialogResult.OK; };
            Controls.Add(ok);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.Location = new Point(s(350), s(278));
            cancel.Size = new Size(s(100), s(34));
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; };
            Controls.Add(cancel);
        }

        // sensitivity is updated only when the dialog is confirmed.
        public static ActionBinding[] Prompt(IWin32Window owner, ActionBinding[] current, ref int sensitivity)
        {
            using (var dialog = new GestureDialog(current, sensitivity))
            {
                if (dialog.ShowDialog(owner) != DialogResult.OK) return null;
                var result = new ActionBinding[4];
                for (int i = 0; i < 4; i++) result[i] = dialog.pickers[i].Binding;
                sensitivity = dialog.sensitivity.Value;
                return result;
            }
        }
    }

    internal static class Theme
    {
        public static string UiFont
        {
            get { return Lang.English ? "Segoe UI" : "Microsoft JhengHei UI"; }
        }
        public static readonly Color Page = Color.FromArgb(242, 244, 248);
        public static readonly Color Card = Color.White;
        public static readonly Color CardBorder = Color.FromArgb(222, 227, 235);
        public static readonly Color Divider = Color.FromArgb(236, 239, 245);
        public static readonly Color Text = Color.FromArgb(32, 37, 48);
        public static readonly Color Muted = Color.FromArgb(110, 119, 135);
        public static readonly Color Accent = Color.FromArgb(30, 107, 240);
        public static readonly Color AccentHover = Color.FromArgb(22, 90, 210);
        public static readonly Color AccentDown = Color.FromArgb(16, 74, 180);
        public static readonly Color HeaderStart = Color.FromArgb(24, 29, 44);
        public static readonly Color HeaderEnd = Color.FromArgb(44, 54, 82);
        public static readonly Color HeaderSubtitle = Color.FromArgb(176, 185, 204);
        public static readonly Color Console = Color.FromArgb(23, 27, 36);
        public static readonly Color ConsoleText = Color.FromArgb(205, 224, 245);
        public static readonly Color Good = Color.FromArgb(24, 160, 110);
        public static readonly Color Warning = Color.FromArgb(217, 138, 0);
        // Segoe MDL2 Assets ships with Windows 10 and 11; null means "draw text only".
        public static readonly string IconFont = FindIconFont();

        private static string FindIconFont()
        {
            const string name = "Segoe MDL2 Assets";
            try
            {
                using (var probe = new Font(name, 10F))
                    return probe.Name == name ? name : null;
            }
            catch { return null; }
        }

        // Pixel layout is authored at 96 DPI; fonts already scale because the process is DPI aware.
        public static float GetScale()
        {
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
                    return Math.Max(1F, g.DpiX / 96F);
            }
            catch { return 1F; }
        }

        public static Icon LoadAppIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MXMasterWin.app.ico"))
                {
                    if (stream != null) return new Icon(stream);
                }
            }
            catch { }
            return SystemIcons.Application;
        }

        public static Image LoadLogo()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MXMasterWin.app.png"))
                {
                    if (stream != null)
                    {
                        using (var decoded = new Bitmap(stream))
                            return new Bitmap(decoded);
                    }
                }
            }
            catch { }
            return SystemIcons.Application.ToBitmap();
        }

        public static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
        {
            float d = radius * 2F;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static Button CreateButton(string text, bool primary)
        {
            return new RoundButton(primary) { Text = text };
        }
    }

    // Rounded, flat button used everywhere instead of the stock control.
    internal sealed class RoundButton : Button
    {
        private readonly bool primary;
        private readonly float scale = Theme.GetScale();
        private bool hover, pressed;

        public RoundButton(bool isPrimary)
        {
            primary = isPrimary;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color fill, border, ink;
            if (!Enabled)
            {
                fill = Color.FromArgb(236, 239, 244);
                border = Theme.CardBorder;
                ink = Color.FromArgb(160, 168, 182);
            }
            else if (primary)
            {
                fill = pressed ? Theme.AccentDown : hover ? Theme.AccentHover : Theme.Accent;
                border = fill;
                ink = Color.White;
            }
            else
            {
                fill = pressed ? Color.FromArgb(222, 228, 238) : hover ? Color.FromArgb(240, 243, 249) : Theme.Card;
                border = Color.FromArgb(198, 205, 217);
                ink = Theme.Text;
            }
            if (Focused && ShowFocusCues && !primary) border = Theme.Accent;

            var bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 6F * scale))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(border))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }
            TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, fill,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    // Horizontal slider that snaps to Increment. Drag, click, mouse wheel and arrow keys all work.
    internal sealed class SliderBar : Control
    {
        private readonly float scale = Theme.GetScale();
        private int minimum, maximum = 100, increment = 1, current;
        private bool dragging;
        public event EventHandler ValueChanged;

        public SliderBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public int Minimum { get { return minimum; } set { minimum = value; Value = current; Invalidate(); } }
        public int Maximum { get { return maximum; } set { maximum = value; Value = current; Invalidate(); } }
        public int Increment { get { return increment; } set { increment = Math.Max(1, value); } }

        public int Value
        {
            get { return current; }
            set
            {
                int next = Math.Max(minimum, Math.Min(Math.Max(minimum, maximum), value));
                if (next == current) return;
                current = next;
                Invalidate();
                EventHandler handler = ValueChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        private float KnobRadius { get { return 9F * scale; } }

        private void SetFromX(int x)
        {
            float left = KnobRadius, right = Width - KnobRadius;
            if (right <= left || maximum <= minimum) return;
            float t = Math.Max(0F, Math.Min(1F, (x - left) / (right - left)));
            int steps = (int)Math.Round(t * (maximum - minimum) / increment);
            Value = minimum + steps * increment;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            Focus();
            dragging = true;
            SetFromX(e.X);
            base.OnMouseDown(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (dragging) SetFromX(e.X);
            base.OnMouseMove(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            Value = current + Math.Sign(e.Delta) * increment;
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys code = keyData & Keys.KeyCode;
            return code == Keys.Left || code == Keys.Right || code == Keys.Up || code == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value = current - increment;
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value = current + increment;
            else if (e.KeyCode == Keys.PageDown) Value = current - increment * 10;
            else if (e.KeyCode == Keys.PageUp) Value = current + increment * 10;
            else if (e.KeyCode == Keys.Home) Value = minimum;
            else if (e.KeyCode == Keys.End) Value = maximum;
            base.OnKeyDown(e);
        }

        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float radius = KnobRadius;
            float left = radius, right = Width - radius, middle = Height / 2F;
            float t = maximum > minimum ? (current - minimum) / (float)(maximum - minimum) : 0F;
            float knobX = left + (right - left) * t;
            float thickness = 4F * scale;
            Color accent = Enabled ? Theme.Accent : Color.FromArgb(190, 197, 210);

            using (var rail = new SolidBrush(Color.FromArgb(210, 216, 226)))
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(left, middle - thickness / 2F, right - left, thickness), thickness / 2F - 0.2F))
                g.FillPath(rail, path);
            if (knobX - left > thickness)
            {
                using (var filled = new SolidBrush(accent))
                using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(left, middle - thickness / 2F, knobX - left, thickness), thickness / 2F - 0.2F))
                    g.FillPath(filled, path);
            }

            if (Focused && Enabled)
            {
                using (var halo = new SolidBrush(Color.FromArgb(40, Theme.Accent)))
                    g.FillEllipse(halo, knobX - radius, middle - radius, radius * 2F, radius * 2F);
            }
            float knob = radius - 2F * scale;
            using (var ring = new Pen(accent, 2F * scale))
            {
                g.FillEllipse(Brushes.White, knobX - knob, middle - knob, knob * 2F, knob * 2F);
                g.DrawEllipse(ring, knobX - knob, middle - knob, knob * 2F, knob * 2F);
            }
        }
    }

    // A CheckBox drawn as a label with an on/off switch at its right edge.
    // A row of segments (like a macOS segmented control) for the Keyboard page's sub-pages; matches the
    // rounded card style instead of the system TabControl.
    internal sealed class SegmentedTabs : Control
    {
        private readonly float scale = Theme.GetScale();
        private readonly string[] items;
        private readonly Font regular = new Font(Theme.UiFont, 9.5F);
        private readonly Font bold = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
        private int selected;
        private int hover = -1;
        public event EventHandler SelectedIndexChanged;

        public SegmentedTabs(string[] items)
        {
            this.items = items;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public int SelectedIndex
        {
            get { return selected; }
            set
            {
                if (value < 0 || value >= items.Length || value == selected) return;
                selected = value;
                Invalidate();
                if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
            }
        }

        private RectangleF Segment(int index)
        {
            float pad = 3F * scale;
            float width = (Width - pad * 2F) / items.Length;
            return new RectangleF(pad + index * width, pad, width, Height - pad * 2F);
        }

        private int HitTest(Point point)
        {
            for (int i = 0; i < items.Length; i++)
                if (Segment(i).Contains(point)) return i;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int index = HitTest(e.Location);
            if (index != hover) { hover = index; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                int index = HitTest(e.Location);
                if (index >= 0) SelectedIndex = index;
            }
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Card;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 9F * scale))
            using (var fill = new SolidBrush(Theme.Page))
                g.FillPath(fill, path);
            for (int i = 0; i < items.Length; i++)
            {
                RectangleF r = Segment(i);
                Color ink = Theme.Muted;
                if (i == selected)
                {
                    using (GraphicsPath pill = Theme.RoundedRectangle(r, 7F * scale))
                    using (var card = new SolidBrush(Theme.Card))
                    using (var border = new Pen(Theme.CardBorder))
                    {
                        g.FillPath(card, pill);
                        g.DrawPath(border, pill);
                    }
                    ink = Theme.Text;
                }
                else if (i == hover) ink = Theme.Text;
                Color under = i == selected ? Theme.Card : Theme.Page;
                TextRenderer.DrawText(g, items[i], i == selected ? bold : regular, Rectangle.Round(r), ink, under,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { regular.Dispose(); bold.Dispose(); }
            base.Dispose(disposing);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private readonly float scale = Theme.GetScale();

        public ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnCheckedChanged(EventArgs e)
        {
            Invalidate();
            base.OnCheckedChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color back = Parent != null ? Parent.BackColor : Theme.Page;
            g.Clear(back);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            float trackWidth = 40F * scale;
            float trackHeight = 22F * scale;
            float trackLeft = Width - trackWidth - 1F;
            float trackTop = (Height - trackHeight) / 2F;
            Color track = Checked ? Theme.Accent : Color.FromArgb(196, 203, 216);
            if (!Enabled) track = Color.FromArgb(222, 227, 235);
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(trackLeft, trackTop, trackWidth, trackHeight), trackHeight / 2F - 0.5F))
            using (var brush = new SolidBrush(track))
                g.FillPath(brush, path);

            float knob = trackHeight - 6F * scale;
            float knobLeft = Checked ? trackLeft + trackWidth - knob - 3F * scale : trackLeft + 3F * scale;
            g.FillEllipse(Brushes.White, knobLeft, trackTop + 3F * scale, knob, knob);

            var textArea = new Rectangle(0, 0, (int)(trackLeft - 8F * scale), Height);
            TextRenderer.DrawText(g, Text, Font, textArea, Enabled ? Theme.Text : Theme.Muted, back,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Engineering view: the raw event list that used to sit on the main page.
    internal sealed class DiagnosticsForm : Form
    {
        private readonly CheckBox liveLog;

        public DiagnosticsForm(CheckBox liveLogSwitch, ListBox eventLog, Action clearEvents, string logPath, Icon icon)
        {
            liveLog = liveLogSwitch;
            float scale = Theme.GetScale();
            Func<int, int> s = delegate(int v) { return (int)Math.Round(v * scale); };

            Text = Lang.T("MX Master Win — 工程模式", "MX Master Win — Engineering mode");
            Icon = icon;
            Font = new Font(Theme.UiFont, 9.5F);
            BackColor = Theme.Console;
            ForeColor = Theme.Text;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(s(780), s(520));
            MinimumSize = new Size(s(600), s(320));

            var console = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Console, Padding = new Padding(s(12)) };
            eventLog.Dock = DockStyle.Fill;
            eventLog.Font = new Font("Consolas", 9F);
            eventLog.HorizontalScrollbar = true;
            eventLog.IntegralHeight = false;
            eventLog.BorderStyle = BorderStyle.None;
            eventLog.BackColor = Theme.Console;
            eventLog.ForeColor = Theme.ConsoleText;
            console.Controls.Add(eventLog);
            Controls.Add(console);

            var bar = new Panel { Dock = DockStyle.Top, Height = s(70), BackColor = Theme.Card };
            Controls.Add(bar);

            liveLog.Location = new Point(s(16), s(10));
            liveLog.Size = new Size(s(190), s(26));
            bar.Controls.Add(liveLog);
            bar.Controls.Add(new Label
            {
                Text = Lang.T("記錄檔：", "Log file: ") + logPath + Lang.T("　（關閉此視窗會停止記錄）", "   (closing this window stops capture)"),
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.25F),
                Location = new Point(s(16), s(42)),
                Size = new Size(bar.Width - s(32), s(20)),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            });

            Button folder = Theme.CreateButton(Lang.T("開啟記錄資料夾", "Open log folder"), false);
            folder.Size = new Size(s(130), s(30));
            folder.Location = new Point(bar.Width - s(16) - folder.Width, s(8));
            folder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            folder.Click += delegate
            {
                try
                {
                    if (File.Exists(logPath)) Process.Start("explorer.exe", "/select,\"" + logPath + "\"");
                    else Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(logPath) + "\"");
                }
                catch { }
            };
            bar.Controls.Add(folder);

            Button copy = Theme.CreateButton(Lang.T("複製全部", "Copy all"), false);
            copy.Size = new Size(s(96), s(30));
            copy.Location = new Point(folder.Left - s(8) - copy.Width, s(8));
            copy.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            copy.Click += delegate
            {
                var text = new StringBuilder();
                foreach (object item in eventLog.Items) text.AppendLine(item.ToString());
                try
                {
                    if (text.Length > 0) Clipboard.SetText(text.ToString());
                }
                catch { }
            };
            bar.Controls.Add(copy);

            Button clear = Theme.CreateButton(Lang.T("清除", "Clear"), false);
            clear.Size = new Size(s(80), s(30));
            clear.Location = new Point(copy.Left - s(8) - clear.Width, s(8));
            clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.Click += delegate { clearEvents(); };
            bar.Controls.Add(clear);
        }

        // Closing only hides the window, and capture stops so nothing is logged unseen.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                liveLog.Checked = false;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }
    }

    // Simplified top view of the mouse with a numbered badge per mappable control. The numbers
    // match the rows of the button list; the thumb wheel carries two rows (left and right).
    // The outline is an original schematic, not a reproduction of any product artwork.
    internal sealed class MouseDiagram : Control
    {
        private const float DesignWidth = 296F, DesignHeight = 372F, OffsetX = 36F, OffsetY = 24F;
        private const float BadgeRadius = 11F;

        private sealed class Spot
        {
            public int[] Rows;
            public PointF Target, Badge;
        }

        // Row order: 0 middle, 1 gesture, 2 side A, 3 side B, 4/5 thumb wheel, 6 Actions Ring pad, 7 wheel mode.
        private static readonly Spot[] Spots =
        {
            new Spot { Rows = new[] { 0 }, Target = new PointF(144F, 78F), Badge = new PointF(246F, 78F) },
            new Spot { Rows = new[] { 7 }, Target = new PointF(144F, 140F), Badge = new PointF(246F, 140F) },
            new Spot { Rows = new[] { 1 }, Target = new PointF(62F, 125F), Badge = new PointF(-14F, 112F) },
            new Spot { Rows = new[] { 4, 5 }, Target = new PointF(66F, 152F), Badge = new PointF(-10F, 143F) },
            new Spot { Rows = new[] { 2 }, Target = new PointF(68F, 192F), Badge = new PointF(-14F, 246F) },
            new Spot { Rows = new[] { 3 }, Target = new PointF(70F, 207F), Badge = new PointF(-14F, 276F) },
            new Spot { Rows = new[] { 6 }, Target = new PointF(40F, 184F), Badge = new PointF(40F, 184F) }
        };

        private readonly float scale = Theme.GetScale();
        private int highlight = -1;
        public event Action<int> HotspotClicked;
        public event Action HighlightChanged;

        public MouseDiagram()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        // The highlighted list row, or -1.
        public int Highlight
        {
            get { return highlight; }
            set
            {
                if (value == highlight) return;
                highlight = value;
                Invalidate();
                Action handler = HighlightChanged;
                if (handler != null) handler();
            }
        }

        private float Fit
        {
            get { return Math.Min(Width / DesignWidth, Height / DesignHeight); }
        }

        private static float HalfWidth(Spot spot)
        {
            return spot.Rows.Length > 1 ? BadgeRadius + 7F : BadgeRadius;
        }

        private Spot SpotAt(Point location)
        {
            float x = location.X / Fit - OffsetX, y = location.Y / Fit - OffsetY;
            foreach (Spot spot in Spots)
            {
                if (Math.Abs(x - spot.Badge.X) <= HalfWidth(spot) + 3F && Math.Abs(y - spot.Badge.Y) <= BadgeRadius + 3F)
                    return spot;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Spot spot = SpotAt(e.Location);
            Cursor = spot != null ? Cursors.Hand : Cursors.Default;
            if (spot != null && Array.IndexOf(spot.Rows, highlight) < 0) Highlight = spot.Rows[0];
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Spot spot = SpotAt(e.Location);
            Action<int> handler = HotspotClicked;
            if (spot != null && handler != null) handler(Array.IndexOf(spot.Rows, highlight) >= 0 ? highlight : spot.Rows[0]);
            base.OnMouseClick(e);
        }

        private static void Pill(Graphics g, Brush brush, float x, float y, float width, float height)
        {
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(x, y, width, height), Math.Min(width, height) / 2F - 0.3F))
                g.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.ScaleTransform(Fit, Fit);
            g.TranslateTransform(OffsetX, OffsetY);

            // Shell, clockwise from the front-left corner. Proportions follow the real mouse seen
            // from above: squared front, straight left flank, and the thumb rest flaring out below it.
            PointF[] outline =
            {
                new PointF(67F, 24F), new PointF(73F, 12F), new PointF(100F, 9F), new PointF(135F, 15F),
                new PointF(170F, 23F), new PointF(197F, 38F), new PointF(206F, 56F), new PointF(212F, 110F),
                new PointF(220F, 180F), new PointF(218F, 250F), new PointF(200F, 300F), new PointF(170F, 320F),
                new PointF(140F, 325F), new PointF(110F, 316F), new PointF(86F, 294F), new PointF(68F, 276F),
                new PointF(45F, 275F), new PointF(20F, 260F), new PointF(6F, 233F), new PointF(5F, 208F),
                new PointF(16F, 168F), new PointF(31F, 141F), new PointF(50F, 133F), new PointF(63F, 130F),
                new PointF(65F, 90F), new PointF(66F, 50F)
            };
            using (var shell = new GraphicsPath())
            {
                shell.AddClosedCurve(outline, 0.35F);
                using (var shadow = new SolidBrush(Color.FromArgb(14, 20, 30, 50)))
                {
                    for (int i = 4; i >= 1; i--)
                    {
                        g.TranslateTransform(0F, i * 2.5F);
                        g.FillPath(shadow, shell);
                        g.TranslateTransform(0F, -i * 2.5F);
                    }
                }
                using (var body = new LinearGradientBrush(new RectangleF(0F, 0F, 226F, 334F),
                    Color.FromArgb(92, 95, 104), Color.FromArgb(40, 42, 48), 65F))
                    g.FillPath(body, shell);

                // Thumb rest panel with its ripple texture, clipped to the shell.
                PointF[] rest =
                {
                    new PointF(23F, 168F), new PointF(35F, 146F), new PointF(60F, 140F), new PointF(68F, 200F),
                    new PointF(70F, 268F), new PointF(50F, 270F), new PointF(26F, 257F), new PointF(12F, 231F),
                    new PointF(11F, 208F)
                };
                using (var panel = new GraphicsPath())
                {
                    panel.AddClosedCurve(rest, 0.3F);
                    using (var fill = new SolidBrush(Color.FromArgb(46, 48, 55)))
                        g.FillPath(fill, panel);
                    GraphicsState state = g.Save();
                    g.SetClip(panel, CombineMode.Intersect);
                    using (var ripple = new Pen(Color.FromArgb(26, 255, 255, 255), 1F))
                    {
                        for (float r = 14F; r < 150F; r += 9F)
                            g.DrawEllipse(ripple, 60F - r, 162F - r, r * 2F, r * 2F);
                    }
                    g.Restore(state);
                    using (var rim = new Pen(Color.FromArgb(150, 24, 26, 32), 1.2F))
                        g.DrawPath(rim, panel);
                }
                using (var edge = new Pen(Color.FromArgb(30, 32, 38), 1.4F))
                    g.DrawPath(edge, shell);
            }

            using (var seam = new Pen(Color.FromArgb(150, 24, 26, 32), 1.3F))
            using (var soft = new Pen(Color.FromArgb(44, 255, 255, 255), 1.1F))
            using (var slot = new SolidBrush(Color.FromArgb(26, 28, 34)))
            using (var part = new SolidBrush(Color.FromArgb(104, 108, 120)))
            using (var steel = new LinearGradientBrush(new RectangleF(0F, 0F, 20F, 20F),
                Color.FromArgb(232, 234, 240), Color.FromArgb(150, 154, 166), 0F))
            using (var ridge = new Pen(Color.FromArgb(110, 40, 44, 56), 1F))
            {
                steel.WrapMode = WrapMode.TileFlipX;
                // Split between the two main buttons, and the line where they end.
                g.DrawLine(seam, 135F, 16F, 135F, 50F);
                g.DrawLine(seam, 135F, 107F, 135F, 121F);
                g.DrawCurve(seam, new[] { new PointF(66F, 132F), new PointF(96F, 124F), new PointF(140F, 121F), new PointF(213F, 113F) }, 0.4F);
                // Contour of the right flank.
                g.DrawCurve(soft, new[] { new PointF(207F, 196F), new PointF(211F, 250F), new PointF(199F, 292F) }, 0.5F);

                // Scroll wheel in its slot, then the mode button and status light behind it.
                using (GraphicsPath well = Theme.RoundedRectangle(new RectangleF(122F, 51F, 27F, 55F), 5F))
                    g.FillPath(slot, well);
                using (GraphicsPath wheel = Theme.RoundedRectangle(new RectangleF(127F, 56F, 17F, 45F), 4F))
                    g.FillPath(steel, wheel);
                for (float y = 61F; y < 99F; y += 4F) g.DrawLine(ridge, 128F, y, 143F, y);
                using (GraphicsPath mode = Theme.RoundedRectangle(new RectangleF(127F, 130F, 17F, 20F), 4F))
                {
                    g.FillPath(part, mode);
                    g.DrawPath(seam, mode);
                }
                using (var light = new SolidBrush(Color.FromArgb(70, 208, 146)))
                    g.FillEllipse(light, 133.2F, 158.5F, 4F, 4F);

                // Along the left flank, front to back: gesture button, thumb wheel, two side buttons.
                Pill(g, part, 60F, 116F, 8F, 18F);
                using (GraphicsPath thumb = Theme.RoundedRectangle(new RectangleF(63F, 142F, 14F, 39F), 3.5F))
                    g.FillPath(steel, thumb);
                for (float y = 146F; y < 180F; y += 4F) g.DrawLine(ridge, 64F, y, 76F, y);
                g.DrawLine(seam, 63F, 161.5F, 77F, 161.5F);
                using (var forward = new GraphicsPath())
                using (var back = new GraphicsPath())
                {
                    forward.AddPolygon(new[] { new PointF(64F, 185F), new PointF(77F, 185F), new PointF(75F, 199F), new PointF(66F, 199F) });
                    back.AddPolygon(new[] { new PointF(66.5F, 201F), new PointF(74.5F, 201F), new PointF(72F, 216F) });
                    g.FillPath(part, forward);
                    g.FillPath(part, back);
                }
            }

            using (var font = new Font("Segoe UI", 10.5F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var accent = new SolidBrush(Theme.Accent))
            using (var white = new SolidBrush(Color.White))
            using (var ring = new Pen(Theme.Accent, 1.6F))
            using (var leader = new Pen(Color.FromArgb(170, Theme.Accent), 1.1F))
            {
                foreach (Spot spot in Spots)
                {
                    bool active = Array.IndexOf(spot.Rows, highlight) >= 0;
                    float half = HalfWidth(spot);
                    if (spot.Badge != spot.Target)
                    {
                        float edgeX = spot.Badge.X + (spot.Target.X > spot.Badge.X ? half : -half);
                        g.DrawLine(leader, edgeX, spot.Badge.Y, spot.Target.X, spot.Target.Y);
                        g.FillEllipse(accent, spot.Target.X - 2.6F, spot.Target.Y - 2.6F, 5.2F, 5.2F);
                    }
                    var area = new RectangleF(spot.Badge.X - half, spot.Badge.Y - BadgeRadius, half * 2F, BadgeRadius * 2F);
                    using (GraphicsPath badge = Theme.RoundedRectangle(area, BadgeRadius - 0.3F))
                    {
                        g.FillPath(active ? accent : white, badge);
                        g.DrawPath(ring, badge);
                    }
                    string text = spot.Rows.Length > 1 ? (spot.Rows[0] + 1) + "·" + (spot.Rows[1] + 1) : (spot.Rows[0] + 1).ToString();
                    g.DrawString(text, font, active ? white : accent, area, center);
                }
            }
        }
    }

    // Left-hand page selector: icon plus label, filled with the accent colour when selected.
    internal sealed class NavButton : Control
    {
        private readonly float scale = Theme.GetScale();
        private bool selected, hover;
        public string Glyph = "";

        public NavButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        public bool Selected
        {
            get { return selected; }
            set { selected = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Color page = Parent != null ? Parent.BackColor : Theme.Page;
            g.Clear(page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = selected ? Theme.Accent : hover ? Color.FromArgb(228, 233, 242) : page;
            Color ink = selected ? Color.White : Theme.Text;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var brush = new SolidBrush(fill))
                g.FillPath(brush, path);

            int iconWidth = (int)(46F * scale);
            if (Theme.IconFont != null && Glyph.Length > 0)
            {
                using (var icons = new Font(Theme.IconFont, 12F))
                    TextRenderer.DrawText(g, Glyph, icons, new Rectangle(0, 0, iconWidth, Height), ink, fill,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            using (var font = new Font(Theme.UiFont, 10F, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Text, font, new Rectangle(iconWidth, 0, Width - iconWidth - 6, Height), ink, fill,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    // Small white chip showing battery level and how the mouse is connected.
    internal sealed class BatteryChip : Control
    {
        private readonly float scale = Theme.GetScale();
        // Call Invalidate() after changing these.
        public string ConnectionText = "";
        public bool Connected;
        public int BatteryPercent = -1;
        public bool Charging;
        // A second row for the keyboard while one is connected (KeyboardPercent >= 0); MainForm makes
        // the chip taller then.
        public int KeyboardPercent = -1;
        public bool KeyboardCharging;
        public string KeyboardText = "";
        // One more row for a device on a Lightspeed receiver (G502) while it is connected.
        public int OtherPercent = -1;
        public bool OtherCharging;
        public string OtherName = "";
        public event Action OtherRowClicked;

        public BatteryChip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int RowCount { get { return 1 + (KeyboardPercent >= 0 ? 1 : 0) + (OtherPercent >= 0 ? 1 : 0); } }

        private bool OnOtherRow(Point point)
        {
            int rows = RowCount;
            if (OtherPercent < 0 || rows < 2) return false;
            float rowHeight = (Height - 4F * scale) / rows;
            return point.Y >= 2F * scale + rowHeight * (rows - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = OnOtherRow(e.Location) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Action handler = OtherRowClicked;
            if (e.Button == MouseButtons.Left && handler != null && OnOtherRow(e.Location)) handler();
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var fill = new SolidBrush(Theme.Card))
            using (var border = new Pen(Theme.CardBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }

            int rows = RowCount;
            if (rows == 1)
            {
                DrawRow(g, Height / 2F, null, Connected, BatteryPercent, Charging, ConnectionText);
                return;
            }
            // Mouse first, then keyboard and G502, each marked with its Segoe MDL2 glyph (Mouse, KeyboardClassic, Game).
            float rowHeight = (Height - 4F * scale) / rows;
            float middle = 2F * scale + rowHeight / 2F;
            DrawRow(g, middle, "\uE962", Connected, BatteryPercent, Charging, ConnectionText);
            if (KeyboardPercent >= 0)
            {
                middle += rowHeight;
                DrawRow(g, middle, "\uE765", true, KeyboardPercent, KeyboardCharging, KeyboardText);
            }
            if (OtherPercent >= 0)
            {
                middle += rowHeight;
                DrawRow(g, middle, "\uE7FC", true, OtherPercent, OtherCharging, OtherName);
            }
        }

        private void DrawRow(Graphics g, float middle, string glyph, bool connected, int percentValue, bool charging, string connection)
        {
            float x = 12F * scale;
            if (glyph != null && Theme.IconFont != null)
            {
                using (var icon = new Font(Theme.IconFont, 9F))
                using (var muted = new SolidBrush(Theme.Muted))
                {
                    SizeF size = g.MeasureString(glyph, icon);
                    g.DrawString(glyph, icon, muted, x, middle - size.Height / 2F);
                }
                x += 20F * scale;
            }
            Color state = connected ? Theme.Good : Theme.Warning;
            if (percentValue >= 0)
            {
                bool low = percentValue <= 20 && !charging;
                Color level = low ? Color.FromArgb(220, 70, 70) : Theme.Good;
                using (var bold = new Font(Theme.UiFont, 9.5F, FontStyle.Bold))
                using (var ink = new SolidBrush(level))
                {
                    string percent = percentValue + "%";
                    SizeF size = g.MeasureString(percent, bold);
                    g.DrawString(percent, bold, ink, x, middle - size.Height / 2F);
                    x += size.Width + 4F * scale;

                    float bodyWidth = 22F * scale, bodyHeight = 11F * scale;
                    float top = middle - bodyHeight / 2F;
                    float inset = 2F * scale;
                    using (GraphicsPath body = Theme.RoundedRectangle(new RectangleF(x, top, bodyWidth, bodyHeight), 2.5F * scale))
                    using (var outline = new Pen(level, 1.3F * scale))
                    {
                        g.FillRectangle(ink, x + inset, top + inset,
                            Math.Max(2F * scale, (bodyWidth - inset * 2F) * Math.Min(100, percentValue) / 100F), bodyHeight - inset * 2F);
                        g.DrawPath(outline, body);
                        g.FillRectangle(ink, x + bodyWidth + 1F * scale, middle - 2.5F * scale, 2F * scale, 5F * scale);
                    }
                    x += bodyWidth + 3F * scale;
                    if (charging)
                    {
                        // 充電中：電池右邊畫一個小閃電（不寫文字，免得超出框框）。
                        float w = 7F * scale, h = 12F * scale, bx = x + 3F * scale, by = middle - h / 2F;
                        var bolt = new PointF[]
                        {
                            new PointF(bx + w * 0.62F, by),
                            new PointF(bx, by + h * 0.58F),
                            new PointF(bx + w * 0.45F, by + h * 0.58F),
                            new PointF(bx + w * 0.34F, by + h),
                            new PointF(bx + w, by + h * 0.40F),
                            new PointF(bx + w * 0.55F, by + h * 0.40F)
                        };
                        g.FillPolygon(ink, bolt);
                        x += w + 3F * scale;
                    }
                    x += 9F * scale;
                }
            }
            else
            {
                float dot = 8F * scale;
                using (var mark = new SolidBrush(state))
                    g.FillEllipse(mark, x, middle - dot / 2F, dot, dot);
                x += dot + 8F * scale;
            }

            // 充電狀態改由電池旁的閃電表示；沒有電量時（percentValue < 0）仍用文字。
            string text = connection + (charging && percentValue < 0 ? Lang.T("・充電中", " · charging") : "");
            using (var font = new Font(Theme.UiFont, 9F))
            using (var muted = new SolidBrush(Theme.Muted))
            using (var format = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter, LineAlignment = StringAlignment.Center })
            {
                // 超出框框時以省略號截斷。
                var area = new RectangleF(x, middle - 10F * scale, Math.Max(0F, Width - x - 8F * scale), 20F * scale);
                g.DrawString(text, font, muted, area, format);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        private readonly float scale;
        public string Title = "";

        public CardPanel(float uiScale)
        {
            scale = uiScale;
            BackColor = Theme.Card;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Page);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var bounds = new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F);
            using (GraphicsPath path = Theme.RoundedRectangle(bounds, 10 * scale))
            using (var fill = new SolidBrush(Theme.Card))
            using (var border = new Pen(Theme.CardBorder))
            {
                g.FillPath(fill, path);
                g.DrawPath(border, path);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (var titleFont = new Font(Theme.UiFont, 10.5F, FontStyle.Bold))
                TextRenderer.DrawText(e.Graphics, Title, titleFont,
                    new Point((int)Math.Round(16 * scale), (int)Math.Round(12 * scale)), Theme.Text, Theme.Card, TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Dictionary<MouseInput, ActionPicker> selectors = new Dictionary<MouseInput, ActionPicker>();
        private readonly Dictionary<MouseInput, ActionBinding> mappings = new Dictionary<MouseInput, ActionBinding>();
        private ActionBinding[] ringBindings = ActionRingOverlay.DefaultSlots();
        // Up, right, down, left.
        private ActionBinding[] gestureBindings = GestureDialog.Defaults();
        private readonly ComboBox ringStyle = new ComboBox();
        private readonly SliderBar ringSize = new SliderBar();
        private readonly SliderBar bubbleSize = new SliderBar();
        private readonly SliderBar ringOpacity = new SliderBar();
        private readonly ComboBox ringColor = new ComboBox();
        private readonly CheckBox ringHaptic = new ToggleSwitch();
        private readonly ComboBox ringHapticWave = new ComboBox();
        // Haptics page: the mouse's strength, and which moments tap (each with its own waveform).
        private readonly CheckBox gestureHaptic = new ToggleSwitch();
        private readonly ComboBox gestureHapticWave = new ComboBox();
        private readonly CheckBox flowHaptic = new ToggleSwitch();
        private readonly ComboBox flowHapticWave = new ComboBox();
        private readonly CheckBox modeHaptic = new ToggleSwitch();
        private readonly ComboBox modeHapticWave = new ComboBox();
        private readonly Dictionary<CheckBox, Button> hapticTryButtons = new Dictionary<CheckBox, Button>();
        private readonly SliderBar hapticStrength = new SliderBar();
        // Low battery: one tray notification per discharge, and optionally no automatic taps.
        private readonly CheckBox lowBatteryHapticPause = new ToggleSwitch();
        private readonly Label hapticPauseNote = new Label();
        private readonly CheckBox lowBatteryAlert = new ToggleSwitch();
        private readonly ComboBox lowBatteryLevel = new ComboBox();
        private readonly Label batteryLevel = new Label();
        private readonly Label batteryDetail = new Label();
        private static readonly int[] LowBatteryLevels = { 5, 10, 15, 20, 25, 30 };
        private bool lowBatteryNotified;
        // Gesture sensitivity, 0-100; 50 keeps the original 40 px / 65 px distances.
        private int gestureSensitivity = 50;
        private readonly Label hapticStrengthValue = new Label();
        private Button hapticStrengthApply;
        // Pointer page, right column: precision mode and the on-screen hint.
        private readonly SliderBar precisionDpi = new SliderBar();
        private readonly Label precisionDpiValue = new Label();
        private readonly ComboBox precisionTrigger = new ComboBox();
        private readonly CheckBox modeHud = new ToggleSwitch();
        private readonly ModeOverlay modeOverlay = new ModeOverlay();
        // Buttons that can be held, in the order of the trigger list (after "None").
        private static readonly MouseInput[] PrecisionInputs =
            { MouseInput.Middle, MouseInput.XButton2, MouseInput.XButton1, MouseInput.GestureButton, MouseInput.ActionRingButton };
        private DeviceSnapshot lastSnapshot;
        private bool precisionActive;
        private int precisionNormalDpi;
        private int precisionLowDpi;
        private MouseInput? precisionHeldBy;    // the button holding precision mode on, if any
        private int precisionSeq;               // only the latest requested speed is written
        private bool precisionRestorePending;   // precision mode is off but the normal speed is not confirmed yet
        private readonly object precisionLock = new object();
        private int manualDpiSeq;               // precisionSeq when a speed from the pointer page was written
        private bool loadingPrecision;
        // Short haptic waveforms shared by every haptic moment, in menu order (ids follow Solaar;
        // the same list as the macOS version). 0x04 is the ring's default.
        private static readonly int[] HapticWaves = { 0x1B, 0x04, 0x01, 0x03, 0x00, 0x02 };
        private const int DefaultHapticWave = 1;
        private readonly System.Windows.Forms.Timer appearanceSave = new System.Windows.Forms.Timer();
        private readonly DeviceSettings deviceSettings = new DeviceSettings();
        private DiagnosticsForm diagnostics;
        private readonly BatteryChip chip = new BatteryChip();
        private readonly List<NavButton> navigation = new List<NavButton>();
        private readonly List<CardPanel> pages = new List<CardPanel>();
        private readonly PictureBox ringPreview = new PictureBox();
        private readonly MouseDiagram diagram = new MouseDiagram();
        private readonly List<MouseInput> rowInputs = new List<MouseInput>();
        private readonly List<Label> rowBadges = new List<Label>();
        private readonly CheckBox enableSwitch = new ToggleSwitch();
        private readonly CheckBox startMinimized = new ToggleSwitch();
        private readonly CheckBox startWithWindows = new ToggleSwitch();
        private bool allowShow;
        private CardPanel devicePage;
        private DeviceDialog deviceEditor;
        private bool trayHintShown;
        private readonly System.Windows.Forms.Timer batteryTimer = new System.Windows.Forms.Timer();
        private string batteryText = "";
        private readonly ListBox eventLog = new ListBox();
        private readonly CheckBox liveLog = new ToggleSwitch();
        private readonly CheckBox gestureMode = new ToggleSwitch();
        private readonly CheckBox actionRingMode = new ToggleSwitch();
        private readonly Label status = new Label();
        private readonly Label statusDot = new Label();
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly Icon appIcon;
        private readonly float uiScale;
        private readonly string configPath;
        private readonly string diagnosticLogPath;
        private readonly RawInputMonitor rawInput;
        private readonly HidppDiversionManager hidpp;
        // Keyboard page: a Logitech keyboard's status, backlight and top row, and following the mouse.
        private readonly KeyboardManager keyboard;
        private readonly BoltReceiver bolt;
        private OtherDevicesManager others;
        private OtherDevicesPage othersPage;
        private int demoOthers;
        private bool mouseAway;
        // Actions for buttons on another mouse, "<wpid>.<button slot>" -> action. Kept in the INI, not in the mouse.
        private readonly Dictionary<string, ActionBinding> otherBindings = new Dictionary<string, ActionBinding>(StringComparer.OrdinalIgnoreCase);
        private List<OtherDeviceSnapshot> otherSnapshots = new List<OtherDeviceSnapshot>();
        private string otherHeldKey = "";
        private string chipOtherId;
        private volatile bool otherGestureHeld;
        private readonly System.Windows.Forms.Timer othersClock = new System.Windows.Forms.Timer();
        private readonly string othersBackupFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "profile-backups");
        private BoltReceiverSnapshot receiverSnapshot = new BoltReceiverSnapshot();
        private int demoReceiver;
        private readonly Label receiverTitle = new Label();
        private readonly Label receiverCount = new Label();
        private readonly Label receiverMessage = new Label();
        private readonly Label[] receiverRows = new Label[6];
        private Panel receiverPaired;
        private readonly Button[] receiverUnpair = new Button[6];
        private readonly Label pairingStatus = new Label();
        private readonly Label pairingCode = new Label();
        private Button pairingStart, pairingCancel;
        private KeyboardSnapshot keyboardSnapshot;
        private int keyboardBusy;
        private readonly System.Windows.Forms.Timer keyboardTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer keyboardSoon = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer keyboardLevelApply = new System.Windows.Forms.Timer();
        private readonly Label keyboardName = new Label();
        // The keyboard's name in bold; the longer not-found explanation in a lighter, smaller hint style.
        private readonly Font keyboardNameFont = new Font(Theme.UiFont, 10F, FontStyle.Bold);
        private readonly Font keyboardMissingFont = new Font(Theme.UiFont, 9.5F);
        private readonly Label keyboardConnection = new Label();
        private readonly Label keyboardBattery = new Label();
        private readonly Label keyboardFirmware = new Label();
        private readonly Panel keyboardLightGroup = new Panel(), keyboardFnGroup = new Panel(), keyboardOsGroup = new Panel();
        private readonly ComboBox keyboardPlatform = new ComboBox();
        private readonly CheckBox[] keyboardDisabled = new CheckBox[8];
        private readonly NumericUpDown[] keyboardDelays = new NumericUpDown[3];
        private readonly Panel keyboardLevelRow = new Panel();
        private readonly Panel[] keyboardTabPages = new Panel[4];
        private readonly Label[] keyboardEmpty = new Label[4];
        private Label keyboardDelayNote, keyboardDisableHint;
        // What RefreshKeyboardUi wants shown (Control.Visible reads false while the sub-page is hidden).
        private readonly Dictionary<Control, bool> keyboardWanted = new Dictionary<Control, bool>();
        private readonly Dictionary<Control, Panel> keyboardRowDividers = new Dictionary<Control, Panel>();
        private readonly Panel[] keyboardDelayRows = new Panel[3];
        private readonly ComboBox keyboardBacklight = new ComboBox();
        private readonly List<int> keyboardBacklightChoices = new List<int>();   // -1 off, 1 automatic, 3 manual, other = keyboard's own
        private readonly Label keyboardLevelLabel = new Label();
        private readonly SliderBar keyboardLevel = new SliderBar();
        private readonly Label keyboardLevelValue = new Label();
        private readonly ComboBox keyboardTopRow = new ComboBox();
        private readonly CheckBox keyboardFollow = new ToggleSwitch();
        private readonly Label[] keyboardRowLabels = new Label[3];
        private readonly ComboBox[] keyboardRowTargets = new ComboBox[3];
        // Per mouse channel: -2 automatic, -1 keyboard stays, 0.. keyboard channel.
        private readonly int[] keyboardChannels = { -2, -2, -2 };
        private bool loadingKeyboard;
        private readonly GestureOverlay overlay = new GestureOverlay();
        private readonly ActionRingOverlay actionRing = new ActionRingOverlay();
        private bool reallyExit;
        private bool gestureActive;
        private bool actionRingActive;
        private readonly Dictionary<int, bool> vendorState = new Dictionary<int, bool>();
        private bool swallowLeftUp;
        private bool swallowRightUp;
        private readonly CheckBox ringClickMode = new ToggleSwitch();
        // Edge switching: pushing the pointer against an outer screen edge moves the mouse to another host.
        private static readonly string[] EdgeKeys = { "EdgeLeft", "EdgeRight", "EdgeTop", "EdgeBottom" };   // in ScreenEdge order
        private static readonly string[] EdgeKeyNames =
            { "None", "Ctrl", "Alt", "Shift", "Win", "Middle", "XButton2", "XButton1", "GestureButton", "ActionRingButton" };
        private readonly CheckBox edgeSwitch = new ToggleSwitch();
        private readonly ComboBox[] edgeTargets = new ComboBox[4];
        private readonly int[] edgeHosts = { -1, -1, -1, -1 };      // host per edge, -1 = off
        private readonly SliderBar edgeDwell = new SliderBar();      // tenths of a second
        private readonly ComboBox edgeKey = new ComboBox();
        private readonly CheckBox edgePlaceCursor = new ToggleSwitch();
        private readonly Label edgeScreens = new Label();
        private readonly Label[] hostLabels = new Label[3];
        private readonly Button[] hostButtons = new Button[3];
        private readonly System.Windows.Forms.Timer hostCheck = new System.Windows.Forms.Timer();
        private List<HostEntry> hosts = new List<HostEntry>();
        private int currentHost = -1;
        private bool loadingEdge;
        private Rectangle[] screenBounds = new Rectangle[0];
        private bool edgePushing;
        private ScreenEdge edgeCandidate;
        private int pushStart, lastPush;
        private int edgeCooldownUntil = Environment.TickCount;
        private int lastResume = unchecked(Environment.TickCount - 600000);
        private int lastArrival = unchecked(Environment.TickCount - 600000);
        private bool readyOnce;
        private readonly HashSet<MouseInput> heldInputs = new HashSet<MouseInput>();
        private bool deferredPending;       // the edge-switch key's own action waits for its release
        private MouseInput deferredInput;
        private Microsoft.Win32.PowerModeChangedEventHandler powerHandler;
        private EventHandler displayHandler;
        private Point gestureStart;
        private string gestureDirection = "";
        private string gestureCandidate = "";
        private bool gestureMoved;

        public MainForm()
        {
            Text = "MX Master Win";
            uiScale = Theme.GetScale();
            Font = new Font(Theme.UiFont, 9.5F);
            BackColor = Theme.Page;
            ForeColor = Theme.Text;
            ClientSize = new Size(S(1040), S(620));
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            appIcon = Theme.LoadAppIcon();
            Icon = appIcon;
            configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-win.ini");
            diagnosticLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mxmaster-events.log");

            BuildUi();
            LoadSettings();
            actionRing.ClickMode = ringClickMode.Checked;
            UpdateRingPreview();

            // Hooked up after loading so that restoring the saved state does not rewrite the file.
            foreach (ActionPicker picker in selectors.Values)
            {
                picker.Changed += SaveSettings;
                picker.Changed += RefreshPrecisionTrigger;
            }
            gestureMode.CheckedChanged += delegate { SaveSettings(); };
            // Sliders report every step of a drag; apply at once but write the file after a pause.
            appearanceSave.Interval = 500;
            appearanceSave.Tick += delegate { appearanceSave.Stop(); SaveSettings(); };
            EventHandler appearanceChanged = delegate
            {
                ApplyRingAppearance();
                appearanceSave.Stop();
                appearanceSave.Start();
            };
            ringStyle.SelectedIndexChanged += appearanceChanged;
            ringSize.ValueChanged += appearanceChanged;
            bubbleSize.ValueChanged += appearanceChanged;
            ringOpacity.ValueChanged += appearanceChanged;
            ringColor.SelectedIndexChanged += appearanceChanged;
            WireHaptic(ringHaptic, ringHapticWave);
            WireHaptic(gestureHaptic, gestureHapticWave);
            WireHaptic(flowHaptic, flowHapticWave);
            WireHaptic(modeHaptic, modeHapticWave);
            actionRing.SlotEntered = delegate { PlayHaptic(ringHaptic, ringHapticWave); };
            precisionDpi.ValueChanged += delegate
            {
                precisionDpiValue.Text = precisionDpi.Value + " DPI";
                if (loadingPrecision) return;
                appearanceSave.Stop();
                appearanceSave.Start();
            };
            precisionTrigger.SelectedIndexChanged += delegate { if (!loadingPrecision) ApplyPrecisionTrigger(); };
            modeHud.CheckedChanged += delegate { SaveSettings(); };
            lowBatteryHapticPause.CheckedChanged += delegate { UpdateHapticPauseNote(); UpdateBatteryPage(lastSnapshot); SaveSettings(); };
            lowBatteryAlert.CheckedChanged += delegate { BatterySettingsChanged(); SaveSettings(); };
            lowBatteryLevel.SelectedIndexChanged += delegate { BatterySettingsChanged(); SaveSettings(); };
            ActionRunner.MouseAction = RunMouseAction;
            startMinimized.CheckedChanged += delegate { SaveSettings(); };
            actionRingMode.CheckedChanged += delegate { SaveSettings(); };
            ringClickMode.CheckedChanged += delegate
            {
                actionRing.ClickMode = ringClickMode.Checked;
                UpdateRingPreview();
                SaveSettings();
            };

            edgeSwitch.CheckedChanged += delegate { edgePushing = false; SaveSettings(); };
            edgePlaceCursor.CheckedChanged += delegate { SaveSettings(); };
            edgeKey.SelectedIndexChanged += delegate { deferredPending = false; SaveSettings(); };
            edgeDwell.ValueChanged += delegate { appearanceSave.Stop(); appearanceSave.Start(); };
            hostCheck.Interval = 2500;
            hostCheck.Tick += delegate
            {
                // The mouse is still sending input here, so the switch did not take: pick the channel up again.
                hostCheck.Stop();
                if (rawInput.MillisecondsSinceInput < 1000) hidpp.Recheck();
            };
            powerHandler = delegate(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
            {
                if (e.Mode != Microsoft.Win32.PowerModes.Resume) return;
                lastResume = Environment.TickCount;
                if (others != null) others.Refresh();
            };
            displayHandler = delegate
            {
                try { BeginInvoke(new Action(UpdateScreens)); }
                catch { }
            };
            Microsoft.Win32.SystemEvents.PowerModeChanged += powerHandler;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += displayHandler;
            UpdateScreens();

            ActionRunner.OpenSettings = delegate
            {
                try { BeginInvoke(new Action(ShowWindow)); }
                catch { }
            };
            rawInput = new RawInputMonitor(this.Handle, AppendRawEvent, OnVendorButton, OnMouseActivity, OnMxMouseMove, OnBoltShortReport);
            hidpp = new HidppDiversionManager(OnHidppStatus, OnHidppReady);
            keyboard = new KeyboardManager(OnKeyboardReport);
            bolt = new BoltReceiver(OnBoltReport);
            bolt.Changed += OnBoltChanged;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.StartsWith("--demo-receiver=", StringComparison.OrdinalIgnoreCase))
                    Int32.TryParse(argument.Substring(16), out demoReceiver);
                if (argument.StartsWith("--demo-others=", StringComparison.OrdinalIgnoreCase))
                    Int32.TryParse(argument.Substring(14), out demoOthers);
            }
            MouseHook.MouseEvent += OnMouseEvent;
            MouseHook.Install();

            bool capture = false;
            foreach (string argument in Environment.GetCommandLineArgs())
            {
                if (argument.Equals("--capture", StringComparison.OrdinalIgnoreCase))
                {
                    liveLog.Checked = true;
                    capture = true;
                    break;
                }
            }

            tray.Icon = new Icon(appIcon, SystemInformation.SmallIconSize);
            tray.Text = "MX Master Win";
            tray.Visible = true;
            tray.DoubleClick += delegate { ShowWindow(); };
            var menu = new ContextMenuStrip();
            menu.Items.Add(Lang.T("開啟設定", "Open settings"), null, delegate { ShowWindow(); });
            menu.Items.Add(Lang.T("暫停 / 啟用", "Pause / resume"), null, delegate { ToggleEnabled(); });
            // Ticked while MX Flow is on; the same setting as the switch on the MX Flow page.
            var flowItem = new ToolStripMenuItem("MX Flow");
            flowItem.Click += delegate { edgeSwitch.Checked = !edgeSwitch.Checked; };
            menu.Items.Add(flowItem);
            menu.Opening += delegate { flowItem.Checked = edgeSwitch.Checked; };
            menu.Items.Add(Lang.T("工程模式…", "Engineering mode…"), null, delegate { ShowDiagnostics(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(Lang.T("結束", "Exit"), null, delegate { reallyExit = true; Close(); });
            tray.ContextMenuStrip = menu;

            bool present = RawInputMonitor.IsMxMaster4Present();
            chip.ConnectionText = present ? Lang.T("連線中…", "Connecting…") : Lang.T("未偵測到滑鼠", "No mouse found");
            SetDeviceStatus(present
                ? Lang.T("正在連線到 MX Master 4…", "Connecting to MX Master 4…")
                : Lang.T("尚未偵測到 MX Master 4；標準按鍵對應仍可使用", "MX Master 4 not detected; standard button mapping still works"), false);

            batteryTimer.Interval = 10 * 60 * 1000;
            batteryTimer.Tick += delegate { RefreshBattery(); };
            batteryTimer.Start();

            // The window may never be shown (start minimized), so nothing here can wait for Shown.
            bool show = capture || !startMinimized.Checked;
            foreach (string argument in Environment.GetCommandLineArgs())
                if (argument.Equals("--show", StringComparison.OrdinalIgnoreCase)) show = true;
            allowShow = show;
            hidpp.Start();
            if (demoOthers != 0)
            {
                List<OtherDeviceSnapshot> demoList = DemoOthers(demoOthers);
                othersPage.ShowDevices(demoList);
                UpdateOtherChip(demoList);
            }
            else
            {
                others.Start();
                UpdateOtherReports();
            }
            othersClock.Interval = 30000;
            othersClock.Tick += delegate { if (pages[8].Visible) othersPage.RefreshTimes(); };
            othersClock.Start();
            // Looks for the keyboard while none is connected (it may come back with Easy-Switch at any
            // time), and keeps the keyboard page current while it is open.
            keyboardTimer.Interval = 10000;
            keyboardTimer.Tick += delegate
            {
                // A switch waiting for a sleeping keyboard is checked often so it goes as soon as a key
                // wakes it; otherwise every 10 seconds while there is no keyboard, it is asleep, or its
                // page is open.
                bool pending = keyboard.PendingSwitch;
                int interval = pending ? 1500 : 10000;
                if (keyboardTimer.Interval != interval) keyboardTimer.Interval = interval;
                if (pending || !keyboard.Connected || keyboard.Asleep || pages[6].Visible) RefreshKeyboard(false);
            };
            keyboardTimer.Start();
            // Read the keyboard shortly after the Bolt receiver says it connected (back from another
            // computer, or woken by a key), instead of waiting for the next 10-second tick.
            keyboardSoon.Interval = 700;
            keyboardSoon.Tick += delegate
            {
                keyboardSoon.Stop();
                RefreshKeyboard(true);
            };
            RefreshKeyboard(true);
            if (capture) ShowDiagnostics();
            if (!show && !trayHintShown)
            {
                trayHintShown = true;
                SaveSettings();
                try
                {
                    tray.ShowBalloonTip(4000, "MX Master Win",
                        Lang.T("已在系統匣執行。按兩下系統匣圖示可開啟設定。", "Running in the tray. Double-click the tray icon to open settings."), ToolTipIcon.Info);
                }
                catch { }
            }
        }

        // Keeps the form hidden when Application.Run tries to show it at start-up.
        protected override void SetVisibleCore(bool value)
        {
            if (value && !allowShow)
            {
                if (!IsHandleCreated) CreateHandle();
                value = false;
            }
            base.SetVisibleCore(value);
        }

        // Reads the battery on a worker thread; HID++ requests can block for a second or more.
        private void RefreshBattery()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate { ShowBattery(snapshot); }));
                }
                catch { }
            });
        }

        // Runs once the HID++ channel is up: writes back the device settings saved in the INI
        // when the mouse reports something else, then shows the battery level.
        // seq is the precisionSeq taken when the mouse (re)connected; a newer speed request since then
        // owns the DPI, so the saved speed is not written over it.
        private void RestoreDeviceSettings(int seq)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                var results = new List<string>();
                // Under the precision lock so a precision write cannot land between the read and the restore.
                lock (precisionLock)
                {
                    try
                    {
                        snapshot = hidpp.ReadDevice();
                        bool ownsDpi = seq == Thread.VolatileRead(ref precisionSeq);
                        if (snapshot != null && deviceSettings.Restore(hidpp, snapshot, results, ownsDpi))
                            snapshot = hidpp.ReadDevice();
                    }
                    catch { }
                }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        // An owed precision restore is done once the mouse reports the saved speed.
                        if (precisionRestorePending && !precisionActive && seq == Thread.VolatileRead(ref precisionSeq)
                            && snapshot != null && deviceSettings.Dpi > 0 && snapshot.Dpi == deviceSettings.Dpi)
                            precisionRestorePending = false;
                        ShowBattery(snapshot);
                        if (results.Count == 0) return;
                        LogDeviceLines(results);
                        bool failed = results.Exists(delegate(string line) { return line.EndsWith("verified=False"); });
                        status.Text = failed
                            ? Lang.T("部分儲存的裝置設定重新套用失敗，請到「指標與滾輪」確認", "Some saved device settings could not be re-applied; check Pointer & Wheel")
                            : Lang.T("已重新套用儲存的裝置設定", "Saved device settings re-applied");
                    }));
                }
                catch { }
            });
        }

        private void ShowBattery(DeviceSnapshot snapshot)
        {
            batteryText = snapshot == null || snapshot.BatteryPercent < 0 ? "" : Lang.T("電量 ", "Battery ") + snapshot.BatteryText;
            UpdateTrayText();
            if (snapshot == null)
            {
                chip.Connected = false;
                chip.ConnectionText = Lang.T("未連線", "Not connected");
                chip.BatteryPercent = -1;
                chip.Charging = false;
            }
            else
            {
                lastSnapshot = snapshot;
                if (snapshot.DpiFeature == 0x2201 && snapshot.DpiStep > 0 && snapshot.DpiMax > snapshot.DpiMin)
                {
                    loadingPrecision = true;
                    int wanted = (int)precisionDpi.Value;
                    precisionDpi.Minimum = snapshot.DpiMin;
                    precisionDpi.Maximum = Math.Min(snapshot.DpiMax, 4000);
                    precisionDpi.Increment = snapshot.DpiStep;
                    precisionDpi.Value = wanted;
                    precisionDpiValue.Text = precisionDpi.Value + " DPI";
                    loadingPrecision = false;
                }
                chip.Connected = true;
                chip.ConnectionText = snapshot.Transport == "BOLT" ? "Logi Bolt" : "Bluetooth";
                // A sleeping mouse can miss a read; keep the last good value instead of blanking it.
                if (snapshot.BatteryPercent >= 0)
                {
                    chip.BatteryPercent = snapshot.BatteryPercent;
                    chip.Charging = snapshot.BatteryCharging;
                    BatteryChanged();
                }
            }
            chip.Invalidate();
            UpdateBatteryPage(snapshot);
            if (liveLog.Checked && snapshot != null)
                foreach (string line in snapshot.Raw)
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Device " + line);
        }

        private void UpdateTrayText()
        {
            string text = MouseHook.Enabled ? "MX Master Win" : Lang.T("MX Master Win（已暫停）", "MX Master Win (paused)");
            if (batteryText.Length > 0) text += "\n" + batteryText;
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void LogDeviceLines(IEnumerable<string> lines)
        {
            if (!liveLog.Checked) return;
            foreach (string line in lines)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Device " + line);
        }

        // Layout: device title on top, page navigation on the left with the battery chip under it,
        // one card per page on the right, and a single status line at the bottom.
        private void BuildUi()
        {
            int width = ClientSize.Width;
            Controls.Add(new PictureBox
            {
                Image = Theme.LoadLogo(),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(S(28), S(24)),
                Size = new Size(S(48), S(48))
            });
            Controls.Add(new Label
            {
                Text = "MX Master 4",
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 20F),
                Location = new Point(S(86), S(16))
            });
            Controls.Add(new Label
            {
                Text = Lang.T("MX Master Win　免安裝的按鍵、手勢與圓環自訂工具", "MX Master Win  ·  portable button, gesture and ring customizer"),
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 9F),
                Location = new Point(S(91), S(56))
            });

            AddNavigation(Lang.T("按鈕", "Buttons"), "", 0);
            AddNavigation(Lang.T("手勢與圓環", "Gestures & Ring"), "", 1);
            AddNavigation(Lang.T("指標與滾輪", "Pointer & Wheel"), "", 2);
            AddNavigation(Lang.T("觸覺回饋", "Haptics"), "\uE877", 3);   // Vibrate
            AddNavigation("MX Flow", "", 4);
            AddNavigation(Lang.T("電池", "Battery"), "\uE83F", 5);   // Battery10
            AddNavigation(Lang.T("鍵盤", "Keyboard"), "\uE765", 6);   // KeyboardClassic
            AddNavigation(Lang.T("接收器", "Receiver"), "\uE88E", 7);   // USB
            AddNavigation(Lang.T("其他裝置", "Other Devices"), "\uE7FC", 8);   // Game
            AddNavigation(Lang.T("進階", "Advanced"), "", 9);

            chip.Location = new Point(S(24), S(566));
            chip.Size = new Size(S(202), S(36));
            chip.OtherRowClicked += delegate
            {
                if (chipOtherId != null) othersPage.SelectDevice(chipOtherId);
                ShowPage(8);
            };
            Controls.Add(chip);

            // ---- Page 0: button mapping ----
            CardPanel mappingPage = AddPage(Lang.T("按鈕", "Buttons"));
            diagram.Location = new Point(S(12), S(54));
            diagram.Size = new Size(S(284), S(390));
            diagram.HighlightChanged += delegate
            {
                foreach (Label badge in rowBadges) badge.Invalidate();
            };
            diagram.HotspotClicked += delegate(int row)
            {
                ComboBox combo = selectors[rowInputs[row]].Combo;
                combo.Focus();
                combo.DroppedDown = true;
            };
            mappingPage.Controls.Add(diagram);

            // The row order is also the numbering shown on the diagram.
            int y = 56;
            AddMappingRow(mappingPage, Lang.T("中鍵", "Middle button"), Lang.T("按下滾輪", "Press the wheel"), MouseInput.Middle, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 1・拇指手勢鍵", "Side 1 · Gesture button"), Lang.T("單擊時執行", "Runs on a single click"), MouseInput.GestureButton, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 2・上一頁", "Side 2 · Back"), "", MouseInput.XButton2, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("側鍵 3・下一頁", "Side 3 · Forward"), "", MouseInput.XButton1, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("水平滾輪向左", "Horizontal scroll left"), Lang.T("拇指滾輪", "Thumb wheel"), MouseInput.WheelLeft, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("水平滾輪向右", "Horizontal scroll right"), Lang.T("拇指滾輪", "Thumb wheel"), MouseInput.WheelRight, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("Actions Ring 鍵", "Actions Ring button"), Lang.T("圓環停用時執行", "Runs when the ring is off"), MouseInput.ActionRingButton, y); y += 50;
            AddMappingRow(mappingPage, Lang.T("滾輪模式鍵", "Wheel mode button"), Lang.T("保留原功能即可切換滾輪模式", "Keep original to switch modes"), MouseInput.WheelModeButton, y);

            // ---- Page 1: gestures and the ring, with a live preview of the ring ----
            CardPanel modePage = AddPage(Lang.T("手勢與圓環", "Gestures & Ring"));
            int column = S(330);
            gestureMode.Text = Lang.T("拇指手勢鍵四向手勢", "Four-way gestures (gesture button)");
            gestureMode.Location = new Point(S(18), S(58));
            gestureMode.Size = new Size(column, S(26));
            modePage.Controls.Add(gestureMode);
            AddHint(modePage, Lang.T("按住並滑動；方向動作與靈敏度在「自訂手勢…」設定", "Hold and swipe; directions and sensitivity are under Customize gestures…"), 88);
            AddDivider(modePage, 122, column);

            actionRingMode.Text = Lang.T("Actions Ring 八方向圓環", "Actions Ring eight-way menu");
            actionRingMode.Checked = true;
            actionRingMode.Location = new Point(S(18), S(136));
            actionRingMode.Size = new Size(column, S(26));
            modePage.Controls.Add(actionRingMode);
            AddHint(modePage, Lang.T("按住顯示、移動選擇、放開執行；在中心放開取消", "Hold to show, move to choose, release to run"), 166);

            ringClickMode.Text = Lang.T("圓環改用點選（不必按住）", "Click to choose (no holding)");
            ringClickMode.Location = new Point(S(18), S(200));
            ringClickMode.Size = new Size(column, S(26));
            modePage.Controls.Add(ringClickMode);
            AddHint(modePage, Lang.T("點一下顯示，再點功能執行；點中心、圓環外或右鍵取消", "Click to show, click a slice to run; right-click cancels"), 230);

            Button customizeRing = Theme.CreateButton(Lang.T("自訂圓環…", "Customize ring…"), true);
            customizeRing.Location = new Point(S(18), S(266));
            customizeRing.Size = new Size(S(124), S(34));
            customizeRing.Click += delegate
            {
                ActionBinding[] edited = RingDialog.Prompt(this, ringBindings);
                if (edited == null) return;
                ringBindings = edited;
                actionRing.SetSlots(ringBindings);
                UpdateRingPreview();
                SaveSettings();
            };
            modePage.Controls.Add(customizeRing);

            Button customizeGestures = Theme.CreateButton(Lang.T("自訂手勢…", "Customize gestures…"), false);
            customizeGestures.Location = new Point(S(152), S(266));
            customizeGestures.Size = new Size(S(144), S(34));
            customizeGestures.Click += delegate
            {
                ActionBinding[] edited = GestureDialog.Prompt(this, gestureBindings, ref gestureSensitivity);
                if (edited == null) return;
                gestureBindings = edited;
                UpdateGestureNames();
                SaveSettings();
            };
            modePage.Controls.Add(customizeGestures);

            // Ring appearance: style, overall size, bubble size and opacity.
            AddRingOption(modePage, Lang.T("圓環樣式", "Ring style"), 314, null);
            ringStyle.DropDownStyle = ComboBoxStyle.DropDownList;
            ringStyle.Items.Add(Lang.T("分格圓環", "Ring"));
            ringStyle.Items.Add(Lang.T("泡泡（只顯示圖示）", "Bubbles (icons only)"));
            ringStyle.SelectedIndex = 0;
            ringStyle.Location = new Point(S(104), S(312));
            ringStyle.Width = S(186);
            modePage.Controls.Add(ringStyle);
            AddRingOption(modePage, Lang.T("整體大小", "Overall size"), 348, ringSize);
            AddRingOption(modePage, Lang.T("泡泡大小", "Bubble size"), 380, bubbleSize);
            ringOpacity.Minimum = 40;
            AddRingOption(modePage, Lang.T("不透明度", "Opacity"), 412, ringOpacity);

            ringPreview.SizeMode = PictureBoxSizeMode.Zoom;
            ringPreview.Location = new Point(S(400), S(54));
            ringPreview.Size = new Size(S(240), S(240));
            modePage.Controls.Add(ringPreview);
            modePage.Controls.Add(new Label
            {
                Text = Lang.T("圓環預覽", "Ring preview"),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(400), S(296)),
                Size = new Size(S(240), S(20))
            });

            // Right column under the preview: ring colour, and where the slice haptics went.
            int ringRight = 372;
            modePage.Controls.Add(new Label { Text = Lang.T("圓環顏色", "Ring color"), AutoSize = false, Location = new Point(S(ringRight), S(330)), Size = new Size(S(84), S(22)) });
            ringColor.DropDownStyle = ComboBoxStyle.DropDownList;
            ringColor.Items.Add(Lang.T("深色", "Dark"));
            ringColor.Items.Add(Lang.T("白色", "White"));
            ringColor.SelectedIndex = 0;
            ringColor.Location = new Point(S(ringRight + 86), S(328));
            ringColor.Width = S(150);
            modePage.Controls.Add(ringColor);

            // The slice tick now lives on the Haptics page with the other haptic moments.
            modePage.Controls.Add(new Label { Text = Lang.T("滑過選項時震動", "Haptic tick on each slice"), AutoSize = false, Location = new Point(S(ringRight), S(370)), Size = new Size(S(250), S(22)) });
            var hapticLink = new LinkLabel
            {
                Text = Lang.T("在「觸覺回饋」頁設定 →", "Set it on the Haptics page →"),
                AutoSize = true,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentDown,
                LinkBehavior = LinkBehavior.HoverUnderline,
                UseMnemonic = false,
                Font = new Font(Theme.UiFont, 9F),
                Location = new Point(S(ringRight), S(396))
            };
            hapticLink.LinkClicked += delegate { ShowPage(3); };
            modePage.Controls.Add(hapticLink);

            // ---- Page 2: pointer and wheel. The left part is filled in on first use, once HID++ exists. ----
            devicePage = AddPage("");
            BuildPrecisionColumn(devicePage);

            // ---- Page 3: haptics ----
            BuildHapticsPage(AddPage(Lang.T("觸覺回饋", "Haptics")));

            // ---- Page 4: MX Flow, switching computers at a screen edge ----
            CardPanel switchPage = AddPage("MX Flow");
            edgeSwitch.Text = Lang.T("啟用 MX Flow：游標推到螢幕邊緣時切換電腦", "Enable MX Flow: switch computers at a screen edge");
            edgeSwitch.Location = new Point(S(18), S(58));
            edgeSwitch.Size = new Size(switchPage.Width - S(36), S(26));
            switchPage.Controls.Add(edgeSwitch);
            AddHint(switchPage, Lang.T("游標貼著邊緣並持續往外推，滑鼠就切到指定的 Easy-Switch 頻道；按著左、右鍵拖曳時不會切換",
                "Keep pushing the pointer against an edge to move the mouse to that Easy-Switch channel; never while dragging"), 88);
            AddDivider(switchPage, 118, switchPage.Width - S(36));

            string[] edgeNames = { Lang.T("左邊緣", "Left edge"), Lang.T("右邊緣", "Right edge"), Lang.T("上邊緣", "Top edge"), Lang.T("下邊緣", "Bottom edge") };
            for (int i = 0; i < 4; i++)
            {
                int edgeIndex = i;
                int rowY = 130 + i * 34;
                switchPage.Controls.Add(new Label { Text = edgeNames[i], AutoSize = false, Location = new Point(S(18), S(rowY + 3)), Size = new Size(S(84), S(22)) });
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(104), S(rowY)), Width = S(250) };
                combo.SelectedIndexChanged += delegate
                {
                    if (loadingEdge) return;
                    edgeHosts[edgeIndex] = combo.SelectedIndex - 1;
                    edgePushing = false;
                    SaveSettings();
                };
                edgeTargets[i] = combo;
                switchPage.Controls.Add(combo);
            }

            int right = 390;
            switchPage.Controls.Add(new Label { Text = Lang.T("觸發時間", "Trigger time"), AutoSize = false, Location = new Point(S(right), S(133)), Size = new Size(S(84), S(22)) });
            edgeDwell.Maximum = 20;
            edgeDwell.Minimum = 1;
            edgeDwell.Value = 3;
            edgeDwell.Location = new Point(S(right + 86), S(130));
            edgeDwell.Size = new Size(S(186), S(26));
            var dwellReadout = new Label
            {
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(S(right + 276), S(131)),
                Size = new Size(S(64), S(22))
            };
            EventHandler showDwell = delegate { dwellReadout.Text = (edgeDwell.Value / 10.0).ToString("0.0") + Lang.T(" 秒", " s"); };
            edgeDwell.ValueChanged += showDwell;
            showDwell(null, EventArgs.Empty);
            switchPage.Controls.Add(edgeDwell);
            switchPage.Controls.Add(dwellReadout);

            switchPage.Controls.Add(new Label { Text = Lang.T("按住按鍵", "Held key"), AutoSize = false, Location = new Point(S(right), S(167)), Size = new Size(S(84), S(22)) });
            edgeKey.DropDownStyle = ComboBoxStyle.DropDownList;
            edgeKey.Items.AddRange(new object[]
            {
                Lang.T("不需要", "None"), "Ctrl", "Alt", "Shift", "Win",
                Lang.T("中鍵", "Middle button"), Lang.T("側鍵 2・上一頁", "Side 2 · Back"), Lang.T("側鍵 3・下一頁", "Side 3 · Forward"),
                Lang.T("拇指手勢鍵", "Gesture button"), Lang.T("Actions Ring 鍵", "Actions Ring button")
            });
            edgeKey.SelectedIndex = 0;
            edgeKey.Location = new Point(S(right + 86), S(164));
            edgeKey.Width = S(200);
            switchPage.Controls.Add(edgeKey);
            AddHintAt(switchPage, Lang.T("選了按鍵就要按住它再推到邊緣；選滑鼠按鍵時，", "With a key chosen, hold it while pushing. A mouse button"), right, 194);
            AddHintAt(switchPage, Lang.T("它原本的動作改在放開時執行", "chosen here runs its own action on release instead"), right, 212);

            edgePlaceCursor.Text = Lang.T("切回來時把游標移到邊緣", "Place the pointer at the edge on return");
            edgePlaceCursor.Checked = true;
            edgePlaceCursor.Location = new Point(S(right), S(236));
            edgePlaceCursor.Size = new Size(switchPage.Width - S(18) - S(right), S(26));
            switchPage.Controls.Add(edgePlaceCursor);

            edgeScreens.AutoSize = true;
            edgeScreens.ForeColor = Theme.Muted;
            edgeScreens.Font = new Font(Theme.UiFont, 8.5F);
            edgeScreens.Location = new Point(S(18), S(270));
            switchPage.Controls.Add(edgeScreens);
            AddDivider(switchPage, 296, switchPage.Width - S(36));

            switchPage.Controls.Add(new Label { Text = Lang.T("Easy-Switch 頻道", "Easy-Switch channels"), AutoSize = true, Location = new Point(S(18), S(306)) });
            for (int i = 0; i < 3; i++)
            {
                int hostIndex = i;
                hostLabels[i] = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Location = new Point(S(18), S(332 + i * 30)),
                    Size = new Size(switchPage.Width - S(36) - S(130), S(26))
                };
                Button go = Theme.CreateButton(Lang.T("切換過去", "Switch"), false);
                go.Size = new Size(S(112), S(26));
                go.Location = new Point(switchPage.Width - S(18) - go.Width, S(332 + i * 30));
                go.Click += delegate { SwitchHost(hostIndex, "manual"); };
                hostButtons[i] = go;
                switchPage.Controls.Add(hostLabels[i]);
                switchPage.Controls.Add(go);
            }
            AddHint(switchPage, Lang.T("切過去後，另一台電腦也要執行本程式並設定邊緣才能同樣切回來；否則請按 Easy-Switch 鍵。鍵盤一起切換在「鍵盤」頁",
                "To come back the same way the other computer must run this app with an edge set, or use Easy-Switch. Keyboard: see the Keyboard page"), 428);

            // ---- Page 5: battery ----
            BuildBatteryPage(AddPage(Lang.T("電池", "Battery")));

            // ---- Page 6: keyboard ----
            BuildKeyboardPage(AddPage(Lang.T("鍵盤", "Keyboard")));

            // ---- Page 7: Logi Bolt receiver ----
            BuildReceiverPage(AddPage(Lang.T("接收器", "Receiver")));

            // ---- Page 8: devices on a Lightspeed receiver ----
            others = new OtherDevicesManager(
                open => HidppDiversionManager.LightspeedPort.OpenNew(open, OnOthersReport),
                SecondsSinceMouse, PostOthers, othersBackupFolder);
            others.Log += OnOthersReport;
            others.Changed += delegate(List<OtherDeviceSnapshot> list)
            {
                if (demoOthers != 0) return;
                otherSnapshots = list;
                othersPage.ShowDevices(list);
                UpdateOtherChip(list);
            };
            others.ButtonChanged += OnOtherButton;
            others.ButtonsLost += OnOtherButtonsLost;
            CardPanel othersCard = AddPage(Lang.T("其他裝置", "Other Devices"));
            othersPage = new OtherDevicesPage(others, S, HasOriginalProfile, GetOtherBinding, SetOtherBinding) { Bounds = new Rectangle(S(10), S(46), S(746), S(402)) };
            othersCard.Controls.Add(othersPage);

            // ---- Page 9: everything else ----
            CardPanel advancedPage = AddPage(Lang.T("進階", "Advanced"));
            enableSwitch.Text = Lang.T("啟用按鍵對應", "Enable button mapping");
            enableSwitch.Checked = true;
            enableSwitch.Location = new Point(S(18), S(58));
            enableSwitch.Size = new Size(advancedPage.Width - S(36), S(26));
            enableSwitch.CheckedChanged += delegate
            {
                if (enableSwitch.Checked != MouseHook.Enabled) ToggleEnabled();
            };
            advancedPage.Controls.Add(enableSwitch);
            AddHint(advancedPage, Lang.T("關閉後所有按鍵恢復原本功能，等同系統匣選單的「暫停」", "When off, every button behaves normally (same as Pause in the tray menu)"), 88);
            AddDivider(advancedPage, 122, advancedPage.Width - S(36));

            startMinimized.Text = Lang.T("啟動時直接縮小到系統匣", "Start minimized to the tray");
            startMinimized.Checked = true;
            startMinimized.Location = new Point(S(18), S(136));
            startMinimized.Size = new Size(advancedPage.Width - S(36), S(26));
            advancedPage.Controls.Add(startMinimized);
            AddHint(advancedPage, Lang.T("開啟程式時不顯示視窗；關閉視窗也只是隱藏，要結束請用系統匣圖示的選單",
                "No window is shown at launch. Closing the window only hides it; use the tray icon menu to exit"), 166);

            // Off unless the user turns it on. The switch mirrors whether the shortcut exists, so it is
            // not stored in the INI, and a shortcut left by an older location is refreshed at start-up.
            startWithWindows.Text = Lang.T("登入 Windows 時自動啟動", "Start when I sign in to Windows");
            startWithWindows.Location = new Point(S(18), S(194));
            startWithWindows.Size = new Size(advancedPage.Width - S(36), S(26));
            startWithWindows.Checked = StartupShortcut.Exists;
            if (startWithWindows.Checked)
            {
                string ignored;
                StartupShortcut.Create(out ignored);
            }
            startWithWindows.CheckedChanged += delegate { ApplyStartWithWindows(); };
            advancedPage.Controls.Add(startWithWindows);
            AddHint(advancedPage, Lang.T("會在你的「啟動」資料夾放一個捷徑；關閉時移除。不需要管理員權限，也不修改登錄檔",
                "Puts a shortcut in your Startup folder and removes it when turned off. No admin rights, no registry change"), 224);
            AddDivider(advancedPage, 256, advancedPage.Width - S(36));

            // Language: every string is chosen at start-up, so a change relaunches the app.
            advancedPage.Controls.Add(new Label { Text = "語言 / Language", AutoSize = true, Location = new Point(S(18), S(268)) });
            AddHint(advancedPage, Lang.T("變更後程式會自動重新啟動", "The app restarts after a change"), 292);
            var language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = S(190) };
            language.Items.Add(Lang.T("跟隨系統", "Follow system"));
            language.Items.Add("繁體中文");
            language.Items.Add("English");
            language.SelectedIndex = Lang.Setting == "zh" ? 1 : Lang.Setting == "en" ? 2 : 0;
            language.Location = new Point(advancedPage.Width - S(18) - language.Width, S(276));
            language.SelectionChangeCommitted += delegate
            {
                string chosen = language.SelectedIndex == 1 ? "zh" : language.SelectedIndex == 2 ? "en" : "Auto";
                if (chosen == Lang.Setting) return;
                Lang.Setting = chosen;
                SaveSettings();
                Program.RestartRequested = true;
                reallyExit = true;
                Close();
            };
            advancedPage.Controls.Add(language);
            AddDivider(advancedPage, 330, advancedPage.Width - S(36));

            AddActionRow(advancedPage, Lang.T("工程模式", "Engineering mode"), Lang.T("檢視滑鼠送出的原始事件，用於辨識按鍵或回報問題", "View the raw events the mouse sends, to identify buttons or report problems"), Lang.T("開啟工程模式…", "Open…"), 342,
                delegate { ShowDiagnostics(); });
            AddDivider(advancedPage, 402, advancedPage.Width - S(36));

            advancedPage.Controls.Add(new Label
            {
                Text = Lang.T("關於", "About") + "　MX Master Win " + Program.Version + Lang.T("　免安裝、免管理員權限，不安裝驅動或服務", "    Portable, no admin rights, no driver or service"),
                AutoSize = true,
                Location = new Point(S(18), S(412))
            });
            advancedPage.Controls.Add(new Label
            {
                Text = Lang.T("設定檔：", "Settings file: ") + configPath,
                AutoSize = false,
                AutoEllipsis = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(18), S(434)),
                Size = new Size(advancedPage.Width - S(36), S(20))
            });

            statusDot.Text = "●";
            statusDot.TextAlign = ContentAlignment.MiddleCenter;
            statusDot.Location = new Point(S(250), S(568));
            statusDot.Size = new Size(S(22), S(32));
            statusDot.ForeColor = Theme.Warning;
            Controls.Add(statusDot);

            status.UseMnemonic = false;
            status.AutoSize = false;
            status.AutoEllipsis = true;
            status.TextAlign = ContentAlignment.MiddleLeft;
            status.ForeColor = Theme.Muted;
            status.Location = new Point(S(274), S(568));
            status.Size = new Size(width - S(274) - S(24), S(32));
            Controls.Add(status);

            liveLog.Text = Lang.T("記錄即時事件", "Capture live events");
            liveLog.CheckedChanged += delegate
            {
                if (liveLog.Checked)
                {
                    try
                    {
                        File.WriteAllText(diagnosticLogPath, "MX Master Win diagnostic capture\r\n", Encoding.UTF8);
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Diagnostics enabled; movement-only events are filtered.");
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Log file: " + diagnosticLogPath);
                    }
                    catch (Exception ex)
                    {
                        AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Cannot create log file: " + ex.Message);
                    }
                }
            };
            // The event list and its switch live in the engineering window, not on the main page.
            diagnostics = new DiagnosticsForm(liveLog, eventLog, ClearEvents, diagnosticLogPath, appIcon);

            ShowPage(0);
        }

        private void AddNavigation(string text, string glyph, int index)
        {
            var item = new NavButton
            {
                Text = text,
                Glyph = glyph,
                // Ten pages: dense enough that the two-row battery chip (from y=530) still fits below.
                Location = new Point(S(24), S(100) + index * S(42)),
                Size = new Size(S(202), S(38))
            };
            item.Click += delegate { ShowPage(index); };
            navigation.Add(item);
            Controls.Add(item);
        }

        private CardPanel AddPage(string title)
        {
            var page = new CardPanel(uiScale)
            {
                Title = title,
                Location = new Point(S(250), S(100)),
                Size = new Size(S(766), S(458)),
                Visible = false
            };
            pages.Add(page);
            Controls.Add(page);
            return page;
        }

        // Right column of the pointer page, beside the embedded device editor.
        private void BuildPrecisionColumn(CardPanel page)
        {
            int x = 508, width = 240;
            page.Controls.Add(new Label { Text = Lang.T("精準模式", "Precision mode"), AutoSize = true, Font = new Font(Theme.UiFont, 10F, FontStyle.Bold), Location = new Point(S(x), S(22)) });
            AddWrappedHint(page, Lang.T("按住觸發按鍵時暫時降到較低的速度，放開就恢復；適合修圖、對齊等精細操作",
                "Hold the trigger to drop to a slower speed for detailed work; release to go back"), x, 46, width, 40);

            page.Controls.Add(new Label { Text = Lang.T("精準速度", "Precision speed"), AutoSize = false, Location = new Point(S(x), S(96)), Size = new Size(S(140), S(22)) });
            precisionDpiValue.AutoSize = false;
            precisionDpiValue.TextAlign = ContentAlignment.MiddleRight;
            precisionDpiValue.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            precisionDpiValue.Location = new Point(S(x + width - 100), S(96));
            precisionDpiValue.Size = new Size(S(100), S(22));
            page.Controls.Add(precisionDpiValue);
            precisionDpi.Minimum = 200;
            precisionDpi.Maximum = 4000;
            precisionDpi.Increment = 50;
            precisionDpi.Value = 400;
            precisionDpi.Location = new Point(S(x - 6), S(120));
            precisionDpi.Size = new Size(S(width + 6), S(28));
            precisionDpiValue.Text = precisionDpi.Value + " DPI";
            page.Controls.Add(precisionDpi);

            page.Controls.Add(new Label { Text = Lang.T("觸發按鍵", "Trigger"), AutoSize = false, Location = new Point(S(x), S(160)), Size = new Size(S(140), S(22)) });
            precisionTrigger.DropDownStyle = ComboBoxStyle.DropDownList;
            precisionTrigger.Items.AddRange(new object[]
            {
                Lang.T("不使用", "None"), Lang.T("中鍵", "Middle button"), Lang.T("側鍵 2・上一頁", "Side 2 · Back"), Lang.T("側鍵 3・下一頁", "Side 3 · Forward"),
                Lang.T("拇指手勢鍵", "Gesture button"), Lang.T("Actions Ring 鍵", "Actions Ring button")
            });
            precisionTrigger.SelectedIndex = 0;
            precisionTrigger.Location = new Point(S(x), S(184));
            precisionTrigger.Width = S(width);
            page.Controls.Add(precisionTrigger);
            AddWrappedHint(page, Lang.T("手勢鍵與圓環鍵要先關閉手勢或圓環；也可以在「按鈕」頁或圓環選「精準模式」",
                "The gesture and ring buttons need their gestures or ring off. Precision mode can also go on the Buttons page or the ring"), x, 214, width, 54);

            page.Controls.Add(new Panel { BackColor = Theme.Divider, Location = new Point(S(x), S(280)), Size = new Size(S(width), 1) });
            page.Controls.Add(new Label { Text = Lang.T("切換提示", "On-screen feedback"), AutoSize = true, Font = new Font(Theme.UiFont, 10F, FontStyle.Bold), Location = new Point(S(x), S(294)) });
            modeHud.Text = Lang.T("顯示切換提示", "Show mode hints");
            modeHud.Checked = true;
            modeHud.Location = new Point(S(x), S(320));
            modeHud.Size = new Size(S(width), S(26));
            page.Controls.Add(modeHud);
            AddWrappedHint(page, Lang.T("精準模式或滾動模式切換時，在螢幕下方短暫顯示目前狀態；震動在「觸覺回饋」頁設定",
                "Briefly shows the new state at the bottom of the screen; the matching tap is set on the Haptics page"), x, 350, width, 54);
        }

        private void BuildHapticsPage(CardPanel page)
        {
            int right = 748;
            page.Controls.Add(new Label { Text = Lang.T("觸覺強度", "Strength"), AutoSize = false, Location = new Point(S(18), S(62)), Size = new Size(S(96), S(22)) });
            hapticStrength.Minimum = 0;
            hapticStrength.Maximum = 100;
            hapticStrength.Increment = 5;
            hapticStrength.Location = new Point(S(114), S(58));
            hapticStrength.Size = new Size(S(420), S(30));
            hapticStrength.Enabled = false;
            hapticStrength.ValueChanged += delegate { hapticStrengthValue.Text = hapticStrength.Value > 0 ? hapticStrength.Value.ToString() : Lang.T("關閉", "Off"); };
            page.Controls.Add(hapticStrength);
            hapticStrengthValue.AutoSize = false;
            hapticStrengthValue.TextAlign = ContentAlignment.MiddleCenter;
            hapticStrengthValue.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            hapticStrengthValue.Location = new Point(S(540), S(58));
            hapticStrengthValue.Size = new Size(S(80), S(30));
            hapticStrengthValue.Text = "—";
            page.Controls.Add(hapticStrengthValue);
            hapticStrengthApply = Theme.CreateButton(Lang.T("套用", "Apply"), false);
            hapticStrengthApply.Size = new Size(S(80), S(30));
            hapticStrengthApply.Location = new Point(S(right - 80), S(58));
            hapticStrengthApply.Enabled = false;
            hapticStrengthApply.Click += delegate { ApplyHapticStrength(); };
            page.Controls.Add(hapticStrengthApply);
            AddHint(page, Lang.T("滑鼠本身的震動強度。拉到最左邊為關閉，下方所有震動都會跟著停用",
                "The mouse's own haptic strength. Fully left turns it off, and every tap below with it"), 94);

            // Low-battery pause: the threshold is the one set under Battery on the pointer page.
            lowBatteryHapticPause.Text = Lang.T("電量低時暫停震動", "Pause on low battery");
            lowBatteryHapticPause.Checked = true;
            lowBatteryHapticPause.Location = new Point(S(18), S(118));
            lowBatteryHapticPause.Size = new Size(S(250), S(26));
            page.Controls.Add(lowBatteryHapticPause);
            hapticPauseNote.AutoSize = false;
            hapticPauseNote.AutoEllipsis = true;
            hapticPauseNote.UseMnemonic = false;
            hapticPauseNote.Font = new Font(Theme.UiFont, 8.5F);
            hapticPauseNote.TextAlign = ContentAlignment.MiddleLeft;
            hapticPauseNote.Location = new Point(S(276), S(118));
            hapticPauseNote.Size = new Size(S(right - 276), S(26));
            page.Controls.Add(hapticPauseNote);
            AddDivider(page, 152, S(right - 18));

            page.Controls.Add(new Label { Text = Lang.T("震動時機", "When to tap"), AutoSize = true, Font = new Font(Theme.UiFont, 10F, FontStyle.Bold), Location = new Point(S(18), S(162)) });
            AddHint(page, Lang.T("選擇哪些操作要讓滑鼠輕震一下；每一種都可以挑不同的波形，選了會立刻在滑鼠上震一下",
                "Choose which moments make the mouse tap. Each can use its own waveform; picking one plays it on the mouse"), 186);
            // Default waveforms, as indexes into HapticWaves: subtle, soft bump, crisp bump, crisp.
            AddHapticRow(page, ringHaptic, ringHapticWave, Lang.T("Actions Ring 滑過選項", "Actions Ring slices"),
                Lang.T("游標移到圓環的另一格時", "As the pointer enters another slice"), 214, DefaultHapticWave);
            AddHapticRow(page, gestureHaptic, gestureHapticWave, Lang.T("手勢方向確定", "Gesture recognized"),
                Lang.T("按住手勢鍵滑動、方向判定完成時", "When a thumb-gesture direction is locked in"), 272, 3);
            AddHapticRow(page, flowHaptic, flowHapticWave, Lang.T("切換電腦", "Computer switch"),
                Lang.T("MX Flow 或「切換到頻道」動作讓滑鼠離開之前", "Before MX Flow or a Switch-to-channel action moves the mouse away"), 330, 5);
            AddHapticRow(page, modeHaptic, modeHapticWave, Lang.T("模式切換", "Mode changes"),
                Lang.T("精準模式開關、段落／自由滾動切換時", "Precision mode on or off, ratchet / free spin"), 388, 4);
        }

        // Keyboard page: the keyboard's name (or why none was found), connection, battery, firmware,
        // backlight, top row, and which keyboard channel goes with each mouse channel.
        private void BuildKeyboardPage(CardPanel page)
        {
            keyboardName.AutoSize = false; keyboardName.AutoEllipsis = true; keyboardName.UseMnemonic = false;
            keyboardName.Font = keyboardNameFont;
            keyboardName.Bounds = new Rectangle(S(18), S(54), S(608), S(26));
            page.Controls.Add(keyboardName);
            Button reload = Theme.CreateButton(Lang.T("重新讀取", "Reload"), false);
            reload.Bounds = new Rectangle(S(638), S(52), S(110), S(30));
            reload.Click += delegate { RefreshKeyboard(true); }; page.Controls.Add(reload);
            string[] titles = { Lang.T("連線", "Connection"), Lang.T("電量", "Battery"), Lang.T("韌體", "Firmware") };
            Label[] values = { keyboardConnection, keyboardBattery, keyboardFirmware };
            for (int i = 0; i < 3; i++)
            {
                int x = 18 + i * 250;
                page.Controls.Add(new Label { Text = titles[i], AutoSize = false, ForeColor = Theme.Muted,
                    Bounds = new Rectangle(S(x), S(88), S(230), S(18)) });
                values[i].AutoSize = false; values[i].AutoEllipsis = true; values[i].UseMnemonic = false;
                values[i].Font = new Font(Theme.UiFont, 11F, FontStyle.Bold);
                values[i].Bounds = new Rectangle(S(x), S(108), S(230), S(26)); values[i].Text = "—";
                page.Controls.Add(values[i]);
            }
            // Sub-pages: a segmented control and, under it, one content panel per segment. Rows follow the
            // other pages' style (title and a muted hint on the left, the control right-aligned, dividers between
            // rows) and are stacked by LayoutKeyboardTabs so rows the keyboard does not support leave no gaps.
            // Content runs from page y=188 to 450 (page height 458), also at 150% scaling.
            string[] tabNames = { Lang.T("設定", "Settings"), Lang.T("背光延遲", "Backlight delays"),
                Lang.T("停用按鍵", "Disable keys"), Lang.T("一起切換", "Switch together") };
            var tabs = new SegmentedTabs(tabNames) { Bounds = new Rectangle(S(18), S(146), S(730), S(34)) };
            page.Controls.Add(tabs);
            for (int i = 0; i < 4; i++)
            {
                keyboardTabPages[i] = new Panel { Bounds = new Rectangle(S(10), S(188), S(746), S(262)), BackColor = Theme.Card, Visible = i == 0 };
                page.Controls.Add(keyboardTabPages[i]);
                keyboardEmpty[i] = new Label { AutoSize = false, ForeColor = Theme.Muted, UseMnemonic = false,
                    Bounds = new Rectangle(S(8), S(14), S(730), S(44)), Visible = false };
                keyboardTabPages[i].Controls.Add(keyboardEmpty[i]);
            }
            tabs.SelectedIndexChanged += delegate
            {
                for (int i = 0; i < 4; i++) keyboardTabPages[i].Visible = i == tabs.SelectedIndex;
            };
            Panel settings = keyboardTabPages[0], delays = keyboardTabPages[1], disable = keyboardTabPages[2], follow = keyboardTabPages[3];
            const int left = 8, right = 738;   // inside the content panels (page x 18 to 748)

            // Settings: backlight (with brightness while manual), top row, OS layout.
            KeyboardRow(settings, keyboardLightGroup, Lang.T("背光", "Backlight"),
                Lang.T("自動：手靠近時亮起、亮度跟著環境光；手動：固定亮度", "Auto lights up as your hands approach and follows ambient light; Manual is fixed"), 64);
            keyboardBacklight.DropDownStyle = ComboBoxStyle.DropDownList;
            keyboardBacklight.Bounds = new Rectangle(S(right - 200), S(19), S(200), S(26));
            keyboardBacklight.SelectionChangeCommitted += delegate { ApplyKeyboardBacklight(); };
            keyboardLightGroup.Controls.Add(keyboardBacklight);

            keyboardLevelRow.Bounds = new Rectangle(0, 0, S(746), S(44)); keyboardLevelRow.BackColor = Theme.Card;
            settings.Controls.Add(keyboardLevelRow);
            keyboardLevelLabel.Text = Lang.T("亮度", "Brightness"); keyboardLevelLabel.AutoSize = false;
            keyboardLevelLabel.Bounds = new Rectangle(S(left + 16), S(10), S(120), S(24));
            keyboardLevel.Minimum = 0; keyboardLevel.Maximum = 7;
            keyboardLevel.Bounds = new Rectangle(S(right - 200), S(8), S(160), S(26));
            keyboardLevelValue.AutoSize = false; keyboardLevelValue.TextAlign = ContentAlignment.MiddleRight;
            keyboardLevelValue.Bounds = new Rectangle(S(right - 36), S(10), S(36), S(24));
            keyboardLevelRow.Controls.Add(keyboardLevelLabel); keyboardLevelRow.Controls.Add(keyboardLevel); keyboardLevelRow.Controls.Add(keyboardLevelValue);
            AddKeyboardDivider(keyboardLevelRow, 44);
            keyboardLevelApply.Interval = 400;
            keyboardLevelApply.Tick += delegate
            {
                keyboardLevelApply.Stop(); int level = keyboardLevel.Value;
                if (keyboardSnapshot == null || keyboardSnapshot.Asleep || level == keyboardSnapshot.BacklightLevel) return;
                WriteKeyboard(Lang.T("背光亮度", "Backlight brightness"), delegate { return keyboard.SetBacklight(true, 3, level); });
            };
            keyboardLevel.ValueChanged += delegate
            {
                keyboardLevelValue.Text = keyboardLevel.Value.ToString();
                if (loadingKeyboard) return; keyboardLevelApply.Stop(); keyboardLevelApply.Start();
            };

            KeyboardRow(settings, keyboardFnGroup, Lang.T("最上排按鍵預設", "Top row by default"),
                Lang.T("另一種功能要按住 Fn；每個 Easy-Switch 頻道各自記錄", "Hold Fn for the other one; kept per Easy-Switch channel"), 64);
            keyboardTopRow.DropDownStyle = ComboBoxStyle.DropDownList;
            keyboardTopRow.Items.Add(Lang.T("媒體鍵", "Media keys")); keyboardTopRow.Items.Add("F1–F12");
            keyboardTopRow.Bounds = new Rectangle(S(right - 200), S(19), S(200), S(26)); keyboardFnGroup.Controls.Add(keyboardTopRow);
            keyboardTopRow.SelectionChangeCommitted += delegate
            {
                bool media = keyboardTopRow.SelectedIndex == 0;
                WriteKeyboard(Lang.T("F 鍵", "F-keys"), delegate { return keyboard.SetFnMediaFirst(media); });
            };

            KeyboardRow(settings, keyboardOsGroup, Lang.T("作業系統配置", "OS layout"),
                Lang.T("按鍵依哪個系統配置；只變更目前 Easy-Switch 頻道", "Which system the keys are laid out for; current Easy-Switch channel only"), 64);
            keyboardPlatform.DropDownStyle = ComboBoxStyle.DropDownList; keyboardPlatform.Bounds = new Rectangle(S(right - 200), S(19), S(200), S(26));
            keyboardOsGroup.Controls.Add(keyboardPlatform);
            keyboardPlatform.SelectionChangeCommitted += delegate
            {
                SolaarFeatures.Platform selected = keyboardPlatform.SelectedItem as SolaarFeatures.Platform;
                if (selected != null) { int value = selected.Id; WriteKeyboard(Lang.T("作業系統配置", "OS layout"), delegate { return keyboard.SetPlatform(value); }); }
            };

            // Backlight delays: one row each, number + unit + Apply on one line.
            string[] delayNames = { Lang.T("手離開", "Hands away"), Lang.T("手靠近", "Hands near"), Lang.T("接電源", "External power") };
            string[] delayHints = { Lang.T("手離開鍵盤後，背光多久後熄滅", "How long the backlight stays on after your hands leave"),
                Lang.T("手放在鍵盤附近時，背光多久後熄滅", "How long it stays on while your hands are near"),
                Lang.T("接上電源時，背光多久後熄滅", "How long it stays on while on external power") };
            for (int i = 0; i < 3; i++)
            {
                int field = i;
                Panel row = new Panel();
                keyboardDelayRows[i] = row;
                KeyboardRow(delays, row, delayNames[i], delayHints[i], 62);
                Button apply = Theme.CreateButton(Lang.T("套用", "Apply"), false);
                apply.Bounds = new Rectangle(S(right - 84), S(16), S(84), S(30)); row.Controls.Add(apply);
                row.Controls.Add(new Label { Text = Lang.T("秒", "s"), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                    Bounds = new Rectangle(S(right - 84 - 40), S(18), S(34), S(26)) });
                NumericUpDown seconds = new NumericUpDown { Minimum = 1, Maximum = 600, TextAlign = HorizontalAlignment.Right,
                    Bounds = new Rectangle(S(right - 84 - 40 - 88), S(18), S(84), S(26)) };
                keyboardDelays[i] = seconds; row.Controls.Add(seconds);
                apply.Click += delegate { int value = (int)seconds.Value; WriteKeyboard(delayNames[field], delegate { return keyboard.SetBacklightDelay(field, value); }); };
            }
            keyboardDelayNote = new Label { Text = Lang.T("1–600 秒，以 5 秒為單位（向上取整）；按「套用」才會寫入鍵盤。",
                "1–600 seconds in 5-second steps (rounded up); Apply writes it to the keyboard."),
                AutoSize = false, ForeColor = Theme.Muted, Font = new Font(Theme.UiFont, 8.5F), Bounds = new Rectangle(S(left), 0, S(730), S(36)) };
            delays.Controls.Add(keyboardDelayNote);

            // Disable keys: switches in two columns, only the keys this keyboard can disable.
            keyboardDisableHint = new Label { Text = Lang.T("打開開關就停用那個鍵；只列出這把鍵盤允許停用的鍵。",
                "Turn a switch on to disable that key; only keys this keyboard can disable are listed."),
                AutoSize = false, ForeColor = Theme.Muted, Font = new Font(Theme.UiFont, 8.5F), Bounds = new Rectangle(S(left), S(10), S(730), S(22)) };
            disable.Controls.Add(keyboardDisableHint);
            for (int i = 0; i < 8; i++)
            {
                int bit = i;
                string name = i == 4 ? Lang.T("Windows 鍵", "Windows key") : SolaarFeatures.DisableName(i);
                CheckBox key = new ToggleSwitch { Text = name, Size = new Size(S(345), S(30)) };
                key.Click += delegate { bool value = key.Checked; WriteKeyboard(name, delegate { return keyboard.SetDisabledKey(bit, value); }); };
                keyboardDisabled[i] = key; disable.Controls.Add(key);
            }

            // Switch together: the switch, then which keyboard channel each mouse channel takes.
            Panel followRow = new Panel();
            KeyboardRow(follow, followRow, "", Lang.T("MX Flow、「切換到頻道」動作與「切換過去」按鈕都會帶著鍵盤",
                "MX Flow, the Switch-to-channel action and the Switch button all take the keyboard along"), 64);
            keyboardFollow.Text = Lang.T("滑鼠切換電腦時鍵盤一起切換", "Switch the keyboard with the mouse");
            keyboardFollow.Checked = true; keyboardFollow.Bounds = new Rectangle(S(left), S(6), S(right - left), S(28));
            keyboardFollow.CheckedChanged += delegate
            {
                if (!keyboardFollow.Checked) keyboard.CancelPendingSwitch();
                if (loadingKeyboard) return; RefreshKeyboardUi(); SaveSettings();
            };
            followRow.Controls.Add(keyboardFollow);
            for (int i = 0; i < 3; i++)
            {
                int mouseChannel = i;
                keyboardRowLabels[i] = new Label { AutoEllipsis = true, UseMnemonic = false, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                    Bounds = new Rectangle(S(left), S(76 + i * 40), S(360), S(26)) };
                follow.Controls.Add(keyboardRowLabels[i]);
                ComboBox combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList,
                    Bounds = new Rectangle(S(right - 330), S(76 + i * 40), S(330), S(26)) };
                combo.SelectionChangeCommitted += delegate
                {
                    int selected = combo.SelectedIndex;
                    keyboardChannels[mouseChannel] = selected <= 0 ? -2 : selected == combo.Items.Count - 1 ? -1 : selected - 1;
                    SaveSettings();
                };
                keyboardRowTargets[i] = combo; follow.Controls.Add(combo);
            }
            follow.Controls.Add(new Label { Text = Lang.T("自動＝同名電腦的頻道，否則同一個頻道號。經 Logi Bolt 配對的頻道在鍵盤上常記成「Logitech Bolt receiver」，對不上時請手動指定。",
                "Auto picks the channel with the same computer name, else the same number. Channels paired through Logi Bolt often read \"Logitech Bolt receiver\" on the keyboard; pick one by hand if they do not match."),
                AutoSize = false, ForeColor = Theme.Muted, Font = new Font(Theme.UiFont, 8.5F), Bounds = new Rectangle(S(left), S(202), S(730), S(44)) });
            RefreshKeyboardUi();
        }

        // One Keyboard sub-page row: title and muted hint on the left (title empty when a control such as a
        // switch carries it), a divider at the bottom; the caller adds the right-hand control.
        private void KeyboardRow(Panel parent, Panel row, string title, string hint, int height)
        {
            row.Bounds = new Rectangle(0, 0, S(746), S(height));
            row.BackColor = Theme.Card;
            parent.Controls.Add(row);
            if (title.Length > 0)
                row.Controls.Add(new Label { Text = title, AutoSize = false, UseMnemonic = false, Bounds = new Rectangle(S(8), S(10), S(500), S(24)) });
            row.Controls.Add(new Label { Text = hint, AutoSize = false, AutoEllipsis = true, UseMnemonic = false, ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F), Bounds = new Rectangle(S(8), S(36), S(510), S(20)) });
            AddKeyboardDivider(row, height);
        }

        private void AddKeyboardDivider(Panel row, int height)
        {
            var line = new Panel { BackColor = Theme.Divider, Bounds = new Rectangle(S(8), S(height) - 1, S(730), 1) };
            row.Controls.Add(line);
            keyboardRowDividers[row] = line;
        }

        private void WantKeyboard(Control control, bool show)
        {
            control.Visible = show;
            keyboardWanted[control] = show;
        }

        private bool KeyboardWanted(Control control)
        {
            bool show;
            return keyboardWanted.TryGetValue(control, out show) && show;
        }

        // Stacks the wanted rows from the top (no gaps for unsupported rows); the last one drops its divider.
        private int StackKeyboardRows(Control[] rows)
        {
            int y = 0;
            Control last = null;
            foreach (Control row in rows)
            {
                Panel line;
                if (keyboardRowDividers.TryGetValue(row, out line)) line.Visible = true;
                if (!KeyboardWanted(row)) continue;
                row.Top = y;
                y += row.Height;
                last = row;
            }
            Panel lastLine;
            if (last != null && keyboardRowDividers.TryGetValue(last, out lastLine)) lastLine.Visible = false;
            return y;
        }

        private void LayoutKeyboardTabs(KeyboardSnapshot kb)
        {
            if (keyboardTabPages[0] == null) return;
            string missing = Lang.T("沒有偵測到鍵盤；連上後會顯示這把鍵盤支援的項目。", "No keyboard detected; what it supports appears once it connects.");

            int settingsBottom = StackKeyboardRows(new Control[] { keyboardLightGroup, keyboardLevelRow, keyboardFnGroup, keyboardOsGroup });
            keyboardEmpty[0].Text = kb == null ? missing : Lang.T("這把鍵盤沒有可以在這裡調整的設定。", "This keyboard has no settings to change here.");
            keyboardEmpty[0].Visible = settingsBottom == 0;

            int delaysBottom = StackKeyboardRows(keyboardDelayRows);
            keyboardDelayNote.Top = delaysBottom + S(12);
            keyboardDelayNote.Visible = delaysBottom > 0;
            keyboardEmpty[1].Text = kb == null ? missing : Lang.T("這把鍵盤不支援背光延遲（需要背光 v3）。", "This keyboard has no backlight delays (needs backlight v3).");
            keyboardEmpty[1].Visible = delaysBottom == 0;

            int shown = 0;
            for (int i = 0; i < 8; i++)
            {
                if (!KeyboardWanted(keyboardDisabled[i])) continue;
                keyboardDisabled[i].Location = new Point(S(8 + shown % 2 * 385), S(44 + shown / 2 * 44));
                shown++;
            }
            keyboardDisableHint.Visible = shown > 0;
            keyboardEmpty[2].Text = kb == null ? missing : Lang.T("這把鍵盤沒有可以停用的按鍵。", "This keyboard cannot disable any keys.");
            keyboardEmpty[2].Visible = shown == 0;
        }

        // ---- Receiver page (Logi Bolt pairing) ----

        // Same order as the macOS edition: the receiver, then "Pair a device", then the paired devices.
        private void BuildReceiverPage(CardPanel page)
        {
            int right = 748;
            int textWidth = right - 130 - 12 - 18;
            receiverTitle.AutoSize = false;
            receiverTitle.AutoEllipsis = true;
            receiverTitle.UseMnemonic = false;
            receiverTitle.TextAlign = ContentAlignment.MiddleLeft;
            receiverTitle.Font = keyboardNameFont;
            receiverTitle.Location = new Point(S(18), S(56));
            receiverTitle.Size = new Size(S(right - 110 - 12 - 18), S(24));
            page.Controls.Add(receiverTitle);
            Button reload = Theme.CreateButton(Lang.T("重新讀取", "Reload"), false);
            reload.Size = new Size(S(110), S(30));
            reload.Location = new Point(S(right - 110), S(52));
            reload.Click += delegate { RefreshReceiver(); };
            page.Controls.Add(reload);
            AddDivider(page, 88, S(right - 18));

            // Pair a device.
            page.Controls.Add(new Label { Text = Lang.T("配對新裝置", "Pair a device"), AutoSize = true, Location = new Point(S(18), S(98)) });
            AddHint(page, Lang.T("按「開始配對」後 30 秒內，長按裝置上要用的 Easy-Switch 頻道鍵，直到燈號快速閃爍。",
                "After Start Pairing, hold the Easy-Switch channel button you want within 30 s until its light blinks fast."), 122);
            AddHint(page, Lang.T("配對會取代那個頻道原本的連線（例如原本以藍牙連到另一台電腦）。",
                "Pairing replaces whatever that channel was connected to before (e.g. Bluetooth to another computer)."), 142);
            pairingStart = Theme.CreateButton(Lang.T("開始配對", "Start Pairing"), true);
            pairingStart.Size = new Size(S(130), S(30));
            pairingStart.Location = new Point(S(right - 130), S(94));
            pairingStart.Click += delegate
            {
                receiverMessage.Text = "";
                if (demoReceiver == 0) bolt.StartPairing();
            };
            page.Controls.Add(pairingStart);
            pairingStatus.AutoSize = false;
            pairingStatus.AutoEllipsis = true;
            pairingStatus.UseMnemonic = false;
            pairingStatus.Location = new Point(S(18), S(166));
            pairingStatus.Size = new Size(S(right - 18), S(22));
            page.Controls.Add(pairingStatus);
            pairingCode.AutoSize = false;
            pairingCode.UseMnemonic = false;
            pairingCode.TextAlign = ContentAlignment.MiddleLeft;
            pairingCode.Font = new Font(Theme.UiFont, 17F, FontStyle.Bold);
            pairingCode.Location = new Point(S(18), S(188));
            pairingCode.Size = new Size(S(textWidth), S(38));
            page.Controls.Add(pairingCode);
            pairingCancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            pairingCancel.Size = new Size(S(110), S(30));
            pairingCancel.Location = new Point(S(right - 110), S(192));
            pairingCancel.Visible = false;
            pairingCancel.Click += delegate
            {
                if (demoReceiver != 0) return;
                if (receiverSnapshot.Active) bolt.CancelPairing();
                else bolt.DismissResult();
            };
            page.Controls.Add(pairingCancel);
            // Paired devices: one panel that sits right under the pairing hints while nothing is going on,
            // and moves down (UpdateReceiverUi) to make room for the status and code while pairing.
            // Panel x=10 so the card's rounded border stays visible; inner x=8 is page x=18.
            receiverPaired = new Panel { Bounds = new Rectangle(S(10), S(234), S(746), S(216)), BackColor = Theme.Card };
            page.Controls.Add(receiverPaired);
            receiverPaired.Controls.Add(new Panel { BackColor = Theme.Divider, Bounds = new Rectangle(S(8), 0, S(right - 18), 1) });
            receiverPaired.Controls.Add(new Label { Text = Lang.T("已配對的裝置", "Paired devices"), AutoSize = true, Location = new Point(S(8), S(12)) });
            receiverCount.AutoSize = true;
            receiverCount.ForeColor = Theme.Muted;
            receiverCount.Font = new Font(Theme.UiFont, 8.5F);
            receiverCount.Location = new Point(S(8), S(36));
            receiverPaired.Controls.Add(receiverCount);
            receiverMessage.AutoSize = false;
            receiverMessage.AutoEllipsis = true;
            receiverMessage.UseMnemonic = false;
            receiverMessage.TextAlign = ContentAlignment.TopRight;
            receiverMessage.ForeColor = Theme.Muted;
            receiverMessage.Font = new Font(Theme.UiFont, 8.5F);
            receiverMessage.Location = new Point(S(190), S(36));
            receiverMessage.Size = new Size(S(right - 200), S(18));
            receiverPaired.Controls.Add(receiverMessage);
            for (int i = 0; i < 6; i++)
            {
                int y = 62 + i * 30;
                receiverRows[i] = new Label
                {
                    AutoSize = false,
                    AutoEllipsis = true,
                    UseMnemonic = false,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font(Theme.UiFont, 9.5F),
                    Location = new Point(S(8), S(y)),
                    Size = new Size(S(right - 110 - 12 - 18), S(26)),
                    Visible = false
                };
                receiverPaired.Controls.Add(receiverRows[i]);
                Button unpair = Theme.CreateButton(Lang.T("解除配對", "Unpair"), false);
                unpair.Size = new Size(S(110), S(26));
                unpair.Location = new Point(S(right - 110 - 10), S(y));
                unpair.Visible = false;
                int row = i;
                unpair.Click += delegate { ConfirmUnpair(row); };
                receiverUnpair[i] = unpair;
                receiverPaired.Controls.Add(unpair);
            }
            UpdateReceiverUi();
        }

        private static double SecondsSinceMouse()
        {
            int elapsed = unchecked(Environment.TickCount - MouseHook.LastEventTick);
            return elapsed < 0 ? double.MaxValue : elapsed / 1000.0;
        }

        private void PostOthers(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch { }
        }

        private void OnOthersReport(string text)
        {
            PostOthers(delegate
            {
                if (liveLog.Checked) AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " " + text);
            });
        }

        private bool HasOriginalProfile(OtherDeviceSnapshot d)
        {
            return File.Exists(new OtherProfileBackups(othersBackupFolder).OriginalPath(d.BackupKey, d.ActiveProfile));
        }

        // --demo-others=N: fake cards for checking the layout (1 connected, 2 needs repair, 3 not responding, 4 two devices,
        // 5 three models, 6 eight devices).
        private static List<OtherDeviceSnapshot> DemoOthers(int kind)
        {
            byte[] profile = new byte[255];
            byte[][] buttons =
            {
                new byte[] { 0x80, 1, 0, 1 }, new byte[] { 0x80, 1, 0, 2 }, new byte[] { 0x80, 1, 0, 4 }, new byte[] { 0x80, 1, 0, 8 },
                new byte[] { 0x80, 1, 0, 0x10 }, new byte[] { 0x80, 1, 0, 0x20 }, new byte[] { 0x90, 4, 0, 0 }, new byte[] { 0x90, 3, 0, 0 },
                new byte[] { 0x80, 1, 0, 0x40 }, new byte[] { 0x90, 2, 0, 0 }, new byte[] { 0x90, 1, 0, 0 }
            };
            for (int i = 0; i < buttons.Length; i++) Array.Copy(buttons[i], 0, profile, 0x20 + i * 4, 4);
            var d = new OtherDeviceSnapshot
            {
                Receiver = "C539@DEMO", Slot = 1, Name = "G502 LIGHTSPEED Wireless Gaming Mouse", Kind = 3, Wpid = "407F",
                BatterySource = OtherBatterySource.Voltage, BatteryPercent = 83, Millivolts = 4016, Firmware = "U11 12.03.B002B",
                Dpi = 1800, DpiMin = 100, DpiMax = 25600, ReportRateMs = 1, OnboardMode = 1, ProfileCount = 3, ActiveProfile = 1,
                StageIndex = 2, DefaultStage = 2, ButtonCount = 11, SectorSize = 255, UnitId = "DEMO0001", LastSeen = DateTime.UtcNow
            };
            d.Profiles = new List<int> { 1, 2, 3 };
            d.ProfileSlots = new List<int> { 1, 2, 3 };
            d.OnboardFormatOK = true;
            d.DpiStages = new List<int> { 800, 1200, 1800, 3200, 6400 };
            d.Buttons = OtherDevicesProtocol.ProfileButtons(profile, 11);
            d.Lights = new List<OtherLightEffect> { OtherLightEffect.Cycle(5000, 100), OtherLightEffect.Fixed(255, 255, 255) };
            var list = new List<OtherDeviceSnapshot> { d };
            if (kind == 2) d.RepairSector = 1;
            if (kind == 3) d.Link = OtherDeviceLink.NoAnswer;
            if (kind == 4)
            {
                OtherDeviceSnapshot second = d.Copy();
                second.Slot = 2;
                second.Link = OtherDeviceLink.ReceiverGone;
                list.Add(second);
            }
            if (kind == 5 || kind == 6)
            {
                string[,] models =
                {
                    { "4087", "G903 LIGHTSPEED Wireless Gaming Mouse" }, { "4074", "G305 LIGHTSPEED Wireless Gaming Mouse" },
                    { "4093", "PRO X SUPERLIGHT Wireless Gaming Mouse" }, { "4085", "G604 LIGHTSPEED Wireless Gaming Mouse" },
                    { "4099", "G502 X PLUS LIGHTSPEED Gaming Mouse" }, { "409D", "G705 Wireless Gaming Mouse" }, { "4070", "G703 LIGHTSPEED Wireless Gaming Mouse" }
                };
                int count = kind == 5 ? 2 : models.GetLength(0);
                for (int i = 0; i < count; i++)
                {
                    OtherDeviceSnapshot more = d.Copy();
                    more.Slot = i + 2;
                    more.Wpid = models[i, 0];
                    more.Name = models[i, 1];
                    more.BatteryPercent = 71 - i * 8;
                    if (i == 1) more.Link = OtherDeviceLink.NoAnswer;
                    list.Add(more);
                }
            }
            return list;
        }

        private void RefreshReceiver()
        {
            if (demoReceiver != 0) receiverSnapshot = DemoReceiver(demoReceiver);
            else bolt.Refresh();
            UpdateReceiverUi();
        }

        private void OnBoltReport(string text)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new Action(delegate
                {
                    if (liveLog.Checked) AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Bolt " + text);
                }));
            }
            catch { }
        }

        private void OnBoltChanged(BoltReceiverSnapshot snapshot)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new Action(delegate
                {
                    if (demoReceiver != 0) return;
                    receiverSnapshot = snapshot;
                    UpdateReceiverUi();
                }));
            }
            catch { }
        }

        private static string BoltKindName(int kind)
        {
            switch (kind)
            {
                case 1: return Lang.T("鍵盤", "Keyboard");
                case 2: return Lang.T("滑鼠", "Mouse");
                case 3: return Lang.T("數字鍵盤", "Numpad");
                case 4: return Lang.T("簡報器", "Presenter");
                case 8: return Lang.T("軌跡球", "Trackball");
                case 9: return Lang.T("觸控板", "Touchpad");
                default: return Lang.T("裝置", "Device");
            }
        }

        private static string BoltDisplayName(BoltSlot slot)
        {
            return slot.Name.Length > 0 ? slot.Name : BoltKindName(slot.Kind);
        }

        private void UpdateReceiverUi()
        {
            if (pairingStart == null) return;
            BoltReceiverSnapshot s = receiverSnapshot;
            receiverTitle.Text = s.Multiple ? Lang.T("偵測到多個 Logi Bolt 接收器，請只留一個再操作", "More than one Logi Bolt receiver found; leave only one plugged in")
                : s.Present ? "Logi Bolt"
                : Lang.T("沒有偵測到 Logi Bolt 接收器，插上後按「重新讀取」", "No Logi Bolt receiver found; plug it in and click Reload");
            receiverCount.Text = !s.Present ? Lang.T("這裡的配對只支援 Logi Bolt；其他接收器的裝置請看「其他裝置」頁", "Pairing here is Logi Bolt only; devices on other receivers are on the Other Devices page")
                : s.Slots.Count == 0 ? Lang.T("這個接收器還沒有配對任何裝置", "No devices are paired with this receiver yet")
                : Lang.T("已使用 ", "") + s.Slots.Count + Lang.T("／6 個槽位", " of 6 slots in use");
            for (int i = 0; i < 6; i++)
            {
                bool shown = i < s.Slots.Count;
                receiverRows[i].Visible = shown;
                receiverUnpair[i].Visible = shown;
                if (!shown) continue;
                BoltSlot slot = s.Slots[i];
                receiverRows[i].Text = Lang.T("槽位 ", "Slot ") + slot.Slot + "　" + BoltDisplayName(slot) + "　—　" +
                    BoltKindName(slot.Kind) + " · WPID " + slot.Wpid;
                receiverUnpair[i].Enabled = !s.Active && !s.Multiple;
            }

            bool full = s.Slots.Count >= 6;
            pairingStart.Text = s.Phase == BoltPhase.Failed ? Lang.T("再試一次", "Try Again") : Lang.T("開始配對", "Start Pairing");
            pairingStart.Enabled = s.Present && !s.Multiple && !s.Active && !full;
            pairingCancel.Visible = s.Phase != BoltPhase.Idle;
            pairingCancel.Text = s.Active ? Lang.T("取消", "Cancel")
                : s.Phase == BoltPhase.Succeeded ? Lang.T("完成", "Done") : Lang.T("關閉", "Close");
            pairingStatus.ForeColor = s.Phase == BoltPhase.Failed ? Color.FromArgb(196, 43, 28)
                : s.Phase == BoltPhase.Succeeded ? Color.FromArgb(16, 124, 16) : ForeColor;
            pairingCode.Text = "";
            string name = s.DeviceName.Length > 0 ? s.DeviceName : Lang.T("裝置", "the device");
            switch (s.Phase)
            {
                case BoltPhase.Idle:
                    pairingStatus.Text = s.Present && full
                        ? Lang.T("6 個槽位都用完了，請先解除一個裝置的配對。", "All 6 slots are in use; unpair a device first.")
                        : "";
                    break;
                case BoltPhase.Searching:
                    pairingStatus.Text = Lang.T("正在尋找裝置… 請長按要配對的頻道鍵，直到燈號快速閃爍。", "Looking for a device… Hold the channel button until its light blinks fast.");
                    break;
                case BoltPhase.Pairing:
                    pairingStatus.Text = Lang.T("找到「" + name + "」，正在配對…", "Found " + name + "; pairing…");
                    break;
                case BoltPhase.Passkey:
                    if (s.KeyboardPasskey)
                    {
                        pairingStatus.Text = Lang.T("在「" + name + "」上輸入下面的數字，再按 Enter：", "On " + name + ", type these digits and press Enter:");
                        pairingCode.Text = String.Join("  ", Array.ConvertAll(s.Code.ToCharArray(), c => c.ToString()));
                    }
                    else
                    {
                        pairingStatus.Text = Lang.T("在「" + name + "」上依序按下面的按鍵，最後左右鍵一起按：", "On " + name + ", click these buttons in order, then press left and right together:");
                        bool[] sequence = BoltPackets.MouseSequence(s.Code);
                        var text = new StringBuilder();
                        foreach (bool rightButton in sequence)
                            text.Append(rightButton ? Lang.T("右 ", "R ") : Lang.T("左 ", "L "));
                        text.Append(Lang.T("＋ 左右一起", "+ both"));
                        pairingCode.Text = text.ToString();
                    }
                    break;
                case BoltPhase.Succeeded:
                    pairingStatus.Text = Lang.T("已配對「" + name + "」（槽位 " + s.PairedSlot + "）。要切回其他電腦時，按裝置上的 Easy-Switch 鍵。",
                        "Paired " + name + " (slot " + s.PairedSlot + "). Press its Easy-Switch button to go back to other computers.");
                    break;
                case BoltPhase.Failed:
                    pairingStatus.Text = BoltFailureText(s.Failure);
                    break;
            }
            bool quiet = s.Phase == BoltPhase.Idle && pairingStatus.Text.Length == 0;
            pairingStatus.Visible = pairingCode.Visible = !quiet;
            if (receiverPaired != null) receiverPaired.Top = S(quiet ? 166 : 234);
        }

        private static string BoltFailureText(BoltFailure failure)
        {
            switch (failure)
            {
                case BoltFailure.NoAnswer:
                    return Lang.T("接收器沒有接受配對指令。如果開著 Logi Options+ 或 Logi Bolt，請先結束再試。",
                        "The receiver did not accept the pairing command. Quit Logi Options+ or Logi Bolt and try again.");
                case BoltFailure.NotFound:
                    return Lang.T("30 秒內沒有找到要配對的裝置。請長按頻道鍵，直到燈號快速閃爍。",
                        "No device asked to pair within 30 seconds. Hold the channel button until the light blinks fast.");
                case BoltFailure.Timeout:
                    return Lang.T("驗證沒有在時間內完成。", "Authentication was not completed in time.");
                default:
                    return Lang.T("配對沒有完成，可能是驗證碼按錯了。請再試一次。", "Pairing did not complete; the code may have been entered wrong. Try again.");
            }
        }

        private void ConfirmUnpair(int row)
        {
            if (row >= receiverSnapshot.Slots.Count || receiverSnapshot.Active) return;
            BoltSlot slot = receiverSnapshot.Slots[row];
            string name = BoltDisplayName(slot);
            string warning = Lang.T("裝置會從這個接收器斷線，要再用就得重新配對。", "The device disconnects from this receiver and has to be paired again to use it.");
            if (slot.Kind == 2 || slot.Kind == 8 || slot.Kind == 9)
                warning += Lang.T("\n如果這是你正在用的滑鼠，解除後游標會立刻不能動。", "\nIf this is the mouse you are using, the pointer stops moving right away.");
            else if (slot.Kind == 1)
                warning += Lang.T("\n如果這是你正在用的鍵盤，解除後會立刻不能打字。", "\nIf this is the keyboard you are using, typing stops right away.");
            if (MessageBox.Show(this, warning, Lang.T("解除「" + name + "」的配對？", "Unpair " + name + "?"),
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (demoReceiver != 0) return;
            receiverMessage.Text = Lang.T("解除配對中…", "Unpairing…");
            bolt.Unpair(slot, delegate (int result)
            {
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        receiverMessage.Text = result == 1 ? Lang.T("已解除「" + name + "」的配對", "Unpaired " + name)
                            : result == 2 ? Lang.T("槽位 " + slot.Slot + " 的裝置已經變了，沒有解除；請看過清單再試", "Slot " + slot.Slot + " holds a different device now; nothing was unpaired. Check the list and try again")
                            : Lang.T("接收器沒有確認，「" + name + "」仍然配對著", "The receiver did not confirm; " + name + " is still paired");
                    }));
                }
                catch { }
            });
        }

        // --demo-receiver=N: a fake receiver for screenshots or checking the layout
        // (1 normal, 2 searching, 3 keyboard passkey, 4 mouse passkey, 5 success, 6 failure).
        private static BoltReceiverSnapshot DemoReceiver(int kind)
        {
            var s = new BoltReceiverSnapshot { Present = true };
            s.Slots.Add(new BoltSlot { Slot = 1, Kind = 2, Wpid = "B042", Name = "MX Master 4" });
            s.Slots.Add(new BoltSlot { Slot = 2, Kind = 1, Wpid = "B367", Name = "MX MCHNCL M" });
            switch (kind)
            {
                case 2: s.Phase = BoltPhase.Searching; break;
                case 3: s.Phase = BoltPhase.Passkey; s.Code = "482913"; s.DeviceName = "MX Keys S"; s.KeyboardPasskey = true; break;
                case 4: s.Phase = BoltPhase.Passkey; s.Code = "000613"; s.DeviceName = "MX Anywhere 3S"; break;
                case 5:
                    s.Phase = BoltPhase.Succeeded; s.PairedSlot = 3; s.DeviceName = "MX Keys S";
                    s.Slots.Add(new BoltSlot { Slot = 3, Kind = 1, Wpid = "B378", Name = "MX Keys S" });
                    break;
                case 6: s.Phase = BoltPhase.Failed; s.Failure = BoltFailure.NotFound; break;
            }
            return s;
        }

        private void OnKeyboardReport(string text)
        {
            try
            {
                if (!IsHandleCreated || IsDisposed) return;
                BeginInvoke(new Action(delegate
                {
                    if (liveLog.Checked) AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Keyboard " + text);
                }));
            }
            catch { }
        }

        // Reads the keyboard on a worker thread (searching for one if needed) and updates the page.
        private void RefreshKeyboard(bool force)
        {
            if (Interlocked.Exchange(ref keyboardBusy, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                KeyboardSnapshot read = null;
                try { read = keyboard.Refresh(force); }
                catch { }
                Interlocked.Exchange(ref keyboardBusy, 0);
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        // A periodic search that found nothing changes nothing.
                        if (read == null && keyboardSnapshot == null) return;
                        SetKeyboardSnapshot(read);
                    }));
                }
                catch { }
            });
        }

        private void SetKeyboardSnapshot(KeyboardSnapshot snapshot)
        {
            keyboardSnapshot = snapshot;
            RefreshKeyboardUi();
            UpdateKeyboardChip();
        }

        // Bottom-left chip: a second row with the keyboard's battery while a keyboard is connected; the
        // chip grows upwards into the free space under the navigation.
        private void UpdateKeyboardChip()
        {
            KeyboardSnapshot kb = keyboardSnapshot;
            chip.KeyboardPercent = kb != null ? kb.BatteryPercent : -1;
            chip.KeyboardCharging = kb != null && kb.Charging;
            chip.KeyboardText = kb == null ? "" : kb.Transport == "BOLT" ? "Logi Bolt" : "Bluetooth";
            LayoutChip();
        }

        // Bottom-left chip: a row for the first connected device on a Lightspeed receiver.
        private void UpdateOtherChip(List<OtherDeviceSnapshot> list)
        {
            OtherDeviceSnapshot first = null;
            int count = 0;
            foreach (OtherDeviceSnapshot d in list)
            {
                if (d.Link != OtherDeviceLink.Online || d.BatteryPercent < 0) continue;
                if (first == null) first = d;
                count++;
            }
            chipOtherId = first != null ? first.Id : null;
            chip.OtherPercent = first != null ? first.BatteryPercent : -1;
            chip.OtherCharging = first != null && first.Charging;
            chip.OtherName = first == null ? "" : OtherText.ShortName(first) + (count > 1 ? " +" + (count - 1) : "");
            LayoutChip();
        }

        // The chip grows upwards into the free space under the navigation.
        private void LayoutChip()
        {
            int rows = chip.RowCount;
            int height = rows == 1 ? 36 : rows == 2 ? 72 : 82;
            chip.Bounds = new Rectangle(S(24), S(602 - height), S(202), S(height));
            chip.Invalidate();
        }

        // Writes one keyboard setting on a worker thread; the keyboard confirms it by reading back.
        private void WriteKeyboard(string name, Func<bool> write)
        {
            status.Text = Lang.T("套用中…", "Applying…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                KeyboardSnapshot read = null;
                try { ok = write(); }
                catch { }
                try { read = keyboard.Refresh(false); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        if (liveLog.Checked) AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Keyboard set " + name + " verified=" + ok);
                        status.Text = name + (ok ? Lang.T("：已套用", ": applied") : Lang.T("：鍵盤沒有確認，未套用", ": not confirmed by the keyboard"));
                        SetKeyboardSnapshot(read);
                    }));
                }
                catch { }
            });
        }

        private void ApplyKeyboardBacklight()
        {
            KeyboardSnapshot kb = keyboardSnapshot;
            int index = keyboardBacklight.SelectedIndex;
            if (kb == null || index < 0 || index >= keyboardBacklightChoices.Count) return;
            int choice = keyboardBacklightChoices[index];
            bool enabled = choice != -1;
            int mode = enabled ? choice : kb.BacklightMode;
            // Off reports brightness 0; switching to manual then uses the middle step so it visibly lights.
            int level = kb.BacklightLevel >= 1 ? kb.BacklightLevel : Math.Max(1, kb.BacklightLevels / 2);
            WriteKeyboard(Lang.T("背光", "Backlight"), delegate { return keyboard.SetBacklight(enabled, mode, level); });
        }

        private string KeyboardHostTitle(int index)
        {
            string channel = String.Format(Lang.T("頻道 {0}", "Channel {0}"), index + 1);
            HostEntry entry = keyboardSnapshot == null ? null : keyboardSnapshot.Hosts.Find(delegate(HostEntry h) { return h.Index == index; });
            return entry == null || entry.Name.Length == 0 ? channel : channel + " · " + entry.Name;
        }

        // The keyboard channel that goes with a mouse channel; -1 means the keyboard stays.
        private int KeyboardChannelFor(int mouseChannel)
        {
            int chosen = mouseChannel >= 0 && mouseChannel < keyboardChannels.Length ? keyboardChannels[mouseChannel] : -2;
            return chosen != -2 ? chosen : AutoKeyboardChannel(mouseChannel);
        }

        // Automatic: the keyboard channel stored with the same computer name (and the same connection
        // when several match), otherwise the same channel number. Channels paired through a Bolt receiver
        // usually carry the receiver's name on the keyboard, so those fall back to the number.
        private int AutoKeyboardChannel(int mouseChannel)
        {
            KeyboardSnapshot kb = keyboardSnapshot;
            if (kb == null || kb.Hosts.Count == 0) return mouseChannel;
            HostEntry mouseHost = hosts.Find(delegate(HostEntry h) { return h.Index == mouseChannel; });
            if (mouseHost != null && mouseHost.Name.Length > 0)
            {
                List<HostEntry> same = kb.Hosts.FindAll(delegate(HostEntry h) { return h.Paired && h.Name == mouseHost.Name; });
                if (same.Count > 1) same = same.FindAll(delegate(HostEntry h) { return h.Bus == mouseHost.Bus; });
                if (same.Count == 1) return same[0].Index;
            }
            HostEntry byNumber = kb.Hosts.Find(delegate(HostEntry h) { return h.Index == mouseChannel; });
            return byNumber != null && byNumber.Paired ? mouseChannel : -1;
        }

        private void RefreshKeyboardUi()
        {
            if (keyboardRowTargets[0] == null) return;
            KeyboardSnapshot kb = keyboardSnapshot;
            loadingKeyboard = true;
            try
            {
                keyboardName.Text = kb == null
                    ? Lang.T("沒有偵測到連到這台電腦的 Logitech 鍵盤（切到別台、睡眠時也會這樣；按一下鍵盤再重新讀取）",
                        "No Logitech keyboard on this computer (also while elsewhere or asleep; press a key and reload)")
                    : (kb.Name.Length == 0 ? Lang.T("Logitech 鍵盤", "Logitech keyboard") : kb.Name) +
                      (kb.Asleep ? Lang.T("　沒有回應（休眠中或在別台電腦，按一下鍵盤就會恢復）", "  not answering (asleep or elsewhere; press a key)") : "");
                keyboardName.Font = kb == null ? keyboardMissingFont : keyboardNameFont;
                if (kb == null) keyboardName.ForeColor = Theme.Muted;
                else keyboardName.ResetForeColor();
                keyboardConnection.Text = kb == null ? "—" : kb.Transport == "BOLT" ? "Logi Bolt" : "Bluetooth";
                keyboardBattery.Text = kb == null || kb.BatteryPercent < 0 ? "—"
                    : kb.BatteryPercent + "%" + (kb.Charging ? Lang.T("（充電中）", " (charging)") : "");
                keyboardFirmware.Text = kb == null || kb.Firmware.Length == 0 ? "—" : kb.Firmware;

                WantKeyboard(keyboardLightGroup, kb != null && kb.BacklightPresent);
                WantKeyboard(keyboardFnGroup, kb != null && kb.FnPresent);
                WantKeyboard(keyboardOsGroup, kb != null && kb.Platforms.Count > 0);
                keyboardPlatform.Items.Clear();
                if (kb != null)
                {
                    foreach (SolaarFeatures.Platform platform in kb.Platforms)
                    {
                        keyboardPlatform.Items.Add(platform);
                        if (platform.Id == kb.CurrentPlatform) keyboardPlatform.SelectedItem = platform;
                    }
                }
                keyboardOsGroup.Enabled = kb != null && !kb.Asleep;
                for (int i = 0; i < 8; i++)
                {
                    WantKeyboard(keyboardDisabled[i], kb != null && (kb.DisabledKeysMask & (1 << i)) != 0);
                    keyboardDisabled[i].Enabled = kb != null && !kb.Asleep;
                    keyboardDisabled[i].Checked = kb != null && (kb.DisabledKeys & (1 << i)) != 0;
                }
                for (int i = 0; i < 3; i++)
                {
                    bool present = kb != null && kb.BacklightDelays.Length == 3;
                    WantKeyboard(keyboardDelayRows[i], present);
                    keyboardDelayRows[i].Enabled = present && !kb.Asleep;
                    if (present) keyboardDelays[i].Value = Math.Max(1, Math.Min(600, kb.BacklightDelays[i]));
                }
                keyboardBacklight.Items.Clear();
                keyboardBacklightChoices.Clear();
                if (kb != null && kb.BacklightPresent)
                {
                    if (kb.BacklightAuto) { keyboardBacklight.Items.Add(Lang.T("自動", "Auto")); keyboardBacklightChoices.Add(1); }
                    if (kb.BacklightManual) { keyboardBacklight.Items.Add(Lang.T("手動", "Manual")); keyboardBacklightChoices.Add(3); }
                    if (kb.BacklightEnabled && kb.BacklightMode != 1 && kb.BacklightMode != 3)
                    {
                        keyboardBacklight.Items.Add(Lang.T("鍵盤預設", "Keyboard default"));
                        keyboardBacklightChoices.Add(kb.BacklightMode);
                    }
                    keyboardBacklight.Items.Add(Lang.T("關閉", "Off"));
                    keyboardBacklightChoices.Add(-1);
                    keyboardBacklight.SelectedIndex = keyboardBacklightChoices.IndexOf(kb.BacklightEnabled ? kb.BacklightMode : -1);
                }
                keyboardBacklight.Enabled = kb != null && kb.BacklightPresent && !kb.Asleep;   // writes cannot reach a sleeping keyboard
                bool manual = kb != null && kb.BacklightPresent && kb.BacklightEnabled && kb.BacklightMode == 3 && kb.BacklightLevels > 1;
                WantKeyboard(keyboardLevelRow, manual);
                keyboardLevel.Enabled = kb != null && !kb.Asleep;
                if (manual)
                {
                    keyboardLevel.Maximum = kb.BacklightLevels - 1;
                    keyboardLevel.Value = kb.BacklightLevel;
                    keyboardLevelValue.Text = keyboardLevel.Value.ToString();
                }

                keyboardTopRow.Enabled = kb != null && kb.FnPresent && !kb.Asleep;
                keyboardTopRow.SelectedIndex = kb != null && kb.FnPresent ? (kb.FnMediaFirst ? 0 : 1) : -1;

                int keyboardCount = kb != null && kb.Hosts.Count > 0 ? kb.Hosts.Count : 3;
                for (int i = 0; i < 3; i++)
                {
                    keyboardRowLabels[i].Text = Lang.T("滑鼠 ", "Mouse ") + HostTitle(i) + "  →";
                    ComboBox combo = keyboardRowTargets[i];
                    combo.Items.Clear();
                    int auto = AutoKeyboardChannel(i);
                    combo.Items.Add(auto < 0 ? Lang.T("自動（鍵盤不切換）", "Auto (keyboard stays)") : Lang.T("自動：", "Auto: ") + KeyboardHostTitle(auto));
                    for (int k = 0; k < keyboardCount; k++) combo.Items.Add(Lang.T("鍵盤 ", "Keyboard ") + KeyboardHostTitle(k));
                    combo.Items.Add(Lang.T("鍵盤不切換", "Keyboard stays"));
                    int chosen = keyboardChannels[i];
                    combo.SelectedIndex = chosen == -2 ? 0 : chosen == -1 ? combo.Items.Count - 1 : Math.Min(chosen + 1, combo.Items.Count - 2);
                    combo.Enabled = keyboardFollow.Checked;
                }
                LayoutKeyboardTabs(kb);
            }
            finally { loadingKeyboard = false; }
        }

        // Battery page: the level the mouse reports, the low-battery alert and its threshold
        // (also used by the haptics pause on the Haptics page).
        private void BuildBatteryPage(CardPanel page)
        {
            int right = 748;
            batteryLevel.AutoSize = true;
            batteryLevel.Font = new Font(Theme.UiFont, 20F, FontStyle.Bold);
            batteryLevel.Location = new Point(S(16), S(52));
            batteryLevel.Text = "—";
            page.Controls.Add(batteryLevel);
            batteryDetail.AutoSize = false;
            batteryDetail.AutoEllipsis = true;
            batteryDetail.UseMnemonic = false;
            batteryDetail.ForeColor = Theme.Muted;
            batteryDetail.TextAlign = ContentAlignment.MiddleLeft;
            batteryDetail.Location = new Point(S(130), S(60));
            batteryDetail.Size = new Size(S(440), S(24));
            batteryDetail.Text = Lang.T("尚未讀到電量", "No battery reading yet");
            page.Controls.Add(batteryDetail);
            Button reload = Theme.CreateButton(Lang.T("重新讀取", "Reload"), false);
            reload.Size = new Size(S(110), S(30));
            reload.Location = new Point(S(right - 110), S(58));
            reload.Click += delegate { RefreshBattery(); };
            page.Controls.Add(reload);
            AddHint(page, Lang.T("電量由滑鼠回報，每 10 分鐘讀取一次；開啟本頁或按「重新讀取」時也會讀取",
                "Reported by the mouse and read every 10 minutes, and whenever this page opens or Reload is pressed"), 100);
            AddDivider(page, 130, S(right - 18));

            lowBatteryAlert.Text = Lang.T("低電量提醒", "Low-battery alert");
            lowBatteryAlert.Checked = true;
            lowBatteryAlert.Location = new Point(S(18), S(144));
            lowBatteryAlert.Size = new Size(S(right - 18), S(26));
            page.Controls.Add(lowBatteryAlert);
            AddHint(page, Lang.T("電量降到門檻且沒有在充電時，從系統匣跳出一次通知；充電後再降下來才會再提醒",
                "One tray notification when the level drops to the threshold while not charging; it alerts again only after charging"), 174);
            AddDivider(page, 206, S(right - 18));

            page.Controls.Add(new Label { Text = Lang.T("電量低的門檻", "Low-battery threshold"), AutoSize = true, Location = new Point(S(18), S(220)) });
            AddHint(page, Lang.T("低於門檻且沒有在充電時才算電量低", "Low means at or below the threshold while not charging"), 244);
            lowBatteryLevel.DropDownStyle = ComboBoxStyle.DropDownList;
            foreach (int level in LowBatteryLevels) lowBatteryLevel.Items.Add(level + "%");
            lowBatteryLevel.SelectedIndex = Array.IndexOf(LowBatteryLevels, 15);
            lowBatteryLevel.Location = new Point(S(right - 120), S(224));
            lowBatteryLevel.Width = S(120);
            page.Controls.Add(lowBatteryLevel);
            AddDivider(page, 278, S(right - 18));

            page.Controls.Add(new Label { Text = Lang.T("電量低時暫停震動", "Pause haptics on low battery"), AutoSize = true, Location = new Point(S(18), S(292)) });
            AddHint(page, Lang.T("同一個門檻；開關在「觸覺回饋」頁，「試震」不受影響", "Uses the same threshold; the switch is on the Haptics page, and Try still plays"), 316);
            var hapticsLink = new LinkLabel
            {
                Text = Lang.T("在「觸覺回饋」頁設定 →", "Set it on the Haptics page →"),
                AutoSize = true,
                LinkColor = Theme.Accent,
                ActiveLinkColor = Theme.AccentDown,
                LinkBehavior = LinkBehavior.HoverUnderline,
                UseMnemonic = false,
                Font = new Font(Theme.UiFont, 9F),
                Location = new Point(S(right - 200), S(296))
            };
            hapticsLink.LinkClicked += delegate { ShowPage(3); };
            page.Controls.Add(hapticsLink);
        }

        // The battery page's level and state, from the last good reading.
        private void UpdateBatteryPage(DeviceSnapshot snapshot)
        {
            if (chip.BatteryPercent < 0)
            {
                batteryLevel.Text = "—";
                batteryDetail.Text = snapshot == null ? Lang.T("滑鼠沒有連線", "Mouse not connected") : Lang.T("尚未讀到電量", "No battery reading yet");
                return;
            }
            batteryLevel.Text = chip.BatteryPercent + "%";
            batteryLevel.ForeColor = BatteryLow ? Theme.Warning : Theme.Text;
            var parts = new List<string>();
            parts.Add(chip.Charging ? Lang.T("充電中", "Charging") : Lang.T("未充電", "Not charging"));
            if (BatteryLow) parts.Add(Lang.T("電量低", "Low"));
            if (HapticsPaused) parts.Add(Lang.T("震動已暫停", "Haptics paused"));
            if (snapshot == null) parts.Add(Lang.T("滑鼠目前沒有連線，顯示上一次讀到的值", "mouse not connected; last reading shown"));
            else parts.Add(snapshot.Transport == "BOLT" ? "Logi Bolt" : "Bluetooth");
            batteryDetail.Text = String.Join(" · ", parts.ToArray());
        }

        // One haptic moment: name and description on the left; waveform, try and on/off on the right.
        private void AddHapticRow(CardPanel page, CheckBox toggle, ComboBox wave, string title, string hint, int y, int defaultWave)
        {
            int right = 748;
            if (y > 214) page.Controls.Add(new Panel { BackColor = Theme.Divider, Location = new Point(S(18), S(y - 8)), Size = new Size(S(right - 18), 1) });
            page.Controls.Add(new Label { Text = title, AutoSize = true, UseMnemonic = false, Location = new Point(S(18), S(y + 2)) });
            AddHint(page, hint, y + 26);
            toggle.Text = "";
            toggle.Checked = true;
            toggle.Location = new Point(S(right - 46), S(y + 6));
            toggle.Size = new Size(S(46), S(26));
            page.Controls.Add(toggle);
            Button tryButton = Theme.CreateButton(Lang.T("試震", "Try"), false);
            tryButton.Size = new Size(S(72), S(28));
            tryButton.Location = new Point(S(right - 46 - 12 - 72), S(y + 5));
            tryButton.Click += delegate { PlayWave(wave); };
            page.Controls.Add(tryButton);
            hapticTryButtons[toggle] = tryButton;
            wave.DropDownStyle = ComboBoxStyle.DropDownList;
            wave.Items.AddRange(HapticWaveNames());
            wave.SelectedIndex = defaultWave;
            wave.Width = S(170);
            wave.Location = new Point(S(right - 46 - 12 - 72 - 8 - 170), S(y + 6));
            page.Controls.Add(wave);
        }

        private static object[] HapticWaveNames()
        {
            return new object[]
            {
                Lang.T("極輕", "Whisper"), Lang.T("輕微", "Subtle"), Lang.T("柔和", "Soft"),
                Lang.T("柔和碰撞", "Soft bump"), Lang.T("清脆", "Crisp"), Lang.T("清脆碰撞", "Crisp bump")
            };
        }

        private void AddWrappedHint(Control parent, string text, int x, int y, int width, int height)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = false,
                UseMnemonic = false,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(x), S(y)),
                Size = new Size(S(width), S(height))
            });
        }

        private void ShowPage(int index)
        {
            if (index == 2) ShowDevicePage();
            if (index == 3) ReadHapticStrength();
            if (index == 5) RefreshBattery();
            if (index == 4) RefreshHosts();
            if (index == 6) RefreshKeyboard(true);
            if (index == 7) RefreshReceiver();
            if (index == 8 && demoOthers == 0) others.Refresh();
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navigation[i].Selected = i == index;
            }
        }

        // The pointer/wheel page hosts the device editor directly inside the card.
        private void ShowDevicePage()
        {
            if (hidpp == null) return;
            if (deviceEditor == null)
            {
                deviceEditor = new DeviceDialog(hidpp, LogDeviceLines, deviceSettings, delegate
                {
                    SaveSettings();
                    RefreshBattery();
                });
                deviceEditor.NormalDpi = delegate { return precisionActive ? precisionNormalDpi : 0; };
                deviceEditor.WriteDpi = WriteManualDpi;
                deviceEditor.DpiApplied += ManualDpiApplied;
                deviceEditor.Embed(devicePage, new Point(S(12), S(10)));
            }
            deviceEditor.Reload();
        }

        private void UpdateRingPreview()
        {
            Image old = ringPreview.Image;
            ringPreview.Image = actionRing.RenderFrame();
            if (old != null) old.Dispose();
        }

        private void ClearEvents()
        {
            eventLog.Items.Clear();
            if (liveLog.Checked)
            {
                try { File.WriteAllText(diagnosticLogPath, "MX Master Win diagnostic capture\r\n", Encoding.UTF8); }
                catch { }
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Capture cleared; waiting for input...");
            }
        }

        private void ShowDiagnostics()
        {
            if (!diagnostics.Visible) diagnostics.Show();
            if (diagnostics.WindowState == FormWindowState.Minimized) diagnostics.WindowState = FormWindowState.Normal;
            diagnostics.Activate();
        }

        private void HideToTray()
        {
            Hide();
            if (trayHintShown) return;
            // Shown once ever, so the user knows where the window went.
            trayHintShown = true;
            SaveSettings();
            try { tray.ShowBalloonTip(4000, "MX Master Win", Lang.T("程式仍在背景執行。可從系統匣圖示開啟設定或結束。", "Still running in the background. Use the tray icon to open settings or exit."), ToolTipIcon.Info); }
            catch { }
        }

        private void SetDeviceStatus(string text, bool good)
        {
            status.Text = text;
            statusDot.ForeColor = good ? Theme.Good : Theme.Warning;
        }

        private int S(int value)
        {
            return (int)Math.Round(value * uiScale);
        }

        private void AddHint(Control parent, string text, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(18), S(y))
            });
        }

        // One labelled row of the ring appearance settings; slider rows also get a percent readout.
        private void AddRingOption(Control parent, string text, int y, SliderBar slider)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = false, Location = new Point(S(18), S(y + 2)), Size = new Size(S(84), S(22)) });
            if (slider == null) return;
            slider.Maximum = 100;
            slider.Value = slider == ringOpacity ? 100 : 50;
            slider.Location = new Point(S(104), S(y));
            slider.Size = new Size(S(186), S(26));
            var readout = new Label
            {
                Text = slider.Value + "%",
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(S(294), S(y + 1)),
                Size = new Size(S(48), S(22))
            };
            slider.ValueChanged += delegate { readout.Text = slider.Value + "%"; };
            parent.Controls.Add(slider);
            parent.Controls.Add(readout);
        }

        // Percent settings to multipliers. 50% is the default size; the bubble scale matches the macOS version.
        // 2026-10-17: the overall size base was 0.78; the user's 44% (0.78 × 0.94) became the new 50%.
        private void ApplyRingAppearance()
        {
            float size = 0.7332F * (0.5F + ringSize.Value / 100F);
            float bubble = bubbleSize.Value <= 50
                ? 0.6F + 0.2F * (bubbleSize.Value / 50F)
                : 0.8F + 0.225F * ((bubbleSize.Value - 50) / 50F);
            bubbleSize.Enabled = ringStyle.SelectedIndex == 1;
            actionRing.Configure(ringStyle.SelectedIndex == 1, ringColor.SelectedIndex == 1, size, bubble, ringOpacity.Value / 100F);
            UpdateRingPreview();
        }

        // A haptic moment: its switch enables the waveform list and try button; picking a waveform
        // plays it once so it can be felt straight away.
        private void WireHaptic(CheckBox toggle, ComboBox wave)
        {
            toggle.CheckedChanged += delegate
            {
                UpdateHapticControls();
                SaveSettings();
            };
            wave.SelectedIndexChanged += delegate
            {
                PlayWave(wave);
                SaveSettings();
            };
        }

        private void UpdateHapticControls()
        {
            foreach (var pair in hapticTryButtons) pair.Value.Enabled = pair.Key.Checked;
            ringHapticWave.Enabled = ringHaptic.Checked;
            gestureHapticWave.Enabled = gestureHaptic.Checked;
            flowHapticWave.Enabled = flowHaptic.Checked;
            modeHapticWave.Enabled = modeHaptic.Checked;
        }

        private int WaveId(ComboBox wave)
        {
            return HapticWaves[Math.Max(0, Math.Min(HapticWaves.Length - 1, wave.SelectedIndex))];
        }

        private void PlayWave(ComboBox wave)
        {
            if (hidpp == null) return;
            hidpp.PlayHaptic((byte)WaveId(wave));
        }

        // Automatic taps; the Try buttons and the waveform menus call PlayWave directly and still play.
        private void PlayHaptic(CheckBox toggle, ComboBox wave)
        {
            if (toggle.Checked && !HapticsPaused) PlayWave(wave);
        }

        private int LowBatteryThreshold
        {
            get { return LowBatteryLevels[Math.Max(0, Math.Min(LowBatteryLevels.Length - 1, lowBatteryLevel.SelectedIndex))]; }
        }

        // Below the threshold and not charging (the last level the mouse reported).
        private bool BatteryLow
        {
            get { return chip.BatteryPercent >= 0 && !chip.Charging && chip.BatteryPercent <= LowBatteryThreshold; }
        }

        private bool HapticsPaused
        {
            get { return lowBatteryHapticPause.Checked && BatteryLow; }
        }

        private void UpdateHapticPauseNote()
        {
            if (HapticsPaused)
            {
                hapticPauseNote.ForeColor = Theme.Warning;
                hapticPauseNote.Text = String.Format(Lang.T("電量 {0}%，震動已暫停；充電後恢復，「試震」仍會震",
                    "Battery {0}%: taps paused until it charges; Try still plays"), chip.BatteryPercent);
            }
            else
            {
                hapticPauseNote.ForeColor = Theme.Muted;
                hapticPauseNote.Text = String.Format(Lang.T("電量 {0}% 以下且沒有充電時，下方的震動都不播放",
                    "Below {0}% and not charging, none of the taps below play"), LowBatteryThreshold);
            }
        }

        // Called whenever a battery level arrives. One notification per discharge: it is re-armed
        // by charging or by climbing 3% above the threshold.
        private void BatteryChanged()
        {
            UpdateHapticPauseNote();
            int percent = chip.BatteryPercent;
            if (percent < 0) return;
            if (chip.Charging || percent > LowBatteryThreshold + 3)
            {
                lowBatteryNotified = false;
                return;
            }
            // While the alert is off nothing is marked as notified, so turning it on still alerts once.
            if (percent > LowBatteryThreshold || lowBatteryNotified || !lowBatteryAlert.Checked) return;
            lowBatteryNotified = true;
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Battery low: " + percent + "% (threshold " + LowBatteryThreshold + "%)" + (HapticsPaused ? "; haptics paused" : ""));
            try
            {
                tray.ShowBalloonTip(8000, Lang.T("MX Master 4 電量低", "MX Master 4 battery low"),
                    String.Format(Lang.T("剩 {0}%，請盡快充電。", "{0}% left. Charge it soon."), percent)
                    + (HapticsPaused ? Lang.T("充電前震動會暫停。", " Haptics are paused until it charges.") : ""), ToolTipIcon.Warning);
            }
            catch { }
        }

        private void BatterySettingsChanged()
        {
            if (chip.BatteryPercent > LowBatteryThreshold + 3) lowBatteryNotified = false;
            BatteryChanged();
            UpdateBatteryPage(lastSnapshot);
        }

        private static void SelectWave(ComboBox wave, string text, int fallback)
        {
            // Stored as the waveform id; anything not in the menu falls back to the default.
            int id;
            int found = int.TryParse(text.Trim(), out id) ? Array.IndexOf(HapticWaves, id) : -1;
            wave.SelectedIndex = found >= 0 ? found : fallback;
        }

        // ---- Haptic strength (Haptics page) ----

        private void ReadHapticStrength()
        {
            if (hidpp == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate { ShowHapticStrength(snapshot); }));
                }
                catch { }
            });
        }

        private void ShowHapticStrength(DeviceSnapshot snapshot)
        {
            bool present = snapshot != null && snapshot.HapticPresent;
            hapticStrength.Enabled = hapticStrengthApply.Enabled = present;
            if (present) hapticStrength.Value = Math.Max(0, Math.Min(100, snapshot.HapticLevel));
            else hapticStrengthValue.Text = "—";
            if (snapshot == null) status.Text = Lang.T("尚未連上滑鼠，無法讀取觸覺強度；下方的設定仍可先調整", "The mouse is not connected, so the strength cannot be read; the settings below can still be changed");
        }

        private void ApplyHapticStrength()
        {
            int level = (int)hapticStrength.Value;
            hapticStrengthApply.Enabled = false;
            status.Text = Lang.T("正在套用觸覺強度…", "Applying haptic strength…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = hidpp.SetHaptic(level); }
                catch { }
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        LogDeviceLines(new[] { "write haptic " + level + " verified=" + ok });
                        if (ok)
                        {
                            deviceSettings.Haptic = level;
                            SaveSettings();
                        }
                        ShowHapticStrength(snapshot);
                        status.Text = ok
                            ? Lang.T("觸覺強度已套用並儲存", "Haptic strength applied and saved")
                            : Lang.T("觸覺強度套用失敗：滑鼠沒有確認新數值", "Haptic strength failed: the mouse did not confirm the new value");
                    }));
                }
                catch { }
            });
        }

        // ---- Precision mode and wheel mode ----

        // Run from a menu action (ring, gesture, thumb wheel, or a button's plain click).
        private bool RunMouseAction(MappedAction action)
        {
            if (action == MappedAction.WheelModeToggle) return ToggleWheelMode();
            if (action == MappedAction.HostChannel1) return SwitchToChannel(0);
            if (action == MappedAction.HostChannel2) return SwitchToChannel(1);
            if (action == MappedAction.HostChannel3) return SwitchToChannel(2);
            precisionHeldBy = null;
            return SetPrecision(!precisionActive, false);
        }

        private void HoldPrecision(MouseInput input, bool down)
        {
            if (down)
            {
                if (precisionHeldBy.HasValue) return;
                if (SetPrecision(true, false)) precisionHeldBy = input;
            }
            else if (precisionHeldBy == input)
            {
                precisionHeldBy = null;
                SetPrecision(false, false);
            }
        }

        // The state changes at once; the speed is written in the background, and only the latest
        // request is written, so a quick press and release always ends at the normal speed.
        // The hint shows once the mouse confirms the speed; a failed write is retried once, then reported.
        private bool SetPrecision(bool on, bool quiet)
        {
            if (on == precisionActive) return true;
            if (on)
            {
                DeviceSnapshot s = lastSnapshot;
                if (hidpp == null || s == null || s.DpiFeature != 0x2201 || s.Dpi <= 0) return false;
                int normal = deviceSettings.Dpi > 0 ? deviceSettings.Dpi : s.Dpi;
                if (deviceSettings.Dpi <= 0)
                {
                    // Kept in the INI so a crash in precision mode is undone at the next start or reconnect.
                    deviceSettings.Dpi = normal;
                    SaveSettings();
                }
                int low = (int)precisionDpi.Value;
                if (s.DpiMax > s.DpiMin) low = Math.Max(s.DpiMin, Math.Min(s.DpiMax, low));
                precisionNormalDpi = normal;
                precisionLowDpi = low;
                precisionActive = true;
                precisionRestorePending = false;
                QueueDpi(low, true, quiet);
            }
            else
            {
                precisionActive = false;
                precisionHeldBy = null;
                precisionRestorePending = true;
                QueueDpi(precisionNormalDpi, false, quiet);
            }
            if (!quiet) PlayHaptic(modeHaptic, modeHapticWave);
            return true;
        }

        private void QueueDpi(int dpi, bool on, bool quiet)
        {
            int seq = Interlocked.Increment(ref precisionSeq);
            string reason = on ? "Precision on" : "Precision off";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false, retried = false;
                lock (precisionLock)
                {
                    if (seq != Thread.VolatileRead(ref precisionSeq)) return;
                    try { ok = hidpp.SetDpi(dpi); }
                    catch { }
                    if (!ok && seq == Thread.VolatileRead(ref precisionSeq))
                    {
                        retried = true;
                        try { ok = hidpp.SetDpi(dpi); }
                        catch { }
                    }
                }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate { PrecisionWritten(seq, dpi, on, quiet, ok, retried, reason); }));
                }
                catch { }
            });
        }

        // Back on the UI thread. A result is ignored once something newer (another toggle, a reconnect,
        // a speed applied on the pointer page) has replaced the request.
        private void PrecisionWritten(int seq, int dpi, bool on, bool quiet, bool ok, bool retried, string reason)
        {
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " " + reason + " DPI " + dpi + " verified=" + ok + (retried ? " (retried)" : ""));
            if (seq != Thread.VolatileRead(ref precisionSeq)) return;
            if (ok)
            {
                if (on)
                {
                    if (!quiet) ShowMode(Lang.T("精準模式", "Precision mode"), dpi + " DPI");
                }
                else
                {
                    precisionRestorePending = false;
                    if (!quiet) ShowMode(Lang.T("指標速度已恢復", "Pointer speed restored"), dpi + " DPI");
                }
            }
            else if (on)
            {
                // Not confirmed is not the same as not written (the read-back can time out), so the
                // normal speed is written again and stays owed until the mouse confirms it.
                precisionActive = false;
                precisionHeldBy = null;
                precisionRestorePending = true;
                QueueDpi(precisionNormalDpi, false, true);
                if (!quiet) modeOverlay.ShowMode(Lang.T("精準模式未能開啟", "Precision mode failed"), Lang.T("無法寫入滑鼠速度", "Couldn't set the mouse speed"));
            }
            else
            {
                // Still owed: written again on exit, and the saved speed is restored on reconnect.
                if (!quiet) modeOverlay.ShowMode(Lang.T("指標速度未能恢復", "Pointer speed not restored"), String.Format(Lang.T("重新連線後會套用 {0} DPI", "{0} DPI is applied on reconnect"), dpi));
            }
        }

        // A speed applied on the pointer page (runs on a worker thread). It cancels queued precision
        // writes and shares their lock, so an older precision write cannot land after it.
        private bool WriteManualDpi(int value)
        {
            int seq = Interlocked.Increment(ref precisionSeq);
            Interlocked.Exchange(ref manualDpiSeq, seq);
            bool ok = false;
            lock (precisionLock)
            {
                try { ok = hidpp.SetDpi(value); }
                catch { }
            }
            if (!ok)
            {
                try
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(delegate { ManualDpiFailed(seq); }));
                }
                catch { }
            }
            return ok;
        }

        // The write above cancelled an owed restore; queue it again.
        private void ManualDpiFailed(int seq)
        {
            if (seq != Thread.VolatileRead(ref precisionSeq)) return;
            if (precisionRestorePending && !precisionActive) QueueDpi(precisionNormalDpi, false, true);
        }

        // The new speed is the normal one now.
        private void ManualDpiApplied(int value)
        {
            precisionNormalDpi = value;
            if (Thread.VolatileRead(ref precisionSeq) == Thread.VolatileRead(ref manualDpiSeq))
            {
                // Nothing else was written since, so the mouse is at the new speed and precision mode is over.
                precisionActive = false;
                precisionHeldBy = null;
                precisionRestorePending = false;
                Interlocked.Increment(ref precisionSeq);
            }
            else if (!precisionActive)
            {
                // Precision mode was switched off during the write and may have put the old speed back after it.
                precisionRestorePending = true;
                QueueDpi(value, false, true);
            }
            else
            {
                // Precision mode was switched on during the write, which may have landed after the slow
                // speed; write the slow speed again. Releasing it restores the new speed.
                QueueDpi(precisionLowDpi, true, true);
            }
        }

        private bool ToggleWheelMode()
        {
            if (hidpp == null) return false;
            ThreadPool.QueueUserWorkItem(delegate
            {
                int mode = -1, threshold = -1;
                bool ok = false;
                try { ok = hidpp.ToggleWheelMode(out mode, out threshold); }
                catch { }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        LogDeviceLines(new[] { "toggle wheel mode -> " + mode + "/" + threshold + " verified=" + ok });
                        if (!ok) return;
                        // Saved like a choice on the pointer page, so a reconnect keeps the new mode.
                        deviceSettings.WheelMode = mode;
                        deviceSettings.SmartShiftThreshold = threshold;
                        SaveSettings();
                        if (mode == 1) ShowMode(Lang.T("自由滾動", "Free spin"), Lang.T("滾輪無阻力", "No resistance"));
                        else ShowMode(Lang.T("段落滾動", "Ratchet"), Lang.T("一格一格", "Step by step"));
                        PlayHaptic(modeHaptic, modeHapticWave);
                    }));
                }
                catch { }
            });
            return true;
        }

        private void ShowMode(string title, string detail)
        {
            if (modeHud.Checked) modeOverlay.ShowMode(title, detail);
        }

        // The trigger list on the pointer page mirrors which button is set to "precision mode (hold)".
        private void RefreshPrecisionTrigger()
        {
            int index = 0;
            for (int i = 0; i < PrecisionInputs.Length; i++)
                if (GetMapping(PrecisionInputs[i]).Action == MappedAction.PrecisionHold) { index = i + 1; break; }
            loadingPrecision = true;
            precisionTrigger.SelectedIndex = index;
            loadingPrecision = false;
        }

        // One button at a time: the one that had it goes back to its default action.
        private void ApplyPrecisionTrigger()
        {
            MouseInput? chosen = precisionTrigger.SelectedIndex > 0 ? PrecisionInputs[precisionTrigger.SelectedIndex - 1] : (MouseInput?)null;
            foreach (MouseInput input in PrecisionInputs)
            {
                ActionPicker picker;
                if (!selectors.TryGetValue(input, out picker)) continue;
                if (chosen == input) picker.Binding = ActionBinding.Of(MappedAction.PrecisionHold);
                else if (picker.Binding.Action == MappedAction.PrecisionHold) picker.Binding = DefaultMapping(input);
            }
            SaveSettings();
        }

        private static ActionBinding DefaultMapping(MouseInput input)
        {
            switch (input)
            {
                case MouseInput.XButton1: return ActionBinding.Of(MappedAction.BrowserBack);
                case MouseInput.XButton2: return ActionBinding.Of(MappedAction.BrowserForward);
                case MouseInput.GestureButton: return ActionBinding.Of(MappedAction.AltTab);
                case MouseInput.ActionRingButton: return ActionBinding.Of(MappedAction.TaskView);
                default: return ActionBinding.Of(MappedAction.PassThrough);
            }
        }

        private bool applyingStartup;

        private void ApplyStartWithWindows()
        {
            if (applyingStartup) return;
            string error;
            bool ok = startWithWindows.Checked ? StartupShortcut.Create(out error) : StartupShortcut.Remove(out error);
            if (ok)
            {
                status.Text = startWithWindows.Checked
                    ? Lang.T("已在「啟動」資料夾建立捷徑", "Shortcut added to the Startup folder")
                    : Lang.T("已移除「啟動」資料夾的捷徑", "Shortcut removed from the Startup folder");
                return;
            }
            // Put the switch back to what is really on disk, then say why.
            applyingStartup = true;
            startWithWindows.Checked = StartupShortcut.Exists;
            applyingStartup = false;
            MessageBox.Show(Lang.T("無法變更自動啟動：", "Could not change start-up: ") + error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void UpdateGestureNames()
        {
            for (int i = 0; i < 4; i++) overlay.Names[i] = gestureBindings[i].ShortLabel;
        }

        private void AddHintAt(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = Theme.Muted,
                Font = new Font(Theme.UiFont, 8.5F),
                Location = new Point(S(x), S(y))
            });
        }

        private void AddDivider(Control parent, int y, int width)
        {
            parent.Controls.Add(new Panel
            {
                BackColor = Theme.Divider,
                Location = new Point(S(18), S(y)),
                Size = new Size(width, 1)
            });
        }

        // A titled row with a description on the left and one button on the right.
        private void AddActionRow(Control parent, string title, string hint, string buttonText, int y, EventHandler click)
        {
            parent.Controls.Add(new Label { Text = title, AutoSize = true, Location = new Point(S(18), S(y)) });
            AddHint(parent, hint, y + 24);
            Button button = Theme.CreateButton(buttonText, false);
            button.Size = new Size(S(150), S(34));
            button.Location = new Point(parent.Width - S(18) - button.Width, S(y + 6));
            button.Click += click;
            parent.Controls.Add(button);
        }

        private void AddMappingRow(Control parent, string labelText, string hint, MouseInput input, int y)
        {
            int row = rowInputs.Count;
            rowInputs.Add(input);
            int left = S(304);
            parent.Controls.Add(new Panel
            {
                BackColor = Theme.Divider,
                Location = new Point(left, S(y)),
                Size = new Size(parent.Width - S(18) - left, 1)
            });

            // Number badge matching the diagram; filled while its control is highlighted there.
            var badge = new Label { Location = new Point(left, S(y + 14)), Size = new Size(S(22), S(22)) };
            badge.Paint += delegate(object sender, PaintEventArgs e)
            {
                bool active = diagram.Highlight == row;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                var circle = new RectangleF(1F, 1F, badge.Width - 2.5F, badge.Height - 2.5F);
                using (var fill = new SolidBrush(active ? Theme.Accent : Theme.Card))
                using (var ring = new Pen(Theme.Accent, 1.5F * uiScale))
                using (var ink = new SolidBrush(active ? Color.White : Theme.Accent))
                using (var font = new Font("Segoe UI", 8F, FontStyle.Bold))
                using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    e.Graphics.FillEllipse(fill, circle);
                    e.Graphics.DrawEllipse(ring, circle);
                    e.Graphics.DrawString((row + 1).ToString(), font, ink, circle, center);
                }
            };
            rowBadges.Add(badge);
            parent.Controls.Add(badge);

            parent.Controls.Add(new Label
            {
                Text = labelText,
                AutoSize = true,
                Location = new Point(left + S(30), S(hint.Length > 0 ? y + 7 : y + 15))
            });
            if (hint.Length > 0)
            {
                parent.Controls.Add(new Label
                {
                    Text = hint,
                    AutoSize = true,
                    ForeColor = Theme.Muted,
                    Font = new Font(Theme.UiFont, 8.25F),
                    Location = new Point(left + S(30), S(y + 27))
                });
            }
            var picker = new ActionPicker(parent, new Point(S(522), S(y + 12)), S(172), S(50), S(4));
            picker.Combo.Enter += delegate { diagram.Highlight = row; };
            picker.Combo.MouseEnter += delegate { diagram.Highlight = row; };
            selectors[input] = picker;
        }

        private void LoadSettings()
        {
            foreach (MouseInput input in Enum.GetValues(typeof(MouseInput)))
                mappings[input] = DefaultMapping(input);

            if (File.Exists(configPath))
            {
                foreach (string original in File.ReadAllLines(configPath))
                {
                    string line = original.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.IndexOf('=') < 0) continue;
                    string[] parts = line.Split(new[] { '=' }, 2);
                    if (parts[0].Equals("GestureMode", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) gestureMode.Checked = value;
                        continue;
                    }
                    if (parts[0].Equals("ActionRingMode", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) actionRingMode.Checked = value;
                        continue;
                    }
                    if (parts[0].Equals("ActionRingClick", StringComparison.OrdinalIgnoreCase))
                    {
                        bool value;
                        if (bool.TryParse(parts[1], out value)) ringClickMode.Checked = value;
                        continue;
                    }
                    string key = parts[0].Trim();
                    if (key.StartsWith("OtherButton.", StringComparison.OrdinalIgnoreCase))
                    {
                        ActionBinding otherBinding = ActionBinding.Parse(parts[1]);
                        if (otherBinding != null && !otherBinding.IsPassThrough) otherBindings[key.Substring(12)] = otherBinding;
                        continue;
                    }
                    if (key.Equals("RingStyle", StringComparison.OrdinalIgnoreCase))
                    {
                        ringStyle.SelectedIndex = parts[1].Trim().Equals("Bubble", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        continue;
                    }
                    if (key.Equals("RingColor", StringComparison.OrdinalIgnoreCase))
                    {
                        ringColor.SelectedIndex = parts[1].Trim().Equals("Light", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
                        continue;
                    }
                    CheckBox hapticSwitch = key.Equals("RingHaptic", StringComparison.OrdinalIgnoreCase) ? ringHaptic
                        : key.Equals("GestureHaptic", StringComparison.OrdinalIgnoreCase) ? gestureHaptic
                        : key.Equals("FlowHaptic", StringComparison.OrdinalIgnoreCase) ? flowHaptic
                        : key.Equals("ModeHaptic", StringComparison.OrdinalIgnoreCase) ? modeHaptic
                        : key.Equals("ModeHud", StringComparison.OrdinalIgnoreCase) ? modeHud
                        : key.Equals("LowBatteryAlert", StringComparison.OrdinalIgnoreCase) ? lowBatteryAlert
                        : key.Equals("LowBatteryHapticPause", StringComparison.OrdinalIgnoreCase) ? lowBatteryHapticPause : null;
                    if (hapticSwitch != null)
                    {
                        bool on;
                        if (bool.TryParse(parts[1].Trim(), out on)) hapticSwitch.Checked = on;
                        continue;
                    }
                    if (key.Equals("RingHapticWave", StringComparison.OrdinalIgnoreCase)) { SelectWave(ringHapticWave, parts[1], DefaultHapticWave); continue; }
                    if (key.Equals("GestureHapticWave", StringComparison.OrdinalIgnoreCase)) { SelectWave(gestureHapticWave, parts[1], 3); continue; }
                    if (key.Equals("FlowHapticWave", StringComparison.OrdinalIgnoreCase)) { SelectWave(flowHapticWave, parts[1], 5); continue; }
                    if (key.Equals("ModeHapticWave", StringComparison.OrdinalIgnoreCase)) { SelectWave(modeHapticWave, parts[1], 4); continue; }
                    if (key.Equals("LowBatteryLevel", StringComparison.OrdinalIgnoreCase))
                    {
                        int level;
                        int found = int.TryParse(parts[1].Trim(), out level) ? Array.IndexOf(LowBatteryLevels, level) : -1;
                        if (found >= 0) lowBatteryLevel.SelectedIndex = found;
                        continue;
                    }
                    if (key.Equals("GestureSensitivity", StringComparison.OrdinalIgnoreCase))
                    {
                        int value;
                        if (int.TryParse(parts[1].Trim(), out value)) gestureSensitivity = Math.Max(0, Math.Min(100, value));
                        continue;
                    }
                    if (key.Equals("PrecisionDpi", StringComparison.OrdinalIgnoreCase))
                    {
                        int dpi;
                        if (int.TryParse(parts[1].Trim(), out dpi)) precisionDpi.Value = dpi;
                        continue;
                    }
                    if (key.Equals("RingSize", StringComparison.OrdinalIgnoreCase) || key.Equals("BubbleSize", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("RingOpacity", StringComparison.OrdinalIgnoreCase))
                    {
                        int percent;
                        if (int.TryParse(parts[1].Trim(), out percent))
                        {
                            if (key.Equals("RingSize", StringComparison.OrdinalIgnoreCase)) ringSize.Value = percent;
                            else if (key.Equals("BubbleSize", StringComparison.OrdinalIgnoreCase)) bubbleSize.Value = percent;
                            else ringOpacity.Value = percent;
                        }
                        continue;
                    }
                    if (key.Equals("TrayHintShown", StringComparison.OrdinalIgnoreCase))
                    {
                        bool.TryParse(parts[1], out trayHintShown);
                        continue;
                    }
                    if (key.Equals("StartMinimized", StringComparison.OrdinalIgnoreCase))
                    {
                        bool minimized;
                        if (bool.TryParse(parts[1], out minimized)) startMinimized.Checked = minimized;
                        continue;
                    }
                    if (key.Equals("EdgeSwitch", StringComparison.OrdinalIgnoreCase) || key.Equals("EdgePlaceCursor", StringComparison.OrdinalIgnoreCase))
                    {
                        bool on;
                        if (bool.TryParse(parts[1], out on))
                        {
                            if (key.Equals("EdgeSwitch", StringComparison.OrdinalIgnoreCase)) edgeSwitch.Checked = on;
                            else edgePlaceCursor.Checked = on;
                        }
                        continue;
                    }
                    if (key.Equals("KeyboardFollow", StringComparison.OrdinalIgnoreCase))
                    {
                        bool follow;
                        if (bool.TryParse(parts[1].Trim(), out follow)) keyboardFollow.Checked = follow;
                        continue;
                    }
                    if (key.Length == 16 && key.StartsWith("KeyboardChannel", StringComparison.OrdinalIgnoreCase))
                    {
                        int mouseChannel, keyboardChannel;
                        if (!int.TryParse(key.Substring(15), out mouseChannel) || mouseChannel < 1 || mouseChannel > 3) continue;
                        string value = parts[1].Trim();
                        if (value.Equals("Auto", StringComparison.OrdinalIgnoreCase)) keyboardChannels[mouseChannel - 1] = -2;
                        else if (int.TryParse(value, out keyboardChannel) && keyboardChannel >= 0 && keyboardChannel <= 8)
                            keyboardChannels[mouseChannel - 1] = keyboardChannel - 1;
                        continue;
                    }
                    if (key.Equals("EdgeDwell", StringComparison.OrdinalIgnoreCase))
                    {
                        int milliseconds;
                        if (int.TryParse(parts[1].Trim(), out milliseconds)) edgeDwell.Value = (milliseconds + 50) / 100;
                        continue;
                    }
                    if (key.Equals("EdgeKey", StringComparison.OrdinalIgnoreCase))
                    {
                        string wanted = parts[1].Trim();
                        int found = Array.FindIndex(EdgeKeyNames, delegate(string name) { return name.Equals(wanted, StringComparison.OrdinalIgnoreCase); });
                        if (found >= 0) edgeKey.SelectedIndex = found;
                        continue;
                    }
                    int edgeSlot = Array.FindIndex(EdgeKeys, delegate(string name) { return name.Equals(key, StringComparison.OrdinalIgnoreCase); });
                    if (edgeSlot >= 0)
                    {
                        // Stored as the channel number printed under the mouse (1-3); 0 means off.
                        int channel;
                        if (int.TryParse(parts[1].Trim(), out channel) && channel >= 0 && channel <= 8) edgeHosts[edgeSlot] = channel - 1;
                        continue;
                    }
                    // Read earlier by Lang.Load, before any window existed.
                    if (key.Equals("Language", StringComparison.OrdinalIgnoreCase)) continue;
                    if (deviceSettings.TryLoad(key, parts[1])) continue;
                    ActionBinding binding = ActionBinding.Parse(parts[1]);
                    if (binding == null) continue;
                    int gesture = Array.FindIndex(GestureKeys, delegate(string name) { return name.Equals(key, StringComparison.OrdinalIgnoreCase); });
                    if (gesture >= 0)
                    {
                        gestureBindings[gesture] = binding;
                        continue;
                    }
                    int slot;
                    if (key.Length == 5 && key.StartsWith("Ring", StringComparison.OrdinalIgnoreCase) &&
                        int.TryParse(key.Substring(4), out slot) && slot >= 0 && slot < 8)
                    {
                        ringBindings[slot] = binding;
                        continue;
                    }
                    MouseInput input;
                    if (Enum.TryParse(key, true, out input) && Enum.IsDefined(typeof(MouseInput), input))
                        mappings[input] = binding;
                }
            }

            foreach (var pair in selectors)
                pair.Value.Binding = mappings[pair.Key];
            actionRing.SetSlots(ringBindings);
            UpdateGestureNames();
            ApplyRingAppearance();
            UpdateHapticControls();
            UpdateHapticPauseNote();
            RefreshPrecisionTrigger();
            RefreshHostUi();
            RefreshKeyboardUi();
        }

        private static readonly string[] GestureKeys = { "GestureUp", "GestureRight", "GestureDown", "GestureLeft" };

        private void SaveSettings()
        {
            try
            {
                var lines = new List<string>();
                lines.Add("# MX Master Win settings");
                foreach (var pair in selectors)
                {
                    ActionBinding binding = pair.Value.Binding;
                    mappings[pair.Key] = binding;
                    lines.Add(pair.Key + "=" + binding);
                }
                lines.Add("GestureMode=" + gestureMode.Checked);
                lines.Add("ActionRingMode=" + actionRingMode.Checked);
                lines.Add("ActionRingClick=" + ringClickMode.Checked);
                lines.Add("RingStyle=" + (ringStyle.SelectedIndex == 1 ? "Bubble" : "Ring"));
                lines.Add("RingSize=" + ringSize.Value);
                lines.Add("BubbleSize=" + bubbleSize.Value);
                lines.Add("RingOpacity=" + ringOpacity.Value);
                lines.Add("RingColor=" + (ringColor.SelectedIndex == 1 ? "Light" : "Dark"));
                lines.Add("RingHaptic=" + ringHaptic.Checked);
                lines.Add("RingHapticWave=" + WaveId(ringHapticWave));
                lines.Add("GestureHaptic=" + gestureHaptic.Checked);
                lines.Add("GestureHapticWave=" + WaveId(gestureHapticWave));
                lines.Add("FlowHaptic=" + flowHaptic.Checked);
                lines.Add("FlowHapticWave=" + WaveId(flowHapticWave));
                lines.Add("ModeHaptic=" + modeHaptic.Checked);
                lines.Add("ModeHapticWave=" + WaveId(modeHapticWave));
                lines.Add("PrecisionDpi=" + (int)precisionDpi.Value);
                lines.Add("ModeHud=" + modeHud.Checked);
                lines.Add("GestureSensitivity=" + gestureSensitivity);
                lines.Add("LowBatteryAlert=" + lowBatteryAlert.Checked);
                lines.Add("LowBatteryLevel=" + LowBatteryThreshold);
                lines.Add("LowBatteryHapticPause=" + lowBatteryHapticPause.Checked);
                for (int i = 0; i < GestureKeys.Length; i++)
                    lines.Add(GestureKeys[i] + "=" + gestureBindings[i]);
                lines.Add("StartMinimized=" + startMinimized.Checked);
                lines.Add("EdgeSwitch=" + edgeSwitch.Checked);
                for (int i = 0; i < EdgeKeys.Length; i++)
                    lines.Add(EdgeKeys[i] + "=" + (edgeHosts[i] + 1));
                lines.Add("EdgeDwell=" + edgeDwell.Value * 100);
                lines.Add("EdgeKey=" + EdgeKeyNames[Math.Max(0, edgeKey.SelectedIndex)]);
                lines.Add("EdgePlaceCursor=" + edgePlaceCursor.Checked);
                lines.Add("KeyboardFollow=" + keyboardFollow.Checked);
                // Per mouse channel: Auto, 0 = keyboard stays, or the keyboard channel number (1-3).
                for (int i = 0; i < keyboardChannels.Length; i++)
                    lines.Add("KeyboardChannel" + (i + 1) + "=" + (keyboardChannels[i] == -2 ? "Auto" : (keyboardChannels[i] + 1).ToString()));
                lines.Add("Language=" + Lang.Setting);
                // Ring0 is the top slice; the rest follow clockwise.
                for (int i = 0; i < ringBindings.Length; i++)
                    lines.Add("Ring" + i + "=" + ringBindings[i]);
                lines.AddRange(deviceSettings.ToLines());
                foreach (KeyValuePair<string, ActionBinding> other in otherBindings)
                    lines.Add("OtherButton." + other.Key + "=" + other.Value);
                if (trayHintShown) lines.Add("TrayHintShown=True");
                File.WriteAllLines(configPath, lines.ToArray(), Encoding.UTF8);
                status.Text = Lang.T("設定已自動儲存", "Settings saved");
            }
            catch (Exception ex)
            {
                MessageBox.Show(Lang.T("無法儲存設定：", "Cannot save settings: ") + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private bool OnMouseEvent(MouseHookEvent e)
        {
            if (liveLog.Checked && e.Kind != MouseHookKind.Move)
                BeginInvoke(new Action(delegate
                {
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Hook " + e.Kind + (e.Injected ? " [logitech-translated]" : ""));
                }));

            // Logitech's Bluetooth translation layer marks real horizontal-wheel
            // and side-button messages as injected. This app only injects keyboard
            // events, so accepting these mouse messages cannot recurse into itself.

            if (actionRingActive && e.Kind == MouseHookKind.Move)
            {
                actionRing.UpdateSelection(e.Point);
                return false;
            }

            // Click-to-choose ring: the next click picks a slice or dismisses the ring, and
            // neither that click nor its release reaches the window underneath.
            if (actionRingActive && actionRing.ClickMode)
            {
                if (e.Kind == MouseHookKind.LeftDown)
                {
                    actionRing.UpdateSelection(e.Point);
                    actionRingActive = false;
                    swallowLeftUp = true;
                    BeginInvoke(new Action(FinishActionRing));
                    return true;
                }
                if (e.Kind == MouseHookKind.RightDown)
                {
                    actionRingActive = false;
                    swallowRightUp = true;
                    BeginInvoke(new Action(CancelActionRing));
                    return true;
                }
            }
            if (swallowLeftUp && e.Kind == MouseHookKind.LeftUp)
            {
                swallowLeftUp = false;
                return true;
            }
            if (swallowRightUp && e.Kind == MouseHookKind.RightUp)
            {
                swallowRightUp = false;
                return true;
            }

            if ((gestureMode.Checked || otherGestureHeld) && gestureActive && e.Kind == MouseHookKind.Move)
            {
                int dx = e.Point.X - gestureStart.X;
                int dy = e.Point.Y - gestureStart.Y;
                int ax = Math.Abs(dx);
                int ay = Math.Abs(dy);
                int major = Math.Max(ax, ay);
                // Scaled by the gesture sensitivity; 50% keeps the original 40 px / 65 px.
                double factor = Math.Pow(2, (50 - gestureSensitivity) / 50.0);
                int moveDistance = (int)Math.Round(40 * factor), lockDistance = (int)Math.Round(65 * factor);
                if (major >= moveDistance) gestureMoved = true;

                // Do not let a late wobble change an already selected action. Before
                // locking, require one axis to be at least 35% stronger than the other.
                if (gestureDirection.Length == 0)
                {
                    string next = "";
                    if (major >= moveDistance)
                    {
                        if (ax * 100 >= ay * 135) next = dx < 0 ? "左" : "右";
                        else if (ay * 100 >= ax * 135) next = dy < 0 ? "上" : "下";
                        else next = "斜";
                    }

                    if (major >= lockDistance && next != "" && next != "斜")
                    {
                        gestureDirection = next;
                        PlayHaptic(gestureHaptic, gestureHapticWave);
                    }

                    string display = gestureDirection.Length > 0 ? gestureDirection : next;
                    if (display != gestureCandidate)
                    {
                        gestureCandidate = display;
                        BeginInvoke(new Action(delegate { overlay.SetDirection(gestureCandidate); }));
                    }
                }
                return false;
            }

            MouseInput input;
            bool actionable = true;
            bool button = true;
            switch (e.Kind)
            {
                case MouseHookKind.MiddleUp: return ButtonReleased(MouseInput.Middle);
                case MouseHookKind.XButton1Up: return ButtonReleased(MouseInput.XButton1);
                case MouseHookKind.XButton2Up: return ButtonReleased(MouseInput.XButton2);
                case MouseHookKind.MiddleDown: input = MouseInput.Middle; break;
                case MouseHookKind.XButton1Down: input = MouseInput.XButton1; break;
                case MouseHookKind.XButton2Down: input = MouseInput.XButton2; break;
                case MouseHookKind.WheelLeft: input = MouseInput.WheelLeft; button = false; break;
                case MouseHookKind.WheelRight: input = MouseInput.WheelRight; button = false; break;
                default: input = MouseInput.Middle; actionable = false; break;
            }
            if (!actionable) return false;
            if (button) heldInputs.Add(input);
            ActionBinding mapped = GetMapping(input);
            if (mapped.IsPassThrough) return false;
            if (button && mapped.Action == MappedAction.PrecisionHold)
            {
                BeginInvoke(new Action(delegate { HoldPrecision(input, true); }));
                return true;
            }
            if (button && IsEdgeKey(input))
            {
                // Held to switch computers: its own action runs on release, unless a switch happened.
                deferredPending = true;
                deferredInput = input;
                return true;
            }
            BeginInvoke(new Action(delegate { ActionRunner.Run(mapped); }));
            return true;
        }

        // Releases are passed on as before; a deferred action of the edge-switch key runs now.
        private bool ButtonReleased(MouseInput input)
        {
            heldInputs.Remove(input);
            if (precisionHeldBy == input || GetMapping(input).Action == MappedAction.PrecisionHold)
                BeginInvoke(new Action(delegate { HoldPrecision(input, false); }));
            if (deferredPending && deferredInput == input)
            {
                deferredPending = false;
                ActionBinding mapped = GetMapping(input);
                if (!mapped.IsPassThrough) BeginInvoke(new Action(delegate { ActionRunner.Run(mapped); }));
            }
            return false;
        }

        private void OnVendorButton(string transport, int deviceIndex, int controlCode, bool pressed)
        {
            if (!MouseHook.Enabled) return;

            MouseInput input;
            string name;
            if (controlCode == 0x00C3)
            {
                input = MouseInput.GestureButton;
                name = "GestureButton";
            }
            else if (controlCode == 0x01A0)
            {
                input = MouseInput.ActionRingButton;
                name = "ActionRingButton";
            }
            else if (controlCode == 0x00C4)
            {
                input = MouseInput.WheelModeButton;
                name = "WheelModeButton";
            }
            else return;

            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Decoded " + transport +
                    " dev=" + deviceIndex.ToString("X2") + " " + name + (pressed ? " Down" : " Up"));

            // The same press can arrive in more than one report format (for example while
            // Options+ is also running). Act only when the button's state really changes.
            bool last;
            if (vendorState.TryGetValue(controlCode, out last) && last == pressed) return;
            vendorState[controlCode] = pressed;

            if (input == MouseInput.GestureButton && gestureMode.Checked)
            {
                if (pressed) BeginGesture(Cursor.Position);
                else if (gestureActive) FinishGesture(MouseInput.GestureButton);
                return;
            }

            if (input == MouseInput.ActionRingButton && actionRingMode.Checked)
            {
                if (actionRingActive && actionRing.ClickMode)
                {
                    // An open click-to-choose ring ignores the release and closes on the next press.
                    if (pressed) CancelActionRing();
                }
                else if (pressed) BeginActionRing(Cursor.Position);
                else if (actionRingActive) FinishActionRing();
                return;
            }

            if (GetMapping(input).Action == MappedAction.PrecisionHold)
            {
                HoldPrecision(input, pressed);
                return;
            }

            // A special button used as a plain button acts on press. When it is the edge-switch key
            // it acts on release instead, and not at all when the mouse switched in between.
            bool run = pressed;
            if (IsEdgeKey(input))
            {
                run = !pressed && deferredPending && deferredInput == input;
                deferredPending = pressed;
                deferredInput = input;
            }
            if (run)
            {
                ActionBinding action = GetMapping(input);
                bool sent = !action.IsPassThrough && ActionRunner.Run(action);
                if (liveLog.Checked)
                    AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Mapped " + name +
                        " selected=" + action + " sent=" + sent);
            }
        }

        private ActionBinding GetOtherBinding(string wpid, int button)
        {
            ActionBinding binding;
            return otherBindings.TryGetValue(wpid + "." + button, out binding) ? binding : ActionBinding.Of(MappedAction.PassThrough);
        }

        private void SetOtherBinding(string wpid, int button, ActionBinding binding)
        {
            string key = wpid + "." + button;
            if (binding == null || binding.IsPassThrough) otherBindings.Remove(key);
            else otherBindings[key] = binding;
            SaveSettings();
            UpdateOtherReports();
        }

        // The mouse only sends button reports while a button has an action and mapping is on.
        private void UpdateOtherReports()
        {
            if (demoOthers == 0 && others != null) others.SetButtonReports(MouseHook.Enabled && otherBindings.Count > 0);
        }

        // Only buttons the mouse's profile hands to the app act here, so nothing fires twice.
        private bool IsOtherAppKey(string wpid, int button)
        {
            foreach (OtherDeviceSnapshot d in otherSnapshots)
                if (d.Wpid == wpid && d.Link == OtherDeviceLink.Online && button < d.Buttons.Count && d.Buttons[button].AppButton.HasValue)
                    return true;
            return false;
        }

        private void OnOtherButton(string wpid, int button, bool pressed)
        {
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Others button " + wpid + "." + button + (pressed ? " Down" : " Up"));
            if (!MouseHook.Enabled) return;
            string key = wpid + "." + button;
            if (!pressed && key == otherHeldKey)
            {
                otherHeldKey = "";
                if (otherGestureHeld)
                {
                    otherGestureHeld = false;
                    if (gestureActive) FinishGesture(ActionBinding.Of(MappedAction.PassThrough));
                }
                else if (actionRingActive && !actionRing.ClickMode) FinishActionRing();
                return;
            }
            if (!pressed || !IsOtherAppKey(wpid, button)) return;
            ActionBinding binding = GetOtherBinding(wpid, button);
            if (binding.IsPassThrough) return;
            if (binding.Action == MappedAction.AppGesture)
            {
                if (gestureActive || actionRingActive) return;
                otherHeldKey = key;
                otherGestureHeld = true;
                BeginGesture(Cursor.Position);
                return;
            }
            if (binding.Action == MappedAction.AppRing)
            {
                if (actionRingActive)
                {
                    if (actionRing.ClickMode) CancelActionRing();
                    return;
                }
                if (gestureActive) return;
                BeginActionRing(Cursor.Position);
                if (!actionRing.ClickMode) otherHeldKey = key;
                return;
            }
            bool sent = ActionRunner.Run(binding);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Mapped Others " + key + " selected=" + binding + " sent=" + sent);
        }

        // The mouse went away (or mapping was paused) with a button held: cancel instead of running an action.
        private void OnOtherButtonsLost()
        {
            if (otherHeldKey.Length == 0) return;
            otherHeldKey = "";
            if (otherGestureHeld)
            {
                otherGestureHeld = false;
                gestureActive = false;
                overlay.Hide();
            }
            else if (actionRingActive && !actionRing.ClickMode) CancelActionRing();
        }

        private void BeginGesture(Point start)
        {
            gestureActive = true;
            gestureStart = start;
            gestureDirection = "";
            gestureCandidate = "";
            gestureMoved = false;
            overlay.ShowAt(gestureStart);
        }

        private void FinishGesture(MouseInput fallbackInput)
        {
            FinishGesture(gestureMoved ? ActionBinding.Of(MappedAction.PassThrough) : GetMapping(fallbackInput));
        }

        // tap runs when the button is released without a direction.
        private void FinishGesture(ActionBinding tap)
        {
            gestureActive = false;
            string direction = gestureDirection;
            overlay.Hide();
            ActionBinding selected;
            if (direction == "左") selected = gestureBindings[3];
            else if (direction == "右") selected = gestureBindings[1];
            else if (direction == "上") selected = gestureBindings[0];
            else if (direction == "下") selected = gestureBindings[2];
            else selected = tap;

            bool sent = !selected.IsPassThrough && ActionRunner.Run(selected);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Gesture direction=" +
                    (String.IsNullOrEmpty(direction) ? (gestureMoved ? "Ambiguous" : "None") : direction) +
                    " selected=" + selected + " sent=" + sent);
        }

        private void BeginActionRing(Point center)
        {
            actionRingActive = true;
            actionRing.ClickMode = ringClickMode.Checked;
            actionRing.ShowAt(center);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing opened" + (actionRing.ClickMode ? " click-mode" : ""));
        }

        private void CancelActionRing()
        {
            actionRingActive = false;
            actionRing.Hide();
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing cancelled");
        }

        private void FinishActionRing()
        {
            actionRingActive = false;
            ActionBinding selected = actionRing.SelectedBinding;
            actionRing.Hide();
            bool sent = selected != null && !selected.IsPassThrough && ActionRunner.Run(selected);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " ActionRing selected=" +
                    (selected != null ? selected.ToString() : "None") + " sent=" + sent +
                    " " + actionRing.SelectionDebug);
        }

        private ActionBinding GetMapping(MouseInput input)
        {
            ActionPicker picker;
            if (selectors.TryGetValue(input, out picker))
                return picker.Binding;
            return mappings.ContainsKey(input) ? mappings[input] : ActionBinding.Of(MappedAction.PassThrough);
        }

        private void AppendRawEvent(string text)
        {
            if (!liveLog.Checked) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendRawEvent), text);
                return;
            }
            AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Raw  " + text);
        }

        private void AddLog(string text)
        {
            eventLog.Items.Add(text);
            while (eventLog.Items.Count > 250) eventLog.Items.RemoveAt(0);
            eventLog.TopIndex = eventLog.Items.Count - 1;
            if (liveLog.Checked)
            {
                try { File.AppendAllText(diagnosticLogPath, text + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
        }

        private void OnHidppReady(int deviceIndex, int featureIndex)
        {
            rawInput.ConfigureHidppDiversion(deviceIndex, featureIndex);
            if (keyboard != null) keyboard.SetMouseSlot(deviceIndex);
            // The saved speed is written back below, so any queued precision write is out of date.
            int restoreSeq = Interlocked.Increment(ref precisionSeq);
            try
            {
                // A button state left over from before a reconnect would swallow the next press.
                BeginInvoke(new Action(delegate
                {
                    vendorState.Clear();
                    rawInput.ResetPressed();
                    // After a ring click that switched computers, its release never arrived here.
                    swallowLeftUp = false;
                    swallowRightUp = false;
                    // The saved speed is written back on (re)connect, which ends precision mode. The
                    // restore stays owed until RestoreDeviceSettings sees the normal speed on the mouse.
                    if (precisionActive) precisionRestorePending = true;
                    precisionActive = false;
                    precisionHeldBy = null;
                    MouseArrived();
                }));
            }
            catch { }
            RestoreDeviceSettings(restoreSeq);
            RefreshHosts();
        }

        // ---- MX Flow: switching computers ----

        private void UpdateScreens()
        {
            Screen[] all = Screen.AllScreens;
            screenBounds = Array.ConvertAll(all, delegate(Screen screen) { return screen.Bounds; });
            edgeScreens.Text = all.Length > 1
                ? String.Format(Lang.T("偵測到 {0} 個螢幕：只有最外側螢幕的外緣會觸發，螢幕之間相接的邊與角落不會",
                    "{0} monitors detected: only the outer edge of the outermost monitor triggers; edges between monitors and corners never do"), all.Length)
                : Lang.T("角落不會觸發。接上多個螢幕時，只有最外側螢幕的外緣會觸發",
                    "Corners never trigger. With several monitors, only the outer edge of the outermost monitor triggers");
        }

        private void RefreshHosts()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                int current = -1;
                List<HostEntry> read = null;
                try { read = hidpp.ReadHosts(out current); }
                catch { }
                if (read == null || read.Count == 0) return;
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        hosts = read;
                        currentHost = current;
                        RefreshHostUi();
                        RefreshKeyboardUi();
                    }));
                }
                catch { }
            });
        }

        private string HostTitle(int index)
        {
            string channel = String.Format(Lang.T("頻道 {0}", "Channel {0}"), index + 1);
            HostEntry entry = hosts.Find(delegate(HostEntry h) { return h.Index == index; });
            return entry == null || entry.Name.Length == 0 ? channel : channel + " · " + entry.Name;
        }

        // Fills the per-edge lists and the channel rows from what the mouse reported; before the
        // first read the three channels are listed without names so the settings stay visible.
        private void RefreshHostUi()
        {
            int count = hosts.Count > 0 ? hosts.Count : 3;
            loadingEdge = true;
            try
            {
                for (int i = 0; i < edgeTargets.Length; i++)
                {
                    ComboBox combo = edgeTargets[i];
                    combo.Items.Clear();
                    combo.Items.Add(Lang.T("不切換", "Off"));
                    for (int h = 0; h < Math.Max(count, edgeHosts[i] + 1); h++)
                        combo.Items.Add(HostTitle(h) + (h == currentHost ? Lang.T("（這台電腦）", " (this computer)") : ""));
                    combo.SelectedIndex = edgeHosts[i] + 1;
                }
            }
            finally { loadingEdge = false; }

            for (int i = 0; i < hostLabels.Length; i++)
            {
                HostEntry entry = hosts.Find(delegate(HostEntry h) { return h.Index == i; });
                var parts = new List<string>();
                parts.Add(HostTitle(i));
                if (entry != null && entry.BusName.Length > 0) parts.Add(entry.BusName);
                if (entry != null && !entry.Paired) parts.Add(Lang.T("未配對", "Not paired"));
                if (i == currentHost) parts.Add(Lang.T("目前使用中", "In use now"));
                hostLabels[i].Text = String.Join("　", parts.ToArray());
                hostButtons[i].Enabled = entry != null && entry.Paired && i != currentHost;
            }
        }

        private bool IsEdgeKey(MouseInput input)
        {
            return edgeSwitch.Checked && edgeKey.SelectedIndex >= 5 &&
                EdgeKeyNames[edgeKey.SelectedIndex].Equals(input.ToString(), StringComparison.Ordinal);
        }

        private static bool IsKeyDown(int virtualKey)
        {
            return (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
        }

        private bool VendorDown(int controlCode)
        {
            bool down;
            return vendorState.TryGetValue(controlCode, out down) && down;
        }

        private bool EdgeKeyHeld()
        {
            switch (edgeKey.SelectedIndex)
            {
                case 1: return IsKeyDown(0x11);                       // VK_CONTROL
                case 2: return IsKeyDown(0x12);                       // VK_MENU
                case 3: return IsKeyDown(0x10);                       // VK_SHIFT
                case 4: return IsKeyDown(0x5B) || IsKeyDown(0x5C);      // VK_LWIN / VK_RWIN
                case 5: return heldInputs.Contains(MouseInput.Middle);
                case 6: return heldInputs.Contains(MouseInput.XButton2);
                case 7: return heldInputs.Contains(MouseInput.XButton1);
                case 8: return VendorDown(0x00C3);
                case 9: return VendorDown(0x01A0);
                default: return true;
            }
        }

        // Called for every relative movement report of the MX mouse itself, so a touchpad or another
        // mouse never triggers a switch. The pointer has to sit on a configured outer edge and keep
        // being pushed outwards for the trigger time; resting there or sliding along the edge does not count.
        private void OnMxMouseMove(int dx, int dy)
        {
            if (!MouseHook.Enabled || !edgeSwitch.Checked)
            {
                edgePushing = false;
                return;
            }
            int now = Environment.TickCount;
            if (unchecked(now - edgeCooldownUntil) < 0) return;
            // A ring or gesture in progress blocks switching, unless the held key is the very button that opens it.
            if ((actionRingActive && edgeKey.SelectedIndex != 9) || (gestureActive && edgeKey.SelectedIndex != 8) ||
                IsKeyDown(0x01) || IsKeyDown(0x02))
            {
                edgePushing = false;
                return;
            }
            ScreenEdge edge;
            if (!EdgeGeometry.TryGetEdge(Cursor.Position, screenBounds, out edge))
            {
                edgePushing = false;
                return;
            }
            int target = edgeHosts[(int)edge];
            if (target < 0 || target == currentHost || !EdgeKeyHeld())
            {
                edgePushing = false;
                return;
            }

            int outward, along;
            switch (edge)
            {
                case ScreenEdge.Left: outward = -dx; along = Math.Abs(dy); break;
                case ScreenEdge.Right: outward = dx; along = Math.Abs(dy); break;
                case ScreenEdge.Top: outward = -dy; along = Math.Abs(dx); break;
                default: outward = dy; along = Math.Abs(dx); break;
            }
            if (outward <= 0 || outward < along) return;

            if (!edgePushing || edge != edgeCandidate || unchecked(now - lastPush) > 250)
            {
                edgePushing = true;
                edgeCandidate = edge;
                pushStart = now;
            }
            lastPush = now;
            if (unchecked(now - pushStart) >= edgeDwell.Value * 100)
                SwitchHost(target, "edge " + edge);
        }

        // "Switch to channel N" from a button, gesture or the ring. Nothing happens on the current
        // channel or on one the mouse reports as unpaired.
        private bool SwitchToChannel(int channel)
        {
            HostEntry entry = hosts.Find(delegate(HostEntry h) { return h.Index == channel; });
            string skip = channel == currentHost ? "already on this channel" : entry != null && !entry.Paired ? "channel not paired" : null;
            if (skip != null)
            {
                if (liveLog.Checked) AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Switch host -> " + channel + " skipped (" + skip + ")");
                return false;
            }
            // Leave the ring's click (and the hook callback) before the mouse goes away.
            BeginInvoke(new Action(delegate { SwitchHost(channel, "action"); }));
            return true;
        }

        private void SwitchHost(int host, string reason)
        {
            edgeCooldownUntil = unchecked(Environment.TickCount + 3000);
            edgePushing = false;
            // Once the mouse has left, the releases of whatever is held never arrive here.
            deferredPending = false;
            heldInputs.Clear();
            vendorState.Clear();
            rawInput.ResetPressed();
            if (actionRingActive) CancelActionRing();
            if (gestureActive)
            {
                gestureActive = false;
                overlay.Hide();
            }
            int normalDpi = precisionActive || precisionRestorePending ? precisionNormalDpi : -1;
            // Cancels any queued precision write now; the restore stays owed (and is retried on exit)
            // until the mouse confirms it.
            int restoreSeq = normalDpi > 0 ? Interlocked.Increment(ref precisionSeq) : 0;
            precisionActive = false;
            precisionHeldBy = null;
            precisionRestorePending = normalDpi > 0;
            int haptic = flowHaptic.Checked && !HapticsPaused ? WaveId(flowHapticWave) : -1;
            int keyboardTarget = keyboardFollow.Checked ? KeyboardChannelFor(host) : -1;
            if (keyboardTarget < 0) keyboard.CancelPendingSwitch();
            int keyboardFrom = keyboardSnapshot != null ? keyboardSnapshot.CurrentHost : -1;
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Switch host -> " + host + " (" + reason + ")");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool sent = false, restored = false, keyboardSent = false;
                // Leaving in precision mode would keep the slow speed on the other computer.
                if (normalDpi > 0)
                {
                    lock (precisionLock)
                    {
                        // Skipped when exit (or a newer request) already took over the restore.
                        if (restoreSeq == Thread.VolatileRead(ref precisionSeq))
                        {
                            try { restored = hidpp.SetDpi(normalDpi); }
                            catch { }
                        }
                    }
                }
                try { sent = hidpp.ChangeHost(host, haptic); }
                catch { }
                // Only once the mouse has really left, so the keyboard never goes alone.
                if (sent && keyboardTarget >= 0 && keyboardTarget != keyboardFrom)
                {
                    try { keyboardSent = keyboard.ChangeHost(keyboardTarget); }
                    catch { }
                }
                try
                {
                    if (!IsHandleCreated || IsDisposed) return;
                    BeginInvoke(new Action(delegate
                    {
                        if (keyboardSent) SetKeyboardSnapshot(null);
                        if (sent && liveLog.Checked && keyboardTarget >= 0 && keyboardTarget != keyboardFrom)
                            AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Keyboard switch host -> " + keyboardTarget + " sent=" + keyboardSent +
                                (keyboard.PendingSwitch ? " (asleep; queued until it answers)" : ""));
                        // Asleep: check often so the queued switch goes as soon as a key wakes it.
                        if (keyboard.PendingSwitch)
                        {
                            keyboardTimer.Stop();
                            keyboardTimer.Interval = 1500;
                            keyboardTimer.Start();
                        }
                        if (restored && restoreSeq == Thread.VolatileRead(ref precisionSeq))
                            precisionRestorePending = false;
                        if (liveLog.Checked)
                        {
                            if (normalDpi > 0)
                                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Precision restore DPI " + normalDpi + " verified=" + restored);
                            AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Switch host sent=" + sent);
                        }
                        if (!sent) return;
                        hostCheck.Stop();
                        hostCheck.Start();
                    }));
                }
                catch { }
            });
        }

        // The HID++ channel came up again after the first time: the mouse is back on this computer.
        // With exactly one edge configured the pointer is put just inside it, as if it came in from
        // that side. Which computer it came from is unknown, so several edges mean no move, and a
        // reconnect right after the computer wakes up is left alone.
        private void MouseArrived()
        {
            // The mouse is back on this computer, so a keyboard switch still waiting for the keyboard to
            // wake would send it away for nothing.
            keyboard.CancelPendingSwitch();
            bool first = !readyOnce;
            readyOnce = true;
            int now = Environment.TickCount;
            if (first || !MouseHook.Enabled || !edgeSwitch.Checked || !edgePlaceCursor.Checked) return;
            if (unchecked(now - lastResume) < 60000 || unchecked(now - lastArrival) < 5000) return;
            lastArrival = now;
            int only = -1, configured = 0;
            for (int i = 0; i < edgeHosts.Length; i++)
            {
                if (edgeHosts[i] < 0) continue;
                only = i;
                configured++;
            }
            Point target;
            if (configured != 1 || !EdgeGeometry.TryGetArrivalPoint((ScreenEdge)only, Cursor.Position, screenBounds, S(24), out target)) return;
            Cursor.Position = target;
            edgeCooldownUntil = unchecked(now + 1000);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Mouse arrived; pointer placed at " + (ScreenEdge)only + " edge");
        }

        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int virtualKey);

        // The mouse sent something after a pause: it may have just come back from another host.
        // A short HID++ report from the Bolt receiver (raw input, UI thread): 10 slot 8F … is an error
        // answer, 10 slot 41 … a device connection notice (bit 6 of byte 4 set = link lost).
        private void OnBoltShortReport(byte[] bytes)
        {
            if (keyboard == null) return;
            int slot = bytes[1];
            if (bytes[2] == 0x8F && (bytes[4] & 0x0F) == KeyboardManager.SoftwareIdValue)
            {
                keyboard.SlotError(slot);
                return;
            }
            if (bytes[2] != 0x41 || slot == rawInput.MouseSlot || (bytes[4] & 0x40) != 0) return;
            // Another device came back or woke up: most likely the keyboard.
            keyboard.DeviceAnnounced(slot);
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " Keyboard Bolt dev=" + slot.ToString("X2") + " connected; reading the keyboard");
            keyboardSoon.Stop();
            keyboardSoon.Start();
        }

        private void OnMouseActivity()
        {
            if (hidpp != null) hidpp.Recheck();
        }

        private void OnHidppStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(OnHidppStatus), text);
                return;
            }
            if (liveLog.Checked)
                AddLog(DateTime.Now.ToString("HH:mm:ss.fff") + " HID++ " + text);
            // Protocol details stay in the engineering log; the main window only shows the outcome.
            if (text.StartsWith(HidppDiversionManager.ReadyMark, StringComparison.Ordinal))
            {
                mouseAway = false;
                SetDeviceStatus(Lang.T("滑鼠已就緒，專用按鍵已啟用", "Mouse ready; special buttons enabled"), true);
            }
            else if (text.StartsWith(HidppDiversionManager.AwayMark, StringComparison.Ordinal))
            {
                SetDeviceStatus(text.Substring(HidppDiversionManager.AwayMark.Length), false);
                if (!mouseAway)
                {
                    mouseAway = true;
                    ShowBattery(null);
                }
            }
            else if (text.StartsWith(HidppDiversionManager.ProblemMark, StringComparison.Ordinal))
                SetDeviceStatus(text.Substring(HidppDiversionManager.ProblemMark.Length), false);
        }

        private void ToggleEnabled()
        {
            MouseHook.Enabled = !MouseHook.Enabled;
            if (actionRingActive) CancelActionRing();
            if (!MouseHook.Enabled && precisionActive) SetPrecision(false, true);
            edgePushing = false;
            deferredPending = false;
            heldInputs.Clear();
            hidpp.SetEnabled(MouseHook.Enabled);
            OnOtherButtonsLost();
            UpdateOtherReports();
            UpdateTrayText();
            enableSwitch.Checked = MouseHook.Enabled;
            status.Text = MouseHook.Enabled ? Lang.T("已啟用按鍵對應", "Button mapping enabled") : Lang.T("已暫停：所有按鍵恢復原本功能", "Paused: all buttons behave normally");
        }

        private void ShowWindow()
        {
            allowShow = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Program.ShowWindowMessage && Program.ShowWindowMessage != 0)
            {
                ShowWindow();
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!reallyExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HideToTray();
                return;
            }
            tray.Visible = false;
            diagnostics.Dispose();
            batteryTimer.Stop();
            hostCheck.Stop();
            keyboardTimer.Stop();
            keyboardSoon.Stop();
            keyboardLevelApply.Stop();
            Microsoft.Win32.SystemEvents.PowerModeChanged -= powerHandler;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= displayHandler;
            if (appearanceSave.Enabled)
            {
                appearanceSave.Stop();
                SaveSettings();
            }
            if (precisionActive || precisionRestorePending)
            {
                // Put the normal speed back before the HID++ channel goes away; this also covers a
                // restore that was just queued and has not run yet (it is cancelled by the new sequence).
                Interlocked.Increment(ref precisionSeq);
                lock (precisionLock)
                {
                    try { hidpp.SetDpi(precisionNormalDpi); }
                    catch { }
                }
            }
            hidpp.Dispose();
            keyboard.Dispose();
            bolt.Dispose();
            othersClock.Stop();
            others.Dispose();
            rawInput.Dispose();
            MouseHook.Uninstall();
            overlay.Close();
            modeOverlay.Close();
            actionRing.Close();
            base.OnFormClosing(e);
        }
    }

    internal sealed class GestureOverlay : Form
    {
        private readonly Label label = new Label();

        public GestureOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.FromArgb(35, 35, 38);
            Opacity = 0.90;
            Size = new Size(230, 86);
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.ForeColor = Color.White;
            label.Font = new Font("Segoe UI Semibold", 11F);
            Controls.Add(label);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        public void ShowAt(Point cursor)
        {
            Location = new Point(cursor.X - Width / 2, cursor.Y - Height / 2);
            SetDirection("");
            Show();
        }

        // Names of the actions bound to up, right, down and left, in that order.
        public string[] Names = { "", "", "", "" };

        public void SetDirection(string direction)
        {
            if (direction == "左") label.Text = "←  " + Names[3];
            else if (direction == "右") label.Text = Names[1] + "  →";
            else if (direction == "上") label.Text = "↑  " + Names[0];
            else if (direction == "下") label.Text = "↓  " + Names[2];
            else if (direction == "斜") label.Text = Lang.T("方向不明\r\n請往單一方向直線移動", "Unclear direction\r\nMove in one straight line");
            else label.Text = Lang.T("按住並往 上／下／左／右 移動", "Hold and move up, down, left or right");
        }
    }

    // Shows the new state for a moment at the bottom of the screen under the pointer when precision
    // mode or the wheel mode changes (like the system volume hint). Never takes focus or clicks.
    internal sealed class ModeOverlay : Form
    {
        private readonly System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
        private readonly float scale = Theme.GetScale();
        private readonly Font titleFont = new Font(Theme.UiFont, 11F, FontStyle.Bold);
        private readonly Font detailFont = new Font(Theme.UiFont, 9F);
        private string title = "";
        private string detail = "";

        public ModeOverlay()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(35, 37, 44);
            Opacity = 0.94;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            hideTimer.Interval = 1400;
            hideTimer.Tick += delegate
            {
                hideTimer.Stop();
                Hide();
            };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000 | 0x00000080 | 0x00000020;   // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT
                return cp;
            }
        }

        private int S(float value) { return (int)Math.Round(value * scale); }

        public void ShowMode(string titleText, string detailText)
        {
            title = titleText ?? "";
            detail = detailText ?? "";
            Size titleSize = TextRenderer.MeasureText(title, titleFont, Size.Empty, TextFormatFlags.NoPrefix);
            Size detailSize = TextRenderer.MeasureText(detail, detailFont, Size.Empty, TextFormatFlags.NoPrefix);
            int width = Math.Max(S(190), Math.Max(titleSize.Width, detailSize.Width) + S(64));
            int height = S(62);
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Bounds = new Rectangle(area.Left + (area.Width - width) / 2, area.Bottom - height - S(88), width, height);
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0, 0, width, height), S(16)))
                Region = new Region(path);
            Invalidate();
            if (!Visible) Show();
            hideTimer.Stop();
            hideTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // Accent dot on the left, then the state and a detail line.
            float dot = S(12);
            using (var accent = new SolidBrush(Theme.Accent))
                g.FillEllipse(accent, S(22), (Height - dot) / 2F, dot, dot);
            int left = S(46);
            TextRenderer.DrawText(g, title, titleFont, new Point(left, S(detail.Length > 0 ? 10 : 19)), Color.White, BackColor, TextFormatFlags.NoPrefix);
            if (detail.Length > 0)
                TextRenderer.DrawText(g, detail, detailFont, new Point(left, S(34)), Color.FromArgb(176, 185, 204), BackColor, TextFormatFlags.NoPrefix);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                hideTimer.Dispose();
                titleFont.Dispose();
                detailFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ActionRingOverlay : Form
    {
        // Slot 0 is the top slice; the rest follow clockwise.
        private ActionBinding[] slots = DefaultSlots();
        private string[] labels = LabelsFor(DefaultSlots());

        public static ActionBinding[] DefaultSlots()
        {
            return new[]
            {
                ActionBinding.Of(MappedAction.Copy), ActionBinding.Of(MappedAction.PlayPause),
                ActionBinding.Of(MappedAction.BrowserForward), ActionBinding.Of(MappedAction.Paste),
                ActionBinding.Of(MappedAction.ShowDesktop), ActionBinding.Of(MappedAction.TaskView),
                ActionBinding.Of(MappedAction.BrowserBack), ActionBinding.Of(MappedAction.AltTab)
            };
        }

        private static string[] LabelsFor(ActionBinding[] bindings)
        {
            var result = new string[8];
            for (int i = 0; i < 8; i++) result[i] = bindings[i].ShortLabel;
            return result;
        }

        public void SetSlots(ActionBinding[] bindings)
        {
            slots = (ActionBinding[])bindings.Clone();
            labels = LabelsFor(slots);
            if (Visible) Redraw();
        }

        // Geometry at 96 DPI; everything is multiplied by the system scale.
        private const float OuterRadius = 152F;
        private const float InnerRadius = 60F;
        private const float HubRadius = 50F;
        private const float PopOut = 7F;
        private const float ShadowSize = 14F;
        private const float GapDegrees = 3F;

        private readonly float dpiScale;
        private float scale;
        private float bubbleFactor = 1F;
        private int maxAlpha = 255;
        private readonly string iconFont;
        private readonly System.Windows.Forms.Timer fade = new System.Windows.Forms.Timer();
        private Bitmap frame;
        private int alpha = 255;
        private Point origin;
        private Point screenCenter;
        private int selectedIndex = -1;
        private int lastDx;
        private int lastDy;

        public ActionRingOverlay()
        {
            dpiScale = Theme.GetScale();
            scale = dpiScale;
            iconFont = Theme.IconFont;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            // A borderless form would otherwise be held to the system minimum window size.
            MinimumSize = new Size(1, 1);
            int side = (int)Math.Ceiling((OuterRadius + PopOut + ShadowSize) * 2F * scale);
            Size = new Size(side, side);
            fade.Interval = 15;
            fade.Tick += delegate
            {
                alpha = Math.Min(maxAlpha, alpha + 64);
                if (alpha >= maxAlpha) fade.Stop();
                Push();
            };
        }

        // Bubble style: one round icon per slot; the name shows only for the slot under the pointer.
        public bool Bubbles;

        // White theme: light slices with dark text instead of the default dark ones. The selection stays blue.
        public bool Light;

        // Called when the pointer moves onto another slice (used for the haptic tick).
        public Action<int> SlotEntered;

        // sizeFactor and bubble are multipliers of the design size; opacityValue is 0.4-1.
        public void Configure(bool bubbles, bool light, float sizeFactor, float bubble, float opacityValue)
        {
            Bubbles = bubbles;
            Light = light;
            bubbleFactor = bubble;
            maxAlpha = (int)Math.Round(255F * Math.Max(0.4F, Math.Min(1F, opacityValue)));
            scale = dpiScale * sizeFactor;
            int side = (int)Math.Ceiling((OuterRadius + PopOut + ShadowSize) * 2F * scale);
            Size = new Size(side, side);
            if (Visible) Redraw();
        }

        // A per-pixel-alpha layered window: smooth edges and a soft shadow, and it never takes
        // focus or mouse input away from the window underneath.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x00080000 | 0x00000020 | 0x08000000 | 0x00000080; // LAYERED, TRANSPARENT, NOACTIVATE, TOOLWINDOW
                return p;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnPaintBackground(PaintEventArgs e) { }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            fade.Dispose();
            if (frame != null) frame.Dispose();
            frame = null;
            base.OnFormClosed(e);
        }

        private static string Glyph(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.BrowserBack: return "";
                case MappedAction.BrowserForward: return "";
                case MappedAction.Copy: return "";
                case MappedAction.Paste: return "";
                case MappedAction.Cut: return "";
                case MappedAction.Undo: return "";
                case MappedAction.Redo: return "";
                case MappedAction.TaskView: return "";
                case MappedAction.ShowDesktop: return "";
                case MappedAction.DesktopLeft: return "";
                case MappedAction.DesktopRight: return "";
                case MappedAction.AltTab: return "";
                case MappedAction.PlayPause: return "";
                case MappedAction.VolumeUp: return "";
                case MappedAction.VolumeDown: return "";
                case MappedAction.Mute: return "";
                case MappedAction.Enter: return "";
                case MappedAction.Escape: return "";
                case MappedAction.CustomShortcut: return "";
                case MappedAction.LaunchProgram: return "";
            }
            return ExtraGlyph(action);
        }

        private static string ExtraGlyph(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.NextTrack: return "";
                case MappedAction.PrevTrack: return "";
                case MappedAction.FileExplorer: return "";
                case MappedAction.Settings: return "";
                case MappedAction.Calculator: return "";
                case MappedAction.TaskManager: return "";
                case MappedAction.Search: return "";
                case MappedAction.StartMenu: return "";
                case MappedAction.NewTab: return "";
                case MappedAction.CloseTab: return "";
                case MappedAction.ReopenTab: return "";
                case MappedAction.Refresh: return "";
                case MappedAction.ZoomIn: return "";
                case MappedAction.ZoomOut: return "";
                case MappedAction.Screenshot: return "";
                case MappedAction.Emoji: return "";
                case MappedAction.ClipboardHistory: return "";
                case MappedAction.LockPC: return "";
                case MappedAction.QuickSettings: return "";
                case MappedAction.MinimizeWindow: return "";
                case MappedAction.MaximizeWindow: return "";
                case MappedAction.CloseWindow: return "";
                case MappedAction.SnapLeft: return "";
                case MappedAction.SnapRight: return "";
                case MappedAction.SelectAll: return "";
                case MappedAction.Save: return "";
                case MappedAction.Find: return "";
                case MappedAction.Delete: return "";
                case MappedAction.InsertDate: return "";
                case MappedAction.InsertTime: return "";
                case MappedAction.OpenSettings: return "";
                case MappedAction.PrecisionHold:
                case MappedAction.PrecisionToggle: return "\uE7C9";     // TouchPointer
                case MappedAction.WheelModeToggle: return "\uE962";     // Mouse
                case MappedAction.HostChannel1:
                case MappedAction.HostChannel2:
                case MappedAction.HostChannel3: return "\uE7F4";       // the MX Flow navigation icon
            }
            return "";
        }

        // Draws one frame without showing the window: highlight is the slice to pop out
        // (-1 for none) and numbered adds the 1-8 badges used by the customize dialog.
        internal Bitmap RenderGuide(int highlight)
        {
            int previous = selectedIndex;
            selectedIndex = highlight;
            showNumbers = true;
            try { return RenderFrame(); }
            finally
            {
                showNumbers = false;
                selectedIndex = previous;
            }
        }

        // Which slice a point inside a guide picture of the given size falls on, or -1.
        internal int SliceAt(Point point, Size picture)
        {
            float zoom = Math.Min(picture.Width, picture.Height) / (float)Width;
            double dx = (point.X - picture.Width / 2.0) / zoom;
            double dy = (point.Y - picture.Height / 2.0) / zoom;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < InnerRadius * scale || distance > (OuterRadius + PopOut) * scale) return -1;
            double angleFromUp = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
            if (angleFromUp < 0) angleFromUp += 360.0;
            return ((int)Math.Floor((angleFromUp + 22.5) / 45.0)) % 8;
        }

        private bool showNumbers;

        public ActionBinding SelectedBinding
        {
            get { return selectedIndex >= 0 ? slots[selectedIndex] : null; }
        }

        public string SelectionDebug
        {
            get { return "delta=" + lastDx + "," + lastDy + " center=" + screenCenter.X + "," + screenCenter.Y; }
        }

        // Click-to-choose: the ring stays open until a click, so only the ring itself selects
        // and it is kept fully on screen where every slice can be reached.
        public bool ClickMode;

        public void ShowAt(Point center)
        {
            if (ClickMode)
            {
                Rectangle area = Screen.FromPoint(center).Bounds;
                int half = Width / 2;
                if (area.Width >= Width && area.Height >= Height)
                    center = new Point(Math.Max(area.Left + half, Math.Min(area.Right - half, center.X)),
                        Math.Max(area.Top + half, Math.Min(area.Bottom - half, center.Y)));
            }
            screenCenter = center;
            selectedIndex = -1;
            lastDx = 0;
            lastDy = 0;
            origin = new Point(center.X - Width / 2, center.Y - Height / 2);
            Location = origin;
            alpha = 0;
            Redraw();
            Show();
            fade.Start();
        }

        public void UpdateSelection(Point cursor)
        {
            int dx = cursor.X - screenCenter.X;
            int dy = cursor.Y - screenCenter.Y;
            lastDx = dx;
            lastDy = dy;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            int next = -1;
            // The dead zone is the centre hub, so it grows with the ring on high-DPI screens.
            if (distance >= HubRadius * scale && (!ClickMode || distance <= (OuterRadius + PopOut) * scale))
            {
                double angleFromUp = Math.Atan2(dx, -dy) * 180.0 / Math.PI;
                if (angleFromUp < 0) angleFromUp += 360.0;
                next = ((int)Math.Floor((angleFromUp + 22.5) / 45.0)) % 8;
            }
            if (next != selectedIndex)
            {
                selectedIndex = next;
                Redraw();
                if (next >= 0 && SlotEntered != null) SlotEntered(next);
            }
        }

        private void Redraw()
        {
            Bitmap next = RenderFrame();
            if (frame != null) frame.Dispose();
            frame = next;
            Push();
        }

        // Hands the current frame to the compositor with the fade-in opacity.
        private void Push()
        {
            if (frame == null || IsDisposed) return;
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                bitmap = frame.GetHbitmap(Color.FromArgb(0));
                previous = SelectObject(memory, bitmap);
                var size = new RingSize { cx = frame.Width, cy = frame.Height };
                var source = new RingPoint();
                var target = new RingPoint { x = origin.X, y = origin.Y };
                var blend = new BlendFunction { SourceConstantAlpha = (byte)alpha, AlphaFormat = 1 };
                UpdateLayeredWindow(Handle, screen, ref target, ref size, memory, ref source, 0, ref blend, 2);
            }
            catch { }
            finally
            {
                if (bitmap != IntPtr.Zero)
                {
                    SelectObject(memory, previous);
                    DeleteObject(bitmap);
                }
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        private static GraphicsPath Slice(float c, float inner, float outer, float start, float sweep)
        {
            var path = new GraphicsPath();
            path.AddArc(c - outer, c - outer, outer * 2F, outer * 2F, start, sweep);
            path.AddArc(c - inner, c - inner, inner * 2F, inner * 2F, start + sweep, -sweep);
            path.CloseFigure();
            return path;
        }

        // Text and centre colours for the dark (default) and white themes.
        private Color Ink { get { return Light ? Color.FromArgb(52, 60, 78) : Color.FromArgb(214, 221, 236); } }
        private Color MutedInk { get { return Light ? Color.FromArgb(150, 158, 175) : Color.FromArgb(138, 148, 170); } }
        private Color HubFace { get { return Light ? Color.FromArgb(250, 255, 255, 255) : Color.FromArgb(246, 20, 23, 33); } }

        // Eight round icons on a circle. Hit-testing stays the same eight directions as the ring.
        private Bitmap RenderBubbles()
        {
            int side = Width;
            float c = side / 2F;
            float outer = OuterRadius * scale;
            float bubble = 35F * bubbleFactor * scale;
            // Larger bubbles push the circle outwards so neighbours keep a small gap.
            float orbit = Math.Max(outer * 0.74F, (bubble * 2F + 8F * scale) * 8F / (2F * (float)Math.PI));
            var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var glyphFont = new Font(iconFont ?? Theme.UiFont, Math.Max(8F, 23F * bubbleFactor * scale), FontStyle.Regular, GraphicsUnit.Pixel))
            using (var textFont = new Font(Theme.UiFont, Math.Max(8F, 10.5F * bubbleFactor * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var nameFont = new Font(Theme.UiFont, Math.Max(12F, 14.5F * scale), FontStyle.Bold, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Ink))
            using (var muted = new SolidBrush(MutedInk))
            using (var normal = new SolidBrush(Light ? Color.FromArgb(246, 250, 251, 253) : Color.FromArgb(240, 30, 34, 47)))
            using (var shadow = new SolidBrush(Color.FromArgb(Light ? 14 : 12, 0, 0, 0)))
            using (var edge = new Pen(Light ? Color.FromArgb(70, 120, 130, 150) : Color.FromArgb(70, 255, 255, 255), Math.Max(1F, 1.2F * scale)))
            using (var chosenEdge = new Pen(Color.FromArgb(120, 255, 255, 255), Math.Max(1F, 1.4F * scale)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                for (int i = 0; i < 8; i++)
                {
                    bool selected = i == selectedIndex;
                    double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                    float x = c + (float)Math.Cos(angle) * orbit;
                    float y = c + (float)Math.Sin(angle) * orbit;
                    float r = selected ? bubble + 4.5F * scale : bubble;
                    for (int k = 5; k >= 1; k--)
                    {
                        float grow = r + k * 1.3F * scale;
                        g.FillEllipse(shadow, x - grow, y - grow + 2F * scale, grow * 2F, grow * 2F);
                    }
                    var circle = new RectangleF(x - r, y - r, r * 2F, r * 2F);
                    if (selected)
                    {
                        using (var glow = new LinearGradientBrush(circle, Color.FromArgb(250, 64, 140, 255), Color.FromArgb(250, 24, 92, 220), 60F))
                            g.FillEllipse(glow, circle);
                    }
                    else g.FillEllipse(normal, circle);
                    g.DrawEllipse(selected ? chosenEdge : edge, circle);

                    ActionBinding slot = slots[i];
                    string glyph = iconFont == null || slot.IsPassThrough ? "" : Glyph(slot.Action);
                    Brush ink = selected ? white : slot.IsPassThrough ? muted : soft;
                    // Without the icon font (or for an unset slot) the short name goes inside the bubble.
                    if (glyph.Length > 0)
                    {
                        // DrawString pads the text box and follows the font's line metrics, which leaves
                        // icon glyphs off-centre. Centre the glyph's actual outline on the bubble instead.
                        using (var outline = new GraphicsPath())
                        {
                            outline.AddString(glyph, glyphFont.FontFamily, (int)FontStyle.Regular, glyphFont.Size,
                                PointF.Empty, StringFormat.GenericTypographic);
                            RectangleF bounds = outline.GetBounds();
                            using (var move = new Matrix())
                            {
                                move.Translate(x - (bounds.Left + bounds.Width / 2F), y - (bounds.Top + bounds.Height / 2F));
                                outline.Transform(move);
                            }
                            g.FillPath(ink, outline);
                        }
                    }
                    else g.DrawString(labels[i], textFont, ink, circle, center);
                }

                if (selectedIndex >= 0)
                {
                    string name = labels[selectedIndex];
                    float width = Math.Min(g.MeasureString(name, nameFont).Width + 26F * scale, Math.Max(60F * scale, (orbit - bubble) * 2F - 6F * scale));
                    float height = Math.Max(26F, 32F * scale);
                    var pill = new RectangleF(c - width / 2F, c - height / 2F, width, height);
                    using (var back = new SolidBrush(HubFace))
                    using (var pillEdge = new Pen(Light ? Color.FromArgb(70, 120, 130, 150) : Color.Transparent, Math.Max(1F, scale)))
                    using (var title = new SolidBrush(Light ? Theme.Text : Color.White))
                    using (GraphicsPath shape = Theme.RoundedRectangle(pill, height / 2F - 0.5F))
                    {
                        g.FillPath(back, shape);
                        if (Light) g.DrawPath(pillEdge, shape);
                        using (var single = new StringFormat(center) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                            g.DrawString(name, nameFont, title, pill, single);
                    }
                }
                else
                {
                    // A small dot marks the centre, where releasing or clicking cancels.
                    float dot = Math.Max(5F, 8F * scale);
                    using (var back = new SolidBrush(Light ? Color.FromArgb(220, 255, 255, 255) : Color.FromArgb(190, 20, 23, 33)))
                    using (var ring = new Pen(Light ? Color.FromArgb(130, 120, 130, 150) : Color.FromArgb(130, 255, 255, 255), Math.Max(1F, 1.4F * scale)))
                    {
                        g.FillEllipse(back, c - dot, c - dot, dot * 2F, dot * 2F);
                        g.DrawEllipse(ring, c - dot, c - dot, dot * 2F, dot * 2F);
                    }
                }
            }
            return bitmap;
        }

        internal Bitmap RenderFrame()
        {
            if (Bubbles && !showNumbers) return RenderBubbles();
            int side = Width;
            float c = side / 2F;
            float outer = OuterRadius * scale;
            float inner = InnerRadius * scale;
            float hub = HubRadius * scale;
            var bitmap = new Bitmap(side, side, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var labelFont = new Font(Theme.UiFont, 12.5F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hubFont = new Font(Theme.UiFont, 14.5F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var hintFont = new Font(Theme.UiFont, 11F * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var glyphFont = new Font(iconFont ?? Theme.UiFont, 21F * scale, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var white = new SolidBrush(Color.White))
            using (var soft = new SolidBrush(Ink))
            using (var muted = new SolidBrush(MutedInk))
            using (var title = new SolidBrush(Light ? Theme.Text : Color.White))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);

                // Stacked translucent discs fade out towards the edge and read as a soft shadow.
                using (var shadow = new SolidBrush(Color.FromArgb(7, 0, 0, 0)))
                {
                    for (int i = 12; i >= 1; i--)
                    {
                        float r = outer + ShadowSize * scale * i / 12F;
                        g.FillEllipse(shadow, c - r, c - r + 2F * scale, r * 2F, r * 2F);
                    }
                }

                using (var normal = new SolidBrush(Light ? Color.FromArgb(244, 250, 251, 253) : Color.FromArgb(238, 30, 34, 47)))
                using (var edge = new Pen(Light ? Color.FromArgb(60, 120, 130, 150) : Color.FromArgb(46, 255, 255, 255), Math.Max(1F, scale)))
                {
                    for (int i = 0; i < 8; i++)
                    {
                        bool selected = i == selectedIndex;
                        float start = -112.5F + i * 45F + GapDegrees / 2F;
                        float reach = selected ? outer + PopOut * scale : outer;
                        using (GraphicsPath slice = Slice(c, inner, reach, start, 45F - GapDegrees))
                        {
                            if (selected)
                            {
                                using (var glow = new LinearGradientBrush(new RectangleF(0, 0, side, side),
                                    Color.FromArgb(250, 64, 140, 255), Color.FromArgb(250, 24, 92, 220), 60F))
                                    g.FillPath(glow, slice);
                            }
                            else
                            {
                                g.FillPath(normal, slice);
                                g.DrawPath(edge, slice);
                            }
                        }

                        double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                        float middle = (inner + reach) / 2F;
                        float x = c + (float)Math.Cos(angle) * middle;
                        float y = c + (float)Math.Sin(angle) * middle;
                        ActionBinding slot = slots[i];
                        string glyph = iconFont == null || slot.IsPassThrough ? "" : Glyph(slot.Action);
                        Brush ink = selected ? white : slot.IsPassThrough ? muted : soft;
                        float labelY = y;
                        if (glyph.Length > 0)
                        {
                            g.DrawString(glyph, glyphFont, ink, new RectangleF(x - 20F * scale, y - 27F * scale, 40F * scale, 30F * scale), center);
                            labelY = y + 15F * scale;
                        }
                        g.DrawString(labels[i], labelFont, ink,
                            new RectangleF(x - 43F * scale, labelY - 17F * scale, 86F * scale, 34F * scale), center);
                    }
                }

                using (var hubBrush = new SolidBrush(HubFace))
                using (var hubEdge = new Pen(selectedIndex >= 0 ? Color.FromArgb(220, 64, 140, 255)
                    : Light ? Color.FromArgb(80, 120, 130, 150) : Color.FromArgb(60, 255, 255, 255), 1.5F * scale))
                {
                    g.FillEllipse(hubBrush, c - hub, c - hub, hub * 2F, hub * 2F);
                    g.DrawEllipse(hubEdge, c - hub, c - hub, hub * 2F, hub * 2F);
                }
                bool none = selectedIndex < 0;
                string hubText = none ? (showNumbers ? "Actions Ring" : ClickMode ? Lang.T("點選功能", "Choose") : Lang.T("移動選擇", "Select")) : labels[selectedIndex];
                var hubArea = new RectangleF(c - hub + 4F * scale, c - 24F * scale, hub * 2F - 8F * scale, 30F * scale);
                // Long custom shortcuts or program names drop to the smaller font and stay on one line.
                bool compact = g.MeasureString(hubText, hubFont).Width > hubArea.Width;
                using (var single = new StringFormat(center) { FormatFlags = StringFormatFlags.NoWrap, Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(hubText, compact ? hintFont : hubFont, none ? soft : title, hubArea, single);
                string hubHint = ClickMode
                    ? (none ? Lang.T("點此取消", "click to cancel") : Lang.T("點一下執行", "click to run"))
                    : (none ? Lang.T("放開取消", "release to cancel") : Lang.T("放開執行", "release to run"));
                if (showNumbers)
                {
                    // A numbered tab on the rim of each slice, matching the rows of the customize dialog.
                    using (var badgeFont = new Font("Segoe UI", 11F * scale, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (var accent = new SolidBrush(Color.FromArgb(47, 107, 255)))
                    using (var rim = new Pen(Color.White, 1.5F * scale))
                    {
                        for (int i = 0; i < 8; i++)
                        {
                            bool selected = i == selectedIndex;
                            double angle = (-90.0 + i * 45.0) * Math.PI / 180.0;
                            float radius = (selected ? outer + PopOut * scale : outer) - 2F * scale;
                            float r = 10F * scale;
                            var circle = new RectangleF(c + (float)Math.Cos(angle) * radius - r, c + (float)Math.Sin(angle) * radius - r, r * 2F, r * 2F);
                            g.FillEllipse(selected ? white : accent, circle);
                            g.DrawEllipse(rim, circle);
                            g.DrawString((i + 1).ToString(), badgeFont, selected ? accent : white, circle, center);
                        }
                    }
                }
                if (showNumbers)
                    hubHint = Lang.T("點一格來設定", "click a slice");
                g.DrawString(hubHint, hintFont, muted,
                    new RectangleF(c - hub, c + 6F * scale, hub * 2F, 18F * scale), center);
            }
            return bitmap;
        }

        [StructLayout(LayoutKind.Sequential)] private struct RingPoint { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct RingSize { public int cx, cy; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destination, ref RingPoint position,
            ref RingSize size, IntPtr source, ref RingPoint sourcePoint, int colorKey, ref BlendFunction blend, int flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);
        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);
    }

    internal static class ActionRunner
    {
        private const ushort KEYEVENTF_KEYUP = 0x0002;
        private const ushort VK_CONTROL = 0x11;
        private const ushort VK_MENU = 0x12;
        private const ushort VK_LWIN = 0x5B;
        private const ushort VK_SHIFT = 0x10;

        // Set by MainForm: shows this app's settings window (posted to the UI thread).
        public static Action OpenSettings;
        // Set by MainForm: precision mode and the wheel mode. Run from the ring, a gesture or the thumb
        // wheel there is no "holding", so "precision mode (hold)" toggles there.
        public static Func<MappedAction, bool> MouseAction;

        public static bool Run(ActionBinding binding)
        {
            if (binding == null) return false;
            if (binding.Action == MappedAction.CustomShortcut) return RunShortcut(binding.Argument);
            if (binding.Action == MappedAction.LaunchProgram) return Launch(binding.Argument);
            return Run(binding.Action);
        }

        private static bool RunShortcut(string text)
        {
            Keys key;
            bool ctrl, alt, shift, win;
            if (!ShortcutText.TryParse(text, out key, out ctrl, out alt, out shift, out win)) return false;
            var keys = new List<ushort>();
            if (ctrl) keys.Add(VK_CONTROL);
            if (alt) keys.Add(VK_MENU);
            if (shift) keys.Add(VK_SHIFT);
            if (win) keys.Add(VK_LWIN);
            keys.Add((ushort)key);
            bool ok = true;
            for (int i = 0; i < keys.Count; i++) ok = Key(keys[i], false, IsExtended(keys[i])) && ok;
            for (int i = keys.Count - 1; i >= 0; i--) ok = Key(keys[i], true, IsExtended(keys[i])) && ok;
            return ok;
        }

        // Navigation-cluster keys need the extended flag, otherwise Shift+arrow is read as numpad input.
        private static bool IsExtended(ushort key)
        {
            return (key >= 0x21 && key <= 0x28) || key == 0x2D || key == 0x2E ||
                key == 0x5B || key == 0x5C || key == 0x6F || key == 0x90;
        }

        // A quoted first token is the program and the remainder its arguments; otherwise the
        // whole text is one target, so unquoted paths with spaces and URLs work as typed.
        public static void SplitCommand(string command, out string file, out string arguments)
        {
            file = (command ?? "").Trim();
            arguments = "";
            if (file.StartsWith("\""))
            {
                int end = file.IndexOf('"', 1);
                if (end > 0)
                {
                    arguments = file.Substring(end + 1).Trim();
                    file = file.Substring(1, end - 1);
                }
            }
        }

        private static bool Launch(string command)
        {
            string file, arguments;
            SplitCommand(command, out file, out arguments);
            if (file.Length == 0) return false;
            try
            {
                var info = new ProcessStartInfo(file, arguments);
                info.UseShellExecute = true;
                if (File.Exists(file))
                {
                    string directory = Path.GetDirectoryName(file);
                    if (!String.IsNullOrEmpty(directory)) info.WorkingDirectory = directory;
                }
                Process.Start(info);
                return true;
            }
            catch { return false; }
        }

        public static bool Run(MappedAction action)
        {
            switch (action)
            {
                case MappedAction.PassThrough: return false;
                case MappedAction.OpenSettings:
                    if (OpenSettings == null) return false;
                    OpenSettings();
                    return true;
                case MappedAction.PrecisionHold:
                case MappedAction.PrecisionToggle:
                case MappedAction.WheelModeToggle:
                case MappedAction.HostChannel1:
                case MappedAction.HostChannel2:
                case MappedAction.HostChannel3:
                    return MouseAction != null && MouseAction(action);
                case MappedAction.BrowserBack: return Tap(0xA6);
                case MappedAction.BrowserForward: return Tap(0xA7);
                case MappedAction.Copy: return Chord(VK_CONTROL, 0x43);
                case MappedAction.Paste: return Chord(VK_CONTROL, 0x56);
                case MappedAction.Cut: return Chord(VK_CONTROL, 0x58);
                case MappedAction.Undo: return Chord(VK_CONTROL, 0x5A);
                case MappedAction.Redo: return Chord(VK_CONTROL, 0x59);
                case MappedAction.TaskView: return Chord(VK_LWIN, 0x09);
                case MappedAction.ShowDesktop: return Chord(VK_LWIN, 0x44);
                case MappedAction.DesktopLeft: return Chord(VK_LWIN, VK_CONTROL, 0x25);
                case MappedAction.DesktopRight: return Chord(VK_LWIN, VK_CONTROL, 0x27);
                case MappedAction.AltTab: return Chord(VK_MENU, 0x09);
                case MappedAction.PlayPause: return Tap(0xB3);
                case MappedAction.VolumeUp: return Tap(0xAF);
                case MappedAction.VolumeDown: return Tap(0xAE);
                case MappedAction.Mute: return Tap(0xAD);
                case MappedAction.Enter: return Tap(0x0D);
                case MappedAction.Escape: return Tap(0x1B);
                case MappedAction.NextTrack: return Tap(0xB0);
                case MappedAction.PrevTrack: return Tap(0xB1);
                case MappedAction.FileExplorer: return Combo(VK_LWIN, 0x45);
                case MappedAction.Settings: return Combo(VK_LWIN, 0x49);
                case MappedAction.Calculator: return Launch("calc.exe");
                case MappedAction.TaskManager: return Combo(VK_CONTROL, VK_SHIFT, 0x1B);
                case MappedAction.Search: return Combo(VK_LWIN, 0x53);
                case MappedAction.StartMenu: return Combo(VK_LWIN);
                case MappedAction.NewTab: return Combo(VK_CONTROL, 0x54);
                case MappedAction.CloseTab: return Combo(VK_CONTROL, 0x57);
                case MappedAction.ReopenTab: return Combo(VK_CONTROL, VK_SHIFT, 0x54);
                case MappedAction.Refresh: return Combo(0x74);
                case MappedAction.ZoomIn: return Combo(VK_CONTROL, 0xBB);
                case MappedAction.ZoomOut: return Combo(VK_CONTROL, 0xBD);
                case MappedAction.Screenshot: return Combo(VK_LWIN, VK_SHIFT, 0x53);
                case MappedAction.Emoji: return Combo(VK_LWIN, 0xBE);
                case MappedAction.ClipboardHistory: return Combo(VK_LWIN, 0x56);
                // Windows ignores a synthesized Win+L, so this one goes through the API.
                case MappedAction.LockPC: return LockWorkStation();
                case MappedAction.QuickSettings: return Combo(VK_LWIN, 0x41);
                case MappedAction.MinimizeWindow: return Combo(VK_LWIN, 0x28);
                case MappedAction.MaximizeWindow: return Combo(VK_LWIN, 0x26);
                case MappedAction.CloseWindow: return Combo(VK_MENU, 0x73);
                case MappedAction.SnapLeft: return Combo(VK_LWIN, 0x25);
                case MappedAction.SnapRight: return Combo(VK_LWIN, 0x27);
                case MappedAction.SelectAll: return Combo(VK_CONTROL, 0x41);
                case MappedAction.Save: return Combo(VK_CONTROL, 0x53);
                case MappedAction.Find: return Combo(VK_CONTROL, 0x46);
                case MappedAction.Delete: return Combo(0x2E);
                case MappedAction.InsertDate: return TypeText(DateTime.Now.ToString("yyyy/MM/dd", System.Globalization.CultureInfo.InvariantCulture));
                case MappedAction.InsertTime: return TypeText(DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            }
            return false;
        }

        // Like Chord, but navigation-cluster and Win keys carry the extended flag.
        private static bool Combo(params ushort[] keys)
        {
            bool ok = true;
            for (int i = 0; i < keys.Length; i++) ok = Key(keys[i], false, IsExtended(keys[i])) && ok;
            for (int i = keys.Length - 1; i >= 0; i--) ok = Key(keys[i], true, IsExtended(keys[i])) && ok;
            return ok;
        }

        // Types text as Unicode characters, independent of the keyboard layout or IME state.
        private static bool TypeText(string text)
        {
            bool ok = text.Length > 0;
            foreach (char c in text)
            {
                for (int phase = 0; phase < 2; phase++)
                {
                    INPUT input = new INPUT();
                    input.type = 1;
                    input.U.ki.wScan = c;
                    input.U.ki.dwFlags = 0x0004u | (phase == 1 ? (uint)KEYEVENTF_KEYUP : 0u);
                    ok = SendInput(1, new[] { input }, Marshal.SizeOf(typeof(INPUT))) == 1 && ok;
                }
            }
            return ok;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool LockWorkStation();

        private static bool Tap(ushort key)
        {
            bool down = Key(key, false);
            bool up = Key(key, true);
            return down && up;
        }

        private static bool Chord(params ushort[] keys)
        {
            bool ok = true;
            for (int i = 0; i < keys.Length; i++) ok = Key(keys[i], false) && ok;
            for (int i = keys.Length - 1; i >= 0; i--) ok = Key(keys[i], true) && ok;
            return ok;
        }

        private static bool Key(ushort key, bool up)
        {
            return Key(key, up, false);
        }

        private static bool Key(ushort key, bool up, bool extended)
        {
            INPUT input = new INPUT();
            input.type = 1;
            input.U.ki.wVk = key;
            input.U.ki.dwFlags = (up ? KEYEVENTF_KEYUP : (ushort)0) | (extended ? 1u : 0u);
            INPUT[] inputs = { input };
            return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT { public uint type; public InputUnion U; }
        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx, dy;
            public uint mouseData, dwFlags, time;
            public IntPtr dwExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk, wScan;
            public uint dwFlags, time;
            public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    }

    // A host stored on one of the mouse's Easy-Switch channels.
    internal sealed class HostEntry
    {
        public int Index;
        public string Name = "";
        public bool Paired;
        public int Bus;     // 4 = Bluetooth, 5 = Logi Bolt (matched against replies captured from the mouse)

        public string BusName
        {
            get { return Bus == 4 ? "Bluetooth" : Bus == 5 ? "Logi Bolt" : ""; }
        }
    }

    internal enum ScreenEdge { Left, Right, Top, Bottom }

    // Which outer edge of the desktop the pointer is touching. With several monitors only the
    // outer edge of the outermost monitor counts; edges where two monitors meet never do.
    internal static class EdgeGeometry
    {
        private static Rectangle Union(Rectangle[] screens)
        {
            Rectangle all = screens[0];
            for (int i = 1; i < screens.Length; i++) all = Rectangle.Union(all, screens[i]);
            return all;
        }

        private static bool Outermost(Rectangle screen, ScreenEdge edge, Rectangle all)
        {
            switch (edge)
            {
                case ScreenEdge.Left: return screen.Left <= all.Left;
                case ScreenEdge.Right: return screen.Right >= all.Right;
                case ScreenEdge.Top: return screen.Top <= all.Top;
                default: return screen.Bottom >= all.Bottom;
            }
        }

        // A corner touches two edges at once and is ignored.
        public static bool TryGetEdge(Point point, Rectangle[] screens, out ScreenEdge edge)
        {
            edge = ScreenEdge.Left;
            if (screens == null || screens.Length == 0) return false;
            Rectangle all = Union(screens);
            foreach (Rectangle screen in screens)
            {
                if (!Rectangle.Inflate(screen, 1, 1).Contains(point)) continue;
                int found = 0;
                // The pointer's range is Left ... Right-1, so the right and bottom sides get one more pixel.
                if (point.X <= screen.Left + 1 && Outermost(screen, ScreenEdge.Left, all)) { edge = ScreenEdge.Left; found++; }
                if (point.X >= screen.Right - 2 && Outermost(screen, ScreenEdge.Right, all)) { edge = ScreenEdge.Right; found++; }
                if (point.Y <= screen.Top + 1 && Outermost(screen, ScreenEdge.Top, all)) { edge = ScreenEdge.Top; found++; }
                if (point.Y >= screen.Bottom - 2 && Outermost(screen, ScreenEdge.Bottom, all)) { edge = ScreenEdge.Bottom; found++; }
                return found == 1;
            }
            return false;
        }

        // Where to put the pointer when the mouse comes back: just inside the given outer edge,
        // keeping its position along the edge when that still falls on the monitor.
        public static bool TryGetArrivalPoint(ScreenEdge edge, Point near, Rectangle[] screens, int inset, out Point result)
        {
            result = near;
            if (screens == null || screens.Length == 0) return false;
            Rectangle all = Union(screens);
            bool horizontal = edge == ScreenEdge.Left || edge == ScreenEdge.Right;
            bool have = false;
            Rectangle chosen = screens[0];
            foreach (Rectangle screen in screens)
            {
                if (!Outermost(screen, edge, all)) continue;
                bool spans = horizontal ? near.Y >= screen.Top && near.Y < screen.Bottom : near.X >= screen.Left && near.X < screen.Right;
                if (!have || spans)
                {
                    chosen = screen;
                    have = true;
                    if (spans) break;
                }
            }
            if (!have) return false;
            int x = Math.Min(Math.Max(near.X, chosen.Left + inset), chosen.Right - 1 - inset);
            int y = Math.Min(Math.Max(near.Y, chosen.Top + inset), chosen.Bottom - 1 - inset);
            switch (edge)
            {
                case ScreenEdge.Left: result = new Point(chosen.Left + inset, y); break;
                case ScreenEdge.Right: result = new Point(chosen.Right - 1 - inset, y); break;
                case ScreenEdge.Top: result = new Point(x, chosen.Top + inset); break;
                default: result = new Point(x, chosen.Bottom - 1 - inset); break;
            }
            return true;
        }
    }

    internal enum MouseHookKind
    {
        Move, LeftDown, LeftUp, RightDown, RightUp, MiddleDown, MiddleUp, XButton1Down, XButton1Up, XButton2Down, XButton2Up, WheelLeft, WheelRight, Other
    }

    internal sealed class MouseHookEvent
    {
        public MouseHookKind Kind;
        public Point Point;
        public bool Injected;
    }

    internal static class MouseHook
    {
        public delegate bool MouseEventHandler(MouseHookEvent e);
        public static event MouseEventHandler MouseEvent;
        public static bool Enabled = true;
        // Tick of the latest event from any mouse; the other-devices reader waits for the pointer to rest.
        public static volatile int LastEventTick = Environment.TickCount;
        private static IntPtr hook;
        private static readonly HookProc callback = HookCallback;
        private const int WH_MOUSE_LL = 14;
        private const int WM_MOUSEMOVE = 0x0200, WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
        private const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
        private const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C, WM_MOUSEHWHEEL = 0x020E;

        public static void Install()
        {
            hook = SetWindowsHookEx(WH_MOUSE_LL, callback, IntPtr.Zero, 0);
            if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }

        public static void Uninstall()
        {
            if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        }

        private static IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
        {
            if (code >= 0) LastEventTick = Environment.TickCount;
            if (code >= 0 && Enabled && MouseEvent != null)
            {
                MSLLHOOKSTRUCT data = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(MSLLHOOKSTRUCT));
                MouseHookKind kind = MouseHookKind.Other;
                int msg = wParam.ToInt32();
                if (msg == WM_MOUSEMOVE) kind = MouseHookKind.Move;
                else if (msg == WM_LBUTTONDOWN) kind = MouseHookKind.LeftDown;
                else if (msg == WM_LBUTTONUP) kind = MouseHookKind.LeftUp;
                else if (msg == WM_RBUTTONDOWN) kind = MouseHookKind.RightDown;
                else if (msg == WM_RBUTTONUP) kind = MouseHookKind.RightUp;
                else if (msg == WM_MBUTTONDOWN) kind = MouseHookKind.MiddleDown;
                else if (msg == WM_MBUTTONUP) kind = MouseHookKind.MiddleUp;
                else if (msg == WM_XBUTTONDOWN || msg == WM_XBUTTONUP)
                {
                    int button = (short)((data.mouseData >> 16) & 0xFFFF);
                    if (button == 1) kind = msg == WM_XBUTTONDOWN ? MouseHookKind.XButton1Down : MouseHookKind.XButton1Up;
                    else if (button == 2) kind = msg == WM_XBUTTONDOWN ? MouseHookKind.XButton2Down : MouseHookKind.XButton2Up;
                }
                else if (msg == WM_MOUSEHWHEEL)
                {
                    short delta = (short)((data.mouseData >> 16) & 0xFFFF);
                    kind = delta < 0 ? MouseHookKind.WheelLeft : MouseHookKind.WheelRight;
                }
                var ev = new MouseHookEvent { Kind = kind, Point = new Point(data.pt.x, data.pt.y), Injected = (data.flags & 1) != 0 };
                try { if (MouseEvent(ev)) return new IntPtr(1); }
                catch { }
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        }

        private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);
        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] private struct MSLLHOOKSTRUCT
        {
            public POINT pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc fn, IntPtr mod, uint threadId);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);
    }

    // Easy-Switch channel names (HID++ 0x1815 Hosts Info). When the channel the device is on now (this computer)
    // holds a different name than this computer (a Bolt-paired channel stored as "Logitech Bolt receiver", or a
    // renamed computer), this computer's name is written to the device, as Logi Options+ and Solaar
    // (hidpp20.py get_host_names / set_host_name) do. Only the current channel is written.
    //   function 0: [capabilities (bit 0 readable, bit 1 writable), ?, channel count, current channel]
    //   function 1: [channel, status, bus, ?, name length, name limit]
    //   function 4: write name [current channel, offset, up to 14 bytes]
    // Pure logic; tools/host-name-test tests it on its own.
    internal static class HostName
    {
        public const int ChunkSize = 14;

        public static string ComputerName
        {
            get
            {
                try { return Environment.MachineName; }
                catch { return ""; }
            }
        }

        // Quotes, surrounding spaces and case do not count as a difference.
        public static bool Same(string a, string b) { return Normalize(a) == Normalize(b); }

        private static string Normalize(string text)
        {
            return (text ?? "").Replace('\u2019', '\'').Replace('\u2018', '\'').Trim().ToLowerInvariant();
        }

        // UTF-8, at most maxLength bytes, never cutting a character in two.
        public static byte[] Encode(string name, int maxLength)
        {
            var bytes = new List<byte>();
            var e = System.Globalization.StringInfo.GetTextElementEnumerator(name ?? "");
            while (e.MoveNext())
            {
                byte[] piece = Encoding.UTF8.GetBytes((string)e.Current);
                if (bytes.Count + piece.Length > maxLength) break;
                bytes.AddRange(piece);
            }
            return bytes.ToArray();
        }

        // Write when the device can, this computer has a name, and the stored one differs from it as it would be
        // stored (cut to the limit, so an over-long name is not written again on every read).
        public static bool NeedsWrite(byte capabilities, string stored, string computer, int maxLength)
        {
            if ((capabilities & 0x02) == 0 || maxLength <= 0) return false;
            string fitted = Encoding.UTF8.GetString(Encode(computer, maxLength));
            return fitted.Length > 0 && !Same(stored, fitted);
        }

        // Function 4 parameters: [channel, offset, up to 14 bytes] per piece.
        public static List<byte[]> WritePackets(int host, byte[] name)
        {
            var packets = new List<byte[]>();
            for (int offset = 0; offset < name.Length; offset += ChunkSize)
            {
                int take = Math.Min(ChunkSize, name.Length - offset);
                byte[] p = new byte[2 + take];
                p[0] = (byte)host;
                p[1] = (byte)offset;
                Array.Copy(name, offset, p, 2, take);
                packets.Add(p);
            }
            return packets;
        }
    }

    // Pure HID++ codecs. Protocol references: Solaar settings_templates.py ScrollRatchetTorque,
    // DisableKeyboardKeys, Multiplatform, Backlight2Duration*, ThumbInvert, HiresSmoothResolution;
    // hidpp20.py Backlight. Replies include their four-byte HID++ header.
    internal static class SolaarFeatures
    {
        public static byte[] Torque(byte[] caps, byte[] current, int value)
        {
            if (caps == null || caps.Length < 5 || (caps[4] & 1) == 0 || current == null || current.Length < 7 ||
                current[4] < 1 || current[4] > 2 || value < 1 || value > 100) return null;
            return new byte[] { current[4], current[5], (byte)value };
        }
        public static bool? MaskValue(byte[] reply, int offset, byte mask, int width)
        {
            if (reply == null || offset < 0 || offset >= width || reply.Length < 4 + width) return null;
            return (reply[4 + offset] & mask) != 0;
        }
        public static bool Matches(byte[] reply, byte[] parameters)
        {
            if (reply == null || parameters == null || parameters.Length == 0 || reply.Length < 4 + parameters.Length) return false;
            for (int i = 0; i < parameters.Length; i++) if (reply[4 + i] != parameters[i]) return false;
            return true;
        }
        public static byte[] Masked(byte[] current, int offset, byte mask, bool on, int width)
        {
            if (current == null || width <= 0 || offset < 0 || offset >= width || current.Length < 4 + width) return null;
            byte[] p = new byte[width];
            Array.Copy(current, 4, p, 0, width);
            p[offset] = (byte)(on ? p[offset] | mask : p[offset] & ~mask);
            return p;
        }
        public static byte[] DisabledKey(byte[] caps, byte[] current, int bit, bool disabled)
        {
            if (caps == null || caps.Length < 5 || bit < 0 || bit > 7 || (caps[4] & (1 << bit)) == 0) return null;
            return Masked(current, 0, (byte)(1 << bit), disabled, 1);
        }
        public static string DisableName(int bit)
        {
            string[] names = { "Caps Lock", "Num Lock", "Scroll Lock", "Insert", "Win / ⌘" };
            return bit >= 0 && bit < names.Length ? names[bit] : "0x" + (1 << bit).ToString("X2");
        }
        internal sealed class Platform
        {
            public int Id;
            public string Name;
            public override string ToString() { return Name; }
        }
        private static string Version(int value)
        {
            return value == 0 ? "" : (value & 255) == 0 ? (value >> 8).ToString() : (value >> 8) + "." + (value & 255);
        }
        public static Platform ParsePlatform(byte[] d) { return ParsePlatform(d, 0); }
        private static Platform ParsePlatform(byte[] d, int preferredMask)
        {
            if (d == null || d.Length < 12) return null;
            int flags = (d[6] << 8) | d[7];
            string[] names = { "Linux", "macOS", "Windows", "iOS", "Android", "WebOS", "Chrome", "WinEmb", "Tizen" };
            int[] masks = { 0x0400, 0x2000, 0x0100, 0x4000, 0x1000, 0x8000, 0x0800, 0x0200, 0x0001 };
            for (int i = 0; i < masks.Length; i++)
            {
                if ((flags & masks[i]) == 0 || (preferredMask != 0 && masks[i] != preferredMask)) continue;
                int low = (d[8] << 8) | d[9], high = (d[10] << 8) | d[11];
                return new Platform { Id = d[4], Name = names[i] + (low == 0 && high == 0 ? "" : " " + Version(low) + "–" + Version(high)) };
            }
            return null;
        }
        public static List<Platform> Platforms(List<byte[]> descriptors)
        {
            var choices = new List<Platform>();
            foreach (int mask in new[] { 0x0400, 0x2000, 0x0100, 0x4000, 0x1000, 0x8000, 0x0800, 0x0200, 0x0001 })
                foreach (byte[] d in descriptors)
                {
                    Platform p = ParsePlatform(d, mask);
                    if (p != null && !choices.Exists(delegate(Platform old) { return old.Id == p.Id || old.Name == p.Name; })) choices.Add(p);
                }
            return choices;
        }
        public static byte[] PlatformWrite(byte[] info, List<Platform> choices, int selected)
        {
            if (info == null || info.Length < 11 || (info[4] & 2) == 0 || selected < 0 || selected > 255 ||
                choices == null || !choices.Exists(delegate(Platform p) { return p.Id == selected; })) return null;
            return new byte[] { 0xFF, (byte)selected };
        }
        public static bool PlatformMatches(byte[] info, int selected, int beforeHost, int afterHost)
        {
            return info != null && info.Length >= 11 && beforeHost >= 0 && beforeHost == afterHost && info[10] == selected;
        }
        public static int Duration(byte[] reply, int field)
        {
            if (reply == null || reply.Length < 16 || field < 0 || field > 2) return -1;
            int i = 10 + field * 2;
            return (reply[i] | (reply[i + 1] << 8)) * 5;
        }
        public static byte[] BacklightDelay(byte[] r, int version, int field, int seconds)
        {
            if (r == null || r.Length < 16 || version < 3 || field < 0 || field > 2 || seconds < 1 || seconds > 600) return null;
            int mode = (r[5] >> 3) & 3;
            byte[] p = new byte[] { r[4], (byte)((r[5] & 7) | (mode << 3)), 0xFF, mode == 3 ? r[9] : (byte)0,
                r[10], r[11], r[12], r[13], r[14], r[15] };
            int units = (seconds + 4) / 5, i = 4 + field * 2;
            p[i] = (byte)(units & 255); p[i + 1] = (byte)(units >> 8);
            return p;
        }
        public static bool BacklightMatches(byte[] r, byte[] p)
        {
            if (r == null || r.Length < 16 || p == null || p.Length != 10 || r[4] != p[0] || (r[5] & 0x1F) != p[1] ||
                (((p[1] >> 3) & 3) == 3 && r[9] != p[3])) return false;
            for (int i = 0; i < 6; i++) if (r[10 + i] != p[4 + i]) return false;
            return true;
        }
    }

    internal sealed class HidppDiversionManager : IDisposable
    {
        private const ushort TargetFeature = 0x1B04;
        private bool hostNameTried;     // this connection already tried to write the channel name (under sync)
        private const byte SoftwareId = 0x0A;
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint OpenExisting = 3;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfDeviceInterface = 0x00000010;
        private const int HidpStatusSuccess = 0x00110000;
        private static readonly int[] TargetControls = { 0x00C3, 0x01A0 };
        // Prefixes on report text that tell the UI the outcome without parsing translated wording.
        public const string ReadyMark = "[ready] ";
        public const string ProblemMark = "[!] ";
        public const string AwayMark = "[away] ";

        private readonly Action<string> report;
        private readonly Action<int, int> ready;
        private readonly object sync = new object();
        private Thread worker;
        private volatile bool disposed;
        private volatile bool desiredEnabled = true;
        private HidTransport transport;
        private int deviceIndex = -1;
        private int featureIndex = -1;
        private readonly List<int> divertedControls = new List<int>();
        private int recheckBusy;
        private int silentChecks;
        private int lastBoltIndex = -1;

        public HidppDiversionManager(Action<string> reportAction, Action<int, int> readyAction)
        {
            report = reportAction;
            ready = readyAction;
        }

        public void Start()
        {
            if (worker != null) return;
            worker = new Thread(Initialize);
            worker.IsBackground = true;
            worker.Name = "MXMasterWin HID++";
            worker.Start();
        }

        public void SetEnabled(bool enabled)
        {
            desiredEnabled = enabled;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                lock (sync)
                {
                    if (disposed || transport == null || featureIndex < 0) return;
                    // Multiple tray toggles can queue worker threads in a different order.
                    // Always apply the newest requested state once the lock is acquired.
                    try { ApplyDiversion(desiredEnabled); }
                    catch { }
                }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        private void Initialize()
        {
            try
            {
                lock (sync)
                {
                    if (disposed) return;
                    Connect();
                }
            }
            catch (Exception ex)
            {
                Report(ProblemMark + Lang.T("HID++ 初始化失敗：", "HID++ initialisation failed: ") + ex.Message);
            }
        }

        // Temporary diversion only lasts for the mouse's current connection: it is gone after the
        // mouse switches to another host and back, or reconnects after sleep or power-off. Called
        // when the mouse shows activity after a pause; puts the diversion back if it was dropped.
        public void Recheck()
        {
            if (disposed || worker == null) return;
            if (Interlocked.Exchange(ref recheckBusy, 1) != 0) return;
            Thread thread = new Thread(new ThreadStart(delegate
            {
                try
                {
                    // HID++ may not answer yet right after the first input report of a reconnect.
                    for (int attempt = 0; attempt < 4 && !disposed; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(1500);
                        bool settled;
                        try { settled = EnsureDiversion(); }
                        catch (Exception ex)
                        {
                            settled = false;
                            Report("recheck failed: " + ex.Message);
                        }
                        if (settled) break;
                    }
                }
                finally { Interlocked.Exchange(ref recheckBusy, 0); }
            }));
            thread.IsBackground = true;
            thread.Start();
        }

        // Returns true when nothing is left to do, false when the caller should try again.
        private bool EnsureDiversion()
        {
            lock (sync)
            {
                if (disposed) return true;
                if (transport != null && featureIndex >= 0 && divertedControls.Count > 0)
                {
                    // 1 = as requested, 0 = the mouse reports something else, -1 = no answer, -2 = handle unusable
                    int state;
                    try { state = ReadDiversionState(); }
                    catch { state = -2; }
                    if (state == 1)
                    {
                        silentChecks = 0;
                        return true;
                    }
                    if (state == 0)
                    {
                        silentChecks = 0;
                        Report("diversion state changed on the mouse (reconnected?); re-applying");
                        if (!ApplyDiversion(desiredEnabled)) return false;
                        ready(deviceIndex, featureIndex);
                        Report(ReadyMark + transport.Name + " special buttons re-diverted");
                        return true;
                    }
                    // A Bolt receiver stays plugged in after the mouse has moved to Bluetooth, so a silent
                    // channel must be given up as well; Connect() then looks on every interface.
                    if (state == -1 && ++silentChecks < 2) return false;
                    Report("HID++ channel is not answering; reopening");
                }
                ResetConnection();
                return Connect();
            }
        }

        private int ReadDiversionState()
        {
            foreach (int cid in divertedControls)
            {
                byte[] state = transport.Request(deviceIndex, featureIndex, 2, SoftwareId,
                    new byte[] { (byte)(cid >> 8), (byte)cid }, 700);
                if (state == null || state.Length < 9 ||
                    state[4] != (byte)(cid >> 8) || state[5] != (byte)cid) return -1;
                if (((state[6] & 0x01) != 0) != desiredEnabled) return 0;
            }
            return 1;
        }

        private void ResetConnection()
        {
            if (transport != null)
            {
                transport.Dispose();
                transport = null;
            }
            deviceIndex = -1;
            featureIndex = -1;
            silentChecks = 0;
            divertedControls.Clear();
            featureIndexes.Clear();
            wheelCapability = null;
        }

        // Device type from 0x0005 function 2 (0 keyboard, 3 mouse, 4 trackpad, 5 trackball; Solaar's
        // list); -1 when the device does not say.
        private static int DeviceKind(HidTransport candidate, int device)
        {
            byte[] root = candidate.Request(device, 0, 0, SoftwareId, new byte[] { 0x00, 0x05, 0 }, 900);
            if (root == null || root.Length < 5 || root[4] == 0) return -1;
            byte[] type = candidate.Request(device, root[4], 2, SoftwareId, new byte[0], 900);
            return type == null || type.Length < 5 ? -1 : type[4];
        }

        private static bool IsPointingKind(int kind)
        {
            return kind == 3 || kind == 4 || kind == 5;
        }

        // Finds the mouse's HID++ channel and applies the requested diversion. Caller holds sync.
        private bool Connect()
        {
            List<HidTransport> transports = HidTransport.FindMxMasterTransports(Report);
            // The direct (Bluetooth) interface answers or times out with a single probe, while an
            // empty receiver slot costs a full timeout each, so try Bluetooth first.
            transports.Sort(delegate(HidTransport a, HidTransport b) { return a.IsBolt.CompareTo(b.IsBolt); });
            foreach (HidTransport candidateTransport in transports)
            {
                if (disposed)
                {
                    candidateTransport.Dispose();
                    continue;
                }

                int[] candidateDevices = new[] { 0xFF };
                if (candidateTransport.IsBolt)
                {
                    // Start with the slot the mouse used last time.
                    var slots = new List<int>(new[] { 1, 2, 3, 4, 5, 6 });
                    if (slots.Remove(lastBoltIndex)) slots.Insert(0, lastBoltIndex);
                    candidateDevices = slots.ToArray();
                }

                foreach (int candidateDevice in candidateDevices)
                {
                    byte[] root = candidateTransport.Request(candidateDevice, 0, 0, SoftwareId,
                        // A sleeping Bolt device can take about one second to wake and answer.
                        new byte[] { (byte)(TargetFeature >> 8), (byte)(TargetFeature & 0xFF), 0 }, 1800);
                    if (root == null || root.Length < 7 || root[4] == 0) continue;
                    // A keyboard on the same receiver (an MX Mechanical, for one) also has 1B04; skip any
                    // slot that says it is not a mouse, or the mouse is never found.
                    if (candidateTransport.IsBolt)
                    {
                        int kind = DeviceKind(candidateTransport, candidateDevice);
                        if (kind >= 0 && !IsPointingKind(kind))
                        {
                            Report("BOLT dev=" + candidateDevice.ToString("X2") + " has 1B04 but is not a mouse (type " + kind + "); skipped");
                            if (lastBoltIndex == candidateDevice) lastBoltIndex = -1;
                            continue;
                        }
                    }
                    transport = candidateTransport;
                    deviceIndex = candidateDevice;
                    if (candidateTransport.IsBolt) lastBoltIndex = candidateDevice;
                    featureIndex = root[4];
                    int version = root[6];
                    hostNameTried = false;
                    Report(transport.Name + " dev=" + deviceIndex.ToString("X2") +
                        " feature 1B04 index=" + featureIndex.ToString("X2") + " v" + version);
                    break;
                }

                if (transport == candidateTransport) break;
                candidateTransport.Dispose();
            }

            foreach (HidTransport candidateTransport in transports)
            {
                if (candidateTransport != transport) candidateTransport.Dispose();
            }

            if (transport == null)
            {
                // Protocol detail stays in the engineering log; the main window gets the plain version.
                Report(transports.Count == 0
                    ? "no writable Logitech HID++ long-report interface"
                    : "HID++ interface found, but no MX Master 4 channel with 1B04 answered");
                Report(AwayMark + Lang.T("MX Master 4 目前沒有連到這台電腦（可能切到別台或在睡眠中）",
                    "MX Master 4 isn't connected to this computer (it may be on another one or asleep)"));
                return false;
            }

            DiscoverDivertableControls();
            if (divertedControls.Count == 0)
            {
                Report(ProblemMark + Lang.T("裝置沒有回報可 diversion 的 C3／1A0 控制",
                    "The device reported no divertable C3 / 1A0 controls"));
                return false;
            }

            if (!ApplyDiversion(desiredEnabled)) return false;
            ready(deviceIndex, featureIndex);
            Report(ReadyMark + transport.Name + " special buttons diverted (" +
                String.Join(", ", divertedControls.ConvertAll(delegate(int c) { return c.ToString("X4"); }).ToArray()) + ")");
            return true;
        }

        private void DiscoverDivertableControls()
        {
            byte[] countReply = transport.Request(deviceIndex, featureIndex, 0, SoftwareId, new byte[0], 600);
            if (countReply == null || countReply.Length < 5) return;
            int count = Math.Min((int)countReply[4], 64);
            for (int i = 0; i < count; i++)
            {
                byte[] info = transport.Request(deviceIndex, featureIndex, 1, SoftwareId,
                    new byte[] { (byte)i }, 600);
                if (info == null || info.Length < 9) continue;
                int cid = (info[4] << 8) | info[5];
                int flags = info[8];
                bool target = Array.IndexOf(TargetControls, cid) >= 0;
                bool temporarilyDivertable = (flags & 0x20) != 0;
                if (target)
                {
                    Report("CID " + cid.ToString("X4") + " flags=" + flags.ToString("X2") +
                        (temporarilyDivertable ? " divertable" : " not-divertable"));
                    if (temporarilyDivertable) divertedControls.Add(cid);
                }
            }
        }

        private bool ApplyDiversion(bool enable)
        {
            bool allSucceeded = true;
            foreach (int cid in divertedControls)
            {
                // Bit 0 is temporary diversion; bit 1 says to update bit 0.
                byte changeFlags = enable ? (byte)0x03 : (byte)0x02;
                byte[] response = transport.Request(deviceIndex, featureIndex, 3, SoftwareId,
                    new byte[] { (byte)(cid >> 8), (byte)cid, changeFlags, 0, 0 }, 700);
                bool ok = response != null && response.Length >= 9 &&
                    response[4] == (byte)(cid >> 8) && response[5] == (byte)cid;
                if (ok)
                {
                    byte[] state = transport.Request(deviceIndex, featureIndex, 2, SoftwareId,
                        new byte[] { (byte)(cid >> 8), (byte)cid }, 700);
                    ok = state != null && state.Length >= 9 &&
                        state[4] == (byte)(cid >> 8) && state[5] == (byte)cid &&
                        ((state[6] & 0x01) != 0) == enable;
                }
                if (!ok)
                {
                    allSucceeded = false;
                    Report(ProblemMark + (enable
                        ? Lang.T("無法啟用專用按鍵 ", "Could not divert special button ")
                        : Lang.T("無法解除專用按鍵 ", "Could not release special button ")) + cid.ToString("X4"));
                }
                else
                {
                    Report("CID " + cid.ToString("X4") + " diversion=" + (enable ? "on" : "off") + " verified");
                }
            }
            return allSucceeded;
        }

        // ---- Device information and settings (battery, firmware, DPI, SmartShift, haptics) ----
        // Feature ids and byte layouts follow Solaar's hidpp20 implementation. Every write is
        // read back, and callers only report success when the device confirms the new value.

        private readonly Dictionary<int, int> featureIndexes = new Dictionary<int, int>();
        private string wheelCapability;

        private int FindFeature(int feature)
        {
            int index;
            if (featureIndexes.TryGetValue(feature, out index)) return index;
            byte[] root = transport.Request(deviceIndex, 0, 0, SoftwareId,
                new byte[] { (byte)(feature >> 8), (byte)feature, 0 }, 1800);
            if (root == null || root.Length < 5) return -1;
            index = root[4] != 0 ? root[4] : -1;
            featureIndexes[feature] = index;
            return index;
        }

        private byte[] Call(int feature, int function, params byte[] parameters)
        {
            int index = FindFeature(feature);
            if (index < 0) return null;
            byte[] reply = transport.Request(deviceIndex, index, function, SoftwareId, parameters, 900);
            return reply != null && reply.Length >= 9 ? reply : null;
        }

        private static string Hex(byte[] reply)
        {
            return BitConverter.ToString(reply, 4, Math.Min(reply.Length - 4, 12));
        }

        public DeviceSnapshot ReadDevice()
        {
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return null;
                var snapshot = new DeviceSnapshot();
                snapshot.Transport = transport.Name;
                try
                {
                    ReadBattery(snapshot);
                    ReadFirmware(snapshot);
                    ReadDpi(snapshot);
                    ReadSmartShift(snapshot);
                    ReadWheelDirection(snapshot);
                    ReadHaptic(snapshot);
                    byte[] torqueCaps = Call(0x2111, 0), torque = Call(0x2111, 1);
                    if (torque != null && torque.Length >= 7 && SolaarFeatures.Torque(torqueCaps, torque, torque[6]) != null)
                    {
                        snapshot.TorquePresent = true;
                        snapshot.RatchetTorque = torque[6];
                    }
                    byte[] thumb = Call(0x2150, 1);
                    if (thumb != null && thumb.Length >= 6)
                    {
                        snapshot.ThumbInvertPresent = true;
                        snapshot.ThumbInverted = SolaarFeatures.MaskValue(thumb, 1, 1, 2) == true;
                    }
                }
                catch (Exception ex)
                {
                    snapshot.Raw.Add("read failed: " + ex.Message);
                }
                return snapshot;
            }
        }

        private void ReadBattery(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x1004, 1);
            if (reply != null)
            {
                snapshot.Raw.Add("1004 status=" + Hex(reply));
                snapshot.BatteryPercent = reply[4];
                snapshot.BatteryCharging = reply[6] == 1 || reply[6] == 2;
                snapshot.BatteryFull = reply[6] == 3;
                return;
            }
            reply = Call(0x1000, 0);
            if (reply != null)
            {
                snapshot.Raw.Add("1000 status=" + Hex(reply));
                snapshot.BatteryPercent = reply[4];
                snapshot.BatteryCharging = reply[6] == 1 || reply[6] == 2 || reply[6] == 4;
                snapshot.BatteryFull = reply[6] == 3;
            }
        }

        private void ReadFirmware(DeviceSnapshot snapshot)
        {
            byte[] count = Call(0x0003, 0);
            if (count == null) return;
            int entities = Math.Min((int)count[4], 8);
            for (int i = 0; i < entities; i++)
            {
                byte[] info = Call(0x0003, 1, (byte)i);
                if (info == null || info.Length < 12) continue;
                snapshot.Raw.Add("0003 entity" + i + "=" + Hex(info));
                if ((info[4] & 0x0F) != 0) continue;
                string name = Encoding.ASCII.GetString(info, 5, 3).Trim('\0', ' ');
                snapshot.Firmware = name + " " + info[8].ToString("X2") + "." + info[9].ToString("X2") +
                    ".B" + ((info[10] << 8) | info[11]).ToString("X4");
                return;
            }
        }

        private void ReadDpi(DeviceSnapshot snapshot)
        {
            byte[] current = Call(0x2201, 2, 0);
            if (current == null)
            {
                byte[] extended = Call(0x2202, 5, 0);
                if (extended != null)
                {
                    snapshot.DpiFeature = 0x2202;
                    snapshot.Raw.Add("2202 dpi=" + Hex(extended));
                }
                return;
            }
            snapshot.Raw.Add("2201 dpi=" + Hex(current));
            snapshot.DpiFeature = 0x2201;
            snapshot.DpiDefault = (current[7] << 8) | current[8];
            snapshot.Dpi = (current[5] << 8) | current[6];
            if (snapshot.Dpi == 0) snapshot.Dpi = snapshot.DpiDefault;

            byte[] list = Call(0x2201, 1, 0);
            if (list == null) return;
            snapshot.Raw.Add("2201 list=" + Hex(list));
            var values = new List<int>();
            for (int i = 5; i + 1 < list.Length; i += 2)
            {
                int value = (list[i] << 8) | list[i + 1];
                if (value == 0) break;
                values.Add(value);
            }
            // A value with the top three bits set is a step size between its two neighbours.
            for (int i = 1; i + 1 < values.Count; i++)
            {
                if ((values[i] >> 13) != 7) continue;
                snapshot.DpiMin = values[i - 1];
                snapshot.DpiStep = values[i] & 0x1FFF;
                snapshot.DpiMax = values[i + 1];
                return;
            }
        }

        private void ReadSmartShift(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x2111, 1);
            int feature = 0x2111;
            if (reply == null)
            {
                reply = Call(0x2110, 0);
                feature = 0x2110;
            }
            if (reply == null) return;
            snapshot.Raw.Add(feature.ToString("X4") + " smartshift=" + Hex(reply));
            snapshot.SmartShiftFeature = feature;
            snapshot.WheelMode = reply[4];
            snapshot.SmartShiftThreshold = reply[5];
        }

        // 0x2121 HiRes Wheel mode byte: bit0 = HID++ target, bit1 = high resolution, bit2 = inverted.
        private void ReadWheelDirection(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x2121, 1);
            if (reply == null) return;
            snapshot.Raw.Add("2121 mode=" + Hex(reply));
            if (wheelCapability == null)
            {
                byte[] capability = Call(0x2121, 0);
                wheelCapability = capability != null ? Hex(capability) : "";
            }
            if (wheelCapability.Length > 0) snapshot.Raw.Add("2121 capability=" + wheelCapability);
            snapshot.WheelInvertPresent = true;
            snapshot.WheelInverted = (reply[4] & 0x04) != 0;
            snapshot.HiresResolution = SolaarFeatures.MaskValue(reply, 0, 2, 1) == true;
        }

        private void ReadHaptic(DeviceSnapshot snapshot)
        {
            byte[] reply = Call(0x19B0, 1);
            if (reply == null) return;
            snapshot.Raw.Add("19B0 haptic=" + Hex(reply));
            snapshot.HapticPresent = true;
            snapshot.HapticLevel = (reply[4] & 0x01) != 0 ? reply[5] : 0;
        }

        public bool SetDpi(int dpi)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    if (Call(0x2201, 3, 0, (byte)(dpi >> 8), (byte)dpi) == null) return false;
                    byte[] check = Call(0x2201, 2, 0);
                    return check != null && ((check[5] << 8) | check[6]) == dpi;
                }
                catch { return false; }
            }
        }

        // mode: 1 = free spin, 2 = ratchet. threshold: 1-50, or 255 to never auto-disengage.
        public bool SetSmartShift(int mode, int threshold)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    bool enhanced = FindFeature(0x2111) >= 0;
                    int feature = enhanced ? 0x2111 : 0x2110;
                    if (Call(feature, enhanced ? 2 : 1, (byte)mode, (byte)threshold) == null) return false;
                    byte[] check = Call(feature, enhanced ? 1 : 0);
                    return check != null && check[4] == mode && check[5] == threshold;
                }
                catch { return false; }
            }
        }

        public bool SetRatchetTorque(int value)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] p = SolaarFeatures.Torque(Call(0x2111, 0), Call(0x2111, 1), value);
                    return p != null && Call(0x2111, 2, p) != null && SolaarFeatures.Matches(Call(0x2111, 1), p);
                }
                catch { return false; }
            }
        }
        public bool SetThumbInvert(bool value) { return SetWheelBit(0x2150, 1, 1, value, 2); }
        public bool SetHiresResolution(bool value) { return SetWheelBit(0x2121, 0, 2, value, 1); }
        private bool SetWheelBit(int feature, int offset, byte mask, bool value, int width)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] p = SolaarFeatures.Masked(Call(feature, 1), offset, mask, value, width);
                    return p != null && Call(feature, 2, p) != null && SolaarFeatures.Matches(Call(feature, 1), p);
                }
                catch { return false; }
            }
        }

        // Swaps ratchet and free spin, writing the SmartShift threshold back as read. On success
        // mode and threshold hold the new values.
        public bool ToggleWheelMode(out int mode, out int threshold)
        {
            mode = threshold = -1;
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    bool enhanced = FindFeature(0x2111) >= 0;
                    byte[] reply = Call(enhanced ? 0x2111 : 0x2110, enhanced ? 1 : 0);
                    if (reply == null) return false;
                    int next = reply[4] == 1 ? 2 : 1;
                    int keep = reply[5];
                    if (!SetSmartShift(next, keep)) return false;
                    mode = next;
                    threshold = keep;
                    return true;
                }
                catch { return false; }
            }
        }

        // Only the invert bit changes; the target and resolution bits are written back as read.
        public bool SetWheelInvert(bool inverted)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] mode = Call(0x2121, 1);
                    if (mode == null) return false;
                    byte value = (byte)(inverted ? mode[4] | 0x04 : mode[4] & ~0x04);
                    if (Call(0x2121, 2, value) == null) return false;
                    byte[] check = Call(0x2121, 1);
                    return check != null && ((check[4] & 0x04) != 0) == inverted;
                }
                catch { return false; }
            }
        }

        // level: 0 turns haptics off; otherwise 1-100.
        public bool SetHaptic(int level)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] sent = level <= 0 ? Call(0x19B0, 2, 0x00, 0x32) : Call(0x19B0, 2, 0x01, (byte)level);
                    if (sent == null) return false;
                    byte[] check = Call(0x19B0, 1);
                    if (check == null) return false;
                    bool enabled = (check[4] & 0x01) != 0;
                    return level <= 0 ? !enabled : enabled && check[5] == level;
                }
                catch { return false; }
            }
        }

        private int hapticBusy;
        private bool hapticFailureReported;

        // Plays one haptic waveform (0x19B0 function 4; waveform ids follow Solaar, 0x04 = subtle bump).
        // The strength follows the mouse's own haptic setting, so nothing plays while that is off.
        // Runs in the background; a play still in flight, or a channel busy for long, skips this one
        // rather than letting taps queue up behind a fast sweep across the ring.
        public void PlayHaptic(byte waveform)
        {
            if (disposed || Interlocked.CompareExchange(ref hapticBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool taken = false;
                try
                {
                    Monitor.TryEnter(sync, 100, ref taken);
                    if (!taken || disposed || transport == null || deviceIndex < 0) return;
                    int index = FindFeature(0x19B0);
                    if (index < 0) return;
                    byte[] reply = transport.Request(deviceIndex, index, 4, SoftwareId, new[] { waveform }, 250);
                    if (reply == null && !hapticFailureReported)
                    {
                        hapticFailureReported = true;
                        Report("haptic play " + waveform.ToString("X2") + " no reply");
                    }
                }
                catch { }
                finally
                {
                    if (taken) Monitor.Exit(sync);
                    Interlocked.Exchange(ref hapticBusy, 0);
                }
            });
        }

        // ---- Easy-Switch hosts (0x1814 Change Host, 0x1815 Hosts Info; byte layouts follow Solaar) ----

        // The current channel's name differs from this computer: write this computer's name (see HostName),
        // confirmed by reading it back; tried once per connection.
        private string WriteHostName(int current, string stored)
        {
            hostNameTried = true;
            string computer = HostName.ComputerName;
            byte[] caps = Call(0x1815, 0);
            byte[] info = Call(0x1815, 1, (byte)current);
            if (caps == null || info == null || !HostName.NeedsWrite(caps[4], stored, computer, info[9])) return null;
            string fitted = Encoding.UTF8.GetString(HostName.Encode(computer, info[9]));
            foreach (byte[] packet in HostName.WritePackets(current, HostName.Encode(computer, info[9])))
            {
                if (Call(0x1815, 4, packet) == null)
                {
                    Report("host name write failed");
                    return null;
                }
            }
            byte[] after = Call(0x1815, 1, (byte)current);
            if (after == null) return null;
            var name = new List<byte>();
            while (name.Count < after[8])
            {
                byte[] piece = Call(0x1815, 3, (byte)current, (byte)name.Count);
                if (piece == null) break;
                int take = Math.Min(after[8] - name.Count, Math.Min(14, piece.Length - 6));
                if (take <= 0) break;
                for (int k = 0; k < take; k++) name.Add(piece[6 + k]);
            }
            string now = Encoding.UTF8.GetString(name.ToArray());
            Report("host name of channel " + (current + 1) + " written: " + (HostName.Same(now, fitted) ? "confirmed" : "not confirmed"));
            return now;
        }


        // The host stored on each channel and the channel the mouse is on now (-1 when unknown).
        public List<HostEntry> ReadHosts(out int current)
        {
            current = -1;
            var hosts = new List<HostEntry>();
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return hosts;
                try
                {
                    byte[] info = Call(0x1814, 0);
                    if (info == null) return hosts;
                    current = info[5];
                    int count = Math.Min((int)info[4], 8);
                    var summary = new StringBuilder();
                    for (int i = 0; i < count; i++)
                    {
                        var entry = new HostEntry { Index = i };
                        byte[] host = Call(0x1815, 1, (byte)i);
                        if (host != null)
                        {
                            entry.Paired = host[5] != 0;
                            entry.Bus = host[6];
                            int length = host[8];
                            var name = new List<byte>();
                            while (name.Count < length)
                            {
                                byte[] piece = Call(0x1815, 3, (byte)i, (byte)name.Count);
                                if (piece == null) break;
                                int take = Math.Min(length - name.Count, piece.Length - 6);
                                if (take <= 0) break;
                                for (int k = 0; k < take; k++) name.Add(piece[6 + k]);
                            }
                            entry.Name = Encoding.UTF8.GetString(name.ToArray());
                        }
                        hosts.Add(entry);
                        summary.Append(" " + i + ":" + (entry.Name.Length == 0 ? "-" : entry.Name) + "/" + entry.Bus);
                    }
                    Report("hosts current=" + current + summary);
                    if (!hostNameTried && current >= 0 && current < hosts.Count)
                    {
                        string written = WriteHostName(current, hosts[current].Name);
                        if (written != null) hosts[current].Name = written;
                    }
                }
                catch (Exception ex)
                {
                    Report("hosts read failed: " + ex.Message);
                }
            }
            return hosts;
        }

        // Moves the mouse to another channel. It leaves this computer at once and sometimes has no
        // time to answer, so a request that went out counts as done. The channel is dropped here and
        // found again by Recheck() when the mouse shows activity on this computer.
        // hapticWave >= 0 plays that waveform first, on the same lock, so the tap lands before the mouse leaves.
        public bool ChangeHost(int host, int hapticWave)
        {
            lock (sync)
            {
                if (disposed || transport == null || deviceIndex < 0) return false;
                try
                {
                    int index = FindFeature(0x1814);
                    if (index < 0) return false;
                    byte[] info = transport.Request(deviceIndex, index, 0, SoftwareId, new byte[0], 900);
                    if (info == null || info.Length < 6 || host < 0 || host >= info[4] || host == info[5]) return false;
                    int haptic = hapticWave >= 0 ? FindFeature(0x19B0) : -1;
                    if (haptic >= 0) transport.Request(deviceIndex, haptic, 4, SoftwareId, new[] { (byte)hapticWave }, 250);
                    Report("change host " + info[5] + " -> " + host);
                    try { transport.Request(deviceIndex, index, 1, SoftwareId, new byte[] { (byte)host }, 300); }
                    catch (Exception ex) { Report("change host: " + ex.Message); }
                    ResetConnection();
                    Report(AwayMark + Lang.T("滑鼠已切到另一台電腦", "The mouse has moved to another computer"));
                    return true;
                }
                catch (Exception ex)
                {
                    Report("change host failed: " + ex.Message);
                    return false;
                }
            }
        }

        private void Report(string text)
        {
            try { report(text); }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            lock (sync)
            {
                if (transport != null)
                {
                    if (featureIndex >= 0 && divertedControls.Count > 0)
                    {
                        try { ApplyDiversion(false); }
                        catch { }
                    }
                    transport.Dispose();
                    transport = null;
                }
            }
        }

        // Also used by KeyboardManager (FindKeyboardTransports), which opens its own handles.
        internal sealed class HidTransport : IDisposable
        {
            private readonly FileStream stream;
            private readonly SafeFileHandle handle;
            private readonly int inputLength;
            private readonly int outputLength;
            private readonly Action<string> report;
            public readonly string Name;
            public readonly bool IsBolt;

            private HidTransport(string path, SafeFileHandle fileHandle, HIDP_CAPS caps, Action<string> reportAction)
            {
                handle = fileHandle;
                inputLength = caps.InputReportByteLength;
                outputLength = caps.OutputReportByteLength;
                report = reportAction;
                IsBolt = path.IndexOf("PID_C548", StringComparison.OrdinalIgnoreCase) >= 0;
                Name = IsBolt ? "BOLT" : "BLE";
                stream = new FileStream(handle, FileAccess.ReadWrite, Math.Max(inputLength, outputLength), true);
            }

            public static List<HidTransport> FindMxMasterTransports(Action<string> report)
            {
                return FindTransports(report, false);
            }

            // Bolt receivers and Bluetooth Logitech devices other than the MX Master 4; the caller asks
            // each one whether it is a keyboard.
            public static List<HidTransport> FindKeyboardTransports(Action<string> report)
            {
                return FindTransports(report, true);
            }

            private static List<HidTransport> FindTransports(Action<string> report, bool keyboards)
            {
                var transports = new List<HidTransport>();
                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);
                IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                    DigcfPresent | DigcfDeviceInterface);
                if (infoSet == new IntPtr(-1)) return transports;
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                        data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                        if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                        {
                            if (Marshal.GetLastWin32Error() == 259) break;
                            continue;
                        }

                        uint required = 0;
                        SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                        if (required == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)required);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref data, detail, required, ref required, IntPtr.Zero))
                                continue;
                            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                            if (String.IsNullOrEmpty(path)) continue;
                            string upper = path.ToUpperInvariant();
                            bool bolt = upper.Contains("VID_046D&PID_C548");
                            bool direct = upper.Contains("VID_046D&PID_B042") ||
                                (upper.Contains("VID&02046D") && upper.Contains("PID&B042"));
                            bool otherBluetooth = upper.Contains("VID&02046D") && !upper.Contains("PID&B042");
                            if (keyboards ? !bolt && !otherBluetooth : !bolt && !direct) continue;

                            SafeFileHandle file = CreateFile(path, GenericRead | GenericWrite,
                                FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting,
                                FileFlagOverlapped, IntPtr.Zero);
                            if (file.IsInvalid)
                            {
                                file.Dispose();
                                continue;
                            }

                            HIDP_CAPS caps;
                            if (!TryGetCaps(file, out caps) || caps.OutputReportByteLength < 20 || caps.InputReportByteLength < 20)
                            {
                                file.Dispose();
                                continue;
                            }
                            report((keyboards ? "keyboard search " : "") + (bolt ? "BOLT" : "BLE") + " HID++ interface usage=" +
                                caps.UsagePage.ToString("X4") + ":" + caps.Usage.ToString("X4") +
                                " in=" + caps.InputReportByteLength + " out=" + caps.OutputReportByteLength);
                            transports.Add(new HidTransport(path, file, caps, report));
                        }
                        finally { Marshal.FreeHGlobal(detail); }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(infoSet); }
                return transports;
            }

            private static bool TryGetCaps(SafeFileHandle file, out HIDP_CAPS caps)
            {
                caps = new HIDP_CAPS();
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(file, out preparsed)) return false;
                try { return HidP_GetCaps(preparsed, out caps) == HidpStatusSuccess; }
                finally { HidD_FreePreparsedData(preparsed); }
            }

            public byte[] Request(int targetDevice, int targetFeature, int function, byte softwareId,
                byte[] parameters, int timeoutMilliseconds)
            {
                return Request(targetDevice, targetFeature, function, softwareId, parameters, timeoutMilliseconds, null);
            }

            // giveUp is asked every 100 ms while waiting: a Bolt receiver answers a request to an empty or
            // unreachable slot on its short-report interface, which this handle cannot see, so the caller
            // learns about it elsewhere (raw input) and stops the wait early.
            public byte[] Request(int targetDevice, int targetFeature, int function, byte softwareId,
                byte[] parameters, int timeoutMilliseconds, Func<bool> giveUp)
            {
                byte[] packet = new byte[outputLength];
                packet[0] = 0x11;
                packet[1] = (byte)targetDevice;
                packet[2] = (byte)targetFeature;
                packet[3] = (byte)((function << 4) | (softwareId & 0x0F));
                int parameterCount = Math.Min(parameters.Length, packet.Length - 4);
                Array.Copy(parameters, 0, packet, 4, parameterCount);

                // This handle is read only while a request waits, so everything the interface sent in
                // between (other devices on the receiver, Options+ traffic, a late reply to an earlier
                // request) is still queued. Drop it so an old reply can never be taken for this one.
                HidD_FlushQueue(handle);
                stream.Write(packet, 0, packet.Length);
                stream.Flush();

                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < timeoutMilliseconds)
                {
                    int remaining = Math.Max(1, timeoutMilliseconds - (int)timer.ElapsedMilliseconds);
                    byte[] response = ReadOne(giveUp == null ? remaining : Math.Min(remaining, 100));
                    if (response == null)
                    {
                        if (giveUp == null || giveUp()) return null;
                        continue;
                    }
                    if (response.Length < 4 || response[1] != (byte)targetDevice) continue;
                    if (response[2] == 0xFF && response.Length >= 6 &&
                        response[3] == (byte)targetFeature && response[4] == packet[3])
                    {
                        report(Name + " HID++ error=" + response[5].ToString("X2") +
                            " request=" + BitConverter.ToString(packet, 0, Math.Min(packet.Length, 10)));
                        return null;
                    }
                    if (response[2] == (byte)targetFeature && response[3] == packet[3])
                        return response;
                }
                return null;
            }

            private byte[] ReadOne(int timeoutMilliseconds)
            {
                byte[] buffer = new byte[inputLength];
                IAsyncResult pending = stream.BeginRead(buffer, 0, buffer.Length, null, null);
                int read;
                if (!pending.AsyncWaitHandle.WaitOne(timeoutMilliseconds))
                {
                    CancelIoEx(handle, IntPtr.Zero);
                    // A report that completed just before the cancel is kept instead of lost.
                    try { read = stream.EndRead(pending); }
                    catch { read = 0; }
                }
                else read = stream.EndRead(pending);
                if (read <= 0) return null;
                if (read == buffer.Length) return buffer;
                byte[] exact = new byte[read];
                Array.Copy(buffer, exact, read);
                return exact;
            }

            public void Dispose()
            {
                try { stream.Dispose(); }
                catch { }
            }
        }

        // The Logi Bolt receiver's own HID++ collections, for BoltReceiver: the short one (report 0x10, 7 bytes)
        // and the long one (0x11, 20 bytes). Windows exposes them as separate collections; register reads and
        // writes are answered, and some notifications are sent, on the short one. Each collection gets one
        // handle for a reader thread and another for writing, so the two never share a stream.
        internal sealed class BoltPort : IDisposable
        {
            private sealed class Channel
            {
                public FileStream Reader, Writer;
                public int InputLength, OutputLength;
            }

            private Channel shortChannel, longChannel;
            private volatile bool closed;
            public event Action<byte[]> Received;
            public bool Multiple;       // more than one receiver: only the first one's collections are opened

            // The same receiver's collections share the device path except for the collection number
            // (...&MI_02&COL01#7&1A2B3C4D&0&0000#... and ...&MI_02&COL02#7&1A2B3C4D&0&0001#...).
            internal static string ReceiverKey(string path)
            {
                string[] parts = path.ToUpperInvariant().Split('#');
                if (parts.Length < 3) return path.ToUpperInvariant();
                string device = System.Text.RegularExpressions.Regex.Replace(parts[1], "&COL[0-9A-F]+", "");
                string instance = parts[2];
                int cut = instance.LastIndexOf('&');
                if (cut > 0) instance = instance.Substring(0, cut);
                return device + "#" + instance;
            }

            public static BoltPort Open(Action<string> report)
            {
                var port = new BoltPort();
                var receivers = new List<string>();
                foreach (string path in ReceiverPaths())
                {
                    // Windows lists each receiver interface (MI_00 keyboard, MI_01 mouse, MI_02 HID++) as its
                    // own path, so check the collection first and compare receivers among HID++ ones only;
                    // otherwise the first interface listed (usually MI_00) hid the HID++ one.
                    HIDP_CAPS caps;
                    SafeFileHandle probe = OpenPath(path);
                    if (probe == null) continue;
                    bool ok = TryGetCaps(probe, out caps);
                    probe.Dispose();
                    if (!ok || caps.UsagePage != 0xFF00) continue;
                    bool isLong = caps.OutputReportByteLength >= 20 && caps.InputReportByteLength >= 20;
                    bool isShort = caps.OutputReportByteLength == 7 && caps.InputReportByteLength == 7;
                    if (!isLong && !isShort) continue;
                    string key = ReceiverKey(path);
                    if (!receivers.Contains(key)) receivers.Add(key);
                    if (key != receivers[0]) continue;
                    if (isLong ? port.longChannel != null : port.shortChannel != null) continue;
                    SafeFileHandle reader = OpenPath(path), writer = OpenPath(path);
                    if (reader == null || writer == null)
                    {
                        if (reader != null) reader.Dispose();
                        if (writer != null) writer.Dispose();
                        continue;
                    }
                    var channel = new Channel
                    {
                        InputLength = caps.InputReportByteLength,
                        OutputLength = caps.OutputReportByteLength,
                        Reader = new FileStream(reader, FileAccess.ReadWrite, Math.Max(caps.InputReportByteLength, caps.OutputReportByteLength), true),
                        Writer = new FileStream(writer, FileAccess.ReadWrite, Math.Max(caps.InputReportByteLength, caps.OutputReportByteLength), true)
                    };
                    if (isLong) port.longChannel = channel; else port.shortChannel = channel;
                }
                if (port.longChannel == null)
                {
                    port.Dispose();
                    return null;
                }
                port.Multiple = receivers.Count > 1;
                report("receiver HID++ collections: long" + (port.shortChannel != null ? " + short" : " only") +
                    (port.Multiple ? "; " + receivers.Count + " receivers plugged in" : ""));
                return port;
            }

            public void Start()
            {
                foreach (Channel channel in new[] { shortChannel, longChannel })
                {
                    if (channel == null) continue;
                    Channel c = channel;
                    var thread = new Thread(delegate () { ReadLoop(c); }) { IsBackground = true, Name = "MXMasterWin Bolt reader" };
                    thread.Start();
                }
            }

            private void ReadLoop(Channel channel)
            {
                byte[] buffer = new byte[channel.InputLength];
                while (!closed)
                {
                    int read;
                    try { read = channel.Reader.Read(buffer, 0, buffer.Length); }
                    catch { break; }
                    if (read <= 0) break;
                    byte[] copy = new byte[read];
                    Array.Copy(buffer, copy, read);
                    Action<byte[]> handler = Received;
                    if (handler != null)
                    {
                        try { handler(copy); }
                        catch { }
                    }
                }
            }

            // A short packet goes to the short collection; without one it is sent as a long report instead.
            public bool Write(byte[] packet)
            {
                if (closed || packet == null || packet.Length < 4) return false;
                Channel channel = packet[0] == 0x10 && shortChannel != null ? shortChannel : longChannel;
                byte[] data = new byte[channel.OutputLength];
                Array.Copy(packet, data, Math.Min(packet.Length, data.Length));
                if (channel == longChannel) data[0] = 0x11;
                try
                {
                    channel.Writer.Write(data, 0, data.Length);
                    channel.Writer.Flush();
                    return true;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                closed = true;
                foreach (Channel channel in new[] { shortChannel, longChannel })
                {
                    if (channel == null) continue;
                    try { channel.Reader.Dispose(); }
                    catch { }
                    try { channel.Writer.Dispose(); }
                    catch { }
                }
            }

            private static bool TryGetCaps(SafeFileHandle file, out HIDP_CAPS caps)
            {
                caps = new HIDP_CAPS();
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(file, out preparsed)) return false;
                try { return HidP_GetCaps(preparsed, out caps) == HidpStatusSuccess; }
                finally { HidD_FreePreparsedData(preparsed); }
            }

            private static SafeFileHandle OpenPath(string path)
            {
                SafeFileHandle file = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (!file.IsInvalid) return file;
                file.Dispose();
                return null;
            }

            private static List<string> ReceiverPaths()
            {
                var paths = new List<string>();
                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);
                IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
                if (infoSet == new IntPtr(-1)) return paths;
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                        data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                        if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                        {
                            if (Marshal.GetLastWin32Error() == 259) break;
                            continue;
                        }
                        uint required = 0;
                        SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                        if (required == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)required);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref data, detail, required, ref required, IntPtr.Zero)) continue;
                            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                            if (!String.IsNullOrEmpty(path) && path.ToUpperInvariant().Contains("VID_046D&PID_C548")) paths.Add(path);
                        }
                        finally { Marshal.FreeHGlobal(detail); }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(infoSet); }
                return paths;
            }
        }

        // A Lightspeed receiver's HID++ collections for OtherDevicesManager. Only the vendor collections
        // (usage page 0xFF00) are opened for reading and writing; the receiver's mouse and keyboard
        // collections are checked with a handle that has no access rights and closed again.
        internal sealed class LightspeedPort : IOtherPort
        {
            private sealed class Channel
            {
                public FileStream Reader, Writer;
                public int InputLength, OutputLength;
            }

            // Collections already known not to be HID++, so they are not opened again on every search.
            private static readonly HashSet<string> notHidpp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            private Channel shortChannel, longChannel;
            private volatile bool closed;
            private int closedRaised;

            public string Key { get; private set; }
            public event Action<byte[]> Received;
            public event Action Closed;

            public static List<IOtherPort> OpenNew(ICollection<string> openKeys, Action<string> report)
            {
                var found = new Dictionary<string, LightspeedPort>();
                foreach (string path in ReceiverPaths())
                {
                    if (notHidpp.Contains(path)) continue;
                    SafeFileHandle probe = CreateFile(path, 0, FileShareRead | FileShareWrite, IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                    if (probe.IsInvalid)
                    {
                        probe.Dispose();
                        continue;
                    }
                    HIDP_CAPS caps;
                    bool ok = TryGetCaps(probe, out caps);
                    probe.Dispose();
                    if (!ok) continue;
                    bool isLong = caps.OutputReportByteLength >= 20 && caps.InputReportByteLength >= 20;
                    bool isShort = caps.OutputReportByteLength == 7 && caps.InputReportByteLength == 7;
                    if (caps.UsagePage != 0xFF00 || (!isLong && !isShort))
                    {
                        notHidpp.Add(path);
                        continue;
                    }
                    string key = PortKey(path);
                    if (key == null || openKeys.Contains(key)) continue;
                    LightspeedPort port;
                    if (!found.TryGetValue(key, out port))
                    {
                        port = new LightspeedPort { Key = key };
                        found[key] = port;
                    }
                    if (isLong ? port.longChannel != null : port.shortChannel != null) continue;
                    SafeFileHandle reader = OpenPath(path), writer = OpenPath(path);
                    if (reader == null || writer == null)
                    {
                        if (reader != null) reader.Dispose();
                        if (writer != null) writer.Dispose();
                        continue;
                    }
                    int size = Math.Max(caps.InputReportByteLength, caps.OutputReportByteLength);
                    var channel = new Channel
                    {
                        InputLength = caps.InputReportByteLength,
                        OutputLength = caps.OutputReportByteLength,
                        Reader = new FileStream(reader, FileAccess.ReadWrite, size, true),
                        Writer = new FileStream(writer, FileAccess.ReadWrite, size, true)
                    };
                    if (isLong) port.longChannel = channel; else port.shortChannel = channel;
                }
                var result = new List<IOtherPort>();
                foreach (LightspeedPort port in found.Values)
                {
                    if (port.longChannel == null)
                    {
                        port.Dispose();
                        continue;
                    }
                    report("Others receiver " + port.Key + " collections: long" + (port.shortChannel != null ? " + short" : " only"));
                    result.Add(port);
                }
                return result;
            }

            // PID + the instance part shared by the receiver's collections.
            internal static string PortKey(string path)
            {
                string upper = path.ToUpperInvariant();
                int at = upper.IndexOf("VID_046D&PID_", StringComparison.Ordinal);
                if (at < 0 || upper.Length < at + 17) return null;
                string receiver = BoltPort.ReceiverKey(path);
                int cut = receiver.IndexOf('#');
                if (cut < 0) return null;
                return upper.Substring(at + 13, 4) + "@" + receiver.Substring(cut + 1);
            }

            public void Start()
            {
                foreach (Channel channel in new[] { shortChannel, longChannel })
                {
                    if (channel == null) continue;
                    Channel c = channel;
                    var thread = new Thread(delegate () { ReadLoop(c); }) { IsBackground = true, Name = "MXMasterWin Lightspeed reader" };
                    thread.Start();
                }
            }

            private void ReadLoop(Channel channel)
            {
                byte[] buffer = new byte[channel.InputLength];
                while (!closed)
                {
                    int read;
                    try { read = channel.Reader.Read(buffer, 0, buffer.Length); }
                    catch { break; }
                    if (read <= 0) break;
                    byte[] copy = new byte[read];
                    Array.Copy(buffer, copy, read);
                    Action<byte[]> handler = Received;
                    if (handler != null)
                    {
                        try { handler(copy); }
                        catch { }
                    }
                }
                if (!closed && Interlocked.Exchange(ref closedRaised, 1) == 0)
                {
                    Action handler = Closed;
                    if (handler != null)
                    {
                        try { handler(); }
                        catch { }
                    }
                }
            }

            // A short packet goes to the short collection; without one it is sent as a long report.
            public bool Write(byte[] packet)
            {
                if (closed || packet == null || packet.Length < 4) return false;
                Channel channel = packet[0] == 0x10 && shortChannel != null ? shortChannel : longChannel;
                byte[] data = new byte[channel.OutputLength];
                Array.Copy(packet, data, Math.Min(packet.Length, data.Length));
                if (channel == longChannel) data[0] = 0x11;
                try
                {
                    channel.Writer.Write(data, 0, data.Length);
                    channel.Writer.Flush();
                    return true;
                }
                catch { return false; }
            }

            public void Dispose()
            {
                closed = true;
                foreach (Channel channel in new[] { shortChannel, longChannel })
                {
                    if (channel == null) continue;
                    try { channel.Reader.Dispose(); }
                    catch { }
                    try { channel.Writer.Dispose(); }
                    catch { }
                }
            }

            private static bool TryGetCaps(SafeFileHandle file, out HIDP_CAPS caps)
            {
                caps = new HIDP_CAPS();
                IntPtr preparsed;
                if (!HidD_GetPreparsedData(file, out preparsed)) return false;
                try { return HidP_GetCaps(preparsed, out caps) == HidpStatusSuccess; }
                finally { HidD_FreePreparsedData(preparsed); }
            }

            private static SafeFileHandle OpenPath(string path)
            {
                SafeFileHandle file = CreateFile(path, GenericRead | GenericWrite, FileShareRead | FileShareWrite,
                    IntPtr.Zero, OpenExisting, FileFlagOverlapped, IntPtr.Zero);
                if (!file.IsInvalid) return file;
                file.Dispose();
                return null;
            }

            private static bool IsLightspeedPath(string path)
            {
                string upper = path.ToUpperInvariant();
                foreach (int pid in OtherDevicesProtocol.LightspeedReceivers)
                    if (upper.Contains("VID_046D&PID_" + pid.ToString("X4"))) return true;
                return false;
            }

            private static List<string> ReceiverPaths()
            {
                var paths = new List<string>();
                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);
                IntPtr infoSet = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DigcfPresent | DigcfDeviceInterface);
                if (infoSet == new IntPtr(-1)) return paths;
                try
                {
                    for (uint index = 0; ; index++)
                    {
                        SP_DEVICE_INTERFACE_DATA data = new SP_DEVICE_INTERFACE_DATA();
                        data.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                        if (!SetupDiEnumDeviceInterfaces(infoSet, IntPtr.Zero, ref hidGuid, index, ref data))
                        {
                            if (Marshal.GetLastWin32Error() == 259) break;
                            continue;
                        }
                        uint required = 0;
                        SetupDiGetDeviceInterfaceDetail(infoSet, ref data, IntPtr.Zero, 0, ref required, IntPtr.Zero);
                        if (required == 0) continue;
                        IntPtr detail = Marshal.AllocHGlobal((int)required);
                        try
                        {
                            Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                            if (!SetupDiGetDeviceInterfaceDetail(infoSet, ref data, detail, required, ref required, IntPtr.Zero)) continue;
                            string path = Marshal.PtrToStringUni(new IntPtr(detail.ToInt64() + 4));
                            if (!String.IsNullOrEmpty(path) && IsLightspeedPath(path)) paths.Add(path);
                        }
                        finally { Marshal.FreeHGlobal(detail); }
                    }
                }
                finally { SetupDiDestroyDeviceInfoList(infoSet); }
                return paths;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_GetPreparsedData(SafeFileHandle device, out IntPtr data);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll", SetLastError = true)] private static extern bool HidD_FlushQueue(SafeFileHandle device);
        [DllImport("hid.dll")] private static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid guid, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data,
            IntPtr detail, uint detailSize, ref uint required, IntPtr deviceInfo);
        [DllImport("setupapi.dll")] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security,
            uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle handle, IntPtr overlapped);
    }

    // What the keyboard reported; null in MainForm means no keyboard is connected to this computer.
    // ---- Logi Bolt pairing ----
    // HID++ 1.0 registers and receiver notifications, packet layout per Solaar (lib/logitech_receiver/
    // receiver.py BoltReceiver, notifications.py, base.py request/write). Same logic as BoltProtocol.swift
    // in the macOS edition. BoltPackets and BoltPairingFlow are pure (no I/O) so tools/bolt-pairing-test can
    // drive them with a fake receiver.
    //   0x00   notification flags (3 bytes; WIRELESS = 0x000100 is needed for the pairing notifications)
    //   0xC0   discovery: [timeout seconds, 1 start / 2 cancel]
    //   0x2C1  pairing: [1 pair / 2 cancel / 3 unpair, slot, address x6, authentication, entropy (keyboard 20, else 10)]
    //   0x2B5  receiver info: sub-register 0x5N pairing info of slot N, 0x6N 01 name of slot N
    // Notifications (10/11 FF <sub> <address> data...): 0x53 discovery status (address 0 = discovering),
    // 0x4F discovered device, 0x54 pairing status (address 0 = in progress, 2 = done), 0x4D passkey.
    internal sealed class BoltSlot
    {
        public int Slot;
        public int Kind;            // HID++ 1.0 kind: 1 keyboard, 2 mouse, 3 numpad, 4 presenter, 8 trackball, 9 touchpad
        public string Wpid = "";
        public string Serial = "";
        public string Name = "";
    }

    internal enum BoltPhase { Idle, Searching, Pairing, Passkey, Succeeded, Failed }

    internal enum BoltFailure
    {
        None,
        NoAnswer,       // the receiver did not accept the discovery or pair command
        NotFound,       // discovery ended without a device asking to pair
        Timeout,        // the device did not finish in time (e.g. the passkey was not completed)
        Failed          // the receiver reported a failure (e.g. a wrong passkey)
    }

    internal static class BoltPackets
    {
        public const int WirelessFlag = 0x000100;
        public const byte SearchSeconds = 30;

        // Write 0x80 / read 0x81, plus 2 for the long registers (0x2xx). A long report (0x11, 20 bytes) when
        // there are more than 3 parameter bytes or for a long-register write, otherwise a short one (0x10, 7 bytes).
        public static byte[] Register(int register, bool write, params byte[] parameters)
        {
            int sub = (write ? 0x80 : 0x81) | ((register >> 8) & 0x02);
            bool isLong = parameters.Length > 3 || sub == 0x82;
            byte[] packet = new byte[isLong ? 20 : 7];
            packet[0] = (byte)(isLong ? 0x11 : 0x10);
            packet[1] = 0xFF;
            packet[2] = (byte)sub;
            packet[3] = (byte)(register & 0xFF);
            for (int i = 0; i < parameters.Length && i + 4 < packet.Length; i++) packet[i + 4] = parameters[i];
            return packet;
        }

        public static byte[] ReadFlags() { return Register(0x00, false); }
        public static byte[] WriteFlags(int flags)
        {
            return Register(0x00, true, (byte)((flags >> 16) & 0xFF), (byte)((flags >> 8) & 0xFF), (byte)(flags & 0xFF));
        }
        public static byte[] Discover(bool start) { return Register(0xC0, true, SearchSeconds, (byte)(start ? 0x01 : 0x02)); }
        public static byte[] Pair(byte[] address, byte authentication, byte entropy)
        {
            byte[] p = new byte[10];
            p[0] = 0x01;
            p[1] = 0x00;
            for (int i = 0; i < 6 && i < address.Length; i++) p[2 + i] = address[i];
            p[8] = authentication;
            p[9] = entropy;
            return Register(0x2C1, true, p);
        }
        public static byte[] CancelPairing() { return Register(0x2C1, true, 0x02, 0x00, 0, 0, 0, 0, 0, 0, 0x00, 20); }
        public static byte[] Unpair(int slot) { return Register(0x2C1, true, 0x03, (byte)slot); }
        public static byte[] ReadPairing(int slot) { return Register(0x2B5, false, (byte)(0x50 + slot)); }
        public static byte[] ReadName(int slot) { return Register(0x2B5, false, (byte)(0x60 + slot), 0x01); }

        // Whether reply answers request (0x83 B5 also has to match the sub-register).
        public static bool IsReply(byte[] reply, byte[] request)
        {
            if (reply == null || request == null || reply.Length < 5 || request.Length < 5) return false;
            if (reply[1] != 0xFF || reply[2] != request[2] || reply[3] != request[3]) return false;
            if (request[2] == 0x83 && request[3] == 0xB5) return reply[4] == request[4];
            return true;
        }

        // The receiver's error for request (10 FF 8F <sub> <address> <code>).
        public static bool IsError(byte[] reply, byte[] request)
        {
            return reply != null && request != null && reply.Length >= 6 && request.Length >= 4 &&
                reply[0] == 0x10 && reply[1] == 0xFF && reply[2] == 0x8F && reply[3] == request[2] && reply[4] == request[3];
        }

        public static int Flags(byte[] reply)
        {
            if (reply == null || reply.Length < 7) return -1;
            return (reply[4] << 16) | (reply[5] << 8) | reply[6];
        }

        // 0x2B5 0x5N reply: kind in the low nibble of [5], WPID is [7][6], serial [8..11].
        public static BoltSlot ParseSlot(int slot, byte[] pairing, byte[] name)
        {
            if (pairing == null || pairing.Length < 12) return null;
            int kind = pairing[5] & 0x0F;
            string wpid = pairing[7].ToString("X2") + pairing[6].ToString("X2");
            if (kind == 0 && wpid == "0000") return null;
            var entry = new BoltSlot { Slot = slot, Kind = kind, Wpid = wpid };
            entry.Serial = pairing[8].ToString("X2") + pairing[9].ToString("X2") + pairing[10].ToString("X2") + pairing[11].ToString("X2");
            if (name != null && name.Length >= 8)
            {
                int length = Math.Min(14, Math.Min(name[6], name.Length - 7));
                if (length > 0) entry.Name = Encoding.UTF8.GetString(name, 7, length);
            }
            return entry;
        }

        // A mouse passkey: the number as 10 binary digits, 1 = right button, 0 = left (true = right).
        public static bool[] MouseSequence(string code)
        {
            int value;
            if (!Int32.TryParse(code, out value)) return new bool[0];
            string bits = Convert.ToString(value, 2);
            if (bits.Length < 10) bits = new string('0', 10 - bits.Length) + bits;
            bool[] result = new bool[bits.Length];
            for (int i = 0; i < bits.Length; i++) result[i] = bits[i] == '1';
            return result;
        }
    }

    // One pairing attempt: after discovery starts, feed it the receiver's notifications; it decides what to send next.
    internal sealed class BoltPairingFlow
    {
        public enum Output { None, Send, Finished, ClosedWithoutResult }

        public BoltPhase Phase = BoltPhase.Idle;
        public BoltFailure Failure = BoltFailure.None;
        public string Code = "";
        public string DeviceName = "";
        public int DeviceKind;
        public bool KeyboardPasskey;
        public int PairedSlot = -1;
        public bool PairSent, LockOpen, Discovering;
        public bool ClosedWaiting;      // the pairing state closed without a result; a new slot's connection notice decides
        public byte[] Outgoing;         // the packet to send when Handle returns Output.Send

        private int counter = -1;
        private byte[] address;
        private int authentication = -1;
        private string name;
        private HashSet<int> knownSlots = new HashSet<int>();

        public bool Active
        {
            get { return Phase == BoltPhase.Searching || Phase == BoltPhase.Pairing || Phase == BoltPhase.Passkey; }
        }

        public void Reset()
        {
            Phase = BoltPhase.Idle;
            Failure = BoltFailure.None;
            Code = "";
            DeviceName = "";
            DeviceKind = 0;
            KeyboardPasskey = false;
            PairedSlot = -1;
            PairSent = LockOpen = Discovering = ClosedWaiting = false;
            Outgoing = null;
            counter = -1;
            address = null;
            authentication = -1;
            name = null;
            knownSlots = new HashSet<int>();
        }

        public void Begin(IEnumerable<int> known)
        {
            Reset();
            knownSlots = new HashSet<int>(known);
            Phase = BoltPhase.Searching;
            Discovering = true;
        }

        public void Fail(BoltFailure failure)
        {
            if (!Active) return;
            Phase = BoltPhase.Failed;
            Failure = failure;
        }

        // What to send to stop whatever may be running on the receiver: the pairing if it was asked for,
        // discovery if it may still run. Used on cancel and when a command got no reply (it may have run anyway).
        public List<byte[]> CleanupPackets()
        {
            var packets = new List<byte[]>();
            if (PairSent || LockOpen) packets.Add(BoltPackets.CancelPairing());
            if (Discovering) packets.Add(BoltPackets.Discover(false));
            return packets;
        }

        // The user cancelled: the packets to send.
        public List<byte[]> Cancel()
        {
            List<byte[]> packets = CleanupPackets();
            Phase = BoltPhase.Idle;
            return packets;
        }

        public Output Handle(byte[] r)
        {
            Outgoing = null;
            if (!Active || r == null || r.Length < 7 || (r[0] != 0x10 && r[0] != 0x11)) return Output.None;
            if (r[1] != 0xFF)
            {
                // Once the pairing state closed without a result, a connection notice from a slot that was not
                // paired before (10 slot 41 ..., bit 6 of [4] clear = connected) means done. Not earlier: an
                // already paired device waking up would be taken for the new one.
                int slot = r[1];
                if (PairSent && ClosedWaiting && r[2] == 0x41 && (r[4] & 0x40) == 0 && slot >= 1 && slot <= 6 && !knownSlots.Contains(slot))
                {
                    Succeed(slot);
                    return Output.Finished;
                }
                return Output.None;
            }
            byte address = r[3];
            byte error = r[4];
            switch (r[2])
            {
                case 0x53:      // discovery status
                    Discovering = address == 0x00;
                    if (Discovering || PairSent) return Output.None;
                    Phase = BoltPhase.Failed;
                    Failure = error == 0x01 || error == 0 ? BoltFailure.NotFound : BoltFailure.Failed;
                    return Output.Finished;
                case 0x4F:      // discovered device (part 0: kind, address, authentication; part 1: name)
                    if (PairSent || r.Length < 20) return Output.None;
                    int count = address + r[4] * 256;
                    if (counter < 0) counter = count;
                    else if (counter != count) return Output.None;
                    if (r[5] == 0)
                    {
                        DeviceKind = r[7];
                        this.address = new byte[6];
                        Array.Copy(r, 10, this.address, 0, 6);
                        authentication = r[18];
                    }
                    else if (r[5] == 1)
                    {
                        int length = Math.Min(r[6], r.Length - 7);
                        name = length > 0 ? Encoding.UTF8.GetString(r, 7, length) : "";
                    }
                    if (this.address == null || authentication < 0 || name == null) return Output.None;
                    PairSent = true;
                    DeviceName = name;
                    Phase = BoltPhase.Pairing;
                    Outgoing = BoltPackets.Pair(this.address, (byte)authentication, (byte)(DeviceKind == 1 ? 20 : 10));
                    return Output.Send;
                case 0x54:      // pairing status
                    if (address == 0x00) { LockOpen = true; return Output.None; }
                    LockOpen = false;
                    if (error != 0)
                    {
                        Phase = BoltPhase.Failed;
                        Failure = error == 0x01 ? BoltFailure.Timeout : BoltFailure.Failed;
                        return Output.Finished;
                    }
                    if (address == 0x02 && r.Length >= 12)
                    {
                        Succeed(r[11]);
                        return Output.Finished;
                    }
                    ClosedWaiting = true;
                    return Output.ClosedWithoutResult;
                case 0x4D:      // passkey (6 ASCII digits)
                    if (r.Length < 10) return Output.None;
                    var code = new StringBuilder();
                    for (int i = 4; i < 10; i++) if (r[i] >= (byte)'0' && r[i] <= (byte)'9') code.Append((char)r[i]);
                    Code = code.ToString();
                    DeviceName = name ?? "";
                    KeyboardPasskey = (authentication & 0x01) != 0;
                    Phase = BoltPhase.Passkey;
                    return Output.None;
            }
            return Output.None;
        }

        private void Succeed(int slot)
        {
            Phase = BoltPhase.Succeeded;
            PairedSlot = slot;
            DeviceName = name ?? "";
        }
    }

    // ---- Logi Bolt receiver (I/O) ----
    internal sealed class BoltReceiverSnapshot
    {
        public bool Present;
        public bool Multiple;       // more than one Bolt receiver plugged in: listing, pairing and unpairing are off
        public List<BoltSlot> Slots = new List<BoltSlot>();
        public BoltPhase Phase = BoltPhase.Idle;
        public BoltFailure Failure = BoltFailure.None;
        public string Code = "";
        public string DeviceName = "";
        public bool KeyboardPasskey;
        public int PairedSlot = -1;
        public bool Active
        {
            get { return Phase == BoltPhase.Searching || Phase == BoltPhase.Pairing || Phase == BoltPhase.Passkey; }
        }
    }

    // Lists, pairs and unpairs devices on a Logi Bolt receiver. Opens its own handles on the receiver's short
    // and long HID++ collections (HidppDiversionManager.BoltPort), separate from the mouse and the keyboard.
    // Everything runs on one worker thread; requests wait for the matching reply from the reader threads.
    // Only the receiver's own replies and notifications (device 0xFF) and, while pairing, connection notices
    // are looked at; every other report is ignored and never logged.
    internal sealed class BoltReceiver : IDisposable
    {
        private const int PairingDeadline = 120000;     // safety limit; the receiver normally reports its own timeout
        private const int GraceMilliseconds = 3000;

        private readonly Action<string> report;
        private readonly System.Collections.Concurrent.BlockingCollection<Action> queue =
            new System.Collections.Concurrent.BlockingCollection<Action>();
        private readonly Thread worker;
        private readonly System.Threading.Timer ticker;

        // Worker thread only.
        private HidppDiversionManager.BoltPort port;
        private List<BoltSlot> slots = new List<BoltSlot>();
        private readonly BoltPairingFlow flow = new BoltPairingFlow();
        private int restoreFlags = -1;
        private int deadline, grace;
        private bool deadlineSet, graceSet;
        private bool disposed, shuttingDown;
        private List<int> unknownSlots = new List<int>();   // slots whose pairing info did not come back (not known to be empty)
        private bool lastRequestRejected;                    // the last Request got an error reply (not a timeout)

        private readonly object pendingLock = new object();
        private byte[] pendingRequest, pendingReply;
        private bool pendingFailed;
        private readonly ManualResetEvent pendingDone = new ManualResetEvent(false);

        public event Action<BoltReceiverSnapshot> Changed;

        public BoltReceiver(Action<string> reportAction)
        {
            report = reportAction;
            worker = new Thread(Run) { IsBackground = true, Name = "MXMasterWin Bolt" };
            worker.Start();
            ticker = new System.Threading.Timer(delegate { Post(Tick); }, null, 1000, 1000);
        }

        public void Refresh()
        {
            Post(delegate
            {
                EnsureOpen();
                if (!flow.Active)
                {
                    RestoreFlags();
                    ReadSlots();
                }
                Publish();
            });
        }

        public void StartPairing() { Post(StartOnWorker); }
        public void CancelPairing() { Post(CancelOnWorker); }

        // The success or failure has been read; back to the normal view.
        public void DismissResult()
        {
            Post(delegate
            {
                if (flow.Active) return;
                flow.Reset();
                Publish();
            });
        }

        // done: 1 unpaired, 0 failed, 2 not sent because the slot no longer holds the expected device (the
        // list was out of date when the user confirmed).
        public void Unpair(BoltSlot expected, Action<int> done)
        {
            Post(delegate
            {
                int result = 0;
                if (port != null && !port.Multiple && !flow.Active)
                {
                    BoltSlot now = BoltPackets.ParseSlot(expected.Slot, Request(BoltPackets.ReadPairing(expected.Slot)), null);
                    if (now == null || now.Wpid != expected.Wpid || now.Serial != expected.Serial)
                    {
                        result = 2;
                        Report("unpair slot " + expected.Slot + " skipped: the slot changed");
                    }
                    else
                    {
                        result = Request(BoltPackets.Unpair(expected.Slot)) != null ? 1 : 0;
                        Report("unpair slot " + expected.Slot + (result == 1 ? " ok" : " failed"));
                    }
                    ReadSlots();
                    Publish();
                }
                try { done(result); }
                catch { }
            });
        }

        public void Dispose()
        {
            ticker.Dispose();
            // Not disposed here: if the wait below gives up, the worker may still set it later.
            var finished = new ManualResetEvent(false);
            Post(delegate
            {
                shuttingDown = true;
                if (flow.Active) CancelOnWorker();
                RestoreFlags();
                disposed = true;
                if (port != null) port.Dispose();
                port = null;
                finished.Set();
            });
            finished.WaitOne(3000);
            queue.CompleteAdding();
        }

        private void Post(Action action)
        {
            try { queue.Add(action); }
            catch (InvalidOperationException) { }
        }

        private void Run()
        {
            foreach (Action action in queue.GetConsumingEnumerable())
            {
                if (disposed) continue;
                try { action(); }
                catch (Exception ex) { Report("error: " + ex.Message); }
            }
        }

        // ---- Pairing (worker thread) ----

        private void StartOnWorker()
        {
            EnsureOpen();
            if (port == null || port.Multiple || flow.Active) { Publish(); return; }
            graceSet = false;
            // Slots that could not be read count as taken, so a device there waking up is not taken for the new one.
            var known = new List<int>(unknownSlots);
            foreach (BoltSlot s in slots) known.Add(s.Slot);
            flow.Begin(known);
            int flags = BoltPackets.Flags(Request(BoltPackets.ReadFlags()));
            if (flags < 0) { Abort("notification flags could not be read", false); return; }
            if ((flags & BoltPackets.WirelessFlag) == 0)
            {
                // The restore is owed from here on, even if the reply to the write gets lost.
                if (restoreFlags < 0) restoreFlags = flags;
                Request(BoltPackets.WriteFlags(flags | BoltPackets.WirelessFlag));
                int now = BoltPackets.Flags(Request(BoltPackets.ReadFlags()));
                if (now < 0 || (now & BoltPackets.WirelessFlag) == 0) { Abort("pairing notifications could not be turned on", false); return; }
            }
            if (Request(BoltPackets.Discover(true)) == null) { Abort("discovery did not start", true); return; }
            Report("pairing: searching");
            deadline = unchecked(Environment.TickCount + PairingDeadline);
            deadlineSet = true;
            Publish();
        }

        private void CancelOnWorker()
        {
            if (!flow.Active) return;
            foreach (byte[] packet in flow.Cancel()) Request(packet);
            Report("pairing: cancelled");
            Finish();
        }

        private void Notification(byte[] bytes)
        {
            if (!flow.Active) return;
            BoltPhase before = flow.Phase;
            switch (flow.Handle(bytes))
            {
                case BoltPairingFlow.Output.Send:
                    Report("pairing: found " + flow.DeviceName + " (kind " + flow.DeviceKind + "); pairing");
                    if (Request(flow.Outgoing) == null)
                    {
                        Abort("pair command not accepted", true);
                        return;
                    }
                    break;
                case BoltPairingFlow.Output.Finished:
                    Finish();
                    return;
                case BoltPairingFlow.Output.ClosedWithoutResult:
                    // The receiver sometimes closes the pairing state before the new device's connection notice.
                    grace = unchecked(Environment.TickCount + GraceMilliseconds);
                    graceSet = true;
                    break;
            }
            if (flow.Phase == BoltPhase.Passkey && before != BoltPhase.Passkey)
                Report("pairing: passkey requested (" + (flow.KeyboardPasskey ? "keyboard" : "mouse buttons") + ")");
            Publish();
        }

        private void Tick()
        {
            if (!flow.Active) return;
            int now = Environment.TickCount;
            if (graceSet && unchecked(now - grace) >= 0)
            {
                flow.Fail(BoltFailure.Failed);
                Finish();
            }
            else if (deadlineSet && unchecked(now - deadline) >= 0)
            {
                Report("pairing: gave up after " + PairingDeadline / 1000 + " s");
                foreach (byte[] packet in flow.CleanupPackets()) Request(packet);
                flow.Fail(BoltFailure.Timeout);
                Finish();
            }
        }

        // A command got no usable reply. It may have run anyway, so with cleanup the receiver is told to stop
        // the discovery or pairing before the attempt is reported as failed.
        private void Abort(string reason, bool cleanup)
        {
            Report("pairing: " + reason);
            if (cleanup) foreach (byte[] packet in flow.CleanupPackets()) Request(packet);
            flow.Fail(BoltFailure.NoAnswer);
            Finish();
        }

        private void Finish()
        {
            deadlineSet = graceSet = false;
            RestoreFlags();
            if (flow.Phase == BoltPhase.Succeeded) Report("pairing: paired " + flow.DeviceName + " on slot " + flow.PairedSlot);
            else if (flow.Phase == BoltPhase.Failed) Report("pairing: failed (" + flow.Failure + ")");
            if (shuttingDown) return;
            ReadSlots();
            Publish();
        }

        // Puts the notification flags back as they were before pairing, confirmed by reading them back; on
        // failure the restore stays owed and is tried again on the next refresh and on exit.
        private void RestoreFlags()
        {
            if (restoreFlags < 0 || port == null) return;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                Request(BoltPackets.WriteFlags(restoreFlags));
                if (BoltPackets.Flags(Request(BoltPackets.ReadFlags())) == restoreFlags)
                {
                    restoreFlags = -1;
                    return;
                }
            }
            Report("pairing: notification flags not restored yet; will retry");
        }

        // ---- Receiver and transport (worker thread) ----

        private void EnsureOpen()
        {
            if (port != null || disposed) return;
            port = HidppDiversionManager.BoltPort.Open(Report);
            if (port == null) return;
            port.Received += OnReport;
            port.Start();
            Report("receiver found");
        }

        private void Lost()
        {
            if (port == null) return;
            Report("receiver removed");
            port.Dispose();
            port = null;
            deadlineSet = graceSet = false;
            restoreFlags = -1;
            flow.Reset();
            slots = new List<BoltSlot>();
        }

        private void ReadSlots()
        {
            var list = new List<BoltSlot>();
            var unknown = new List<int>();
            if (port != null && !port.Multiple)
            {
                for (int slot = 1; slot <= 6; slot++)
                {
                    byte[] pairing = Request(BoltPackets.ReadPairing(slot));
                    if (pairing == null)
                    {
                        // An error reply means an empty slot; no reply at all says nothing.
                        if (!lastRequestRejected && port != null) unknown.Add(slot);
                        continue;
                    }
                    BoltSlot entry = BoltPackets.ParseSlot(slot, pairing, Request(BoltPackets.ReadName(slot)));
                    if (entry != null) list.Add(entry);
                }
            }
            slots = list;
            unknownSlots = unknown;
        }

        private byte[] Request(byte[] packet)
        {
            if (port == null || packet == null) return null;
            lock (pendingLock)
            {
                pendingRequest = packet;
                pendingReply = null;
                pendingFailed = false;
                pendingDone.Reset();
            }
            if (!port.Write(packet))
            {
                lock (pendingLock) pendingRequest = null;
                Lost();
                return null;
            }
            pendingDone.WaitOne(1000);
            lock (pendingLock)
            {
                byte[] reply = pendingFailed ? null : pendingReply;
                lastRequestRejected = pendingFailed && pendingReply != null;
                pendingRequest = null;
                pendingReply = null;
                return reply;
            }
        }

        // Reader threads.
        private void OnReport(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || (bytes[0] != 0x10 && bytes[0] != 0x11)) return;
            lock (pendingLock)
            {
                if (pendingRequest != null && (BoltPackets.IsReply(bytes, pendingRequest) || BoltPackets.IsError(bytes, pendingRequest)))
                {
                    pendingFailed = BoltPackets.IsError(bytes, pendingRequest);
                    pendingReply = bytes;
                    pendingRequest = null;
                    pendingDone.Set();
                    return;
                }
            }
            bool notice = bytes[1] == 0xFF
                ? bytes[2] == 0x4D || bytes[2] == 0x4E || bytes[2] == 0x4F || bytes[2] == 0x53 || bytes[2] == 0x54
                : bytes[2] == 0x41;
            if (notice) Post(delegate { Notification(bytes); });
        }

        private void Publish()
        {
            var s = new BoltReceiverSnapshot
            {
                Present = port != null,
                Multiple = port != null && port.Multiple,
                Slots = new List<BoltSlot>(slots),
                Phase = flow.Phase,
                Failure = flow.Failure,
                Code = flow.Code,
                DeviceName = flow.DeviceName,
                KeyboardPasskey = flow.KeyboardPasskey,
                PairedSlot = flow.PairedSlot
            };
            Action<BoltReceiverSnapshot> handler = Changed;
            if (handler != null)
            {
                try { handler(s); }
                catch { }
            }
        }

        private void Report(string text)
        {
            try { report(text); }
            catch { }
        }
    }

    internal sealed class KeyboardSnapshot
    {
        public string Name = "";
        public string Transport = "";        // "BLE" or "BOLT"
        public int BatteryPercent = -1;
        public bool Charging;
        public string Firmware = "";
        public List<HostEntry> Hosts = new List<HostEntry>();
        public int CurrentHost = -1;
        public bool BacklightPresent;
        public bool BacklightEnabled;
        public int BacklightMode;            // 1 automatic, 3 manual; other values are the keyboard's own
        public int BacklightLevel;
        public int BacklightLevels;          // manual brightness steps (0 .. levels-1)
        public bool BacklightAuto;
        public bool BacklightManual;
        public int DisabledKeysMask, DisabledKeys, CurrentPlatform = -1;
        public List<SolaarFeatures.Platform> Platforms = new List<SolaarFeatures.Platform>();
        public int[] BacklightDelays = new int[0];
        public bool FnPresent;
        public bool FnMediaFirst = true;     // true: the top row sends media keys, hold Fn for F1-F12
        // The keyboard did not answer this time (asleep, or on another computer); the values above are
        // the last reading. A keyboard on a Bolt receiver sleeps after a while idle and cannot be reached
        // until a key is pressed.
        public bool Asleep;

        public KeyboardSnapshot Copy()
        {
            var copy = (KeyboardSnapshot)MemberwiseClone();
            copy.Hosts = new List<HostEntry>(Hosts);
            return copy;
        }
    }

    // HID++ 2.0 with a Logitech keyboard (tested on an MX Mechanical Mini), on handles of its own so
    // the mouse code is untouched. A Bolt receiver is shared with the mouse: Windows gives every open
    // handle its own copy of the input reports, and replies are matched on device index and software
    // ID. Feature numbers and byte layouts follow Solaar: 0x1814/0x1815 channels, 0x1982 backlight,
    // 0x40A3 Fn inversion, 0x1004 battery, 0x0005 name and type. Typing reports are never read here:
    // the keyboard's own collections cannot be opened, only the HID++ one.
    internal sealed class KeyboardManager : IDisposable
    {
        private const byte SoftwareId = 0x0C;
        private bool hostNameTried;     // this connection already tried to write the channel name (under sync)
        private readonly object sync = new object();
        private readonly Action<string> report;
        private List<HidppDiversionManager.HidTransport> transports = new List<HidppDiversionManager.HidTransport>();
        private HidppDiversionManager.HidTransport transport;
        private int deviceIndex = -1;
        private readonly Dictionary<int, int> features = new Dictionary<int, int>();
        private readonly Dictionary<int, int> featureVersions = new Dictionary<int, int>();
        private volatile bool disposed;
        private volatile bool connected;
        private volatile bool asleep;
        private int lastSearch = unchecked(Environment.TickCount - 600000);
        private KeyboardSnapshot last;          // last good reading of the keyboard in use
        // A switch asked for while the keyboard was asleep: sent as soon as it answers again, if that
        // happens before the deadline (pendingHost -1 = none).
        private volatile int pendingHost = -1;
        // Bumped by CancelPendingSwitch: a switch that a Refresh or ChangeHost already picked up is checked
        // against it again right before it is queued or written, so a cancel during a wait still wins.
        private int switchGeneration;
        // While the keyboard does not answer, look for it on the other connections at most this often
        // (it may have come back over Bluetooth instead of Bolt); a forced refresh looks at once.
        private const int ResearchMilliseconds = 60000;
        private int pendingUntil;
        private const int PendingMilliseconds = 60000;
        private int lastSlot = -1;              // Bolt slot the keyboard was last found on
        private volatile int hintSlot = -1;     // Bolt slot that just announced a connection
        private volatile int errorSlot = -1;    // Bolt slot the receiver just reported empty or unreachable
        private volatile int mouseSlot = -1;    // Bolt slot of the mouse (never the keyboard)

        // The software ID in this manager's requests; the receiver's errors carry it in their low nibble.
        public const int SoftwareIdValue = SoftwareId;

        public KeyboardManager(Action<string> reportAction)
        {
            report = reportAction;
        }

        // Read without the lock so the UI thread never waits behind a search.
        public bool Connected { get { return connected; } }
        public bool Asleep { get { return asleep; } }
        public bool PendingSwitch { get { return pendingHost >= 0 && unchecked(pendingUntil - Environment.TickCount) > 0; } }

        // A device on the Bolt receiver (not the mouse) just connected: a keyboard coming back from
        // another computer or waking up. The next search asks that slot first.
        public void DeviceAnnounced(int slot)
        {
            if (slot >= 1 && slot <= 6) hintSlot = slot;
        }

        // The Bolt receiver answered one of this manager's requests to a slot with an error (empty
        // slot, or a device that is off or out of range): stop waiting for that slot.
        public void SlotError(int slot)
        {
            errorSlot = slot;
        }

        public void SetMouseSlot(int slot)
        {
            mouseSlot = slot >= 1 && slot <= 6 ? slot : -1;
        }

        public void CancelPendingSwitch()
        {
            Interlocked.Increment(ref switchGeneration);
            if (pendingHost >= 0) Report("queued keyboard switch cancelled");
            pendingHost = -1;
        }

        // Reads the keyboard, looking for one first when none is connected (at most every 8 seconds
        // unless forced). Null when there is no keyboard on this computer.
        public KeyboardSnapshot Refresh(bool force)
        {
            lock (sync)
            {
                if (disposed) return null;
                try
                {
                    if (transport == null)
                    {
                        if (!force && unchecked(Environment.TickCount - lastSearch) < 8000) return Kept();
                        Search(force);
                        if (transport == null) return Kept();
                    }
                    KeyboardSnapshot snapshot = ReadSnapshot();
                    if (snapshot == null && last != null &&
                        (force || unchecked(Environment.TickCount - lastSearch) >= ResearchMilliseconds))
                    {
                        // It may have come back over another connection (e.g. Bluetooth instead of Bolt):
                        // look again, keeping the last reading on screen meanwhile.
                        KeyboardSnapshot cached = last;
                        bool wasAsleep = asleep;
                        Drop(null);
                        last = cached;
                        asleep = wasAsleep;
                        Search(force);
                        if (transport != null) snapshot = ReadSnapshot();
                    }
                    if (snapshot == null)
                    {
                        // Never read it: treat it as gone. Read before: it is most likely asleep, so keep
                        // the handle and the last reading and try again later.
                        if (last == null)
                        {
                            Drop("keyboard no longer answers");
                            return null;
                        }
                        if (!asleep) Report("keyboard not answering (asleep or on another computer); keeping the last reading");
                        return Kept();
                    }
                    if (asleep) Report("keyboard answers again");
                    asleep = false;
                    last = snapshot;
                    int generation = Thread.VolatileRead(ref switchGeneration);
                    int queued = pendingHost;
                    if (queued >= 0)
                    {
                        pendingHost = -1;
                        if (unchecked(pendingUntil - Environment.TickCount) <= 0)
                            Report("queued keyboard switch to " + queued + " expired");
                        else if (queued != snapshot.CurrentHost && SendChangeHost(queued, generation) == 1)
                            return null;     // it has left this computer
                    }
                    return snapshot;
                }
                catch (Exception ex)
                {
                    Drop("keyboard read failed: " + ex.Message);
                    return null;
                }
            }
        }

        // Moves the keyboard to another channel. Like the mouse it leaves at once, so a request that
        // went out counts as done.
        public bool ChangeHost(int host)
        {
            lock (sync)
            {
                if (disposed) return false;
                try
                {
                    int generation = Thread.VolatileRead(ref switchGeneration);
                    pendingHost = -1;
                    if (transport == null) Search(false);
                    if (transport == null)
                    {
                        // Asleep and not found again yet (see Refresh): queue it like below.
                        if (last != null) Queue(host, generation);
                        return false;
                    }
                    int result = SendChangeHost(host, generation);
                    if (result >= 0) return result == 1;
                    // Asleep (typical on Bolt): it cannot be reached until a key is pressed, so send
                    // the switch the moment it answers again.
                    if (asleep || last != null) Queue(host, generation);
                    return false;
                }
                catch (Exception ex)
                {
                    Drop("keyboard change host failed: " + ex.Message);
                    return false;
                }
            }
        }

        private void Queue(int host, int generation)
        {
            if (generation != Thread.VolatileRead(ref switchGeneration))
            {
                Report("keyboard switch to " + host + " cancelled while waiting");
                return;
            }
            asleep = true;
            pendingHost = host;
            pendingUntil = unchecked(Environment.TickCount + PendingMilliseconds);
            Report("keyboard not answering; switch to " + host + " queued for " + PendingMilliseconds / 1000 + " s");
        }

        // The last reading, marked asleep (null when there is none).
        private KeyboardSnapshot Kept()
        {
            if (last == null) return null;
            asleep = true;
            KeyboardSnapshot kept = last.Copy();
            kept.Asleep = true;
            return kept;
        }

        // 1 = sent (the keyboard has left), 0 = it answered but is already there, the channel is invalid or
        // the switch was cancelled meanwhile, -1 = no answer.
        private int SendChangeHost(int host, int generation)
        {
            byte[] info = CallTimed(0x1814, 0, FirstCallTimeout);
            if (info == null) return -1;
            if (host < 0 || host >= info[4] || host == info[5]) return 0;
            if (generation != Thread.VolatileRead(ref switchGeneration))
            {
                Report("keyboard switch to " + host + " cancelled while waiting");
                return 0;
            }
            Report("change host " + info[5] + " -> " + host);
            try { CallTimed(0x1814, 1, 300, (byte)host); }
            catch (Exception ex) { Report("change host: " + ex.Message); }
            Drop(null);
            last = null;
            return 1;
        }

        // Backlight: off keeps the current mode; mode 1 automatic, 3 manual; the level is written only
        // in manual mode. Write layout (Solaar): enabled, options, 0xFF, level, then the three delays
        // (hands out, hands in, powered; two bytes each) written back as read. Confirmed by reading back.
        public bool SetBacklight(bool enabled, int mode, int level)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] r = Call(0x1982, 0);
                    if (r == null || r.Length < 16) return false;
                    byte options = (byte)((r[5] & 0x07) | ((mode & 0x03) << 3));
                    byte value = mode == 3 ? (byte)Math.Max(0, Math.Min(255, level)) : r[9];
                    if (Call(0x1982, 1, (byte)(enabled ? 1 : 0), options, 0xFF, value, r[10], r[11], r[12], r[13], r[14], r[15]) == null) return false;
                    byte[] check = Call(0x1982, 0);
                    if (check == null || check.Length < 16 || (check[4] != 0) != enabled) return false;
                    if (!enabled) return true;
                    return ((check[5] >> 3) & 0x03) == mode && (mode != 3 || check[9] == value);
                }
                catch (Exception ex)
                {
                    Report("backlight write failed: " + ex.Message);
                    return false;
                }
            }
        }

        public bool SetDisabledKey(int bit, bool disabled)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] p = SolaarFeatures.DisabledKey(Call(0x4521, 0), Call(0x4521, 1), bit, disabled);
                    return p != null && Call(0x4521, 2, p) != null && SolaarFeatures.Matches(Call(0x4521, 1), p);
                }
                catch { return false; }
            }
        }
        private List<SolaarFeatures.Platform> ReadPlatforms(byte[] info)
        {
            var descriptors = new List<byte[]>();
            if (info == null || info.Length < 11 || (info[4] & 2) == 0) return new List<SolaarFeatures.Platform>();
            for (int i = 0; i < info[6]; i++)
            {
                byte[] descriptor = Call(0x4531, 1, (byte)i);
                if (descriptor == null) return new List<SolaarFeatures.Platform>();
                descriptors.Add(descriptor);
            }
            return SolaarFeatures.Platforms(descriptors);
        }
        public bool SetPlatform(int value)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] host = Call(0x1814, 0), info = Call(0x4531, 0);
                    byte[] p = SolaarFeatures.PlatformWrite(info, ReadPlatforms(info), value);
                    if (host == null || p == null || Call(0x4531, 3, p) == null) return false;
                    byte[] after = Call(0x1814, 0);
                    return after != null && SolaarFeatures.PlatformMatches(Call(0x4531, 0), value, host[5], after[5]);
                }
                catch { return false; }
            }
        }
        public bool SetBacklightDelay(int field, int seconds)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    byte[] r = Call(0x1982, 0);
                    int version;
                    featureVersions.TryGetValue(0x1982, out version);
                    byte[] p = SolaarFeatures.BacklightDelay(r, version, field, seconds);
                    return p != null && Call(0x1982, 1, p) != null && SolaarFeatures.BacklightMatches(Call(0x1982, 0), p);
                }
                catch { return false; }
            }
        }

        // Top row: mediaFirst = true sends media keys by default. 0xFF means the channel of this computer
        // (the keyboard keeps one setting per channel).
        public bool SetFnMediaFirst(bool mediaFirst)
        {
            lock (sync)
            {
                if (disposed || transport == null) return false;
                try
                {
                    if (Call(0x40A3, 1, 0xFF, (byte)(mediaFirst ? 1 : 0)) == null) return false;
                    byte[] check = Call(0x40A3, 0, 0xFF);
                    return check != null && (check[5] != 0) == mediaFirst;
                }
                catch (Exception ex)
                {
                    Report("Fn write failed: " + ex.Message);
                    return false;
                }
            }
        }

        // Opens every candidate interface and asks each device whether it is a keyboard (0x0005
        // function 2 reports 0 for a keyboard, 3 for a mouse). Bolt receivers are tried on slots 1-6.
        private void Search(bool verbose)
        {
            lastSearch = Environment.TickCount;
            Drop(null);
            CloseTransports();
            transports = HidppDiversionManager.HidTransport.FindKeyboardTransports(verbose ? (Action<string>)Report : delegate { });
            // Bolt slots: the one that just announced a connection and the one the keyboard was found on
            // before come first; the mouse's own slot is skipped. Each slot gets the long wait an idle
            // keyboard needs, cut short when the receiver reports the slot empty or unreachable (seen on
            // its short-report interface by raw input, passed in through SlotError).
            int hint = hintSlot;
            hintSlot = -1;
            var boltSlots = new List<int>();
            if (hint >= 1 && hint <= 6) boltSlots.Add(hint);
            if (lastSlot >= 1 && lastSlot <= 6 && !boltSlots.Contains(lastSlot)) boltSlots.Add(lastSlot);
            for (int n = 1; n <= 6; n++) if (!boltSlots.Contains(n)) boltSlots.Add(n);
            boltSlots.Remove(mouseSlot);
            foreach (HidppDiversionManager.HidTransport candidate in transports)
            {
                int[] slots = candidate.IsBolt ? boltSlots.ToArray() : new[] { 0xFF };
                foreach (int slot in slots)
                {
                    transport = candidate;
                    deviceIndex = slot;
                    features.Clear(); featureVersions.Clear();
                    byte[] type = null;
                    try
                    {
                        errorSlot = -1;
                        int probe = slot;
                        byte[] root = candidate.Request(slot, 0, 0, SoftwareId, new byte[] { 0x00, 0x05, 0 }, FirstCallTimeout,
                            delegate { return errorSlot == probe; });
                        if (root != null && root[4] != 0)
                        {
                            features[0x0005] = root[4];
                            type = CallTimed(0x0005, 2, 900);
                        }
                    }
                    catch { }
                    if (type != null && type[4] == 0 && FindFeature(0x1814, 900) >= 0)
                    {
                        // Keep only the handle in use.
                        foreach (HidppDiversionManager.HidTransport other in transports)
                            if (other != candidate) other.Dispose();
                        transports = new List<HidppDiversionManager.HidTransport> { candidate };
                        connected = true;
                        last = null;
                        if (candidate.IsBolt) lastSlot = slot;
                        hostNameTried = false;
                        Report("keyboard found on " + candidate.Name + " dev=" + slot.ToString("X2"));
                        return;
                    }
                }
            }
            transport = null;
            deviceIndex = -1;
            features.Clear(); featureVersions.Clear();
            CloseTransports();
        }

        private void Drop(string reason)
        {
            if (reason != null && transport != null) Report(reason);
            transport = null;
            deviceIndex = -1;
            features.Clear(); featureVersions.Clear();
            connected = false;
            asleep = false;
        }

        private void CloseTransports()
        {
            foreach (HidppDiversionManager.HidTransport t in transports)
            {
                try { t.Dispose(); }
                catch { }
            }
            transports = new List<HidppDiversionManager.HidTransport>();
        }

        // An idle keyboard on Bolt answers the first request only after about a second (measured on
        // the MX Mechanical Mini: 0.9 s timeouts were missed by ~20 ms), so the first call waits longer.
        private const int FirstCallTimeout = 2500;

        private KeyboardSnapshot ReadSnapshot()
        {
            byte[] info = CallTimed(0x1814, 0, FirstCallTimeout);
            if (info == null) return null;
            var s = new KeyboardSnapshot();
            s.Transport = transport.Name;
            s.CurrentHost = info[5];
            for (int i = 0; i < Math.Min((int)info[4], 8); i++)
            {
                var entry = new HostEntry { Index = i };
                byte[] host = Call(0x1815, 1, (byte)i);
                if (host != null)
                {
                    entry.Paired = host[5] != 0;
                    entry.Bus = host[6];
                    entry.Name = ReadText(host[8], delegate(int offset) { return Call(0x1815, 3, (byte)i, (byte)offset); }, 6);
                }
                s.Hosts.Add(entry);
            }
            if (!hostNameTried && s.CurrentHost >= 0 && s.CurrentHost < s.Hosts.Count)
            {
                string written = WriteHostName(s.CurrentHost, s.Hosts[s.CurrentHost].Name);
                if (written != null) s.Hosts[s.CurrentHost].Name = written;
            }
            byte[] count = Call(0x0005, 0);
            if (count != null) s.Name = ReadText(Math.Min((int)count[4], 64), delegate(int offset) { return Call(0x0005, 1, (byte)offset); }, 4);
            byte[] battery = Call(0x1004, 1);
            if (battery != null)
            {
                s.BatteryPercent = battery[4];
                s.Charging = battery[6] == 1 || battery[6] == 2;
            }
            byte[] firmwareCount = Call(0x0003, 0);
            if (firmwareCount != null)
            {
                for (int i = 0; i < Math.Min((int)firmwareCount[4], 8); i++)
                {
                    byte[] fw = Call(0x0003, 1, (byte)i);
                    if (fw == null || fw.Length < 12 || (fw[4] & 0x0F) != 0) continue;
                    string name = Encoding.ASCII.GetString(fw, 5, 3).Trim('\0', ' ');
                    s.Firmware = name + " " + fw[8].ToString("X2") + "." + fw[9].ToString("X2") + ".B" + ((fw[10] << 8) | fw[11]).ToString("X4");
                    break;
                }
            }
            byte[] light = Call(0x1982, 0);
            if (light != null && light.Length >= 16)
            {
                s.BacklightPresent = true;
                s.BacklightEnabled = light[4] != 0;
                s.BacklightMode = (light[5] >> 3) & 0x03;
                s.BacklightAuto = (light[6] & 0x08) != 0;
                s.BacklightManual = (light[6] & 0x20) != 0;
                s.BacklightLevel = light[9];
                byte[] levels = Call(0x1982, 2);
                if (levels != null) s.BacklightLevels = levels[4];
            }
            byte[] disableCaps = Call(0x4521, 0), disabled = Call(0x4521, 1);
            if (disableCaps != null && disabled != null)
            {
                s.DisabledKeysMask = disableCaps[4]; s.DisabledKeys = disabled[4];
            }
            byte[] platform = Call(0x4531, 0);
            if (platform != null)
            {
                s.Platforms = ReadPlatforms(platform); s.CurrentPlatform = platform[10];
            }
            int backlightVersion;
            featureVersions.TryGetValue(0x1982, out backlightVersion);
            if (light != null && light.Length >= 16 && backlightVersion >= 3)
                s.BacklightDelays = new int[] { SolaarFeatures.Duration(light, 0), SolaarFeatures.Duration(light, 1), SolaarFeatures.Duration(light, 2) };
            byte[] fn = Call(0x40A3, 0, 0xFF);
            if (fn != null)
            {
                s.FnPresent = true;
                s.FnMediaFirst = fn[5] != 0;
            }
            Report("keyboard " + s.Name + " " + s.Transport + " battery=" + s.BatteryPercent + " fw=" + s.Firmware + " hosts current=" + s.CurrentHost +
                String.Concat(s.Hosts.ConvertAll(delegate(HostEntry h) { return " " + h.Index + ":" + (h.Name.Length == 0 ? "-" : h.Name) + "/" + h.Bus; }).ToArray()));
            return s;
        }

        // Reads a string that the device hands out in pieces starting at byte `start` of each reply.
        // The current channel's name differs from this computer: write this computer's name (see HostName),
        // confirmed by reading it back; tried once per connection.
        private string WriteHostName(int current, string stored)
        {
            hostNameTried = true;
            string computer = HostName.ComputerName;
            byte[] caps = Call(0x1815, 0);
            byte[] info = Call(0x1815, 1, (byte)current);
            if (caps == null || info == null || !HostName.NeedsWrite(caps[4], stored, computer, info[9])) return null;
            string fitted = Encoding.UTF8.GetString(HostName.Encode(computer, info[9]));
            foreach (byte[] packet in HostName.WritePackets(current, HostName.Encode(computer, info[9])))
            {
                if (Call(0x1815, 4, packet) == null)
                {
                    Report("host name write failed");
                    return null;
                }
            }
            byte[] after = Call(0x1815, 1, (byte)current);
            if (after == null) return null;
            var name = new List<byte>();
            while (name.Count < after[8])
            {
                byte[] piece = Call(0x1815, 3, (byte)current, (byte)name.Count);
                if (piece == null) break;
                int take = Math.Min(after[8] - name.Count, Math.Min(14, piece.Length - 6));
                if (take <= 0) break;
                for (int k = 0; k < take; k++) name.Add(piece[6 + k]);
            }
            string now = Encoding.UTF8.GetString(name.ToArray());
            Report("host name of channel " + (current + 1) + " written: " + (HostName.Same(now, fitted) ? "confirmed" : "not confirmed"));
            return now;
        }

        private static string ReadText(int length, Func<int, byte[]> piece, int start)
        {
            var bytes = new List<byte>();
            while (bytes.Count < length)
            {
                byte[] reply = piece(bytes.Count);
                if (reply == null) break;
                int take = Math.Min(length - bytes.Count, reply.Length - start);
                if (take <= 0) break;
                for (int k = 0; k < take; k++) bytes.Add(reply[start + k]);
            }
            return Encoding.UTF8.GetString(bytes.ToArray()).Trim('\0');
        }

        private int FindFeature(int feature, int timeout)
        {
            int cached;
            if (features.TryGetValue(feature, out cached)) return cached;
            byte[] root = transport.Request(deviceIndex, 0, 0, SoftwareId, new byte[] { (byte)(feature >> 8), (byte)(feature & 0xFF), 0 }, timeout);
            if (root == null) return -1;      // no answer: ask again next time
            int index = root[4] == 0 ? -1 : root[4];
            features[feature] = index;
            featureVersions[feature] = root.Length >= 7 ? root[6] : 0;
            return index;
        }

        private byte[] Call(int feature, int function, params byte[] parameters)
        {
            return CallTimed(feature, function, 900, parameters);
        }

        private byte[] CallTimed(int feature, int function, int timeout, params byte[] parameters)
        {
            if (transport == null) return null;
            int index = FindFeature(feature, 1800);
            if (index < 0) return null;
            byte[] reply = transport.Request(deviceIndex, index, function, SoftwareId, parameters, timeout);
            return reply != null && reply.Length >= 12 ? reply : null;
        }

        private void Report(string text)
        {
            try { report(text); }
            catch { }
        }

        public void Dispose()
        {
            disposed = true;
            lock (sync)
            {
                Drop(null);
                CloseTransports();
            }
        }
    }

    // ---- Other devices protocol ----
    // Pure logic for the "Other devices" page: Logitech devices on a Lightspeed receiver (G502 LIGHTSPEED).
    // Ported from MX Master Mac (OtherDevicesProtocol.swift). No hardware access in this section, so
    // tools/other-devices-test can compile it alone and compare its output with the Swift version.
    internal enum OtherDeviceLink { Online, Reading, NoAnswer, ReceiverGone }
    internal enum OtherChargeState { Discharging, Charging, Full, PluggedNotCharging, Error }
    internal enum OtherBatterySource { None, Percent, Voltage }
    internal enum OtherReadPlan { Battery, Full }
    internal enum OtherButtonKind { Mouse, Key, Consumer, Function, Disabled, Unknown }
    internal enum OtherLightKind { Off, Fixed, Cycle, Breathe, Unknown }
    internal enum OtherRegisterKind { Reply, Error, Timeout }
    internal enum OtherSlotKind { Paired, Empty, Unknown }
    internal enum OtherOnboardEventKind { ProfileChanged, StageChanged }
    internal enum OtherWriteModeKind { Patch, Restore, Repair }

    internal enum OtherWriteResult
    {
        Written,            // written, read back equal, reloaded
        WrittenNotApplied,  // written and read back equal, but the reload or stage restore was not confirmed
        Unchanged,
        ProfileChanged,     // the mouse is no longer on the profile being edited; nothing written
        ReadFailed,
        BackupFailed,
        UnexpectedContent,
        LeftClickLocked,
        NoBackup,
        FailedUnchanged,    // the write failed and the sector still reads as before
        FailedRolledBack,   // the write failed and the previous content was written back
        FailedNeedsRepair   // the write failed and could not be undone; repair when the device returns
    }

    internal sealed class OtherButtonAssignment : IEquatable<OtherButtonAssignment>
    {
        public readonly OtherButtonKind Kind;
        public readonly int Number;
        public readonly byte Modifiers;
        public readonly byte Usage;
        public readonly ushort Code;
        public readonly byte FunctionId;
        private readonly byte[] raw;

        private OtherButtonAssignment(OtherButtonKind kind, int number, byte modifiers, byte usage, ushort code, byte functionId, byte[] raw)
        {
            Kind = kind;
            Number = number;
            Modifiers = modifiers;
            Usage = usage;
            Code = code;
            FunctionId = functionId;
            this.raw = raw;
        }

        public static OtherButtonAssignment Mouse(int number) { return new OtherButtonAssignment(OtherButtonKind.Mouse, number, 0, 0, 0, 0, null); }
        public static OtherButtonAssignment Key(byte modifiers, byte usage) { return new OtherButtonAssignment(OtherButtonKind.Key, 0, modifiers, usage, 0, 0, null); }
        public static OtherButtonAssignment Consumer(ushort code) { return new OtherButtonAssignment(OtherButtonKind.Consumer, 0, 0, 0, code, 0, null); }
        public static OtherButtonAssignment Function(byte id) { return new OtherButtonAssignment(OtherButtonKind.Function, 0, 0, 0, 0, id, null); }
        public static OtherButtonAssignment Disabled() { return new OtherButtonAssignment(OtherButtonKind.Disabled, 0, 0, 0, 0, 0, null); }
        public static OtherButtonAssignment Unknown(byte[] bytes) { return new OtherButtonAssignment(OtherButtonKind.Unknown, 0, 0, 0, 0, 0, (byte[])bytes.Clone()); }

        public static OtherButtonAssignment Parse(byte[] b)
        {
            if (b.Length < 4) return Unknown(b);
            byte[] head = new byte[4];
            Array.Copy(b, head, 4);
            if (b[0] == 0x80 && b[1] == 0x01)
            {
                int mask = b[2] << 8 | b[3];
                if (mask != 0 && (mask & (mask - 1)) == 0)
                {
                    int bit = 0;
                    while ((mask >> bit & 1) == 0) bit++;
                    return Mouse(bit + 1);
                }
                return Unknown(head);
            }
            if (b[0] == 0x80 && b[1] == 0x02) return Key(b[2], b[3]);
            if (b[0] == 0x80 && b[1] == 0x03) return Consumer((ushort)(b[2] << 8 | b[3]));
            if (b[0] == 0x80 && b[1] == 0x00) return Disabled();
            if (b[0] == 0x90) return Function(b[1]);
            return Unknown(head);
        }

        public byte[] Bytes
        {
            get
            {
                switch (Kind)
                {
                    case OtherButtonKind.Mouse:
                        int mask = 1 << (Number - 1);
                        return new byte[] { 0x80, 0x01, (byte)(mask >> 8 & 0xFF), (byte)(mask & 0xFF) };
                    case OtherButtonKind.Key: return new byte[] { 0x80, 0x02, Modifiers, Usage };
                    case OtherButtonKind.Consumer: return new byte[] { 0x80, 0x03, (byte)(Code >> 8), (byte)(Code & 0xFF) };
                    case OtherButtonKind.Disabled: return new byte[] { 0x80, 0x00, 0x00, 0x00 };
                    case OtherButtonKind.Function: return new byte[] { 0x90, FunctionId, 0x00, 0x00 };
                    default: return (byte[])raw.Clone();
                }
            }
        }

        // "Hand to the app": button 6 and up. The mouse sends that button and the app runs the chosen action.
        public int? AppButton
        {
            get
            {
                if (Kind == OtherButtonKind.Mouse && Number >= 6) return Number;
                return null;
            }
        }

        public bool Equals(OtherButtonAssignment other)
        {
            if (ReferenceEquals(other, null) || other.Kind != Kind) return false;
            return OtherDevicesProtocol.SameBytes(Bytes, other.Bytes);
        }

        public override bool Equals(object obj) { return Equals(obj as OtherButtonAssignment); }

        public override int GetHashCode()
        {
            int hash = (int)Kind;
            foreach (byte b in Bytes) hash = hash * 31 + b;
            return hash;
        }
    }

    // One lighting zone. Period is in milliseconds, brightness 1-100.
    internal sealed class OtherLightEffect : IEquatable<OtherLightEffect>
    {
        public readonly OtherLightKind Kind;
        public readonly byte R;
        public readonly byte G;
        public readonly byte B;
        public readonly int Period;
        public readonly int Brightness;
        private readonly byte[] raw;

        private OtherLightEffect(OtherLightKind kind, byte r, byte g, byte b, int period, int brightness, byte[] raw)
        {
            Kind = kind;
            R = r;
            G = g;
            B = b;
            Period = period;
            Brightness = brightness;
            this.raw = raw;
        }

        public static OtherLightEffect Off() { return new OtherLightEffect(OtherLightKind.Off, 0, 0, 0, 0, 0, null); }
        public static OtherLightEffect Fixed(byte r, byte g, byte b) { return new OtherLightEffect(OtherLightKind.Fixed, r, g, b, 0, 0, null); }
        public static OtherLightEffect Cycle(int period, int brightness) { return new OtherLightEffect(OtherLightKind.Cycle, 0, 0, 0, period, brightness, null); }
        public static OtherLightEffect Breathe(byte r, byte g, byte b, int period, int brightness) { return new OtherLightEffect(OtherLightKind.Breathe, r, g, b, period, brightness, null); }
        public static OtherLightEffect Unknown(byte[] bytes) { return new OtherLightEffect(OtherLightKind.Unknown, 0, 0, 0, 0, 0, (byte[])bytes.Clone()); }

        public static OtherLightEffect Parse(byte[] b)
        {
            if (b.Length < 11) return Unknown(b);
            switch (b[0])
            {
                case 0x00: return Off();
                case 0x01: return Fixed(b[1], b[2], b[3]);
                // Brightness 0 means 100% (libratbag hidpp20_led_cycle / breath).
                case 0x03: return Cycle(b[6] << 8 | b[7], b[8] == 0 ? 100 : b[8]);
                case 0x0A: return Breathe(b[1], b[2], b[3], b[4] << 8 | b[5], b[7] == 0 ? 100 : b[7]);
                default:
                    byte[] head = new byte[11];
                    Array.Copy(b, head, 11);
                    return Unknown(head);
            }
        }

        public byte[] Bytes
        {
            get
            {
                if (Kind == OtherLightKind.Unknown) return (byte[])raw.Clone();
                byte[] b = new byte[11];
                switch (Kind)
                {
                    case OtherLightKind.Fixed:
                        b[0] = 0x01; b[1] = R; b[2] = G; b[3] = B;
                        break;
                    case OtherLightKind.Cycle:
                        b[0] = 0x03; b[6] = (byte)(Period >> 8 & 0xFF); b[7] = (byte)(Period & 0xFF);
                        b[8] = (byte)Math.Min(Math.Max(Brightness, 1), 100);
                        break;
                    case OtherLightKind.Breathe:
                        b[0] = 0x0A; b[1] = R; b[2] = G; b[3] = B;
                        b[4] = (byte)(Period >> 8 & 0xFF); b[5] = (byte)(Period & 0xFF);
                        b[7] = (byte)Math.Min(Math.Max(Brightness, 1), 100);
                        break;
                }
                return b;
            }
        }

        public bool Equals(OtherLightEffect other)
        {
            if (ReferenceEquals(other, null) || other.Kind != Kind) return false;
            if (Kind == OtherLightKind.Unknown) return OtherDevicesProtocol.SameBytes(raw, other.raw);
            return R == other.R && G == other.G && B == other.B && Period == other.Period && Brightness == other.Brightness;
        }

        public override bool Equals(object obj) { return Equals(obj as OtherLightEffect); }

        public override int GetHashCode()
        {
            return (int)Kind * 31 + (R << 16 | G << 8 | B) * 17 + Period * 7 + Brightness;
        }
    }

    internal sealed class OtherDeviceSnapshot
    {
        public string Receiver = "";     // receiver identity (PID + location); with the slot it forms the id
        public int Slot;
        public string Name = "";
        public int Kind = -1;            // 0x0005 fn2: 0 keyboard, 3 mouse, 4 trackball, 5 touchpad; -1 unknown
        public string Wpid = "";
        public OtherDeviceLink Link = OtherDeviceLink.Online;
        public OtherBatterySource BatterySource = OtherBatterySource.None;
        public int BatteryPercent = -1;
        public int Millivolts;
        public bool Charging;
        public bool Full;
        public string Firmware = "";
        public int Dpi;
        public int DpiMin;
        public int DpiMax;
        public int ReportRateMs;
        public int OnboardMode;          // 1 onboard profiles, 2 host control, 0 no such feature
        public int ProfileCount;
        public int BatterySeq;           // counts successful battery readings, so old data is not counted twice
        public OtherChargeState ChargeState = OtherChargeState.Discharging;
        public bool FastCharge;
        public bool SlowCharge;
        public DateTime? LastSeen;
        public int ActiveProfile;
        public List<int> Profiles = new List<int>();
        public List<int> DpiStages = new List<int>();
        public int StageIndex = -1;
        public int ButtonCount;
        public int SectorSize;
        public List<OtherButtonAssignment> Buttons = new List<OtherButtonAssignment>();
        public int DefaultStage = -1;
        public List<OtherLightEffect> Lights = new List<OtherLightEffect>();
        public string UnitId = "";       // 0x0003 serial; tells two mice of the same model apart
        public int? RepairSector;        // a profile whose last write did not finish
        public List<int> ProfileSlots = new List<int>();   // every directory entry, disabled ones too; device numbers index this list
        public bool OnboardFormatOK;     // memory, profile and macro formats are ones libratbag knows

        public string BackupKey { get { return OtherProfileBackups.DeviceKey(Wpid, UnitId); } }
        public string Id { get { return Receiver + "#" + Slot; } }
        public string ReceiverPid { get { return Receiver.Length <= 4 ? Receiver : Receiver.Substring(0, 4); } }
        public bool IsPointing { get { return Kind == 3 || Kind == 4 || Kind == 5; } }

        // Writing is open only to models verified on hardware, with a known format.
        public bool WriteVerified
        {
            get { return OnboardFormatOK && Array.IndexOf(OtherDevicesProtocol.WriteVerifiedModels, Wpid) >= 0; }
        }

        // Editable: a verified model, online, in onboard mode, on a writable profile (0x01xx is ROM) with a button table.
        public bool CanEditButtons
        {
            get { return WriteVerified && Link == OtherDeviceLink.Online && OnboardMode == 1 && ActiveProfile >= 1 && ActiveProfile < 0x100 && Buttons.Count > 0 && SectorSize >= 0x50; }
        }

        public bool CanSwitchStage
        {
            get
            {
                if (Link != OtherDeviceLink.Online || OnboardMode != 1) return false;
                foreach (int stage in DpiStages) if (stage > 0) return true;
                return false;
            }
        }

        public int? ProfileNumber
        {
            get
            {
                int index = Profiles.IndexOf(ActiveProfile);
                if (index < 0) return null;
                return index + 1;
            }
        }

        public OtherDeviceSnapshot Copy()
        {
            OtherDeviceSnapshot c = (OtherDeviceSnapshot)MemberwiseClone();
            c.Profiles = new List<int>(Profiles);
            c.ProfileSlots = new List<int>(ProfileSlots);
            c.DpiStages = new List<int>(DpiStages);
            c.Buttons = new List<OtherButtonAssignment>(Buttons);
            c.Lights = new List<OtherLightEffect>(Lights);
            return c;
        }
    }

    internal sealed class OtherVoltageInfo
    {
        public int Millivolts;
        public OtherChargeState State;
        public bool Fast;
        public bool Slow;
    }

    internal sealed class OtherBatteryInfo
    {
        public int Percent;
        public OtherChargeState State;
        public bool Slow;
    }

    internal sealed class OtherOnboardEvent
    {
        public OtherOnboardEventKind Kind;
        public int Stage;
    }

    internal sealed class OtherRegisterOutcome
    {
        public OtherRegisterKind Kind;
        public byte[] Reply;
        public static OtherRegisterOutcome FromReply(byte[] reply) { return new OtherRegisterOutcome { Kind = OtherRegisterKind.Reply, Reply = reply }; }
        public static OtherRegisterOutcome Error() { return new OtherRegisterOutcome { Kind = OtherRegisterKind.Error }; }
        public static OtherRegisterOutcome Timeout() { return new OtherRegisterOutcome { Kind = OtherRegisterKind.Timeout }; }
    }

    internal sealed class OtherSlotState
    {
        public OtherSlotKind Kind;
        public string Wpid = "";
        public int DeviceKind;
    }

    internal sealed class OtherLowBatteryResult
    {
        public int Below;
        public bool Notified;
        public bool Notify;
    }

    // Per-device low battery state. Seq is the battery reading counter, so a re-published cache
    // does not count as a second reading; a different WPID means the slot holds another device.
    internal sealed class OtherLowBatteryState
    {
        public string Wpid = "";
        public int Seq = -1;
        public int Below;
        public bool Notified;
        public OtherLowBatteryState Copy() { return (OtherLowBatteryState)MemberwiseClone(); }
    }

    internal sealed class OtherWriteStep
    {
        public byte Function;
        public byte[] Parameters;
    }

    internal static class OtherDevicesProtocol
    {
        public const int Vendor = 0x046D;
        public const byte OnboardSetProfile = 3;
        public const byte OnboardActiveProfile = 4;
        public const byte OnboardReadData = 5;
        public const byte OnboardStageIndex = 0xB;
        public const byte OnboardSetStage = 0xC;
        public const double IdleSeconds = 2;
        public const double QuickIdleSeconds = 0.3;   // the user is waiting for the page: read after a short stop
        public const double PairingCheckInterval = 300;
        public const int ButtonTableOffset = 0x20;
        public const int MaxButtons = 16;
        public const int LightOffset = 0xD0;
        public const int LightSize = 11;
        public static readonly int[] ReportRates = { 1, 2, 4, 8 };
        // Bolt (C548) and Unifying are handled by the mouse and receiver pages, not here.
        public static readonly int[] LightspeedReceivers = { 0xC539, 0xC53A, 0xC53D, 0xC53F, 0xC541, 0xC545, 0xC547, 0xC54D, 0xC52B, 0xC52F, 0xC532 };
        public static readonly byte[] NotificationFlagsRead = { 0x10, 0xFF, 0x81, 0x00, 0, 0, 0 };
        private static readonly int[,] VoltageTable =
        {
            { 4186, 100 }, { 4067, 90 }, { 3989, 80 }, { 3922, 70 }, { 3859, 60 }, { 3811, 50 },
            { 3778, 40 }, { 3751, 30 }, { 3717, 20 }, { 3671, 10 }, { 3646, 5 }, { 3579, 2 }, { 3500, 0 }
        };

        public static bool SameBytes(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        internal static string TrimControl(string text)
        {
            int start = 0, end = text.Length;
            while (start < end && IsControl(text[start])) start++;
            while (end > start && IsControl(text[end - 1])) end--;
            return text.Substring(start, end - start);
        }

        private static bool IsControl(char c)
        {
            var category = char.GetUnicodeCategory(c);
            return category == System.Globalization.UnicodeCategory.Control || category == System.Globalization.UnicodeCategory.Format;
        }

        // Receiver registers
        public static byte[] PairingRequest(int slot) { return new byte[] { 0x10, 0xFF, 0x83, 0xB5, (byte)(0x20 + slot - 1), 0, 0 }; }
        public static byte[] NameRequest(int slot) { return new byte[] { 0x10, 0xFF, 0x83, 0xB5, (byte)(0x40 + slot - 1), 0, 0 }; }

        public static bool Pairing(byte[] reply, int slot, out string wpid, out int kind)
        {
            wpid = "";
            kind = 0;
            if (reply == null || reply.Length < 12 || reply[0] != 0x11 || reply[1] != 0xFF || reply[2] != 0x83 || reply[3] != 0xB5 ||
                reply[4] != (byte)(0x20 + slot - 1)) return false;
            string id = reply[7].ToString("X2") + reply[8].ToString("X2");
            if (id == "0000") return false;
            wpid = id;
            kind = reply[11] & 0x0F;
            return true;
        }

        public static string PairedName(byte[] reply, int slot)
        {
            if (reply == null || reply.Length < 7 || reply[0] != 0x11 || reply[2] != 0x83 || reply[3] != 0xB5 || reply[4] != (byte)(0x40 + slot - 1)) return "";
            int length = Math.Min(reply[5], reply.Length - 6);
            if (length <= 0) return "";
            try { return TrimControl(new UTF8Encoding(false, true).GetString(reply, 6, length)); }
            catch (ArgumentException) { return ""; }
        }

        // HID++ 1.0 type from the receiver -> HID++ 2.0 type (0x0005).
        public static int KindFromReceiver(int kind)
        {
            switch (kind)
            {
                case 1: return 0;
                case 2: return 3;
                case 8: return 4;
                case 9: return 5;
                default: return -1;
            }
        }

        // Receiver connection notice (10 slot 41 protocol flags ...): flag bit 6 = link lost.
        public static bool ConnectionNotice(byte[] bytes, out int slot, out bool connected)
        {
            slot = 0;
            connected = false;
            if (bytes == null || bytes.Length < 5 || bytes[0] != 0x10 || bytes[2] != 0x41 || bytes[1] < 1 || bytes[1] > 6) return false;
            slot = bytes[1];
            connected = (bytes[4] & 0x40) == 0;
            return true;
        }

        public static bool? WirelessNoticesOn(byte[] reply)
        {
            if (reply == null || reply.Length < 7 || (reply[0] != 0x10 && reply[0] != 0x11) || reply[1] != 0xFF || reply[2] != 0x81 || reply[3] != 0x00) return null;
            return (reply[5] & 0x01) != 0;
        }

        // Turns on only the wireless on/off notice (0x000100); other bits are written back as read.
        public static byte[] EnableWirelessNotices(byte[] reply)
        {
            if (WirelessNoticesOn(reply) != false) return null;
            return new byte[] { 0x10, 0xFF, 0x80, 0x00, reply[4], (byte)(reply[5] | 0x01), reply[6] };
        }

        // Battery
        public static int PercentFromMillivolts(int mv)
        {
            int rows = VoltageTable.GetLength(0);
            if (mv >= VoltageTable[0, 0]) return VoltageTable[0, 1];
            if (mv <= VoltageTable[rows - 1, 0]) return 0;
            for (int i = 0; i < rows - 1; i++)
            {
                int highMv = VoltageTable[i, 0], highPercent = VoltageTable[i, 1];
                int lowMv = VoltageTable[i + 1, 0], lowPercent = VoltageTable[i + 1, 1];
                if (mv > lowMv && mv <= highMv)
                {
                    double ratio = (double)(mv - lowMv) / (highMv - lowMv);
                    return lowPercent + (int)Math.Round(ratio * (highPercent - lowPercent), MidpointRounding.AwayFromZero);
                }
            }
            return 0;
        }

        // 0x1001 fn0 reply or event: voltage (big-endian) + flags. Bit 7 = external power; then the low
        // three bits are 0 charging, 1 full, 2 plugged but not charging, others error; bit 3 fast, bit 4 slow.
        public static OtherVoltageInfo VoltageInfo(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7) return null;
            int mv = bytes[4] << 8 | bytes[5];
            if (mv <= 0) return null;
            byte flags = bytes[6];
            if ((flags & 0x80) == 0) return new OtherVoltageInfo { Millivolts = mv, State = OtherChargeState.Discharging };
            OtherChargeState state;
            switch (flags & 0x07)
            {
                case 0: state = OtherChargeState.Charging; break;
                case 1: state = OtherChargeState.Full; break;
                case 2: state = OtherChargeState.PluggedNotCharging; break;
                default: state = OtherChargeState.Error; break;
            }
            return new OtherVoltageInfo
            {
                Millivolts = mv,
                State = state,
                Fast = state == OtherChargeState.Charging && (flags & 0x08) != 0,
                Slow = state == OtherChargeState.Charging && (flags & 0x10) != 0
            };
        }

        // 0x1004 fn1: percent, level, state (0 discharging, 1 charging, 2 slow, 3 full, 4 error).
        public static OtherBatteryInfo UnifiedBatteryInfo(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes[4] > 100) return null;
            OtherChargeState state;
            switch (bytes[6])
            {
                case 0: state = OtherChargeState.Discharging; break;
                case 1:
                case 2: state = OtherChargeState.Charging; break;
                case 3: state = OtherChargeState.Full; break;
                default: state = OtherChargeState.Error; break;
            }
            return new OtherBatteryInfo { Percent = bytes[4], State = state, Slow = bytes[6] == 2 };
        }

        // 0x1000 fn0: percent, next level, state (0 discharging, 1 charging, 2 almost full, 3 full, 4 slow, 5-6 errors).
        public static OtherBatteryInfo BatteryStatusInfo(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 7 || bytes[4] > 100) return null;
            OtherChargeState state;
            switch (bytes[6])
            {
                case 0: state = OtherChargeState.Discharging; break;
                case 1:
                case 2:
                case 4: state = OtherChargeState.Charging; break;
                case 3: state = OtherChargeState.Full; break;
                default: state = OtherChargeState.Error; break;
            }
            return new OtherBatteryInfo { Percent = bytes[4], State = state, Slow = bytes[6] == 4 };
        }

        public static int Smoothed(IList<int> samples)
        {
            if (samples.Count == 0) return 0;
            long sum = 0;
            foreach (int sample in samples) sum += sample;
            return (int)Math.Round((double)sum / samples.Count, MidpointRounding.AwayFromZero);
        }

        // DPI, report rate, firmware
        // 0x2201 fn1 list from byte 5: 2-byte values ending at 0; values with the top three bits set are steps.
        public static bool DpiRange(byte[] bytes, out int min, out int max)
        {
            min = 0;
            max = 0;
            bool any = false;
            for (int i = 5; bytes != null && i + 1 < bytes.Length; i += 2)
            {
                int value = bytes[i] << 8 | bytes[i + 1];
                if (value == 0) break;
                if (value >> 13 == 7) continue;
                if (!any || value < min) min = value;
                if (!any || value > max) max = value;
                any = true;
            }
            return any;
        }

        public static int CurrentDpi(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 9) return 0;
            int current = bytes[5] << 8 | bytes[6];
            return current != 0 ? current : bytes[7] << 8 | bytes[8];
        }

        public static int ReportRateHz(int ms) { return ms > 0 ? 1000 / ms : 0; }

        public static string Firmware(byte[] info)
        {
            if (info == null || info.Length < 12 || (info[4] & 0x0F) != 0) return null;
            bool ascii = info[5] < 0x80 && info[6] < 0x80 && info[7] < 0x80;
            string name = ascii ? TrimControl(Encoding.ASCII.GetString(info, 5, 3)) : "";
            return name + " " + info[8].ToString("X2") + "." + info[9].ToString("X2") + ".B" + (info[10] << 8 | info[11]).ToString("X4");
        }

        // Low battery: two readings in a row at or below the threshold (not charging) before notifying;
        // charging or rising above threshold + 3 starts over.
        public static OtherLowBatteryResult LowBattery(int percent, bool charging, int threshold, int below, bool notified, bool alertOn)
        {
            if (percent < 0) return new OtherLowBatteryResult { Below = below, Notified = notified };
            if (charging || percent > threshold + 3) return new OtherLowBatteryResult();
            if (percent > threshold) return new OtherLowBatteryResult { Notified = notified };
            int count = below + 1;
            if (!alertOn || notified || count < 2) return new OtherLowBatteryResult { Below = count, Notified = notified };
            return new OtherLowBatteryResult { Below = count, Notified = true, Notify = true };
        }

        public static OtherLowBatteryState LowBatteryStep(OtherLowBatteryState previous, string wpid, int seq, int percent, bool charging, int threshold, bool alertOn, out bool notify)
        {
            notify = false;
            OtherLowBatteryState state = previous != null ? previous.Copy() : new OtherLowBatteryState();
            if (state.Wpid != wpid) state = new OtherLowBatteryState { Wpid = wpid };
            if (seq == state.Seq) return state;
            state.Seq = seq;
            OtherLowBatteryResult r = LowBattery(percent, charging, threshold, state.Below, state.Notified, alertOn);
            state.Below = r.Below;
            state.Notified = r.Notified;
            notify = r.Notify;
            return state;
        }

        // Read timing: every HID++ exchange uses the wireless channel, so read only while the mouse rests.
        public static bool MouseIdle(IEnumerable<double> secondsSinceEvents, double threshold)
        {
            foreach (double seconds in secondsSinceEvents) if (seconds < threshold) return false;
            return true;
        }

        public static bool WakeCheckDue(DateTime? lastPing, DateTime now, double secondsSinceMouse)
        {
            if (!lastPing.HasValue) return true;
            double age = (now - lastPing.Value).TotalSeconds;
            return age >= 60 || (age >= 5 && secondsSinceMouse < 6);
        }

        public static OtherReadPlan ReadPlan(bool known, bool sameDevice, bool online, bool hasFeatures, bool forceFull)
        {
            if (forceFull || !known || !sameDevice || !online || !hasFeatures) return OtherReadPlan.Full;
            return OtherReadPlan.Battery;
        }

        // Onboard profiles (0x8100)
        public static byte[] ReadDataParameters(int sector, int offset)
        {
            return new byte[] { (byte)(sector >> 8 & 0xFF), (byte)(sector & 0xFF), (byte)(offset >> 8 & 0xFF), (byte)(offset & 0xFF) };
        }

        // Directory (sector 0): 4-byte entries [sector hi, sector lo, enabled, ?], ending at 0xFFFF.
        public static List<int> Directory(byte[] data)
        {
            var list = new List<int>();
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                int sector = data[i] << 8 | data[i + 1];
                if (sector == 0xFFFF) break;
                if (data[i + 2] != 0 && sector != 0) list.Add(sector);
            }
            return list;
        }

        // Profile head: [report rate, default stage, shift stage, five stages of 2 bytes little-endian].
        public static int[] ProfileStages(byte[] data)
        {
            if (data == null || data.Length < 13) return null;
            int[] stages = new int[5];
            bool any = false;
            for (int i = 0; i < 5; i++)
            {
                int value = data[3 + i * 2] | data[4 + i * 2] << 8;
                stages[i] = value == 0xFFFF ? 0 : value;
                if (stages[i] > 0) any = true;
            }
            return any ? stages : null;
        }

        public static int? ActiveProfile(byte[] reply)
        {
            if (reply == null || reply.Length < 6) return null;
            int sector = reply[4] << 8 | reply[5];
            if (sector == 0) return null;
            return sector;
        }

        public static int? StageIndex(byte[] reply)
        {
            if (reply == null || reply.Length < 5 || reply[4] >= 5) return null;
            return reply[4];
        }

        // Unsolicited 0x8100 events (software ID 0): function 0 profile changed, 1 stage changed.
        public static OtherOnboardEvent OnboardEvent(byte[] bytes, byte featureIndex)
        {
            if (bytes == null || bytes.Length < 5 || bytes[0] != 0x11 || featureIndex == 0 || bytes[2] != featureIndex || (bytes[3] & 0x0F) != 0) return null;
            switch (bytes[3] >> 4)
            {
                case 0: return new OtherOnboardEvent { Kind = OtherOnboardEventKind.ProfileChanged };
                case 1:
                    if (bytes[4] >= 5) return null;
                    return new OtherOnboardEvent { Kind = OtherOnboardEventKind.StageChanged, Stage = bytes[4] };
                default: return null;
            }
        }

        public static int? DpiForStage(int index, IList<int> stages)
        {
            if (index < 0 || index >= stages.Count || stages[index] <= 0) return null;
            return stages[index];
        }

        // A re-plugged receiver: drop old cards of the same PID from another USB location.
        public static List<string> StaleAfterReceiverFound(IEnumerable<OtherDeviceSnapshot> devices, string foundKey)
        {
            string pid = foundKey.Length <= 4 ? foundKey : foundKey.Substring(0, 4);
            var stale = new List<string>();
            foreach (OtherDeviceSnapshot d in devices)
                if (d.Link == OtherDeviceLink.ReceiverGone && d.ReceiverPid == pid && d.Receiver != foundKey) stale.Add(d.Id);
            return stale;
        }

        // 0x0003 fn0 serial (bytes 5-8); all zero or all FF means none.
        public static string UnitId(byte[] reply)
        {
            if (reply == null || reply.Length < 9) return "";
            bool zero = true, ff = true;
            var text = new StringBuilder();
            for (int i = 5; i < 9; i++)
            {
                if (reply[i] != 0) zero = false;
                if (reply[i] != 0xFF) ff = false;
                text.Append(reply[i].ToString("X2"));
            }
            return zero || ff ? "" : text.ToString();
        }

        // The left button may not be remapped unless the current valid profile already has it that way.
        public static bool LeftClickSafe(byte[] target, byte[] current)
        {
            List<OtherButtonAssignment> left = ProfileButtons(target, 1);
            if (left.Count == 0) return false;
            if (left[0].Equals(OtherButtonAssignment.Mouse(1))) return true;
            if (current == null) return false;
            List<OtherButtonAssignment> before = ProfileButtons(current, 1);
            return before.Count > 0 && before[0].Equals(left[0]);
        }

        public static bool ValidStage(int value, int original, int min, int max, int step = 50)
        {
            if (original == 0) return value == 0;
            return value >= min && value <= max && value % step == 0;
        }

        public static bool ReconnectReadNeeded(bool online, DateTime? lastFullRead, DateTime now)
        {
            if (!online || !lastFullRead.HasValue) return true;
            return (now - lastFullRead.Value).TotalSeconds > 3;
        }

        public static bool ReadConfirmsOnline(int timeouts, Func<bool> stillAnswers)
        {
            return timeouts == 0 || stillAnswers();
        }

        public static string AppActionKey(OtherDeviceSnapshot d, int number)
        {
            return d.BackupKey + "-" + d.ActiveProfile.ToString("X4") + ":" + number;
        }

        // Moves old number-only keys to this profile's key. Returns null when nothing changed.
        public static Dictionary<string, V> MigratedAppActions<V>(Dictionary<string, V> actions, OtherDeviceSnapshot d)
        {
            if (d.UnitId.Length == 0 || d.ActiveProfile == 0) return null;
            var result = new Dictionary<string, V>(actions);
            bool changed = false;
            foreach (OtherButtonAssignment button in d.Buttons)
            {
                int? number = button.AppButton;
                if (!number.HasValue) continue;
                string oldKey = number.Value.ToString();
                V value;
                if (!result.TryGetValue(oldKey, out value)) continue;
                string key = AppActionKey(d, number.Value);
                if (!result.ContainsKey(key)) result[key] = value;
                result.Remove(oldKey);
                changed = true;
            }
            return changed ? result : null;
        }

        // Which device sent this button: prefer the same receiver, then any device whose profile uses it.
        public static OtherDeviceSnapshot DeviceForAppButton(int number, string receiver, IEnumerable<OtherDeviceSnapshot> list)
        {
            OtherButtonAssignment wanted = OtherButtonAssignment.Mouse(number);
            OtherDeviceSnapshot first = null;
            foreach (OtherDeviceSnapshot d in list)
            {
                if (d.Link != OtherDeviceLink.Online || !d.Buttons.Contains(wanted)) continue;
                if (d.Receiver == receiver) return d;
                if (first == null) first = d;
            }
            return first;
        }

        public static OtherSlotState SlotState(OtherRegisterOutcome outcome, int slot)
        {
            if (outcome.Kind == OtherRegisterKind.Timeout) return new OtherSlotState { Kind = OtherSlotKind.Unknown };
            if (outcome.Kind == OtherRegisterKind.Error) return new OtherSlotState { Kind = OtherSlotKind.Empty };
            byte[] bytes = outcome.Reply;
            string wpid;
            int kind;
            if (Pairing(bytes, slot, out wpid, out kind)) return new OtherSlotState { Kind = OtherSlotKind.Paired, Wpid = wpid, DeviceKind = kind };
            bool matches = bytes != null && bytes.Length >= 12 && bytes[0] == 0x11 && bytes[1] == 0xFF && bytes[2] == 0x83 &&
                bytes[3] == 0xB5 && bytes[4] == (byte)(0x20 + slot - 1);
            return new OtherSlotState { Kind = matches ? OtherSlotKind.Empty : OtherSlotKind.Unknown };
        }

        // Keep the 0x1D4B index while a device is away: the reconnect event is recognized through it.
        public static Dictionary<int, byte> FeaturesAfterNoAnswer(Dictionary<int, byte> table)
        {
            var result = new Dictionary<int, byte>();
            byte index;
            if (table != null && table.TryGetValue(0x1D4B, out index)) result[0x1D4B] = index;
            return result;
        }

        public static bool PairingCheckDue(DateTime? last, DateTime now)
        {
            if (!last.HasValue) return true;
            return (now - last.Value).TotalSeconds >= PairingCheckInterval;
        }

        // Button table at 0x20: 4 bytes each.
        public static List<OtherButtonAssignment> ProfileButtons(byte[] sector, int count)
        {
            var list = new List<OtherButtonAssignment>();
            int n = Math.Min(Math.Max(count, 0), MaxButtons);
            for (int i = 0; i < n; i++)
            {
                int start = ButtonTableOffset + i * 4;
                if (start + 4 > sector.Length) break;
                byte[] raw = new byte[4];
                Array.Copy(sector, start, raw, 0, 4);
                list.Add(OtherButtonAssignment.Parse(raw));
            }
            return list;
        }

        // CRC-CCITT (init 0xFFFF, poly 0x1021).
        public static ushort Crc(byte[] bytes, int length)
        {
            ushort c = 0xFFFF;
            for (int i = 0; i < length; i++)
            {
                c ^= (ushort)(bytes[i] << 8);
                for (int bit = 0; bit < 8; bit++)
                    c = (c & 0x8000) != 0 ? (ushort)((c << 1) ^ 0x1021) : (ushort)(c << 1);
            }
            return c;
        }

        public static bool CrcValid(byte[] sector)
        {
            if (sector == null || sector.Length < 3) return false;
            int stored = sector[sector.Length - 2] << 8 | sector[sector.Length - 1];
            return Crc(sector, sector.Length - 2) == stored;
        }

        private static void StoreCrc(byte[] sector)
        {
            ushort c = Crc(sector, sector.Length - 2);
            sector[sector.Length - 2] = (byte)(c >> 8);
            sector[sector.Length - 1] = (byte)(c & 0xFF);
        }

        // The whole profile with new buttons and CRC; null when the current CRC is wrong.
        public static byte[] PatchedProfile(byte[] sector, Dictionary<int, OtherButtonAssignment> buttons)
        {
            if (sector == null || sector.Length <= ButtonTableOffset + 4 || !CrcValid(sector)) return null;
            OtherButtonAssignment left;
            if (buttons.TryGetValue(0, out left) && !left.Equals(OtherButtonAssignment.Mouse(1))) return null;
            byte[] result = (byte[])sector.Clone();
            foreach (KeyValuePair<int, OtherButtonAssignment> item in buttons)
            {
                int start = ButtonTableOffset + item.Key * 4;
                byte[] raw = item.Value.Bytes;
                if (item.Key < 0 || item.Key >= MaxButtons || raw.Length != 4 || start + 4 > result.Length - 2) return null;
                Array.Copy(raw, 0, result, start, 4);
            }
            StoreCrc(result);
            return result;
        }

        // fn6 [sector, offset, length], fn7 16 bytes at a time, fn8 finish.
        public static List<OtherWriteStep> WritePlan(int sector, byte[] data)
        {
            var plan = new List<OtherWriteStep>();
            plan.Add(new OtherWriteStep
            {
                Function = 6,
                Parameters = new byte[] { (byte)(sector >> 8 & 0xFF), (byte)(sector & 0xFF), 0, 0, (byte)(data.Length >> 8 & 0xFF), (byte)(data.Length & 0xFF) }
            });
            for (int start = 0; start < data.Length; start += 16)
            {
                byte[] chunk = new byte[Math.Min(16, data.Length - start)];
                Array.Copy(data, start, chunk, 0, chunk.Length);
                plan.Add(new OtherWriteStep { Function = 7, Parameters = chunk });
            }
            plan.Add(new OtherWriteStep { Function = 8, Parameters = new byte[0] });
            return plan;
        }

        // 16 bytes per read; the last read is aligned back so it does not run past the sector.
        public static List<int> ReadOffsets(int sectorSize)
        {
            var list = new List<int>();
            if (sectorSize < 16)
            {
                list.Add(0);
                return list;
            }
            for (int offset = 0; offset < sectorSize; offset += 16) list.Add(Math.Min(offset, sectorSize - 16));
            return list;
        }

        // The lowest of buttons 6-16 this profile does not use yet.
        public static int? FreeAppButton(IList<OtherButtonAssignment> buttons, int exceptIndex)
        {
            var used = new HashSet<int>();
            for (int i = 0; i < buttons.Count; i++)
            {
                int? number = buttons[i].AppButton;
                if (i != exceptIndex && number.HasValue) used.Add(number.Value);
            }
            for (int n = 6; n <= 16; n++) if (!used.Contains(n)) return n;
            return null;
        }

        // G502 LIGHTSPEED (WPID 407F) button names in profile order; other devices use "Button N".
        public static string[] ButtonName(string wpid, int index)
        {
            string[,] g502 =
            {
                { "左鍵", "Left button" }, { "右鍵", "Right button" }, { "滾輪按下", "Wheel click" },
                { "上一頁（G4）", "Back (G4)" }, { "下一頁（G5）", "Forward (G5)" }, { "狙擊（G6）", "Sniper (G6)" },
                { "DPI 下（G7）", "DPI down (G7)" }, { "DPI 上（G8）", "DPI up (G8)" }, { "G9 鍵", "G9 button" },
                { "滾輪右撥", "Wheel tilt right" }, { "滾輪左撥", "Wheel tilt left" }
            };
            if (wpid == "407F" && index >= 0 && index < g502.GetLength(0)) return new[] { g502[index, 0], g502[index, 1] };
            return new[] { "第 " + (index + 1) + " 鍵", "Button " + (index + 1) };
        }

        // New stages and default stage; unused stages stay 0 and used stages cannot become 0.
        public static byte[] PatchedStages(byte[] sector, IList<int> stages, int? defaultIndex, int min, int max, int step = 50)
        {
            if (sector == null || sector.Length < 16 || !CrcValid(sector) || stages.Count != 5 || min <= 0 || max < min) return null;
            byte[] result = (byte[])sector.Clone();
            for (int i = 0; i < 5; i++)
            {
                int value = stages[i];
                if (value == 0)
                {
                    if (sector[3 + i * 2] != 0 || sector[4 + i * 2] != 0) return null;
                    continue;
                }
                if (value < min || value > max || value % step != 0) return null;
                result[3 + i * 2] = (byte)(value & 0xFF);
                result[4 + i * 2] = (byte)(value >> 8 & 0xFF);
            }
            if (defaultIndex.HasValue)
            {
                int d = defaultIndex.Value;
                if (d < 0 || d >= 5 || stages[d] == 0) return null;
                result[1] = (byte)d;
            }
            StoreCrc(result);
            return result;
        }

        public static byte[] PatchedReportRate(byte[] sector, int ms)
        {
            if (sector == null || sector.Length < 16 || !CrcValid(sector) || Array.IndexOf(ReportRates, ms) < 0) return null;
            byte[] result = (byte[])sector.Clone();
            result[0] = (byte)ms;
            StoreCrc(result);
            return result;
        }

        public static int? DefaultStage(byte[] head)
        {
            if (head == null || head.Length < 2 || head[1] >= 5) return null;
            return head[1];
        }

        // Lighting at 0xD0: 11 bytes per zone (G502: 0 primary, 1 G logo).
        public static List<OtherLightEffect> ProfileLights(byte[] sector, int zones = 2)
        {
            var list = new List<OtherLightEffect>();
            for (int i = 0; i < zones; i++)
            {
                int start = LightOffset + i * LightSize;
                if (start + LightSize > sector.Length) continue;
                byte[] raw = new byte[LightSize];
                Array.Copy(sector, start, raw, 0, LightSize);
                list.Add(OtherLightEffect.Parse(raw));
            }
            return list;
        }

        // Models whose write flow was verified on hardware. Others are read-only; switching stage or profile still works.
        public static readonly string[] WriteVerifiedModels = { "407F" };

        // 0x8100 fn0: [memory model, profile format, macro format, ...]. libratbag accepts only 1, 1-5 and 1.
        public static bool OnboardFormatSupported(byte[] info)
        {
            return info != null && info.Length >= 7 && info[4] == 1 && info[5] >= 1 && info[5] <= 5 && info[6] == 1;
        }

        // Every directory entry, disabled ones too, up to 0xFFFF. Device profile numbers index this list.
        public static List<int> DirectoryEntries(byte[] data)
        {
            var list = new List<int>();
            for (int i = 0; i + 3 < data.Length; i += 4)
            {
                int sector = data[i] << 8 | data[i + 1];
                if (sector == 0xFFFF) break;
                list.Add(sector);
            }
            return list;
        }

        // fn4 reply -> active sector. libratbag: byte 5 is the number, counted from 1 on INDEX_OFFSET models and
        // from 0 on the others. Unlisted models are not guessed. A non-zero high byte (ROM) is used as is only
        // on models counting from 1 (G502 reports 0x0101).
        public static int? ResolvedActiveProfile(byte[] reply, string wpid, IList<int> slots)
        {
            OtherDeviceModel model;
            if (reply == null || reply.Length < 6 || !OtherDeviceCatalog.Models.TryGetValue(wpid, out model)) return null;
            int raw = reply[4] << 8 | reply[5];
            if (reply[4] != 0)
            {
                if (model.OneBasedProfile) return raw;
                return null;
            }
            int number = reply[5];
            int index = model.OneBasedProfile && number > 0 ? number - 1 : number;
            if (index < slots.Count) return slots[index];
            if (model.OneBasedProfile && slots.Count == 0 && number > 0) return raw;
            return null;
        }

        // fn3: libratbag always sends [0, position + 1]; a sector outside the directory (ROM) is sent as is.
        public static byte[] SetProfileParameters(int sector, IList<int> slots)
        {
            int index = slots.IndexOf(sector);
            if (index >= 0 && index < 255) return new byte[] { 0, (byte)(index + 1) };
            return new byte[] { (byte)(sector >> 8 & 0xFF), (byte)(sector & 0xFF) };
        }

        public static int[] LightSlots(string wpid)
        {
            OtherDeviceModel model;
            if (OtherDeviceCatalog.Models.TryGetValue(wpid, out model) && model.SecondLightSlot) return new[] { 1 };
            return new[] { 0, 1 };
        }

        public static List<OtherLightEffect> ProfileLights(byte[] sector, string wpid)
        {
            List<OtherLightEffect> all = ProfileLights(sector, 2);
            var list = new List<OtherLightEffect>();
            foreach (int slot in LightSlots(wpid)) if (slot < all.Count) list.Add(all[slot]);
            return list;
        }

        // Fills in a name or type the receiver did not give.
        public static void ApplyCatalog(OtherDeviceSnapshot d)
        {
            OtherDeviceModel model;
            if (!OtherDeviceCatalog.Models.TryGetValue(d.Wpid, out model)) return;
            if (d.Name.Length == 0) d.Name = model.Name;
            if (d.Kind < 0) d.Kind = model.Kind;
        }

        public static byte[] PatchedLights(byte[] sector, Dictionary<int, OtherLightEffect> lights)
        {
            if (sector == null || sector.Length < LightOffset + 2 * LightSize + 2 || !CrcValid(sector)) return null;
            byte[] result = (byte[])sector.Clone();
            foreach (KeyValuePair<int, OtherLightEffect> item in lights)
            {
                byte[] raw = item.Value.Bytes;
                if (item.Key < 0 || item.Key >= 2 || raw.Length != LightSize) return null;
                Array.Copy(raw, 0, result, LightOffset + item.Key * LightSize, LightSize);
            }
            StoreCrc(result);
            return result;
        }
    }

    // Re-checks the mouse before every HID++ exchange: wait while it moves, and give up on this
    // batch if it keeps moving, so a long read never runs under an active cursor.
    internal sealed class OtherReadGate
    {
        public bool Interrupted { get; private set; }

        public void Begin() { Interrupted = false; }

        public bool Allow(Func<bool> idle, Action<double> wait, double maxWait = 3, double step = 0.2)
        {
            if (Interrupted) return false;
            double waited = 0;
            while (!idle())
            {
                if (waited >= maxWait)
                {
                    Interrupted = true;
                    return false;
                }
                wait(step);
                waited += step;
            }
            return true;
        }
    }

    // Receiver scans waiting for the mouse to stop: one per receiver; a full read request wins.
    internal sealed class OtherScanQueue
    {
        private sealed class Entry { public bool Full; public ulong Token; }
        private readonly Dictionary<string, Entry> queued = new Dictionary<string, Entry>();
        private ulong nextToken;

        public bool Request(string key, bool full)
        {
            Entry existing;
            if (queued.TryGetValue(key, out existing))
            {
                existing.Full = existing.Full || full;
                return false;
            }
            nextToken++;
            queued[key] = new Entry { Full = full, Token = nextToken };
            return true;
        }

        public ulong? Token(string key)
        {
            Entry entry;
            if (!queued.TryGetValue(key, out entry)) return null;
            return entry.Token;
        }

        public bool? Take(string key)
        {
            Entry entry;
            if (!queued.TryGetValue(key, out entry)) return null;
            queued.Remove(key);
            return entry.Full;
        }

        public bool? Take(string key, ulong token)
        {
            Entry entry;
            if (!queued.TryGetValue(key, out entry) || entry.Token != token) return null;
            queued.Remove(key);
            return entry.Full;
        }
    }

    // Rotating software IDs. A timed-out ID is held back so its late reply cannot answer the next request.
    internal sealed class OtherRequestIds
    {
        public const double Hold = 5;
        private byte next = 1;
        private readonly Dictionary<byte, double> quarantine = new Dictionary<byte, double>();

        public byte Take(double now)
        {
            foreach (byte key in new List<byte>(quarantine.Keys))
                if (quarantine[key] <= now) quarantine.Remove(key);
            for (int i = 0; i < 15; i++)
            {
                byte id = next;
                next = next >= 0x0F ? (byte)1 : (byte)(next + 1);
                if (!quarantine.ContainsKey(id)) return id;
            }
            byte soonest = 1;
            double best = double.MaxValue;
            foreach (KeyValuePair<byte, double> item in quarantine)
            {
                if (item.Value < best)
                {
                    best = item.Value;
                    soonest = item.Key;
                }
            }
            quarantine.Remove(soonest);
            return soonest;
        }

        public void TimedOut(byte id, double now) { quarantine[id] = now + Hold; }
    }

    internal sealed class OtherWriteMode
    {
        public OtherWriteModeKind Kind;
        public Func<byte[], byte[]> Patch;
        public byte[] Image;
        public static OtherWriteMode Patched(Func<byte[], byte[]> patch) { return new OtherWriteMode { Kind = OtherWriteModeKind.Patch, Patch = patch }; }
        public static OtherWriteMode Restore(byte[] image) { return new OtherWriteMode { Kind = OtherWriteModeKind.Restore, Image = image }; }
        public static OtherWriteMode Repair(byte[] image) { return new OtherWriteMode { Kind = OtherWriteModeKind.Repair, Image = image }; }
    }

    internal sealed class OtherProfileIO
    {
        public Func<int?> ActiveSector;
        public Func<int, byte[]> Read;
        public Func<byte, byte[], bool> Command;   // 0x8100 function and parameters; true only when answered
        public Func<byte[], bool> Backup;
        public Func<bool> Reload;
    }

    internal sealed class OtherWriteOutcome
    {
        public OtherWriteResult Result;
        public byte[] Target;
        public byte[] RepairImage;   // for FailedNeedsRepair: the content to write back later
        public OtherWriteOutcome(OtherWriteResult result, byte[] target = null, byte[] repairImage = null)
        {
            Result = result;
            Target = target;
            RepairImage = repairImage;
        }
    }

    internal static class OtherProfileWriter
    {
        // Stops at the first unanswered step and never sends "finish" for unconfirmed data.
        public static bool Transfer(int sector, byte[] data, OtherProfileIO io)
        {
            foreach (OtherWriteStep step in OtherDevicesProtocol.WritePlan(sector, data))
                if (!io.Command(step.Function, step.Parameters)) return false;
            return true;
        }

        public static OtherWriteOutcome Run(int sector, OtherWriteMode mode, OtherProfileIO io)
        {
            bool isRepair = mode.Kind == OtherWriteModeKind.Repair;
            if (!isRepair && io.ActiveSector() != sector) return new OtherWriteOutcome(OtherWriteResult.ProfileChanged);
            byte[] current = io.Read(sector);
            bool currentValid = current != null && OtherDevicesProtocol.CrcValid(current);

            byte[] target;
            if (mode.Kind == OtherWriteModeKind.Patch)
            {
                if (!currentValid) return new OtherWriteOutcome(OtherWriteResult.ReadFailed);
                target = mode.Patch(current);
                if (target == null) return new OtherWriteOutcome(OtherWriteResult.UnexpectedContent);
            }
            else
            {
                byte[] image = mode.Image;
                if (image == null || !OtherDevicesProtocol.CrcValid(image) || (current != null && image.Length != current.Length))
                    return new OtherWriteOutcome(OtherWriteResult.NoBackup);
                target = image;
            }
            if (!OtherDevicesProtocol.LeftClickSafe(target, currentValid ? current : null))
                return new OtherWriteOutcome(OtherWriteResult.LeftClickLocked);
            if (OtherDevicesProtocol.SameBytes(target, current)) return new OtherWriteOutcome(OtherWriteResult.Unchanged, target);
            // Only valid content is worth a backup; damaged content must not block a restore.
            if (currentValid && !io.Backup(current)) return new OtherWriteOutcome(OtherWriteResult.BackupFailed);
            // Reading a whole sector takes a moment, so confirm the profile again before writing.
            if (!isRepair && io.ActiveSector() != sector) return new OtherWriteOutcome(OtherWriteResult.ProfileChanged);

            Transfer(sector, target, io);
            byte[] verify = io.Read(sector);
            if (OtherDevicesProtocol.SameBytes(verify, target))
                return new OtherWriteOutcome(io.Reload() ? OtherWriteResult.Written : OtherWriteResult.WrittenNotApplied, target);
            if (verify != null && OtherDevicesProtocol.SameBytes(verify, current)) return new OtherWriteOutcome(OtherWriteResult.FailedUnchanged);
            if (!currentValid) return new OtherWriteOutcome(OtherWriteResult.FailedNeedsRepair, null, target);
            Transfer(sector, current, io);
            if (OtherDevicesProtocol.SameBytes(io.Read(sector), current)) return new OtherWriteOutcome(OtherWriteResult.FailedRolledBack);
            return new OtherWriteOutcome(OtherWriteResult.FailedNeedsRepair, null, current);
        }
    }

    // Profile backups, one set per physical device (WPID + serial). Existing files are never
    // overwritten, and each new file is read back before it counts as saved.
    internal sealed class OtherProfileBackups
    {
        private readonly string folder;

        public OtherProfileBackups(string folder) { this.folder = folder; }

        public static string DeviceKey(string wpid, string unitId) { return unitId.Length == 0 ? wpid : wpid + "-" + unitId; }

        public string OriginalPath(string device, int sector) { return Path.Combine(folder, device + "-" + sector.ToString("X4") + "-original.bin"); }
        public string RepairPath(string device, int sector) { return Path.Combine(folder, device + "-" + sector.ToString("X4") + "-repair.bin"); }

        public byte[] LoadImage(string path, int size)
        {
            try
            {
                byte[] raw = File.ReadAllBytes(path);
                return raw.Length == size && OtherDevicesProtocol.CrcValid(raw) ? raw : null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private bool WriteNew(byte[] data, string path)
        {
            string temp = Path.Combine(folder, ".tmp-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(temp, data);
                File.Move(temp, path);   // fails when the destination exists, so nothing is overwritten
                return OtherDevicesProtocol.SameBytes(File.ReadAllBytes(path), data);
            }
            catch (IOException) { TryDelete(temp); return false; }
            catch (UnauthorizedAccessException) { TryDelete(temp); return false; }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private bool EnsureFolder()
        {
            try
            {
                System.IO.Directory.CreateDirectory(folder);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        // Keeps the first "original" of this profile, plus a timestamped copy before every write.
        public bool Save(byte[] data, string device, int sector, DateTime now)
        {
            if (!EnsureFolder()) return false;
            string original = OriginalPath(device, sector);
            if (!File.Exists(original) && !WriteNew(data, original)) return false;
            string stem = device + "-" + sector.ToString("X4") + "-" + now.ToString("yyyyMMdd-HHmmss-fff", System.Globalization.CultureInfo.InvariantCulture);
            for (int attempt = 0; attempt < 100; attempt++)
            {
                string path = Path.Combine(folder, stem + (attempt == 0 ? "" : "-" + attempt) + ".bin");
                if (File.Exists(path)) continue;
                return WriteNew(data, path);
            }
            return false;
        }

        // An existing repair image is kept: it is the last content known to be good.
        public bool SaveRepair(byte[] data, string device, int sector)
        {
            if (!EnsureFolder()) return false;
            string path = RepairPath(device, sector);
            if (File.Exists(path)) return true;
            return WriteNew(data, path);
        }

        public void ClearRepair(string device, int sector) { TryDelete(RepairPath(device, sector)); }

        private string[] FileNames()
        {
            try
            {
                string[] paths = System.IO.Directory.GetFiles(folder);
                for (int i = 0; i < paths.Length; i++) paths[i] = Path.GetFileName(paths[i]);
                return paths;
            }
            catch (IOException) { return new string[0]; }
            catch (UnauthorizedAccessException) { return new string[0]; }
        }

        private static bool ParseHex(string text, out int value)
        {
            return int.TryParse(text, System.Globalization.NumberStyles.AllowHexSpecifier, System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        public int? PendingRepair(string device)
        {
            string prefix = device + "-", suffix = "-repair.bin";
            int? lowest = null;
            foreach (string name in FileNames())
            {
                if (name.Length <= prefix.Length + suffix.Length || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                int sector;
                if (!ParseHex(name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length), out sector)) continue;
                if (!lowest.HasValue || sector < lowest.Value) lowest = sector;
            }
            return lowest;
        }

        // Older backups were named by WPID only: the first device that reports a serial adopts them.
        public void AdoptLegacy(string wpid, string unitId)
        {
            if (unitId.Length == 0) return;
            string prefix = wpid + "-", suffix = "-original.bin";
            foreach (string name in FileNames())
            {
                if (name.Length != prefix.Length + 4 + suffix.Length || !name.StartsWith(prefix, StringComparison.Ordinal) || !name.EndsWith(suffix, StringComparison.Ordinal)) continue;
                int sector;
                if (!ParseHex(name.Substring(prefix.Length, 4), out sector)) continue;
                string target = OriginalPath(DeviceKey(wpid, unitId), sector);
                if (File.Exists(target)) continue;
                try { File.Move(Path.Combine(folder, name), target); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
    // BEGIN GENERATED device catalog
    // Generated by tools/other-devices-test/catalog.py; do not edit. Source: libratbag 1aec93b2f810 (MIT), see THIRD_PARTY_NOTICES.md.
    internal sealed class OtherDeviceModel
    {
        public string Name;
        public int Kind;
        public bool OneBasedProfile;   // INDEX_OFFSET: the active profile number counts from 1
        public bool SecondLightSlot;   // G502X_PLUS: lighting is in the second slot
    }

    internal static class OtherDeviceCatalog
    {
        public const string Source = "libratbag 1aec93b2f810 (MIT)";
        private static OtherDeviceModel M(string name, int kind, bool oneBased, bool second)
        {
            return new OtherDeviceModel { Name = name, Kind = kind, OneBasedProfile = oneBased, SecondLightSlot = second };
        }

        public static readonly Dictionary<string, OtherDeviceModel> Models = new Dictionary<string, OtherDeviceModel>
        {
            { "400A", M("Logitech M325", 3, false, false) },
            { "4011", M("Logitech Wireless Touchpad", 3, false, false) },
            { "402C", M("Logitech G602", 3, false, false) },
            { "4041", M("Logitech MX Master", 3, false, false) },
            { "404A", M("Logitech MX Anywhere 2", 3, false, false) },
            { "4052", M("Logitech M545", 3, false, false) },
            { "4053", M("Logitech G900", 3, false, false) },
            { "405D", M("Logitech G403 Wireless", 3, false, false) },
            { "405E", M("Logitech M720", 3, false, false) },
            { "405F", M("Logitech G Powerplay", -1, false, false) },
            { "4060", M("Logitech MX Master", 3, false, false) },
            { "4063", M("Logitech MX Anywhere 2", 3, false, false) },
            { "4067", M("Logitech G903", 3, false, false) },
            { "4069", M("Logitech MX Master 2S", 3, false, false) },
            { "406A", M("Logitech MX Anywhere 2S", 3, false, false) },
            { "406B", M("Logitech M585/M590", 3, false, false) },
            { "406C", M("Logitech G603", 3, false, false) },
            { "406D", M("Logitech Marathon M705", 3, false, false) },
            { "406F", M("Logitech MX Ergo", 3, false, false) },
            { "4070", M("Logitech G703", 3, false, false) },
            { "4071", M("Logitech MX Master", 3, false, false) },
            { "4072", M("Logitech MX Anywhere 2", 3, false, false) },
            { "4074", M("Logitech Gaming Mouse G305", 3, false, false) },
            { "4079", M("Logitech G Pro Wireless", 3, false, false) },
            { "407B", M("Logitech MX Vertical", 3, false, false) },
            { "407F", M("Logitech G502 Hero Wireless", 3, true, false) },
            { "4082", M("Logitech MX Master 3", 3, false, false) },
            { "4085", M("Logitech G604", 3, true, false) },
            { "4086", M("Logitech G703 Hero", 3, false, false) },
            { "4087", M("Logitech G903 Hero", 3, false, false) },
            { "4090", M("Logitech MX Anywhere 3", 3, false, false) },
            { "4093", M("Logitech G Pro X Wireless Superlight", 3, false, false) },
            { "4099", M("Logitech G502 X PLUS", 3, true, true) },
            { "409D", M("Logitech G705", 3, false, true) },
            { "409F", M("Logitech G502 X Wireless", 3, true, false) },
        };
    }
    // END GENERATED device catalog

    // ---- Other devices protocol end ----

    // ---- Other devices I/O ----
    // A receiver's HID++ collections; tools/other-devices-test replaces this with a fake receiver.
    internal interface IOtherPort : IDisposable
    {
        string Key { get; }                 // PID + location, e.g. C539@8&2EE1690F&0
        event Action<byte[]> Received;      // raised on a reader thread
        event Action Closed;                // the receiver was unplugged
        void Start();
        bool Write(byte[] packet);
    }

    // Devices on Lightspeed receivers (G502 LIGHTSPEED), ported from MX Master Mac (OtherDevices.swift).
    // Separate handles, thread and queue from the MX Master 4 and keyboard code, so those connections are unaffected.
    // Every exchange uses the wireless channel, so nothing is sent while the mouse moves, except what the
    // user asked for and what the receiver answers itself. Connected devices normally read only the battery.
    internal sealed class OtherDevicesManager : IDisposable
    {
        private const double BatteryInterval = 300;

        private sealed class WriteRequest
        {
            public Func<byte[], OtherDeviceSnapshot, byte[]> Patch;
            public bool RestoreOriginal;
            public bool Repair;
        }

        private readonly Func<ICollection<string>, List<IOtherPort>> discover;
        private readonly Func<double> secondsSinceMouse;
        private readonly Action<Action> postToUi;
        private readonly OtherProfileBackups backups;
        private readonly System.Collections.Concurrent.BlockingCollection<Action> queue =
            new System.Collections.Concurrent.BlockingCollection<Action>();
        private readonly object portLock = new object();
        private readonly List<IOtherPort> ports = new List<IOtherPort>();
        private Thread thread;
        private System.Threading.Timer timer;
        private int tickQueued;
        private volatile bool stopped;
        private bool disposed;

        // Everything below is used only on the worker thread.
        private readonly Dictionary<string, OtherDeviceSnapshot> devices = new Dictionary<string, OtherDeviceSnapshot>();
        private readonly Dictionary<string, Dictionary<int, byte>> features = new Dictionary<string, Dictionary<int, byte>>();
        private readonly Dictionary<string, List<int>> samples = new Dictionary<string, List<int>>();
        private readonly Dictionary<string, DateTime> lastBattery = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastScan = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastPing = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastLightTry = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, DateTime> lastFullRead = new Dictionary<string, DateTime>();
        private readonly HashSet<string> reconnectPending = new HashSet<string>();
        private readonly OtherRequestIds requestIds = new OtherRequestIds();
        private readonly OtherReadGate gate = new OtherReadGate();
        private readonly OtherScanQueue scanQueue = new OtherScanQueue();
        private double idleNeeded = OtherDevicesProtocol.IdleSeconds;
        private int timeouts;
        private int sent;

        // The request waiting for its reply; shared with the reader threads.
        private readonly object pendingLock = new object();
        private IOtherPort pendingPort;
        private byte[] pendingKey;
        private byte[] pendingReply;
        private bool pendingError;
        private ManualResetEventSlim pendingSignal;

        public event Action<string> Log;
        public event Action<List<OtherDeviceSnapshot>> Changed;
        // (wpid, button slot, pressed) from HID++ 0x8110, on the UI thread.
        public event Action<string, int, bool> ButtonChanged;
        public event Action ButtonsLost;

        // 0x8110 state. The indexes and masks are also read on the receiver's reader thread.
        private readonly object spyLock = new object();
        private readonly Dictionary<string, byte> spyIndexes = new Dictionary<string, byte>();
        private readonly Dictionary<string, int> spyMasks = new Dictionary<string, int>();
        private readonly Dictionary<string, string> spyWpids = new Dictionary<string, string>();
        private readonly Dictionary<string, DateTime> lastSpyArm = new Dictionary<string, DateTime>();
        private bool spyWanted;

        public OtherDevicesManager(Func<ICollection<string>, List<IOtherPort>> discover, Func<double> secondsSinceMouse,
            Action<Action> postToUi, string backupFolder)
        {
            this.discover = discover;
            this.secondsSinceMouse = secondsSinceMouse;
            this.postToUi = postToUi;
            backups = new OtherProfileBackups(backupFolder);
        }

        public void Start()
        {
            thread = new Thread(Run) { IsBackground = true, Name = "MXMasterWin Other Devices" };
            thread.Start();
            Post(Discover);
            timer = new System.Threading.Timer(delegate
            {
                if (Interlocked.CompareExchange(ref tickQueued, 1, 0) == 0) Post(TimerTick);
            }, null, 5000, 5000);
        }

        private void Run()
        {
            foreach (Action action in queue.GetConsumingEnumerable())
            {
                try { action(); }
                catch (Exception ex) { WriteLog("worker error: " + ex.GetType().Name + ": " + ex.Message); }
            }
        }

        private void Post(Action action)
        {
            try { queue.Add(action); }
            catch (InvalidOperationException) { }
        }

        private void After(int milliseconds, Action action)
        {
            System.Threading.Timer once = null;
            once = new System.Threading.Timer(delegate
            {
                if (once != null) once.Dispose();
                Post(action);
            }, null, milliseconds, Timeout.Infinite);
        }

        private List<IOtherPort> PortList()
        {
            lock (portLock) return new List<IOtherPort>(ports);
        }

        private IOtherPort PortFor(string key)
        {
            foreach (IOtherPort port in PortList()) if (port.Key == key) return port;
            return null;
        }

        private bool HasPort(IOtherPort port)
        {
            lock (portLock) return ports.Contains(port);
        }

        // The settings page opened or the computer woke up: scan again. Full is the Refresh button.
        public void Refresh(bool full = false)
        {
            Post(delegate
            {
                if (stopped) return;
                foreach (IOtherPort port in PortList())
                    if (full || Age(lastScan, port.Key) > 3) RequestScan(port, full, 30, 3);
            });
        }

        private static double Age(Dictionary<string, DateTime> table, string key)
        {
            DateTime when;
            if (!table.TryGetValue(key, out when)) return double.MaxValue;
            return (DateTime.UtcNow - when).TotalSeconds;
        }

        // Scans once the mouse stops. One scan per receiver is queued; a scan cut short by the mouse is retried.
        private void RequestScan(IOtherPort port, bool full, double giveUpAfter, int retry)
        {
            string key = port.Key;
            if (!scanQueue.Request(key, full)) return;
            ulong? queuedToken = scanQueue.Token(key);
            if (!queuedToken.HasValue) return;
            ulong token = queuedToken.Value;
            WhenIdle(giveUpAfter, true, delegate { scanQueue.Take(key, token); }, delegate
            {
                bool? wantFull = scanQueue.Take(key, token);
                if (!wantFull.HasValue || !HasPort(port)) return;
                if (!Scan(port, wantFull.Value) && retry > 0) RequestScan(port, wantFull.Value, 60, retry - 1);
            });
        }

        private void WhenIdle(double giveUpAfter, bool quick, Action onGiveUp, Action action)
        {
            DateTime limit = DateTime.UtcNow.AddSeconds(giveUpAfter);
            double threshold = quick ? OtherDevicesProtocol.QuickIdleSeconds : OtherDevicesProtocol.IdleSeconds;
            Action attempt = null;
            attempt = delegate
            {
                if (stopped) return;
                if (MouseIdle(threshold))
                {
                    idleNeeded = threshold;
                    action();
                    idleNeeded = OtherDevicesProtocol.IdleSeconds;
                }
                else if (DateTime.UtcNow < limit) After(quick ? 250 : 1000, attempt);
                else if (onGiveUp != null) onGiveUp();
            };
            attempt();
        }

        private bool MouseIdle(double threshold)
        {
            return OtherDevicesProtocol.MouseIdle(new[] { secondsSinceMouse() }, threshold);
        }

        // Receivers appearing and disappearing
        private void Discover()
        {
            if (stopped) return;
            var open = new List<string>();
            foreach (IOtherPort port in PortList()) open.Add(port.Key);
            List<IOtherPort> found;
            try { found = discover(open); }
            catch (Exception ex)
            {
                WriteLog("receiver search failed: " + ex.Message);
                return;
            }
            foreach (IOtherPort port in found) AddPort(port);
        }

        private void AddPort(IOtherPort port)
        {
            lock (portLock) ports.Add(port);
            port.Received += delegate (byte[] bytes) { OnReceived(port, bytes); };
            port.Closed += delegate { Post(delegate { RemovePort(port); }); };
            port.Start();
            string key = port.Key;
            WriteLog("Lightspeed receiver " + key + " found");
            // Plugged into another USB port: its location differs, so the old "not connected" card goes.
            foreach (string id in OtherDevicesProtocol.StaleAfterReceiverFound(new List<OtherDeviceSnapshot>(devices.Values), key)) Forget(id);
            EnableConnectionNotices(port);
            ListPaired(port);
            RequestScan(port, false, 120, 3);
        }

        // Turns on the receiver's online/offline notices. The receiver answers itself, nothing goes over the air.
        private void EnableConnectionNotices(IOtherPort port)
        {
            byte[] read = OtherDevicesProtocol.NotificationFlagsRead;
            byte[] key = { read[1], read[2], read[3] };
            OtherRegisterOutcome first = Send(port, read, key, 1000, true);
            bool? on = first.Kind == OtherRegisterKind.Reply ? OtherDevicesProtocol.WirelessNoticesOn(first.Reply) : null;
            if (!on.HasValue)
            {
                WriteLog("receiver connection notices: couldn't read flags");
                return;
            }
            if (on.Value)
            {
                WriteLog("receiver connection notices already on");
                return;
            }
            byte[] write = OtherDevicesProtocol.EnableWirelessNotices(first.Reply);
            if (write == null) return;
            Send(port, write, new[] { write[1], write[2], write[3] }, 1000, true);
            OtherRegisterOutcome check = Send(port, read, key, 1000, true);
            bool verified = check.Kind == OtherRegisterKind.Reply && OtherDevicesProtocol.WirelessNoticesOn(check.Reply) == true;
            WriteLog("receiver connection notices enabled verified=" + verified);
        }

        // Lists the paired devices right away (the receiver answers, no radio), shown as "reading".
        private void ListPaired(IOtherPort port)
        {
            string key = port.Key;
            for (int slot = 1; slot <= 6; slot++)
            {
                string id = key + "#" + slot;
                OtherSlotState state = OtherDevicesProtocol.SlotState(Register(port, OtherDevicesProtocol.PairingRequest(slot)), slot);
                if (state.Kind != OtherSlotKind.Paired) continue;
                OtherDeviceSnapshot known;
                if (devices.TryGetValue(id, out known) && known.Wpid == state.Wpid)
                {
                    if (known.Link == OtherDeviceLink.ReceiverGone)
                    {
                        OtherDeviceSnapshot back = known.Copy();
                        back.Link = OtherDeviceLink.Reading;
                        devices[id] = back;
                    }
                    continue;
                }
                var card = new OtherDeviceSnapshot
                {
                    Receiver = key,
                    Slot = slot,
                    Wpid = state.Wpid,
                    Kind = OtherDevicesProtocol.KindFromReceiver(state.DeviceKind),
                    Link = OtherDeviceLink.Reading
                };
                OtherRegisterOutcome named = Register(port, OtherDevicesProtocol.NameRequest(slot));
                if (named.Kind == OtherRegisterKind.Reply) card.Name = OtherDevicesProtocol.PairedName(named.Reply, slot);
                OtherDevicesProtocol.ApplyCatalog(card);
                Forget(id);
                devices[id] = card;
                WriteLog("other device slot " + slot + " listed: " + card.Name + " wpid=" + state.Wpid);
            }
            Publish();
        }

        private void RemovePort(IOtherPort gone)
        {
            lock (portLock)
            {
                if (!ports.Remove(gone)) return;
            }
            string key = gone.Key;
            scanQueue.Take(key);
            reconnectPending.Remove(key);
            // Keep the cards as "receiver not connected"; plugging it back in reads everything again.
            foreach (KeyValuePair<string, OtherDeviceSnapshot> item in new List<KeyValuePair<string, OtherDeviceSnapshot>>(devices))
            {
                if (item.Value.Receiver != key) continue;
                OtherDeviceSnapshot d = item.Value.Copy();
                d.Link = OtherDeviceLink.ReceiverGone;
                devices[item.Key] = d;
                ReleaseHeld(item.Key);
                lastSpyArm.Remove(item.Key);
                features.Remove(item.Key);
                samples.Remove(item.Key);
            }
            try { gone.Dispose(); }
            catch { }
            WriteLog("Lightspeed receiver " + key + " removed");
            Publish();
        }

        private void Forget(string id)
        {
            ForgetButtonReports(id);
            devices.Remove(id);
            features.Remove(id);
            samples.Remove(id);
            lastBattery.Remove(id);
            lastPing.Remove(id);
            lastLightTry.Remove(id);
            lastFullRead.Remove(id);
        }

        // Reconnect notices (0x41, 0x1D4B) merge into one read; a device read moments ago is not read again.
        private void DeviceCameBack(IOtherPort source, int slot)
        {
            string key = source.Key, id = key + "#" + slot;
            // The mouse may have forgotten the button reports while it was away.
            if (spyWanted)
                After(400, delegate
                {
                    if (stopped) return;
                    IOtherPort back = PortFor(key);
                    if (back != null) ArmButtonReports(back, id);
                });
            Func<bool> needed = delegate
            {
                OtherDeviceSnapshot d;
                bool online = devices.TryGetValue(id, out d) && d.Link == OtherDeviceLink.Online;
                DateTime when;
                DateTime? last = lastFullRead.TryGetValue(id, out when) ? when : (DateTime?)null;
                return OtherDevicesProtocol.ReconnectReadNeeded(online, last, DateTime.UtcNow);
            };
            if (!needed() || !reconnectPending.Add(key)) return;
            After(500, delegate
            {
                reconnectPending.Remove(key);
                if (stopped || !needed()) return;
                IOtherPort port = PortFor(key);
                if (port != null) RequestScan(port, true, 60, 3);
            });
        }

        // Button reports (HID++ 0x8110). They only tell us what is pressed: the mouse's mode, profile and
        // button mapping stay as they are. Sent as user actions so a moving mouse doesn't hold them up.
        public void SetButtonReports(bool wanted)
        {
            Post(delegate
            {
                if (stopped || spyWanted == wanted) return;
                spyWanted = wanted;
                if (!wanted)
                {
                    StopButtonReports();
                    return;
                }
                foreach (KeyValuePair<string, OtherDeviceSnapshot> item in new List<KeyValuePair<string, OtherDeviceSnapshot>>(devices))
                {
                    IOtherPort port = PortFor(item.Value.Receiver);
                    if (port != null) ArmButtonReports(port, item.Key);
                }
            });
        }

        private void ArmButtonReports(IOtherPort port, string id)
        {
            OtherDeviceSnapshot d;
            if (!spyWanted || stopped || !devices.TryGetValue(id, out d) || d.Link != OtherDeviceLink.Online || d.Wpid != "407F") return;
            byte? index = FindFeature(port, id, d.Slot, 0x8110);
            if (!index.HasValue) return;
            bool first;
            lock (spyLock)
            {
                first = !spyIndexes.ContainsKey(id);
                spyIndexes[id] = index.Value;   // known before the start, so the first press is not lost
                spyWpids[id] = d.Wpid;
                if (first) spyMasks[id] = 0;
            }
            bool ok = Hidpp(port, d.Slot, index.Value, 1, new byte[0], 1000, true) != null;
            if (ok) lastSpyArm[id] = DateTime.UtcNow;
            if (first || !ok) WriteLog("other device " + d.Name + " button reports on ok=" + ok);
        }

        private void StopButtonReports()
        {
            List<KeyValuePair<string, byte>> armed;
            lock (spyLock) armed = new List<KeyValuePair<string, byte>>(spyIndexes);
            foreach (KeyValuePair<string, byte> item in armed)
            {
                OtherDeviceSnapshot d;
                IOtherPort port = devices.TryGetValue(item.Key, out d) ? PortFor(d.Receiver) : null;
                if (port != null && d.Link == OtherDeviceLink.Online)
                {
                    bool ok = Hidpp(port, d.Slot, item.Value, 2, new byte[0], 800, true) != null;
                    WriteLog("other device " + d.Name + " button reports off ok=" + ok);
                }
                ForgetButtonReports(item.Key);
            }
        }

        private void ReleaseHeld(string id)
        {
            bool held;
            lock (spyLock)
            {
                int mask;
                held = spyMasks.TryGetValue(id, out mask) && mask != 0;
                if (spyMasks.ContainsKey(id)) spyMasks[id] = 0;
            }
            Action handler = ButtonsLost;
            if (held && handler != null) postToUi(delegate { handler(); });
        }

        private void ForgetButtonReports(string id)
        {
            ReleaseHeld(id);
            lock (spyLock)
            {
                spyIndexes.Remove(id);
                spyMasks.Remove(id);
                spyWpids.Remove(id);
            }
            lastSpyArm.Remove(id);
        }

        // Runs on the reader thread, so a press is not held up by a slow wireless read on the worker.
        private bool HandleButtonReport(IOtherPort source, byte[] bytes)
        {
            if (bytes[0] != 0x11 || bytes[3] != 0) return false;
            string id = source.Key + "#" + bytes[1], wpid;
            int mask = bytes[4] << 8 | bytes[5], old;
            lock (spyLock)
            {
                byte index;
                if (!spyIndexes.TryGetValue(id, out index) || index != bytes[2]) return false;
                spyMasks.TryGetValue(id, out old);
                spyMasks[id] = mask;
                spyWpids.TryGetValue(id, out wpid);
            }
            Action<string, int, bool> handler = ButtonChanged;
            if (handler == null) return true;
            int changed = old ^ mask;
            for (int bit = 0; bit < 16; bit++)
            {
                if ((changed >> bit & 1) == 0) continue;
                int button = bit;
                bool down = (mask >> bit & 1) != 0;
                postToUi(delegate { handler(wpid, button, down); });
            }
            return true;
        }

        // Timed reads
        private void TimerTick()
        {
            Interlocked.Exchange(ref tickQueued, 0);
            if (stopped) return;
            Discover();
            bool waiting = false;
            foreach (OtherDeviceSnapshot d in devices.Values)
                if (d.Link == OtherDeviceLink.NoAnswer || d.Link == OtherDeviceLink.Reading) waiting = true;
            WhenIdle(waiting ? 1.5 : 4, waiting, null, Tick);
        }

        private void Tick()
        {
            if (stopped) return;
            // A mouse that slept without any notice may have dropped its button reports.
            if (spyWanted)
                foreach (KeyValuePair<string, OtherDeviceSnapshot> armed in new List<KeyValuePair<string, OtherDeviceSnapshot>>(devices))
                {
                    if (armed.Value.Link != OtherDeviceLink.Online || Age(lastSpyArm, armed.Key) < 60) continue;
                    IOtherPort armPort = PortFor(armed.Value.Receiver);
                    if (armPort != null) ArmButtonReports(armPort, armed.Key);
                }
            // Checks the pairing list every five minutes, counted from the last finished scan.
            var due = new List<IOtherPort>();
            foreach (IOtherPort port in PortList())
            {
                DateTime when;
                DateTime? last = lastScan.TryGetValue(port.Key, out when) ? when : (DateTime?)null;
                if (OtherDevicesProtocol.PairingCheckDue(last, DateTime.UtcNow)) due.Add(port);
            }
            if (due.Count > 0)
            {
                foreach (IOtherPort port in due)
                {
                    bool? queuedFull = scanQueue.Take(port.Key);
                    if (!Scan(port, queuedFull ?? false)) break;
                }
                return;
            }
            gate.Begin();
            bool changed = false;
            foreach (KeyValuePair<string, OtherDeviceSnapshot> item in new List<KeyValuePair<string, OtherDeviceSnapshot>>(devices))
            {
                if (gate.Interrupted) break;
                string id = item.Key;
                OtherDeviceSnapshot snapshot = item.Value;
                IOtherPort port = PortFor(snapshot.Receiver);
                if (port == null) continue;
                if (snapshot.Link == OtherDeviceLink.NoAnswer || snapshot.Link == OtherDeviceLink.Reading)
                {
                    if (scanQueue.Token(port.Key).HasValue || reconnectPending.Contains(port.Key)) continue;
                    DateTime when;
                    DateTime? pinged = lastPing.TryGetValue(id, out when) ? when : (DateTime?)null;
                    if (!OtherDevicesProtocol.WakeCheckDue(pinged, DateTime.UtcNow, secondsSinceMouse())) continue;
                    lastPing[id] = DateTime.UtcNow;
                    if (Ping(port, snapshot.Slot))
                    {
                        ReadDevice(port, snapshot.Slot, snapshot);
                        changed = true;
                    }
                    else if (snapshot.Link == OtherDeviceLink.Reading && !gate.Interrupted)
                    {
                        MarkNoAnswer(id);
                        changed = true;
                    }
                }
                else if (Age(lastBattery, id) >= BatteryInterval && Age(lastLightTry, id) >= 60)
                {
                    lastLightTry[id] = DateTime.UtcNow;
                    ReadLight(port, id);
                    changed = true;
                }
            }
            if (changed) Publish();
        }

        // A connected device: battery plus DPI and report rate. No answer at all marks it as away.
        private void ReadLight(IOtherPort port, string id)
        {
            OtherDeviceSnapshot snapshot;
            if (!devices.TryGetValue(id, out snapshot)) return;
            int before = sent;
            if (ReadBattery(port, id)) ReadSpeed(port, id);
            else if (!gate.Interrupted && !Ping(port, snapshot.Slot) && !gate.Interrupted) MarkNoAnswer(id);
            if (gate.Interrupted) return;
            OtherDeviceSnapshot d;
            if (!devices.TryGetValue(id, out d)) d = snapshot;
            WriteLog("other device " + d.Name + " battery read (" + (sent - before) + " requests): " + d.BatteryPercent + "% " + d.Millivolts + "mV dpi=" + d.Dpi);
        }

        // Returns false when the mouse moved and the scan stopped early.
        private bool Scan(IOtherPort port, bool full)
        {
            string key = port.Key;
            gate.Begin();
            for (int slot = 1; slot <= 6; slot++)
            {
                string id = key + "#" + slot;
                OtherRegisterOutcome outcome = Register(port, OtherDevicesProtocol.PairingRequest(slot));
                if (gate.Interrupted) break;
                OtherSlotState state = OtherDevicesProtocol.SlotState(outcome, slot);
                if (state.Kind == OtherSlotKind.Empty)
                {
                    if (devices.ContainsKey(id)) Forget(id);
                    continue;
                }
                if (state.Kind == OtherSlotKind.Unknown) continue;   // keep the card: we don't know

                OtherDeviceSnapshot existing;
                OtherDeviceSnapshot card = devices.TryGetValue(id, out existing) ? existing.Copy() : new OtherDeviceSnapshot();
                card.Receiver = key;
                card.Slot = slot;
                if (card.Wpid != state.Wpid)
                {
                    Forget(id);
                    card = new OtherDeviceSnapshot { Receiver = key, Slot = slot };
                }
                card.Wpid = state.Wpid;
                if (card.Kind < 0 || card.Name.Length == 0) card.Kind = OtherDevicesProtocol.KindFromReceiver(state.DeviceKind);
                if (card.Name.Length == 0)
                {
                    OtherRegisterOutcome named = Register(port, OtherDevicesProtocol.NameRequest(slot));
                    if (named.Kind == OtherRegisterKind.Reply) card.Name = OtherDevicesProtocol.PairedName(named.Reply, slot);
                }
                OtherDevicesProtocol.ApplyCatalog(card);
                if (gate.Interrupted) break;
                bool known = devices.TryGetValue(id, out existing);
                Dictionary<int, byte> table;
                bool hasFeatures = features.TryGetValue(id, out table) && table.Count > 0;
                OtherReadPlan plan = OtherDevicesProtocol.ReadPlan(known, true, known && existing.Link == OtherDeviceLink.Online, hasFeatures, full);
                if (plan == OtherReadPlan.Battery)
                {
                    devices[id] = card;
                    if (Age(lastBattery, id) > 30) ReadLight(port, id);
                }
                else if (Ping(port, slot)) ReadDevice(port, slot, card);
                else if (!gate.Interrupted)
                {
                    card.Link = OtherDeviceLink.NoAnswer;
                    devices[id] = card;
                    WriteLog("other device slot " + slot + " (" + card.Name + ") paired but not answering");
                }
                if (gate.Interrupted) break;
            }
            bool done = !gate.Interrupted;
            if (done) lastScan[key] = DateTime.UtcNow;
            else WriteLog("scan of " + key + " interrupted: mouse moving (" + sent + " requests so far)");
            Publish();
            return done;
        }

        private void MarkNoAnswer(string id)
        {
            OtherDeviceSnapshot current;
            if (!devices.TryGetValue(id, out current) || current.Link == OtherDeviceLink.NoAnswer) return;
            OtherDeviceSnapshot snapshot = current.Copy();
            snapshot.Link = OtherDeviceLink.NoAnswer;
            devices[id] = snapshot;
            ReleaseHeld(id);
            lastSpyArm.Remove(id);
            // Look features up again next time, but keep 0x1D4B so the reconnect event is recognized.
            Dictionary<int, byte> table;
            features.TryGetValue(id, out table);
            features[id] = OtherDevicesProtocol.FeaturesAfterNoAnswer(table);
            WriteLog("other device " + snapshot.Name + " not answering");
        }

        private static string DecodeName(List<byte> bytes)
        {
            try { return OtherDevicesProtocol.TrimControl(new UTF8Encoding(false, true).GetString(bytes.ToArray())); }
            catch (ArgumentException) { return ""; }
        }

        private void ReadDevice(IOtherPort port, int slot, OtherDeviceSnapshot paired)
        {
            if (!HasPort(port)) return;
            int before = sent, timeoutsBefore = timeouts;
            string id = paired.Id;
            if (!features.ContainsKey(id)) features[id] = new Dictionary<int, byte>();
            OtherDeviceSnapshot s = paired.Copy();
            s.Link = OtherDeviceLink.Online;
            byte[] count = Call(port, id, slot, 0x0005, 0);
            if (count != null)
            {
                var name = new List<byte>();
                int length = Math.Min((int)count[4], 64);
                while (name.Count < length)
                {
                    byte[] piece = Call(port, id, slot, 0x0005, 1, new[] { (byte)name.Count });
                    if (piece == null) break;
                    int take = Math.Min(Math.Min(piece.Length, 20) - 4, length - name.Count);
                    if (take <= 0) break;
                    for (int i = 0; i < take; i++) name.Add(piece[4 + i]);
                }
                string fullName = DecodeName(name);
                if (fullName.Length > 0) s.Name = fullName;
                byte[] type = Call(port, id, slot, 0x0005, 2);
                if (type != null) s.Kind = type[4];
            }
            count = Call(port, id, slot, 0x0003, 0);
            if (count != null)
            {
                string unit = OtherDevicesProtocol.UnitId(count);
                if (unit.Length > 0) s.UnitId = unit;
                int entries = Math.Min((int)count[4], 8);
                for (int i = 0; i < entries; i++)
                {
                    byte[] info = Call(port, id, slot, 0x0003, 1, new[] { (byte)i });
                    string text = info != null ? OtherDevicesProtocol.Firmware(info) : null;
                    if (text != null)
                    {
                        s.Firmware = text;
                        break;
                    }
                }
            }
            byte[] list = Call(port, id, slot, 0x2201, 1, new byte[] { 0 });
            int low, high;
            if (list != null && OtherDevicesProtocol.DpiRange(list, out low, out high))
            {
                s.DpiMin = low;
                s.DpiMax = high;
            }
            byte[] current = Call(port, id, slot, 0x2201, 2, new byte[] { 0 });
            if (current != null) s.Dpi = OtherDevicesProtocol.CurrentDpi(current);
            byte[] rate = Call(port, id, slot, 0x8060, 1);
            if (rate != null) s.ReportRateMs = rate[4];
            byte[] onboard = Call(port, id, slot, 0x8100, 0);
            if (onboard != null)
            {
                s.ProfileCount = onboard[7];
                s.ButtonCount = onboard[9];
                s.SectorSize = onboard[11] << 8 | onboard[12];
                s.OnboardFormatOK = OtherDevicesProtocol.OnboardFormatSupported(onboard);
                WriteLog("other device slot " + slot + " onboard format memory=" + onboard[4] + " profile=" + onboard[5] + " macro=" + onboard[6] + " supported=" + s.OnboardFormatOK);
            }
            byte[] mode = Call(port, id, slot, 0x8100, 2);
            if (mode != null) s.OnboardMode = mode[4];
            if (s.OnboardMode == 1) ReadOnboard(port, s, true, false);
            FindFeature(port, id, slot, 0x1D4B);   // the reconnect event is recognized through this index
            if (gate.Interrupted)
            {
                WriteLog("other device slot " + slot + " read interrupted: mouse moving");
                return;
            }
            // A request timed out on the way (perhaps it was just switched off): ask once more.
            if (!OtherDevicesProtocol.ReadConfirmsOnline(timeouts - timeoutsBefore, delegate { return Ping(port, slot); }))
            {
                if (gate.Interrupted) return;
                devices[id] = paired.Copy();
                MarkNoAnswer(id);
                return;
            }
            backups.AdoptLegacy(s.Wpid, s.UnitId);
            s.RepairSector = backups.PendingRepair(s.BackupKey);
            s.LastSeen = DateTime.UtcNow;
            lastFullRead[id] = DateTime.UtcNow;
            devices[id] = s;
            ReadBattery(port, id);
            OtherDeviceSnapshot d = devices[id];
            WriteLog("other device slot " + slot + " connected (" + (sent - before) + " requests): " + d.Name + " wpid=" + d.Wpid +
                " battery=" + d.BatteryPercent + "% " + d.Millivolts + "mV charging=" + d.Charging + " dpi=" + d.Dpi + " rate=" + d.ReportRateMs +
                "ms onboard=" + d.OnboardMode + " fw=" + d.Firmware + " profile=" + (d.ProfileNumber ?? 0) + "/" + d.Profiles.Count + " stage=" + d.StageIndex);
            ArmButtonReports(port, id);
        }

        private static bool AppendData(List<byte> data, byte[] reply)
        {
            if (reply == null || reply.Length < 20) return false;
            for (int i = 4; i < 20; i++) data.Add(reply[i]);
            return true;
        }

        // Active profile and stage, plus that profile's stages, buttons and lights when the profile changed.
        private void ReadOnboard(IOtherPort port, OtherDeviceSnapshot s, bool directory, bool userAction)
        {
            if (!s.OnboardFormatOK) return;   // an unknown format is not parsed
            string id = s.Id;
            int slot = s.Slot;
            if (directory)
            {
                var data = new List<byte>();
                foreach (int offset in new[] { 0, 16 })
                {
                    byte[] r = Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardReadData, OtherDevicesProtocol.ReadDataParameters(0, offset), userAction);
                    if (!AppendData(data, r)) break;
                    if (data.Count >= 4 * (s.ProfileCount + 1)) break;
                }
                List<int> list = OtherDevicesProtocol.Directory(data.ToArray());
                if (list.Count > 0)
                {
                    s.Profiles = list;
                    s.ProfileSlots = OtherDevicesProtocol.DirectoryEntries(data.ToArray());
                }
            }
            int before = s.ActiveProfile;
            byte[] active = Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardActiveProfile, null, userAction);
            int? sector = OtherDevicesProtocol.ResolvedActiveProfile(active, s.Wpid, s.ProfileSlots);
            if (sector.HasValue) s.ActiveProfile = sector.Value;
            byte[] stageReply = Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardStageIndex, null, userAction);
            int? stage = OtherDevicesProtocol.StageIndex(stageReply);
            if (stage.HasValue) s.StageIndex = stage.Value;
            if (s.ActiveProfile == 0 || (s.ActiveProfile == before && s.DpiStages.Count > 0 && s.Buttons.Count > 0)) return;

            var head = new List<byte>();
            int end = s.ButtonCount > 0 ? 0x50 : 0x10;
            for (int offset = 0; offset < end; offset += 16)
            {
                byte[] r = Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardReadData, OtherDevicesProtocol.ReadDataParameters(s.ActiveProfile, offset), userAction);
                if (!AppendData(head, r)) break;
            }
            byte[] headBytes = head.ToArray();
            int[] stages = OtherDevicesProtocol.ProfileStages(headBytes);
            if (stages != null) s.DpiStages = new List<int>(stages);
            int? defaultStage = OtherDevicesProtocol.DefaultStage(headBytes);
            if (defaultStage.HasValue) s.DefaultStage = defaultStage.Value;
            if (headBytes.Length >= 0x50) s.Buttons = OtherDevicesProtocol.ProfileButtons(headBytes, Math.Min(s.ButtonCount, 12));
            if (s.SectorSize < OtherDevicesProtocol.LightOffset + 2 * OtherDevicesProtocol.LightSize + 2) return;
            var light = new List<byte>();
            foreach (int offset in new[] { OtherDevicesProtocol.LightOffset, OtherDevicesProtocol.LightOffset + 16 })
            {
                byte[] r = Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardReadData, OtherDevicesProtocol.ReadDataParameters(s.ActiveProfile, offset), userAction);
                if (!AppendData(light, r)) break;
            }
            if (light.Count >= 2 * OtherDevicesProtocol.LightSize)
            {
                var padded = new byte[OtherDevicesProtocol.LightOffset + light.Count];
                light.CopyTo(padded, OtherDevicesProtocol.LightOffset);
                s.Lights = OtherDevicesProtocol.ProfileLights(padded, s.Wpid);
            }
        }

        // Profile writes. Reads the whole profile, backs it up, writes, reads back; on a mismatch the old
        // content is written back. A reload and a return to the previous stage make the change take effect.
        // Every write carries the profile being edited: if the mouse moved to another one, nothing is written.
        // The completion runs on the UI thread with null on success, otherwise a message.
        public void WriteButtons(string id, int sector, Dictionary<int, OtherButtonAssignment> changes, bool restoreOriginal, Action<string> completion)
        {
            var request = restoreOriginal ? new WriteRequest { RestoreOriginal = true }
                : new WriteRequest { Patch = (current, s) => OtherDevicesProtocol.PatchedProfile(current, changes) };
            WriteProfile(id, sector, request, completion);
        }

        public void WriteReportRate(string id, int sector, int ms, Action<string> completion)
        {
            WriteProfile(id, sector, new WriteRequest { Patch = (current, s) => OtherDevicesProtocol.PatchedReportRate(current, ms) }, completion);
        }

        public void WriteLights(string id, int sector, Dictionary<int, OtherLightEffect> lights, Action<string> completion)
        {
            WriteProfile(id, sector, new WriteRequest { Patch = (current, s) => OtherDevicesProtocol.PatchedLights(current, lights) }, completion);
        }

        public void WriteStages(string id, int sector, IList<int> stages, int? defaultIndex, Action<string> completion)
        {
            WriteProfile(id, sector, new WriteRequest
            {
                Patch = (current, s) => OtherDevicesProtocol.PatchedStages(current, stages, defaultIndex, s.DpiMin > 0 ? s.DpiMin : 100, s.DpiMax > 0 ? s.DpiMax : 25600)
            }, completion);
        }

        // Writes the last content known to be good back to a profile whose write did not finish.
        public void RepairProfile(string id, Action<string> completion)
        {
            Post(delegate
            {
                OtherDeviceSnapshot d;
                if (!devices.TryGetValue(id, out d) || !d.RepairSector.HasValue)
                {
                    Complete(completion, null);
                    return;
                }
                string result = PerformWrite(id, d.RepairSector.Value, new WriteRequest { Repair = true });
                Publish();
                Complete(completion, result);
            });
        }

        private void WriteProfile(string id, int sector, WriteRequest request, Action<string> completion)
        {
            Post(delegate
            {
                string result = PerformWrite(id, sector, request);
                Publish();
                Complete(completion, result);
            });
        }

        private void Complete(Action<string> completion, string result)
        {
            if (completion != null) postToUi(delegate { completion(result); });
        }

        private string PerformWrite(string id, int sector, WriteRequest request)
        {
            bool isRepair = request.Repair;
            OtherDeviceSnapshot found;
            IOtherPort port = null;
            bool allowed = devices.TryGetValue(id, out found);
            if (allowed)
            {
                allowed = isRepair
                    ? found.Link == OtherDeviceLink.Online && found.OnboardMode == 1 && sector >= 1 && sector < 0x100 && found.SectorSize >= 0x50
                    : found.CanEditButtons;
                port = PortFor(found.Receiver);
            }
            if (!allowed || port == null) return Lang.T("裝置沒有連著，或目前的設定檔不能改", "The device isn't connected or this profile can't be changed");
            OtherDeviceSnapshot s = found.Copy();
            if (!isRepair && s.ActiveProfile != sector) return Message(OtherWriteResult.ProfileChanged);
            gate.Begin();
            string device = s.BackupKey;
            OtherDeviceSnapshot snapshot = s.Copy();
            int slot = s.Slot, stage = s.StageIndex;
            OtherWriteMode mode;
            if (request.RestoreOriginal) mode = OtherWriteMode.Restore(backups.LoadImage(backups.OriginalPath(device, sector), s.SectorSize));
            else if (isRepair) mode = OtherWriteMode.Repair(backups.LoadImage(backups.RepairPath(device, sector), s.SectorSize));
            else mode = OtherWriteMode.Patched(current => request.Patch(current, snapshot));
            var io = new OtherProfileIO
            {
                ActiveSector = () => OtherDevicesProtocol.ResolvedActiveProfile(Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardActiveProfile, null, true), snapshot.Wpid, snapshot.ProfileSlots),
                Read = which => ReadSector(port, snapshot, which),
                Command = (function, parameters) => Call(port, id, slot, 0x8100, function, parameters, true) != null,
                Backup = data => backups.Save(data, device, sector, DateTime.Now),
                Reload = () =>
                {
                    if (Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardSetProfile, OtherDevicesProtocol.SetProfileParameters(sector, snapshot.ProfileSlots), true) == null) return false;
                    return stage < 0 || Call(port, id, slot, 0x8100, OtherDevicesProtocol.OnboardSetStage, new[] { (byte)stage }, true) != null;
                }
            };
            OtherWriteOutcome outcome = OtherProfileWriter.Run(sector, mode, io);
            WriteLog("other device " + s.Name + " profile " + sector + " write result=" + outcome.Result);
            if (outcome.Result == OtherWriteResult.FailedNeedsRepair && outcome.RepairImage != null)
            {
                bool saved = backups.SaveRepair(outcome.RepairImage, device, sector);
                WriteLog("other device " + s.Name + " profile " + sector + " repair image saved=" + saved);
                if (saved) s.RepairSector = sector;
                devices[id] = s;
            }
            if (outcome.Target != null)
            {
                byte[] target = outcome.Target;
                if (isRepair)
                {
                    backups.ClearRepair(device, sector);
                    s.RepairSector = null;
                }
                if (s.ActiveProfile == sector)
                {
                    s.Buttons = OtherDevicesProtocol.ProfileButtons(target, Math.Min(s.ButtonCount, 12));
                    int[] stages = OtherDevicesProtocol.ProfileStages(target);
                    if (stages != null) s.DpiStages = new List<int>(stages);
                    int? defaultStage = OtherDevicesProtocol.DefaultStage(target);
                    if (defaultStage.HasValue) s.DefaultStage = defaultStage.Value;
                    if (s.Lights.Count > 0) s.Lights = OtherDevicesProtocol.ProfileLights(target, s.Lights.Count);
                    if (Array.IndexOf(OtherDevicesProtocol.ReportRates, (int)target[0]) >= 0) s.ReportRateMs = target[0];
                }
                RefreshAfterSwitch(port, s);
                devices[id] = s;
            }
            return Message(outcome.Result);
        }

        internal static string Message(OtherWriteResult result)
        {
            switch (result)
            {
                case OtherWriteResult.Written:
                case OtherWriteResult.Unchanged: return null;
                case OtherWriteResult.WrittenNotApplied: return Lang.T("已寫入滑鼠，但還沒確認套用。切換一次設定檔就會生效", "Saved to the mouse, but not confirmed as applied. Switch profiles once to apply it");
                case OtherWriteResult.ProfileChanged: return Lang.T("滑鼠的設定檔已經切換，這次沒有寫入。請重新打開再改", "The mouse switched profiles, so nothing was written. Reopen it and try again");
                case OtherWriteResult.ReadFailed: return Lang.T("讀取設定檔失敗，沒有寫入", "Couldn't read the profile; nothing was written");
                case OtherWriteResult.BackupFailed: return Lang.T("無法備份目前的設定檔，沒有寫入", "Couldn't back up the current profile; nothing was written");
                case OtherWriteResult.UnexpectedContent: return Lang.T("設定檔內容不符預期，沒有寫入", "The profile looks unexpected; nothing was written");
                case OtherWriteResult.LeftClickLocked: return Lang.T("這個內容會改掉左鍵，沒有寫入", "This would change the left button; nothing was written");
                case OtherWriteResult.NoBackup: return Lang.T("找不到可用的備份", "No usable backup was found");
                case OtherWriteResult.FailedUnchanged: return Lang.T("寫入失敗，設定檔沒有改變", "Writing failed; the profile wasn't changed");
                case OtherWriteResult.FailedRolledBack: return Lang.T("寫入失敗，已改回原本的設定", "Writing failed; the previous settings were restored");
                default: return Lang.T("寫入沒有完成，設定檔可能不完整。滑鼠連回來後請按「修復設定檔」", "Writing didn't finish and the profile may be incomplete. Once the mouse is back, click Repair profile");
            }
        }

        private byte[] ReadSector(IOtherPort port, OtherDeviceSnapshot s, int sector)
        {
            if (s.SectorSize < 16) return null;
            byte[] data = new byte[s.SectorSize];
            foreach (int offset in OtherDevicesProtocol.ReadOffsets(s.SectorSize))
            {
                byte[] r = Call(port, s.Id, s.Slot, 0x8100, OtherDevicesProtocol.OnboardReadData, OtherDevicesProtocol.ReadDataParameters(sector, offset), true);
                if (r == null || r.Length < 20) return null;
                Array.Copy(r, 4, data, offset, 16);
            }
            return data;
        }

        // Catches up on anything missed, such as a DPI button pressed while the app was not running.
        private void ReadSpeed(IOtherPort port, string id)
        {
            OtherDeviceSnapshot found;
            if (!devices.TryGetValue(id, out found)) return;
            OtherDeviceSnapshot s = found.Copy();
            byte[] current = Call(port, id, s.Slot, 0x2201, 2, new byte[] { 0 });
            if (current != null) s.Dpi = OtherDevicesProtocol.CurrentDpi(current);
            byte[] rate = Call(port, id, s.Slot, 0x8060, 1);
            if (rate != null) s.ReportRateMs = rate[4];
            if (s.OnboardMode == 1) ReadOnboard(port, s, false, false);
            devices[id] = s;
        }

        // Switches the user asked for: sent right away, the pointer is usually moving while clicking a menu.
        public void SetStage(string id, int index)
        {
            Post(delegate
            {
                OtherDeviceSnapshot found;
                if (!devices.TryGetValue(id, out found) || !found.CanSwitchStage || index < 0 || index >= found.DpiStages.Count || found.DpiStages[index] <= 0) return;
                IOtherPort port = PortFor(found.Receiver);
                if (port == null) return;
                OtherDeviceSnapshot s = found.Copy();
                gate.Begin();
                bool ok = Call(port, id, s.Slot, 0x8100, OtherDevicesProtocol.OnboardSetStage, new[] { (byte)index }, true) != null;
                RefreshAfterSwitch(port, s);
                devices[id] = s;
                WriteLog("other device " + s.Name + " set stage " + (index + 1) + " ok=" + ok + ": stage=" + s.StageIndex + " dpi=" + s.Dpi);
                Publish();
            });
        }

        public void SetProfile(string id, int sector)
        {
            Post(delegate
            {
                OtherDeviceSnapshot found;
                if (!devices.TryGetValue(id, out found) || found.Link != OtherDeviceLink.Online || found.OnboardMode != 1 || !found.Profiles.Contains(sector)) return;
                IOtherPort port = PortFor(found.Receiver);
                if (port == null) return;
                OtherDeviceSnapshot s = found.Copy();
                gate.Begin();
                bool ok = Call(port, id, s.Slot, 0x8100, OtherDevicesProtocol.OnboardSetProfile, OtherDevicesProtocol.SetProfileParameters(sector, s.ProfileSlots), true) != null;
                RefreshAfterSwitch(port, s);
                devices[id] = s;
                WriteLog("other device " + s.Name + " set profile " + sector.ToString("X4") + " ok=" + ok + ": profile=" + (s.ProfileNumber ?? 0) + " stage=" + s.StageIndex + " dpi=" + s.Dpi);
                Publish();
            });
        }

        private void RefreshAfterSwitch(IOtherPort port, OtherDeviceSnapshot s)
        {
            string id = s.Id;
            devices[id] = s.Copy();
            Thread.Sleep(150);
            ReadOnboard(port, s, false, true);
            byte[] current = Call(port, id, s.Slot, 0x2201, 2, new byte[] { 0 }, true);
            if (current != null) s.Dpi = OtherDevicesProtocol.CurrentDpi(current);
            s.LastSeen = DateTime.UtcNow;
        }

        // 0x1004, then 0x1000, then 0x1001 (voltage, converted to a percentage).
        private bool ReadBattery(IOtherPort port, string id)
        {
            OtherDeviceSnapshot found;
            if (!devices.TryGetValue(id, out found)) return false;
            OtherDeviceSnapshot s = found.Copy();
            bool ok = false;
            OtherBatteryInfo info = OtherDevicesProtocol.UnifiedBatteryInfo(Call(port, id, s.Slot, 0x1004, 1));
            if (info == null) info = OtherDevicesProtocol.BatteryStatusInfo(Call(port, id, s.Slot, 0x1000, 0));
            if (info != null)
            {
                s.BatterySource = OtherBatterySource.Percent;
                s.BatteryPercent = info.Percent;
                ApplyCharge(s, info.State, false, info.Slow);
                ok = true;
            }
            else
            {
                byte[] reply = Call(port, id, s.Slot, 0x1001, 0);
                if (reply != null) ok = ApplyVoltage(s, reply);
            }
            if (ok)
            {
                lastBattery[id] = DateTime.UtcNow;
                s.LastSeen = DateTime.UtcNow;
                s.BatterySeq++;
            }
            devices[id] = s;
            return ok;
        }

        private bool ApplyVoltage(OtherDeviceSnapshot s, byte[] bytes)
        {
            OtherVoltageInfo v = OtherDevicesProtocol.VoltageInfo(bytes);
            if (v == null) return false;
            // The voltage jumps when power is connected or removed, so the average starts over.
            List<int> list;
            if (v.State != s.ChargeState || !samples.TryGetValue(s.Id, out list)) list = new List<int>();
            list.Add(v.Millivolts);
            if (list.Count > 5) list.RemoveRange(0, list.Count - 5);
            samples[s.Id] = list;
            s.BatterySource = OtherBatterySource.Voltage;
            s.Millivolts = v.Millivolts;
            ApplyCharge(s, v.State, v.Fast, v.Slow);
            s.BatteryPercent = v.State == OtherChargeState.Full ? 100 : OtherDevicesProtocol.PercentFromMillivolts(OtherDevicesProtocol.Smoothed(list));
            return true;
        }

        private static void ApplyCharge(OtherDeviceSnapshot s, OtherChargeState state, bool fast, bool slow)
        {
            s.ChargeState = state;
            s.Charging = state == OtherChargeState.Charging;
            s.Full = state == OtherChargeState.Full;
            s.FastCharge = fast;
            s.SlowCharge = slow;
        }

        // Reports from the reader threads
        private void OnReceived(IOtherPort source, byte[] bytes)
        {
            if (bytes.Length < 7 || (bytes[0] != 0x10 && bytes[0] != 0x11)) return;
            lock (pendingLock)
            {
                if (pendingKey != null && ReferenceEquals(pendingPort, source))
                {
                    byte[] key = pendingKey;
                    bool reply = bytes.Length > key.Length;
                    for (int i = 0; reply && i < key.Length; i++) if (bytes[i + 1] != key[i]) reply = false;
                    // Errors: HID++ 2.0 is <dev> FF <feature> <function>, HID++ 1.0 is <dev> 8F <sub> <register>.
                    bool error = bytes.Length >= 6 && bytes[1] == key[0] && (bytes[2] == 0xFF || bytes[2] == 0x8F) &&
                        bytes[3] == key[1] && bytes[4] == key[2];
                    if (reply || error)
                    {
                        pendingReply = reply ? bytes : null;
                        pendingError = error && !reply;
                        pendingKey = null;
                        pendingSignal.Set();
                        return;
                    }
                }
            }
            int slot;
            bool connected;
            if (OtherDevicesProtocol.ConnectionNotice(bytes, out slot, out connected))
            {
                Post(delegate
                {
                    if (stopped) return;
                    WriteLog("other device slot " + slot + " connection notice connected=" + connected);
                    if (!connected)
                    {
                        MarkNoAnswer(source.Key + "#" + slot);
                        Publish();
                        return;
                    }
                    DeviceCameBack(source, slot);
                });
                return;
            }
            if (HandleButtonReport(source, bytes)) return;
            // Software ID 0 means the device sent it on its own: battery change, reconnect (0x1D4B), onboard events.
            if (bytes[0] != 0x11 || (bytes[3] & 0x0F) != 0 || bytes[1] < 1 || bytes[1] > 6) return;
            Post(delegate { if (!stopped) HandleEvent(source, bytes); });
        }

        private static byte IndexOf(Dictionary<int, byte> table, int feature)
        {
            byte index;
            return table.TryGetValue(feature, out index) ? index : (byte)0;
        }

        private void HandleEvent(IOtherPort source, byte[] bytes)
        {
            string id = source.Key + "#" + bytes[1];
            OtherDeviceSnapshot found;
            Dictionary<int, byte> table;
            if (!devices.TryGetValue(id, out found) || !features.TryGetValue(id, out table)) return;
            OtherDeviceSnapshot s = found.Copy();
            byte reconnect = IndexOf(table, 0x1D4B);
            OtherOnboardEvent onboard = OtherDevicesProtocol.OnboardEvent(bytes, IndexOf(table, 0x8100));
            byte voltage = IndexOf(table, 0x1001), unified = IndexOf(table, 0x1004);
            if (reconnect != 0 && bytes[2] == reconnect)
            {
                WriteLog("other device " + s.Name + " reconnected");
                DeviceCameBack(source, s.Slot);
            }
            else if (onboard != null)
            {
                s.LastSeen = DateTime.UtcNow;
                if (onboard.Kind == OtherOnboardEventKind.StageChanged)
                {
                    s.StageIndex = onboard.Stage;
                    int? dpi = OtherDevicesProtocol.DpiForStage(onboard.Stage, s.DpiStages);
                    if (dpi.HasValue) s.Dpi = dpi.Value;
                    devices[id] = s;
                    WriteLog("other device " + s.Name + " stage changed to " + (onboard.Stage + 1) + " (" + s.Dpi + " DPI)");
                }
                else
                {
                    devices[id] = s;
                    WriteLog("other device " + s.Name + " profile changed");
                    WhenIdle(60, true, null, delegate { ReloadChangedProfile(id); });
                }
                Publish();
            }
            else if (voltage != 0 && bytes[2] == voltage && ApplyVoltage(s, bytes))
            {
                s.LastSeen = DateTime.UtcNow;
                s.BatterySeq++;
                devices[id] = s;
                lastBattery[id] = DateTime.UtcNow;
                Publish();
            }
            else if (unified != 0 && bytes[2] == unified)
            {
                OtherBatteryInfo info = OtherDevicesProtocol.UnifiedBatteryInfo(bytes);
                if (info == null) return;
                s.BatteryPercent = info.Percent;
                ApplyCharge(s, info.State, false, info.Slow);
                s.LastSeen = DateTime.UtcNow;
                s.BatterySeq++;
                devices[id] = s;
                Publish();
            }
        }

        private void ReloadChangedProfile(string id)
        {
            OtherDeviceSnapshot found;
            if (!devices.TryGetValue(id, out found) || found.Link != OtherDeviceLink.Online) return;
            IOtherPort port = PortFor(found.Receiver);
            if (port == null) return;
            gate.Begin();
            OtherDeviceSnapshot current = found.Copy();
            current.DpiStages = new List<int>();
            current.Buttons = new List<OtherButtonAssignment>();
            ReadOnboard(port, current, false, false);
            int? dpi = OtherDevicesProtocol.DpiForStage(current.StageIndex, current.DpiStages);
            if (dpi.HasValue) current.Dpi = dpi.Value;
            if (!gate.Interrupted) devices[id] = current;
            Publish();
        }

        // Transport
        private bool Ping(IOtherPort port, int slot)
        {
            return Hidpp(port, slot, 0x00, 1, new byte[] { 0, 0, 0x5A }, 1500, false) != null;
        }

        private byte? FindFeature(IOtherPort port, string id, int slot, int feature)
        {
            Dictionary<int, byte> table;
            byte cached;
            if (features.TryGetValue(id, out table) && table.TryGetValue(feature, out cached))
            {
                if (cached == 0) return null;
                return cached;
            }
            byte[] root = Hidpp(port, slot, 0x00, 0, new[] { (byte)(feature >> 8), (byte)(feature & 0xFF) }, 1200, false);
            if (root == null) return null;
            if (!features.TryGetValue(id, out table))
            {
                table = new Dictionary<int, byte>();
                features[id] = table;
            }
            table[feature] = root[4];
            if (root[4] == 0) return null;
            return root[4];
        }

        private byte[] Call(IOtherPort port, string id, int slot, int feature, byte function, byte[] parameters = null, bool userAction = false)
        {
            byte? index = FindFeature(port, id, slot, feature);
            if (!index.HasValue) return null;
            return Hidpp(port, slot, index.Value, function, parameters ?? new byte[0], 1000, userAction);
        }

        private static double NowSeconds()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private byte[] Hidpp(IOtherPort port, int slot, byte feature, byte function, byte[] parameters, int timeout, bool userAction)
        {
            byte[] packet = new byte[20];
            packet[0] = 0x11;
            packet[1] = (byte)slot;
            packet[2] = feature;
            byte requestId = requestIds.Take(NowSeconds());
            packet[3] = (byte)(function << 4 | requestId);
            Array.Copy(parameters, 0, packet, 4, Math.Min(parameters.Length, 16));
            OtherRegisterOutcome outcome = Send(port, packet, new[] { packet[1], packet[2], packet[3] }, timeout, userAction);
            if (outcome.Kind == OtherRegisterKind.Timeout) requestIds.TimedOut(requestId, NowSeconds());
            if (outcome.Kind != OtherRegisterKind.Reply || outcome.Reply.Length < 16) return null;
            return outcome.Reply;
        }

        // HID++ 1.0 receiver register; the receiver answers itself, so it does not wait for the mouse to stop.
        private OtherRegisterOutcome Register(IOtherPort port, byte[] packet)
        {
            return Send(port, packet, new[] { packet[1], packet[2], packet[3], packet[4] }, 1000, true);
        }

        // Re-checks the mouse before each packet. A batch cut short returns Timeout; callers tell it
        // apart through gate.Interrupted, so the device is not mistaken for absent.
        private OtherRegisterOutcome Send(IOtherPort port, byte[] packet, byte[] key, int timeout, bool userAction)
        {
            if (!userAction && !gate.Allow(delegate { return MouseIdle(idleNeeded); }, delegate (double s) { Thread.Sleep((int)(s * 1000)); }))
                return OtherRegisterOutcome.Timeout();
            sent++;
            using (var signal = new ManualResetEventSlim(false))
            {
                lock (pendingLock)
                {
                    pendingPort = port;
                    pendingKey = key;
                    pendingReply = null;
                    pendingError = false;
                    pendingSignal = signal;
                }
                if (port.Write(packet)) signal.Wait(timeout);
                byte[] reply;
                bool error;
                lock (pendingLock)
                {
                    reply = pendingReply;
                    error = pendingError;
                    pendingPort = null;
                    pendingKey = null;
                    pendingReply = null;
                    pendingError = false;
                    pendingSignal = null;
                }
                if (reply != null) return OtherRegisterOutcome.FromReply(reply);
                if (error) return OtherRegisterOutcome.Error();
            }
            timeouts++;
            return OtherRegisterOutcome.Timeout();
        }

        private void WriteLog(string text)
        {
            Action<string> handler = Log;
            if (handler != null) postToUi(delegate { handler("Others " + text); });
        }

        private void Publish()
        {
            var list = new List<OtherDeviceSnapshot>();
            foreach (OtherDeviceSnapshot d in devices.Values) list.Add(d.Copy());
            list.Sort((a, b) =>
            {
                int order = string.CompareOrdinal(a.Receiver, b.Receiver);
                return order != 0 ? order : a.Slot.CompareTo(b.Slot);
            });
            Action<List<OtherDeviceSnapshot>> handler = Changed;
            if (handler != null) postToUi(delegate { handler(list); });
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (timer != null) timer.Dispose();
            using (var done = new ManualResetEventSlim(false))
            {
                Post(delegate
                {
                    try { StopButtonReports(); }
                    catch { }
                    stopped = true;
                    try { done.Set(); }
                    catch (ObjectDisposedException) { }
                });
                // Lets a profile write in progress finish; closing the ports stops anything still running.
                done.Wait(3000);
            }
            stopped = true;
            try { queue.CompleteAdding(); }
            catch (ObjectDisposedException) { }
            foreach (IOtherPort port in PortList())
            {
                try { port.Dispose(); }
                catch { }
            }
            if (thread != null) thread.Join(1000);
        }
    }
    // ---- Other devices I/O end ----

    // ---- Other devices page ----
    // One card per device on a Lightspeed receiver. Status, DPI, report rate, profile, lighting and the
    // buttons stored in the mouse. Windows drops mouse buttons above 5, so a "hand to the app" button on a
    // G502 is picked up through HID++ 0x8110 and its action is kept in the INI.
    internal sealed class OtherChoice
    {
        public string Title;
        public OtherButtonAssignment Value;
        public override string ToString() { return Title; }
    }

    internal static class OtherText
    {
        public static List<OtherChoice> Presets()
        {
            var list = new List<OtherChoice>();
            Action<string, OtherButtonAssignment> add = (title, value) => list.Add(new OtherChoice { Title = title, Value = value });
            add(Lang.T("左鍵", "Left click"), OtherButtonAssignment.Mouse(1));
            add(Lang.T("右鍵", "Right click"), OtherButtonAssignment.Mouse(2));
            add(Lang.T("中鍵", "Middle click"), OtherButtonAssignment.Mouse(3));
            add(Lang.T("上一頁", "Back"), OtherButtonAssignment.Mouse(4));
            add(Lang.T("下一頁", "Forward"), OtherButtonAssignment.Mouse(5));
            add(Lang.T("DPI 上", "DPI up"), OtherButtonAssignment.Function(3));
            add(Lang.T("DPI 下", "DPI down"), OtherButtonAssignment.Function(4));
            add(Lang.T("循環切換 DPI", "Cycle DPI"), OtherButtonAssignment.Function(5));
            add(Lang.T("狙擊（按住降速）", "Sniper (hold to slow)"), OtherButtonAssignment.Function(7));
            add(Lang.T("下一個設定檔", "Next profile"), OtherButtonAssignment.Function(8));
            add(Lang.T("上一個設定檔", "Previous profile"), OtherButtonAssignment.Function(9));
            add(Lang.T("顯示電量", "Show battery"), OtherButtonAssignment.Function(0x0C));
            add(Lang.T("向左捲動", "Scroll left"), OtherButtonAssignment.Function(1));
            add(Lang.T("向右捲動", "Scroll right"), OtherButtonAssignment.Function(2));
            add(Lang.T("播放／暫停", "Play / pause"), OtherButtonAssignment.Consumer(0xCD));
            add(Lang.T("下一首", "Next track"), OtherButtonAssignment.Consumer(0xB5));
            add(Lang.T("上一首", "Previous track"), OtherButtonAssignment.Consumer(0xB6));
            add(Lang.T("音量＋", "Volume up"), OtherButtonAssignment.Consumer(0xE9));
            add(Lang.T("音量－", "Volume down"), OtherButtonAssignment.Consumer(0xEA));
            add(Lang.T("靜音", "Mute"), OtherButtonAssignment.Consumer(0xE2));
            add(Lang.T("複製 Ctrl+C", "Copy Ctrl+C"), OtherButtonAssignment.Key(0x01, 0x06));
            add(Lang.T("貼上 Ctrl+V", "Paste Ctrl+V"), OtherButtonAssignment.Key(0x01, 0x19));
            add(Lang.T("剪下 Ctrl+X", "Cut Ctrl+X"), OtherButtonAssignment.Key(0x01, 0x1B));
            add(Lang.T("復原 Ctrl+Z", "Undo Ctrl+Z"), OtherButtonAssignment.Key(0x01, 0x1D));
            add(Lang.T("切換視窗 Alt+Tab", "Switch windows Alt+Tab"), OtherButtonAssignment.Key(0x04, 0x2B));
            add(Lang.T("工作檢視 Win+Tab", "Task view Win+Tab"), OtherButtonAssignment.Key(0x08, 0x2B));
            add(Lang.T("截圖 Win+Shift+S", "Screenshot Win+Shift+S"), OtherButtonAssignment.Key(0x0A, 0x16));
            add(Lang.T("停用", "Disabled"), OtherButtonAssignment.Disabled());
            return list;
        }

        public static string Title(OtherButtonAssignment a, List<OtherChoice> presets)
        {
            foreach (OtherChoice choice in presets) if (choice.Value.Equals(a)) return choice.Title;
            if (a.AppButton.HasValue) return Lang.T("交給 App（不支援）", "App (unsupported)");
            if (a.Kind == OtherButtonKind.Mouse) return Lang.T("滑鼠第 ", "Mouse button ") + a.Number + Lang.T(" 鍵", "");
            return Lang.T("其他（", "Other (") + BitConverter.ToString(a.Bytes).Replace("-", " ") + Lang.T("）", ")");
        }

        public static string ShortName(OtherDeviceSnapshot d)
        {
            var words = new List<string>();
            foreach (string word in d.Name.Split(' '))
            {
                string upper = word.ToUpperInvariant();
                if (word.Length == 0 || upper == "LIGHTSPEED" || upper == "WIRELESS" || upper == "GAMING" || upper == "MOUSE" || upper == "KEYBOARD" || upper == "LOGITECH") continue;
                words.Add(word);
            }
            string text = string.Join(" ", words.ToArray());
            if (text.Length == 0) text = d.Name.Length == 0 ? Lang.T("其他裝置", "Other") : d.Name;
            if (text.Length > 12 && text.IndexOf('/') > 0)
            {
                // "B330/M330/M331": keep as many of the last names as fit.
                string[] names = text.Split('/');
                string joined = names[names.Length - 1];
                for (int i = names.Length - 2; i >= 0 && names[i].Length + 1 + joined.Length <= 12; i--) joined = names[i] + "/" + joined;
                return joined;
            }
            return text.Length <= 12 ? text : text.Substring(0, 12);
        }

        public static string Battery(OtherDeviceSnapshot d)
        {
            string volts = d.Millivolts > 0 ? (d.Millivolts / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " V" : "";
            bool voltage = d.BatterySource == OtherBatterySource.Voltage;
            if (d.Full) return Lang.T("已充飽", "Fully charged");
            if (d.ChargeState == OtherChargeState.Error) return Lang.T("充電錯誤", "Charging error");
            if (d.BatteryPercent < 0) return "—";
            if (d.Charging)
            {
                string what = d.FastCharge ? Lang.T("快充中", "Fast charging") : d.SlowCharge ? Lang.T("慢充中", "Slow charging") : Lang.T("充電中", "Charging");
                return voltage ? what + Lang.T("（", " (") + volts + Lang.T("）", ")") : d.BatteryPercent + "%" + Lang.T("（", " (") + what + Lang.T("）", ")");
            }
            if (d.ChargeState == OtherChargeState.PluggedNotCharging)
            {
                string plugged = Lang.T("已接電、未充電", "plugged in, not charging");
                return (voltage ? Lang.T("約 ", "About ") : "") + d.BatteryPercent + "%" + Lang.T("（", " (") + plugged + Lang.T("）", ")");
            }
            return voltage ? Lang.T("約 ", "About ") + d.BatteryPercent + "%" + Lang.T("（", " (") + volts + Lang.T("）", ")") : d.BatteryPercent + "%";
        }

        // Battery tile: the level on the main line, charging state and voltage underneath.
        public static string BatteryValue(OtherDeviceSnapshot d)
        {
            if (d.Full) return "100%";
            if (d.BatteryPercent < 0) return "—";
            return (d.BatterySource == OtherBatterySource.Voltage ? Lang.T("約 ", "~") : "") + d.BatteryPercent + "%";
        }

        public static string BatteryNote(OtherDeviceSnapshot d)
        {
            var parts = new List<string>();
            if (d.Full) parts.Add(Lang.T("已充飽", "Full"));
            else if (d.ChargeState == OtherChargeState.Error) parts.Add(Lang.T("充電錯誤", "Charge error"));
            else if (d.Charging) parts.Add(d.FastCharge ? Lang.T("快充中", "Fast charging") : d.SlowCharge ? Lang.T("慢充中", "Slow charging") : Lang.T("充電中", "Charging"));
            else if (d.ChargeState == OtherChargeState.PluggedNotCharging) parts.Add(Lang.T("已接電", "Plugged in"));
            if (d.Millivolts > 0) parts.Add((d.Millivolts / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " V");
            return string.Join(" · ", parts.ToArray());
        }

        public static string Link(OtherDeviceSnapshot d)
        {
            switch (d.Link)
            {
                case OtherDeviceLink.Online: return Lang.T("已連線", "Connected");
                case OtherDeviceLink.Reading: return Lang.T("讀取中…", "Reading…");
                case OtherDeviceLink.NoAnswer: return Lang.T("沒有回應", "Not responding");
                default: return Lang.T("接收器未連接", "Receiver unplugged");
            }
        }

        public static string Offline(OtherDeviceSnapshot d)
        {
            if (d.Link == OtherDeviceLink.ReceiverGone)
                return Lang.T("接收器已拔掉。插回這台電腦後會自動重新讀取。", "The receiver was unplugged. Plug it back in and the device is read again automatically.");
            if (d.Link == OtherDeviceLink.Reading)
                return Lang.T("正在讀取裝置資料，滑鼠停一下就會完成。", "Reading the device; this finishes as soon as the pointer pauses.");
            return Lang.T("裝置配對在接收器上但沒有回應，可能在睡眠中或已關機。動一下或打開電源，幾秒內會自動更新。",
                "The device is paired with the receiver but not responding; it may be asleep or off. Move it or turn it on; this updates within a few seconds.");
        }

        public static string Seen(OtherDeviceSnapshot d)
        {
            if (!d.LastSeen.HasValue) return "";
            int minutes = (int)((DateTime.UtcNow - d.LastSeen.Value).TotalMinutes);
            if (minutes < 1) return Lang.T(" · 剛剛更新", " · updated just now");
            if (minutes < 60) return Lang.T(" · " + minutes + " 分鐘前更新", " · updated " + minutes + " min ago");
            if (minutes < 1440) return Lang.T(" · " + minutes / 60 + " 小時前更新", " · updated " + minutes / 60 + " h ago");
            string when = d.LastSeen.Value.ToLocalTime().ToString("M/d HH:mm");
            return Lang.T(" · " + when + " 更新", " · updated " + when);
        }

        public static string Light(OtherLightEffect e)
        {
            switch (e.Kind)
            {
                case OtherLightKind.Off: return Lang.T("關閉", "Off");
                case OtherLightKind.Fixed: return Lang.T("固定顏色", "Fixed color");
                case OtherLightKind.Cycle: return Lang.T("色彩循環", "Color cycle");
                case OtherLightKind.Breathe: return Lang.T("呼吸", "Breathing");
                default: return Lang.T("其他效果", "Other effect");
            }
        }

        public static string ReceiverKind(string pid)
        {
            switch (pid)
            {
                case "C52B":
                case "C532": return "Unifying";
                case "C52F": return "Nano";
                default: return "Lightspeed";
            }
        }

        public static string Subtitle(OtherDeviceSnapshot d)
        {
            var parts = new List<string>();
            string receiver = ReceiverKind(d.ReceiverPid);
            parts.Add(Lang.T(receiver + " 接收器（" + d.ReceiverPid + "）", receiver + " receiver (" + d.ReceiverPid + ")"));
            parts.Add(Lang.T("槽位 ", "slot ") + d.Slot);
            if (d.OnboardMode == 2) parts.Add(Lang.T("主機控制", "Host mode"));
            if (d.Firmware.Length > 0) parts.Add(Lang.T("韌體 ", "firmware ") + d.Firmware);
            return string.Join(" · ", parts.ToArray()) + Seen(d);
        }
    }

    internal sealed class OtherStageDialog : Form
    {
        private readonly NumericUpDown[] values = new NumericUpDown[5];
        private readonly RadioButton[] defaults = new RadioButton[5];
        private readonly Label problem = new Label();
        private readonly OtherDeviceSnapshot device;
        private readonly int low, high;
        public readonly List<int> Stages = new List<int>();
        public int DefaultIndex;

        public OtherStageDialog(OtherDeviceSnapshot d)
        {
            device = d;
            low = d.DpiMin > 0 ? d.DpiMin : 100;
            high = d.DpiMax > 0 ? d.DpiMax : 25600;
            float scale = Theme.GetScale();
            Func<int, int> S = v => (int)Math.Round(v * scale);
            DialogStyle.Apply(this, Lang.T("各段指標速度", "Pointer speed levels"), 340, 346, scale);
            Controls.Add(new Label { Text = low + "–" + high + Lang.T(" DPI，以 50 為單位；存進滑鼠目前的設定檔（寫入前會備份）", " DPI in steps of 50; saved to the mouse's current profile (backed up first)"), ForeColor = Theme.Muted, Location = new Point(S(16), S(12)), Size = new Size(S(308), S(38)) });
            Controls.Add(new Label { Text = Lang.T("段", "Level"), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(16), S(56)) });
            Controls.Add(new Label { Text = "DPI", ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(70), S(56)) });
            Controls.Add(new Label { Text = Lang.T("開機預設", "Default"), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(210), S(56)) });
            int y = 80;
            for (int i = 0; i < 5; i++)
            {
                int original = i < d.DpiStages.Count ? d.DpiStages[i] : 0;
                if (original <= 0) continue;
                Controls.Add(new Label { Text = (i + 1).ToString(), AutoSize = true, Location = new Point(S(20), S(y + 3)) });
                var number = new NumericUpDown { Minimum = 0, Maximum = 65000, Increment = 50, Value = original, Location = new Point(S(70), S(y)), Size = new Size(S(110), S(26)) };
                number.ValueChanged += delegate { Validate2(); };
                number.KeyUp += delegate { Validate2(); };
                values[i] = number;
                Controls.Add(number);
                var radio = new RadioButton { Location = new Point(S(226), S(y + 2)), Size = new Size(S(22), S(22)), Checked = i == Math.Max(d.DefaultStage, 0) };
                radio.CheckedChanged += delegate { Validate2(); };
                defaults[i] = radio;
                Controls.Add(radio);
                y += 34;
            }
            problem.ForeColor = Color.Firebrick;
            problem.Location = new Point(S(16), S(254));
            problem.Size = new Size(S(308), S(36));
            Controls.Add(problem);
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Bounds = new Rectangle(S(118), S(298), S(96), S(32));
            Controls.Add(cancel);
            save = Theme.CreateButton(Lang.T("寫入滑鼠", "Save to mouse"), true);
            save.Bounds = new Rectangle(S(222), S(298), S(102), S(32));
            save.Click += delegate
            {
                if (!Collect()) return;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);
            CancelButton = cancel;
            Validate2();
        }

        private readonly Button save;

        private bool Collect()
        {
            Stages.Clear();
            DefaultIndex = -1;
            bool ok = true;
            for (int i = 0; i < 5; i++)
            {
                int original = i < device.DpiStages.Count ? device.DpiStages[i] : 0;
                int value = values[i] != null ? (int)values[i].Value : 0;
                if (!OtherDevicesProtocol.ValidStage(value, original, low, high)) ok = false;
                Stages.Add(value);
                if (defaults[i] != null && defaults[i].Checked) DefaultIndex = i;
            }
            return ok && DefaultIndex >= 0 && Stages[DefaultIndex] > 0;
        }

        private void Validate2()
        {
            bool ok = Collect();
            problem.Text = ok ? "" : Lang.T("數值超出範圍或不是 50 的倍數", "A value is out of range or not a multiple of 50");
            bool same = true;
            for (int i = 0; i < 5; i++)
                if (Stages[i] != (i < device.DpiStages.Count ? device.DpiStages[i] : 0)) same = false;
            save.Enabled = ok && (!same || DefaultIndex != device.DefaultStage);
        }
    }

    internal sealed class OtherLightDialog : Form
    {
        private readonly List<OtherLightEffect> original;
        private readonly ComboBox[] kinds;
        private readonly Button[] colors;
        private readonly NumericUpDown[] seconds;
        private readonly NumericUpDown[] brightness;
        private readonly Button save;
        public readonly Dictionary<int, OtherLightEffect> Changes = new Dictionary<int, OtherLightEffect>();

        public OtherLightDialog(List<OtherLightEffect> lights)
        {
            original = lights;
            int n = lights.Count;
            kinds = new ComboBox[n];
            colors = new Button[n];
            seconds = new NumericUpDown[n];
            brightness = new NumericUpDown[n];
            float scale = Theme.GetScale();
            Func<int, int> S = v => (int)Math.Round(v * scale);
            DialogStyle.Apply(this, Lang.T("燈光", "Lighting"), 430, 82 + n * 112, scale);
            for (int i = 0; i < n; i++)
            {
                int y = 14 + i * 112;
                OtherLightEffect e = lights[i];
                string zone = i == 0 ? Lang.T("主要區域", "Main zone") : i == 1 ? Lang.T("G 標誌", "G logo") : Lang.T("區域 ", "Zone ") + (i + 1);
                Controls.Add(new Label { Text = zone, Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold), AutoSize = true, Location = new Point(S(16), S(y + 4)) });
                var kind = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(S(130), S(y)), Size = new Size(S(160), S(26)) };
                kind.Items.AddRange(new object[] { Lang.T("關閉", "Off"), Lang.T("固定顏色", "Fixed color"), Lang.T("色彩循環", "Color cycle"), Lang.T("呼吸", "Breathing") });
                if (e.Kind == OtherLightKind.Unknown) kind.Items.Add(Lang.T("其他效果", "Other effect"));
                kind.SelectedIndex = e.Kind == OtherLightKind.Unknown ? 4 : (int)e.Kind;
                kinds[i] = kind;
                Controls.Add(kind);
                Button color = Theme.CreateButton(Lang.T("顏色…", "Color…"), false);
                color.Bounds = new Rectangle(S(300), S(y), S(112), S(28));
                color.Tag = e.Kind == OtherLightKind.Fixed || e.Kind == OtherLightKind.Breathe ? Color.FromArgb(e.R, e.G, e.B) : Color.White;
                color.Click += delegate
                {
                    using (var dialog = new ColorDialog { Color = (Color)color.Tag, FullOpen = true })
                    {
                        if (dialog.ShowDialog(this) != DialogResult.OK) return;
                        color.Tag = dialog.Color;
                        Refresh2();
                    }
                };
                colors[i] = color;
                Controls.Add(color);
                Controls.Add(new Label { Text = Lang.T("速度（秒）", "Speed (s)"), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(16), S(y + 40)) });
                bool timed = e.Kind == OtherLightKind.Cycle || e.Kind == OtherLightKind.Breathe;
                seconds[i] = new NumericUpDown { DecimalPlaces = 1, Increment = 0.5M, Minimum = 1, Maximum = 20, Value = timed ? Math.Max(1M, Math.Min(20M, e.Period / 1000M)) : 5M, Location = new Point(S(130), S(y + 36)), Size = new Size(S(90), S(26)) };
                Controls.Add(seconds[i]);
                Controls.Add(new Label { Text = Lang.T("亮度（%）", "Brightness (%)"), ForeColor = Theme.Muted, AutoSize = true, Location = new Point(S(232), S(y + 40)) });
                brightness[i] = new NumericUpDown { Increment = 5, Minimum = 10, Maximum = 100, Value = timed ? Math.Max(10, Math.Min(100, e.Brightness)) : 100, Location = new Point(S(340), S(y + 36)), Size = new Size(S(72), S(26)) };
                Controls.Add(brightness[i]);
                kind.SelectedIndexChanged += delegate { Refresh2(); };
                seconds[i].ValueChanged += delegate { Refresh2(); };
                brightness[i].ValueChanged += delegate { Refresh2(); };
                if (i < n - 1) Controls.Add(new Panel { BackColor = Theme.Divider, Bounds = new Rectangle(S(16), S(y + 76), S(396), 1) });
            }
            int bottom = 14 + n * 112;
            Button cancel = Theme.CreateButton(Lang.T("取消", "Cancel"), false);
            cancel.DialogResult = DialogResult.Cancel;
            cancel.Bounds = new Rectangle(S(206), S(bottom + 16), S(96), S(32));
            Controls.Add(cancel);
            save = Theme.CreateButton(Lang.T("寫入滑鼠", "Save to mouse"), true);
            save.Bounds = new Rectangle(S(310), S(bottom + 16), S(102), S(32));
            save.Click += delegate
            {
                Refresh2();
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(save);
            CancelButton = cancel;
            Refresh2();
        }

        private OtherLightEffect Effect(int zone)
        {
            Color c = (Color)colors[zone].Tag;
            int period = (int)Math.Round(seconds[zone].Value * 1000);
            int level = (int)brightness[zone].Value;
            switch (kinds[zone].SelectedIndex)
            {
                case 0: return OtherLightEffect.Off();
                case 1: return OtherLightEffect.Fixed(c.R, c.G, c.B);
                case 2: return OtherLightEffect.Cycle(period, level);
                case 3: return OtherLightEffect.Breathe(c.R, c.G, c.B, period, level);
                default: return original[zone];
            }
        }

        private void Refresh2()
        {
            Changes.Clear();
            for (int i = 0; i < original.Count; i++)
            {
                int kind = kinds[i].SelectedIndex;
                colors[i].Enabled = kind == 1 || kind == 3;
                seconds[i].Enabled = kind == 2 || kind == 3;
                brightness[i].Enabled = kind == 2 || kind == 3;
                OtherLightEffect effect = Effect(i);
                if (!effect.Equals(original[i])) Changes[i] = effect;
            }
            if (save != null) save.Enabled = Changes.Count > 0;
        }
    }

    // Series drawings embedded by build.ps1 from assets\device-diagrams\windows.
    internal static class OtherDiagrams
    {
        private const string Prefix = "MXMasterWin.diagram.";
        private static Dictionary<string, string> models;
        private static readonly Dictionary<string, Image> images = new Dictionary<string, Image>();

        public static Image ForModel(string wpid)
        {
            if (models == null) models = LoadModels();
            string family;
            if (string.IsNullOrEmpty(wpid) || !models.TryGetValue(wpid, out family)) return null;
            Image image;
            if (images.TryGetValue(family, out image)) return image;
            try
            {
                using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + family + ".png"))
                {
                    if (stream != null)
                        using (Image source = Image.FromStream(stream))
                            image = new Bitmap(source);
                }
            }
            catch { image = null; }
            images[family] = image;
            return image;
        }

        private static Dictionary<string, string> LoadModels()
        {
            var table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + "models.txt"))
                {
                    if (stream == null) return table;
                    using (var reader = new StreamReader(stream))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            string[] parts = line.Split('=');
                            if (parts.Length == 2) table[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            }
            catch { }
            return table;
        }
    }

    // One device in the tab row above the card.
    internal sealed class OtherDeviceTab : Button
    {
        private readonly float scale = Theme.GetScale();
        private bool hover, selected, online;
        private string title = "", detail = "";
        public string DeviceId = "";

        public OtherDeviceTab()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            Cursor = Cursors.Hand;
        }

        public void Set(string id, string name, string state, bool isOnline, bool isSelected)
        {
            if (DeviceId == id && title == name && detail == state && online == isOnline && selected == isSelected) return;
            DeviceId = id;
            title = name;
            detail = state;
            online = isOnline;
            selected = isSelected;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = selected ? Theme.Accent : hover || (Focused && ShowFocusCues) ? Color.FromArgb(238, 244, 255) : Color.FromArgb(247, 249, 252);
            Color ink = selected ? Color.White : Theme.Text;
            Color soft = selected ? Color.FromArgb(222, 233, 255) : Theme.Muted;
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(selected ? Theme.Accent : hover ? Theme.Accent : Theme.CardBorder))
            {
                g.FillPath(brush, path);
                g.DrawPath(border, path);
            }
            float dot = 7F * scale, x = 9F * scale;
            using (var mark = new SolidBrush(online ? (selected ? Color.White : Theme.Good) : Color.FromArgb(170, 176, 188)))
                g.FillEllipse(mark, x, (Height - dot) / 2F, dot, dot);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int textLeft = (int)(21F * scale), right = Width - (int)(8F * scale);
            using (var small = new Font(Theme.UiFont, 8F))
            using (var bold = new Font(Theme.UiFont, 9F, FontStyle.Bold))
            {
                int detailWidth = TextRenderer.MeasureText(g, detail, small, Size.Empty, flags).Width;
                TextRenderer.DrawText(g, detail, small, new Rectangle(right - detailWidth, 0, detailWidth, Height), soft, fill, flags | TextFormatFlags.Right);
                TextRenderer.DrawText(g, title, bold, new Rectangle(textLeft, 0, Math.Max(0, right - detailWidth - textLeft - (int)(4F * scale)), Height), ink, fill,
                    flags | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            }
        }
    }

    // A summary tile: caption, main value and a small note. Clickable tiles open a menu or dialog.
    internal sealed class OtherStatTile : Button
    {
        private readonly float scale = Theme.GetScale();
        private readonly bool clickable;
        private bool hover;
        private string main = "", note = "";
        public string Caption = "";

        public OtherStatTile(bool clickable)
        {
            this.clickable = clickable;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            if (!clickable) SetStyle(ControlStyles.Selectable, false);
            FlatStyle = FlatStyle.Flat;
            TabStop = clickable;
            Cursor = clickable ? Cursors.Hand : Cursors.Default;
        }

        public void SetText(string mainText, string noteText)
        {
            if (main == mainText && note == noteText) return;
            main = mainText;
            note = noteText;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            bool active = clickable && Enabled && (hover || (Focused && ShowFocusCues));
            Color fill = active ? Color.FromArgb(238, 244, 255) : Color.FromArgb(247, 249, 252);
            using (GraphicsPath path = Theme.RoundedRectangle(new RectangleF(0.5F, 0.5F, Width - 1F, Height - 1F), 8F * scale))
            using (var brush = new SolidBrush(fill))
            using (var border = new Pen(active ? Theme.Accent : Theme.CardBorder))
            {
                g.FillPath(brush, path);
                g.DrawPath(border, path);
            }
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            int left = (int)(10F * scale), width = Width - (int)(18F * scale);
            Color ink = Enabled ? Theme.Text : Theme.Muted;
            using (var small = new Font(Theme.UiFont, 8F))
                TextRenderer.DrawText(g, Caption, small, new Rectangle(left, (int)(5F * scale), width, (int)(15F * scale)), Theme.Muted, fill, flags);
            using (var big = new Font(Theme.UiFont, 10.5F, FontStyle.Bold))
                TextRenderer.DrawText(g, main, big, new Rectangle(left, (int)(20F * scale), width, (int)(21F * scale)), ink, fill, flags);
            using (var tiny = new Font(Theme.UiFont, 7.5F))
                TextRenderer.DrawText(g, note, tiny, new Rectangle(left, (int)(41F * scale), width, (int)(14F * scale)), Theme.Muted, fill, flags);
            if (clickable)
            {
                float x = Width - 13F * scale, y = 12F * scale, s = 3.2F * scale;
                using (var pen = new Pen(Enabled ? Theme.Muted : Theme.CardBorder, 1.3F * scale))
                    g.DrawLines(pen, new[] { new PointF(x - s, y - s * 0.5F), new PointF(x, y + s * 0.5F), new PointF(x + s, y - s * 0.5F) });
            }
        }
    }

    // Original top-view sketch of a G502 with a numbered marker per button (numbers match the list).
    internal sealed class OtherMouseDiagram : Control
    {
        private const float DesignWidth = 200F, DesignHeight = 256F, BadgeRadius = 9.5F;

        private sealed class Spot
        {
            public int Row;
            public PointF Target, Badge;
        }

        // Rows follow OtherDevicesProtocol.ButtonName for 407F. Positions follow the numbered
        // drawing in the G502 LIGHTSPEED setup guide; the artwork itself is drawn here.
        private static readonly Spot[] Spots =
        {
            new Spot { Row = 0, Target = new PointF(80F, 56F), Badge = new PointF(13F, 30F) },
            new Spot { Row = 7, Target = new PointF(60.5F, 68F), Badge = new PointF(13F, 54F) },
            new Spot { Row = 10, Target = new PointF(99.5F, 76F), Badge = new PointF(13F, 78F) },
            new Spot { Row = 6, Target = new PointF(60F, 91F), Badge = new PointF(13F, 102F) },
            new Spot { Row = 5, Target = new PointF(52F, 103F), Badge = new PointF(13F, 126F) },
            new Spot { Row = 4, Target = new PointF(57F, 130F), Badge = new PointF(13F, 150F) },
            new Spot { Row = 3, Target = new PointF(62F, 158F), Badge = new PointF(13F, 174F) },
            new Spot { Row = 1, Target = new PointF(136F, 62F), Badge = new PointF(187F, 40F) },
            new Spot { Row = 9, Target = new PointF(117.5F, 76F), Badge = new PointF(187F, 74F) },
            new Spot { Row = 2, Target = new PointF(108.5F, 92F), Badge = new PointF(187F, 104F) },
            new Spot { Row = 8, Target = new PointF(110.5F, 130F), Badge = new PointF(187F, 134F) }
        };

        private int highlight = -1;
        public event Action<int> HotspotClicked;
        public event Action<int> HighlightChanged;

        public OtherMouseDiagram()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public int Highlight
        {
            get { return highlight; }
            set
            {
                if (value == highlight) return;
                highlight = value;
                Invalidate();
                Action<int> handler = HighlightChanged;
                if (handler != null) handler(value);
            }
        }

        private float Fit { get { return Math.Min(Width / DesignWidth, Height / DesignHeight); } }

        private Spot SpotAt(Point location)
        {
            float fit = Fit;
            float x = (location.X - (Width - DesignWidth * fit) / 2F) / fit;
            float y = (location.Y - (Height - DesignHeight * fit) / 2F) / fit;
            float reach = (BadgeRadius + 2F) * (BadgeRadius + 2F);
            foreach (Spot spot in Spots)
            {
                float dx = x - spot.Badge.X, dy = y - spot.Badge.Y;
                if (dx * dx + dy * dy <= reach) return spot;
            }
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Spot spot = SpotAt(e.Location);
            Cursor = spot != null ? Cursors.Hand : Cursors.Default;
            Highlight = spot != null ? spot.Row : -1;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            Highlight = -1;
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            Spot spot = e.Button == MouseButtons.Left ? SpotAt(e.Location) : null;
            Action<int> handler = HotspotClicked;
            if (spot != null && handler != null) handler(spot.Row);
            base.OnMouseClick(e);
        }

        private static PointF[] Points(params float[] xy)
        {
            var result = new PointF[xy.Length / 2];
            for (int i = 0; i < result.Length; i++) result[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            return result;
        }

        private static GraphicsPath Outline()
        {
            var p = new GraphicsPath();
            p.StartFigure();
            p.AddLine(97F, 30F, 96F, 47F);
            p.AddLine(96F, 47F, 118F, 47F);
            p.AddLine(118F, 47F, 115F, 24F);
            p.AddBezier(115F, 24F, 128F, 32F, 140F, 38F, 146F, 44F);
            p.AddBezier(146F, 44F, 152F, 52F, 155F, 72F, 158F, 94F);
            p.AddBezier(158F, 94F, 159F, 112F, 158F, 128F, 159F, 140F);
            p.AddBezier(159F, 140F, 160F, 155F, 163F, 170F, 163.5F, 184F);
            p.AddBezier(163.5F, 184F, 164F, 200F, 152F, 224F, 137F, 237F);
            p.AddBezier(137F, 237F, 130F, 243F, 122F, 246F, 116F, 247.4F);
            p.AddLine(116F, 247.4F, 108.5F, 245F);
            p.AddLine(108.5F, 245F, 101.3F, 248.7F);
            p.AddBezier(101.3F, 248.7F, 92F, 247F, 81F, 241F, 74F, 235F);
            p.AddLine(74F, 235F, 38.3F, 181.5F);
            p.AddBezier(38.3F, 181.5F, 35F, 178F, 34F, 176F, 34F, 172F);
            p.AddBezier(34F, 172F, 33F, 158F, 34F, 146F, 36.2F, 136F);
            p.AddBezier(36.2F, 136F, 38F, 130F, 44F, 126F, 48.8F, 121.4F);
            p.AddLine(48.8F, 121.4F, 50.5F, 111F);
            p.AddLine(50.5F, 111F, 47.8F, 104F);
            p.AddLine(47.8F, 104F, 50.9F, 88F);
            p.AddLine(50.9F, 88F, 65.6F, 48F);
            p.AddLine(65.6F, 48F, 97F, 30F);
            p.CloseFigure();
            return p;
        }

        // A main button: soft gradient, tinted while its row is highlighted.
        private void MainPlate(Graphics g, PointF[] points, int row)
        {
            using (var fill = new LinearGradientBrush(new RectangleF(45F, 20F, 115F, 125F),
                Color.FromArgb(92, 97, 108), Color.FromArgb(58, 62, 71), 80F))
                g.FillPolygon(fill, points);
            if (row == highlight)
                using (var tint = new SolidBrush(Color.FromArgb(165, Theme.Accent)))
                    g.FillPolygon(tint, points);
            using (var edge = new Pen(Color.FromArgb(24, 26, 32), 0.9F) { LineJoin = LineJoin.Round })
                g.DrawPolygon(edge, points);
        }

        private void SidePlate(Graphics g, PointF[] points, int row)
        {
            using (var fill = new SolidBrush(row == highlight ? Theme.Accent : Color.FromArgb(120, 126, 138)))
                g.FillPolygon(fill, points);
            using (var edge = new Pen(Color.FromArgb(24, 26, 32), 0.7F) { LineJoin = LineJoin.Round })
                g.DrawPolygon(edge, points);
        }

        private void Key(Graphics g, RectangleF r, float radius, int row, Color normal)
        {
            using (GraphicsPath path = Theme.RoundedRectangle(r, radius))
            using (var brush = new SolidBrush(row >= 0 && row == highlight ? Theme.Accent : normal))
            using (var edge = new Pen(Color.FromArgb(138, 145, 158), 0.7F))
            {
                g.FillPath(brush, path);
                g.DrawPath(edge, path);
            }
        }

        private void Arrow(Graphics g, PointF[] points, int row)
        {
            using (var brush = new SolidBrush(row == highlight ? Theme.Accent : Color.FromArgb(176, 182, 194)))
                g.FillPolygon(brush, points);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Card);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            float fit = Fit;
            g.TranslateTransform((Width - DesignWidth * fit) / 2F, (Height - DesignHeight * fit) / 2F);
            g.ScaleTransform(fit, fit);

            using (GraphicsPath body = Outline())
            {
                using (var shadowPath = (GraphicsPath)body.Clone())
                using (var shadow = new SolidBrush(Color.FromArgb(38, 20, 26, 38)))
                using (var move = new Matrix())
                {
                    move.Translate(1F, 3.5F);
                    shadowPath.Transform(move);
                    g.FillPath(shadow, shadowPath);
                }
                using (var fill = new LinearGradientBrush(new RectangleF(30F, 20F, 136F, 232F),
                    Color.FromArgb(64, 68, 78), Color.FromArgb(36, 38, 45), 72F))
                    g.FillPath(fill, body);

                // Palm: a lighter panel on the thumb side, split by a curved seam.
                GraphicsState state = g.Save();
                g.SetClip(body, CombineMode.Intersect);
                using (var palm = new GraphicsPath())
                {
                    palm.AddBezier(61F, 178F, 72F, 196F, 92F, 200F, 106F, 207F);
                    palm.AddBezier(106F, 207F, 108F, 222F, 105F, 238F, 102F, 250F);
                    palm.AddLine(102F, 250F, 60F, 250F);
                    palm.AddLine(60F, 250F, 28F, 180F);
                    palm.CloseFigure();
                    using (var light = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                        g.FillPath(light, palm);
                }
                g.Restore(state);
                using (var seam = new Pen(Color.FromArgb(22, 24, 30), 1F))
                {
                    g.DrawBezier(seam, 61F, 178F, 72F, 196F, 92F, 200F, 106F, 207F);
                    g.DrawBezier(seam, 106F, 207F, 108F, 222F, 105F, 238F, 102F, 248F);
                    g.DrawBezier(seam, 150F, 141F, 148F, 154F, 148F, 168F, 158F, 186F);
                }

                // Main buttons and the channel between them.
                MainPlate(g, Points(97F, 31F, 96.6F, 47.5F, 101.3F, 137F, 72F, 150F, 65.8F, 86F, 66.3F, 50.5F), 0);
                MainPlate(g, Points(115.5F, 26F, 118.3F, 47.5F, 119.6F, 140F, 149.6F, 139.5F, 155.5F, 95F, 151F, 62F, 144.5F, 46F), 1);
                using (var channel = new SolidBrush(Color.FromArgb(27, 29, 35)))
                    g.FillPolygon(channel, Points(96.8F, 47.5F, 118F, 47.5F, 119.4F, 140F, 101.5F, 137.5F));

                // Side buttons along the left edge, front to back: G8, G7, sniper, forward, back.
                SidePlate(g, Points(65.4F, 50F, 65.4F, 85F, 52.5F, 87F), 7);
                SidePlate(g, Points(52F, 89F, 65.6F, 87F, 67F, 94F, 54F, 97F), 6);
                SidePlate(g, Points(49.2F, 99F, 54F, 98.4F, 55F, 108F, 51F, 110F), 5);
                SidePlate(g, Points(51.6F, 112F, 57F, 111.5F, 63.5F, 146F, 56.5F, 147F), 4);
                SidePlate(g, Points(57F, 149.5F, 64F, 148.5F, 67F, 165F, 61F, 167.5F), 3);

                using (var edge = new Pen(Color.FromArgb(112, 118, 130), 1F) { LineJoin = LineJoin.Round })
                    g.DrawPath(edge, body);
            }

            // Wheel, tilt directions, the fixed wheel-mode key and G9 behind it.
            using (GraphicsPath slot = Theme.RoundedRectangle(new RectangleF(101.5F, 61F, 14F, 43F), 5F))
            using (var dark = new SolidBrush(Color.FromArgb(14, 15, 19)))
                g.FillPath(dark, slot);
            Key(g, new RectangleF(103F, 63.5F, 11F, 38F), 4.5F, 2, Color.FromArgb(166, 172, 182));
            using (var ticks = new Pen(Color.FromArgb(highlight == 2 ? 160 : 120, 34, 37, 44), 0.9F))
                for (float y = 67F; y < 99F; y += 3.6F) g.DrawLine(ticks, 104.5F, y, 112.5F, y);
            Arrow(g, Points(100.8F, 71.5F, 100.8F, 80.5F, 98F, 76F), 10);
            Arrow(g, Points(116.2F, 71.5F, 116.2F, 80.5F, 118.8F, 76F), 9);
            Key(g, new RectangleF(103.6F, 107F, 13F, 11F), 2.5F, -1, Color.FromArgb(48, 51, 59));
            Key(g, new RectangleF(104F, 122.5F, 13F, 14.5F), 2.5F, 8, Color.FromArgb(92, 97, 108));

            using (var numberFont = new Font(Theme.UiFont, 10F, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                foreach (Spot spot in Spots)
                {
                    bool on = spot.Row == highlight;
                    using (var line = new Pen(on ? Theme.Accent : Color.FromArgb(150, Theme.Accent), on ? 1.4F : 0.8F))
                        g.DrawLine(line, spot.Badge, spot.Target);
                    using (var dot = new SolidBrush(on ? Theme.Accent : Color.FromArgb(120, 175, 255)))
                        g.FillEllipse(dot, spot.Target.X - 1.7F, spot.Target.Y - 1.7F, 3.4F, 3.4F);
                    var circle = new RectangleF(spot.Badge.X - BadgeRadius, spot.Badge.Y - BadgeRadius, BadgeRadius * 2F, BadgeRadius * 2F);
                    using (var face = new SolidBrush(on ? Theme.Accent : Color.White))
                    using (var ring = new Pen(Theme.Accent, 1.2F))
                    using (var ink = new SolidBrush(on ? Color.White : Theme.Accent))
                    {
                        g.FillEllipse(face, circle);
                        g.DrawEllipse(ring, circle);
                        g.DrawString((spot.Row + 1).ToString(), numberFont, ink, circle, center);
                    }
                }
            }
        }
    }

    internal sealed class OtherDeviceCard : Panel
    {
        private const int Width0 = 716;
        private readonly OtherDevicesManager manager;
        private readonly Func<int, int> S;
        private readonly Label name = new Label(), subtitle = new Label(), status = new Label(), offline = new Label();
        private readonly Label repairText = new Label(), buttonsHint = new Label(), message = new Label(), appWarning = new Label();
        private readonly Button repair, restore;
        private readonly OtherStatTile batteryTile, dpiButton, rateButton, profileButton, lightButton;
        private readonly OtherMouseDiagram diagram = new OtherMouseDiagram();
        private readonly PictureBox familyPicture = new PictureBox { SizeMode = PictureBoxSizeMode.Zoom, Visible = false };
        private readonly Label pictureNote = new Label { Visible = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleCenter };
        private readonly Label basicHint = new Label { Visible = false, UseMnemonic = false };
        private readonly Panel chips = new Panel(), rule1 = new Panel();
        private readonly Label buttonsTitle = new Label();
        private readonly Label[] rowNames = new Label[12];
        private readonly ComboBox[] rowCombos = new ComboBox[12];
        private readonly List<OtherChoice> presets = OtherText.Presets();
        private OtherDeviceSnapshot device;
        private bool busy;
        private bool updating;
        private bool lastHasBackup;
        private readonly Func<string, int, ActionBinding> getApp;
        private readonly Action<string, int, ActionBinding> setApp;
        private readonly ActionPicker[] rowPickers = new ActionPicker[12];
        private readonly string[] pickerText = new string[12];

        public OtherDeviceCard(OtherDevicesManager manager, Func<int, int> scale,
            Func<string, int, ActionBinding> getApp, Action<string, int, ActionBinding> setApp)
        {
            this.manager = manager;
            this.getApp = getApp;
            this.setApp = setApp;
            S = scale;
            BackColor = Theme.Card;
            Width = S(Width0);
            name.AutoEllipsis = true;
            name.UseMnemonic = false;
            name.Font = new Font(Theme.UiFont, 11F, FontStyle.Bold);
            name.Bounds = new Rectangle(S(4), 0, S(520), S(24));
            subtitle.AutoEllipsis = true;
            subtitle.UseMnemonic = false;
            subtitle.ForeColor = Theme.Muted;
            subtitle.Font = new Font(Theme.UiFont, 8.5F);
            subtitle.Bounds = new Rectangle(S(4), S(25), S(Width0 - 8), S(18));
            status.TextAlign = ContentAlignment.MiddleRight;
            status.Font = new Font(Theme.UiFont, 9F, FontStyle.Bold);
            status.Bounds = new Rectangle(S(Width0 - 180), 0, S(176), S(24));
            Controls.AddRange(new Control[] { name, subtitle, status });

            offline.ForeColor = Theme.Muted;
            offline.UseMnemonic = false;
            offline.Bounds = new Rectangle(S(4), S(52), S(Width0 - 12), S(62));
            Controls.Add(offline);

            repairText.ForeColor = Theme.Warning;
            repairText.AutoEllipsis = true;
            repairText.UseMnemonic = false;
            repairText.TextAlign = ContentAlignment.MiddleLeft;
            repairText.Bounds = new Rectangle(S(4), S(50), S(Width0 - 150), S(30));
            Controls.Add(repairText);
            repair = Theme.CreateButton(Lang.T("修復設定檔", "Repair profile"), true);
            repair.Bounds = new Rectangle(S(Width0 - 134), S(50), S(130), S(30));
            repair.Click += delegate
            {
                if (device != null) Run(c => manager.RepairProfile(device.Id, c));
            };
            Controls.Add(repair);

            chips.BackColor = Theme.Card;
            batteryTile = Tile(Lang.T("電量", "Battery"), null);
            dpiButton = Tile(Lang.T("指標速度", "Pointer speed"), delegate { ShowDpiMenu(); });
            rateButton = Tile(Lang.T("回報率", "Report rate"), delegate { ShowRateMenu(); });
            profileButton = Tile(Lang.T("設定檔", "Profile"), delegate { ShowProfileMenu(); });
            lightButton = Tile(Lang.T("燈光", "Lighting"), delegate { EditLights(); });
            Controls.Add(chips);
            rule1.BackColor = Theme.Divider;
            Controls.Add(rule1);

            diagram.HighlightChanged += row =>
            {
                for (int i = 0; i < rowNames.Length; i++) rowNames[i].ForeColor = i == row ? Theme.Accent : Theme.Text;
            };
            diagram.HotspotClicked += row =>
            {
                if (row >= rowCombos.Length) return;
                ComboBox target = rowPickers[row] != null && rowPickers[row].Combo.Visible ? rowPickers[row].Combo : rowCombos[row];
                if (!target.Visible || !target.Enabled) return;
                target.Focus();
                target.DroppedDown = true;
            };
            Controls.Add(diagram);
            familyPicture.BackColor = Theme.Card;
            Controls.Add(familyPicture);
            pictureNote.ForeColor = Theme.Muted;
            pictureNote.Font = new Font(Theme.UiFont, 7.5F);
            pictureNote.Text = Lang.T("示意圖，尚未對應右側清單", "Illustration; not linked to the list");
            Controls.Add(pictureNote);
            basicHint.ForeColor = Theme.Muted;
            basicHint.Text = Lang.T("這個裝置沒有內建設定檔，目前可以查看電量和連線狀態。", "This device has no onboard profiles. Its battery and connection status are shown here.");
            Controls.Add(basicHint);

            buttonsTitle.Text = Lang.T("按鍵", "Buttons");
            buttonsTitle.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            buttonsTitle.AutoSize = true;
            Controls.Add(buttonsTitle);
            buttonsHint.ForeColor = Theme.Muted;
            buttonsHint.AutoEllipsis = true;
            buttonsHint.UseMnemonic = false;
            buttonsHint.Font = new Font(Theme.UiFont, 8.5F);
            Controls.Add(buttonsHint);
            for (int i = 0; i < rowCombos.Length; i++)
            {
                int index = i;
                rowNames[i] = new Label { AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft, Font = new Font(Theme.UiFont, 9F), Visible = false };
                var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font(Theme.UiFont, 9F), Visible = false, DropDownWidth = S(270), MaxDropDownItems = 18 };
                combo.SelectionChangeCommitted += delegate { ButtonPicked(index); };
                combo.MouseEnter += delegate { diagram.Highlight = index; };
                combo.MouseLeave += delegate { diagram.Highlight = -1; };
                rowNames[i].MouseEnter += delegate { diagram.Highlight = index; };
                rowNames[i].MouseLeave += delegate { diagram.Highlight = -1; };
                rowCombos[i] = combo;
                Controls.Add(rowNames[i]);
                Controls.Add(combo);
            }
            message.ForeColor = Theme.Muted;
            message.AutoEllipsis = true;
            message.UseMnemonic = false;
            message.TextAlign = ContentAlignment.MiddleLeft;
            message.Font = new Font(Theme.UiFont, 8.5F);
            Controls.Add(message);
            restore = Theme.CreateButton(Lang.T("還原原本的設定檔", "Restore original profile"), false);
            restore.Click += delegate
            {
                if (device == null) return;
                string sectorName = device.ProfileNumber.HasValue ? device.ProfileNumber.Value.ToString() : device.ActiveProfile.ToString();
                if (MessageBox.Show(this, Lang.T("把設定檔 " + sectorName + " 改回第一次修改前的內容？按鍵、DPI、回報率與燈光都會還原。", "Put profile " + sectorName + " back to how it was before the first change? Buttons, DPI, report rate and lighting are all restored."),
                    "MX Master Win", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
                int sector = device.ActiveProfile;
                string id = device.Id;
                Run(c => manager.WriteButtons(id, sector, new Dictionary<int, OtherButtonAssignment>(), true, c));
            };
            Controls.Add(restore);
            appWarning.ForeColor = Theme.Warning;
            appWarning.AutoEllipsis = true;
            appWarning.UseMnemonic = false;
            appWarning.TextAlign = ContentAlignment.MiddleLeft;
            appWarning.Font = new Font(Theme.UiFont, 8.5F);
            Controls.Add(appWarning);
        }

        private OtherStatTile Tile(string caption, EventHandler click)
        {
            var tile = new OtherStatTile(click != null) { Caption = caption };
            if (click != null) tile.Click += click;
            chips.Controls.Add(tile);
            return tile;
        }

        // The write result takes the place of the hint next to the "Buttons" title.
        private void ShowMessage(string text)
        {
            message.Text = text;
            bool hasButtons = device != null && device.Link == OtherDeviceLink.Online && device.Buttons.Count > 0;
            message.Visible = hasButtons && text.Length > 0;
            buttonsHint.Visible = hasButtons && text.Length == 0;
        }

        private void Run(Action<Action<string>> start)
        {
            busy = true;
            ShowMessage(Lang.T("正在寫入滑鼠…", "Saving to the mouse…"));
            ApplyEnabled();
            start(result =>
            {
                busy = false;
                ShowMessage(result ?? Lang.T("已寫入滑鼠", "Saved to the mouse"));
                ApplyEnabled();
                if (device != null) UpdateButtons();
            });
        }

        private void ApplyEnabled()
        {
            bool editable = device != null && device.CanEditButtons && !busy;
            bool online = device != null && device.Link == OtherDeviceLink.Online && !busy;
            dpiButton.Enabled = online && device.CanSwitchStage;
            rateButton.Enabled = editable;
            profileButton.Enabled = online && device.OnboardMode == 1 && device.Profiles.Count > 1;
            lightButton.Enabled = editable && device.Lights.Count > 0;
            repair.Enabled = online;
            restore.Enabled = editable;
            for (int i = 1; i < rowCombos.Length; i++) rowCombos[i].Enabled = editable;
        }

        private void ShowDpiMenu()
        {
            if (device == null) return;
            OtherDeviceSnapshot d = device;
            var menu = new ContextMenuStrip();
            for (int i = 0; i < d.DpiStages.Count; i++)
            {
                if (d.DpiStages[i] <= 0) continue;
                int index = i;
                var item = new ToolStripMenuItem(d.DpiStages[i] + " DPI") { Checked = d.StageIndex == i };
                item.Click += delegate { manager.SetStage(d.Id, index); };
                menu.Items.Add(item);
            }
            if (d.CanEditButtons)
            {
                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(Lang.T("編輯各段…", "Edit levels…"), null, delegate { EditStages(); });
            }
            menu.Show(dpiButton, new Point(0, dpiButton.Height));
        }

        private void EditStages()
        {
            if (device == null) return;
            OtherDeviceSnapshot d = device;
            using (var dialog = new OtherStageDialog(d))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
                var stages = new List<int>(dialog.Stages);
                int defaultIndex = dialog.DefaultIndex;
                Run(c => manager.WriteStages(d.Id, d.ActiveProfile, stages, defaultIndex, c));
            }
        }

        private void ShowRateMenu()
        {
            if (device == null) return;
            OtherDeviceSnapshot d = device;
            var menu = new ContextMenuStrip();
            foreach (int rate in OtherDevicesProtocol.ReportRates)
            {
                int ms = rate;
                var item = new ToolStripMenuItem(OtherDevicesProtocol.ReportRateHz(ms) + " Hz") { Checked = d.ReportRateMs == ms };
                item.Click += delegate { Run(c => manager.WriteReportRate(d.Id, d.ActiveProfile, ms, c)); };
                menu.Items.Add(item);
            }
            menu.Show(rateButton, new Point(0, rateButton.Height));
        }

        private void ShowProfileMenu()
        {
            if (device == null) return;
            OtherDeviceSnapshot d = device;
            var menu = new ContextMenuStrip();
            for (int i = 0; i < d.Profiles.Count; i++)
            {
                int sector = d.Profiles[i];
                var item = new ToolStripMenuItem(Lang.T("設定檔 ", "Profile ") + (i + 1)) { Checked = d.ActiveProfile == sector };
                item.Click += delegate { manager.SetProfile(d.Id, sector); };
                menu.Items.Add(item);
            }
            menu.Show(profileButton, new Point(0, profileButton.Height));
        }

        private void EditLights()
        {
            if (device == null) return;
            OtherDeviceSnapshot d = device;
            using (var dialog = new OtherLightDialog(d.Lights))
            {
                if (dialog.ShowDialog(FindForm()) != DialogResult.OK || dialog.Changes.Count == 0) return;
                var changes = new Dictionary<int, OtherLightEffect>(dialog.Changes);
                Run(c => manager.WriteLights(d.Id, d.ActiveProfile, changes, c));
            }
        }

        private void ButtonPicked(int index)
        {
            if (updating || device == null) return;
            var choice = rowCombos[index].SelectedItem as OtherChoice;
            if (choice == null || index >= device.Buttons.Count || choice.Value.Equals(device.Buttons[index])) return;
            OtherDeviceSnapshot d = device;
            var changes = new Dictionary<int, OtherButtonAssignment> { { index, choice.Value } };
            Run(c => manager.WriteButtons(d.Id, d.ActiveProfile, changes, false, c));
        }

        // A button handed to the app picks an app action (kept in the INI) instead of a mouse function.
        private void UpdatePicker(int i, bool appRow)
        {
            ActionPicker picker = rowPickers[i];
            if (!appRow)
            {
                if (picker != null) picker.SetShown(false);
                return;
            }
            if (picker == null)
            {
                Rectangle row = rowCombos[i].Bounds;
                int editWidth = S(42), gap = S(4), index = i;
                picker = new ActionPicker(this, row.Location, row.Width - editWidth - gap, editWidth, gap);
                picker.Combo.Font = rowCombos[i].Font;
                picker.Combo.DropDownWidth = S(270);
                picker.UseChoices(ActionCatalog.ForOtherDevice());
                picker.Changed += delegate { AppPicked(index); };
                picker.Combo.MouseEnter += delegate { diagram.Highlight = index; };
                picker.Combo.MouseLeave += delegate { diagram.Highlight = -1; };
                rowPickers[i] = picker;
            }
            picker.Place(rowCombos[i].Bounds);
            picker.SetShown(true);
            ActionBinding stored = getApp(device.Wpid, i);
            if (pickerText[i] != stored.ToString() && !picker.Combo.DroppedDown)
            {
                picker.Binding = stored;
                pickerText[i] = stored.ToString();
            }
        }

        private void AppPicked(int index)
        {
            ActionPicker picker = rowPickers[index];
            if (device == null || picker == null) return;
            string wpid = device.Wpid;
            ActionBinding chosen = picker.Binding;
            if (chosen.Action == MappedAction.AppMouseFunction)
            {
                // Let the drop-down close first, then put the stored choice back and offer the mouse functions.
                picker.Combo.BeginInvoke(new Action(delegate
                {
                    picker.Binding = getApp(wpid, index);
                    ShowMouseFunctionMenu(index);
                }));
                return;
            }
            setApp(wpid, index, chosen);
            pickerText[index] = getApp(wpid, index).ToString();
            Show(device, lastHasBackup);
        }

        private void ShowMouseFunctionMenu(int index)
        {
            OtherDeviceSnapshot d = device;
            if (d == null || !d.CanEditButtons || busy) return;
            var menu = new ContextMenuStrip();
            menu.Items.Add(new ToolStripMenuItem(Lang.T("會寫進滑鼠設定檔，Mac 也會跟著變", "Saved in the mouse, so the Mac changes too")) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            foreach (OtherChoice choice in presets)
            {
                OtherChoice picked = choice;
                menu.Items.Add(choice.Title, null, delegate
                {
                    var changes = new Dictionary<int, OtherButtonAssignment> { { index, picked.Value } };
                    Run(c => manager.WriteButtons(d.Id, d.ActiveProfile, changes, false, c));
                });
            }
            menu.Show(rowPickers[index].Combo, new Point(0, rowPickers[index].Combo.Height));
        }

        private void UpdateButtons()
        {
            OtherDeviceSnapshot d = device;
            updating = true;
            for (int i = 0; i < rowCombos.Length; i++)
            {
                bool shown = d.Link == OtherDeviceLink.Online && i < d.Buttons.Count;
                bool appRow = shown && d.Wpid == "407F" && d.Buttons[i].AppButton.HasValue;
                rowNames[i].Visible = shown;
                rowCombos[i].Visible = shown && !appRow;
                UpdatePicker(i, appRow);
                if (!shown || rowCombos[i].DroppedDown) continue;
                string[] label = OtherDevicesProtocol.ButtonName(d.Wpid, i);
                rowNames[i].Text = (i + 1) + "  " + Lang.T(label[0], label[1]);
                ComboBox combo = rowCombos[i];
                OtherButtonAssignment value = d.Buttons[i];
                combo.BeginUpdate();
                combo.Items.Clear();
                OtherChoice selected = null;
                if (i == 0)
                {
                    // The left button stays a left click, so a wrong choice can never lock the user out.
                    selected = new OtherChoice { Title = OtherText.Title(value, presets) + Lang.T("（固定）", " (locked)"), Value = value };
                    combo.Items.Add(selected);
                }
                else
                {
                    foreach (OtherChoice choice in presets)
                    {
                        combo.Items.Add(choice);
                        if (choice.Value.Equals(value)) selected = choice;
                    }
                    if (selected == null)
                    {
                        selected = new OtherChoice { Title = OtherText.Title(value, presets), Value = value };
                        combo.Items.Insert(0, selected);
                    }
                }
                combo.SelectedItem = selected;
                combo.EndUpdate();
                combo.Enabled = i != 0 && d.CanEditButtons && !busy;
            }
            updating = false;
        }

        public void Show(OtherDeviceSnapshot d, bool hasBackup)
        {
            device = d;
            lastHasBackup = hasBackup;
            name.Text = d.Name.Length == 0 ? Lang.T("羅技裝置", "Logitech device") : d.Name;
            subtitle.Text = OtherText.Subtitle(d);
            status.Text = "●  " + OtherText.Link(d);
            status.ForeColor = d.Link == OtherDeviceLink.Online ? Theme.Good : d.Link == OtherDeviceLink.ReceiverGone ? Theme.Warning : d.Link == OtherDeviceLink.Reading ? Theme.Accent : Theme.Muted;
            bool online = d.Link == OtherDeviceLink.Online;
            bool hasButtons = online && d.Buttons.Count > 0;
            bool hasDiagram = hasButtons && d.Wpid == "407F";
            Image series = online && d.Wpid != "407F" ? OtherDiagrams.ForModel(d.Wpid) : null;
            bool hasPicture = series != null;
            familyPicture.Visible = hasPicture;
            pictureNote.Visible = hasPicture && hasButtons;
            basicHint.Visible = online && !hasButtons;
            if (hasPicture && !ReferenceEquals(familyPicture.Image, series)) familyPicture.Image = series;
            offline.Visible = !online;
            chips.Visible = online;
            rule1.Visible = hasButtons;
            buttonsTitle.Visible = hasButtons;
            diagram.Visible = hasDiagram;
            message.Visible = hasButtons && message.Text.Length > 0;
            buttonsHint.Visible = hasButtons && message.Text.Length == 0;
            restore.Visible = hasButtons && hasBackup;
            appWarning.Visible = false;
            bool needsRepair = online && d.RepairSector.HasValue;
            repairText.Visible = needsRepair;
            repair.Visible = needsRepair;
            if (!online)
            {
                offline.Text = OtherText.Offline(d) + (d.BatteryPercent >= 0 ? "\r\n" + Lang.T("上次讀到的電量：", "Last battery reading: ") + OtherText.Battery(d) : "");
                UpdateButtons();
                Height = S(122);
                return;
            }
            int y = 50;
            if (needsRepair)
            {
                int index = d.Profiles.IndexOf(d.RepairSector.Value);
                string number = (index >= 0 ? index + 1 : d.RepairSector.Value).ToString();
                repairText.Text = Lang.T("設定檔 " + number + " 上次寫入沒有完成，內容可能不完整", "Profile " + number + " didn't finish saving and may be incomplete");
                y += 36;
            }

            // Summary tiles share the full width.
            chips.Bounds = new Rectangle(S(4), S(y), S(Width0 - 8), S(60));
            batteryTile.SetText(OtherText.BatteryValue(d), OtherText.BatteryNote(d));
            dpiButton.SetText(d.Dpi > 0 ? d.Dpi + " DPI" : "—",
                d.StageIndex >= 0 ? Lang.T("第 " + (d.StageIndex + 1) + " 段", "Level " + (d.StageIndex + 1)) : "");
            rateButton.SetText(d.ReportRateMs > 0 ? OtherDevicesProtocol.ReportRateHz(d.ReportRateMs) + " Hz" : "—",
                d.ReportRateMs > 0 ? Lang.T("每 " + d.ReportRateMs + " 毫秒", "Every " + d.ReportRateMs + " ms") : "");
            dpiButton.Visible = d.Dpi > 0;
            rateButton.Visible = d.ReportRateMs > 0;
            var tiles = new List<OtherStatTile> { batteryTile };
            if (d.Dpi > 0) tiles.Add(dpiButton);
            if (d.ReportRateMs > 0) tiles.Add(rateButton);
            bool showProfile = d.OnboardMode == 1 && d.Profiles.Count > 1;
            profileButton.Visible = showProfile;
            if (showProfile)
            {
                profileButton.SetText(d.ProfileNumber.HasValue ? d.ProfileNumber.Value + " / " + d.Profiles.Count : "—", Lang.T("存在滑鼠內", "On the mouse"));
                tiles.Add(profileButton);
            }
            bool showLight = d.Lights.Count > 0 && d.OnboardMode == 1;
            lightButton.Visible = showLight;
            if (showLight)
            {
                string first = OtherText.Light(d.Lights[0]);
                bool sameLight = true;
                foreach (OtherLightEffect light in d.Lights) if (OtherText.Light(light) != first) sameLight = false;
                lightButton.SetText(sameLight ? first : Lang.T("多種效果", "Mixed"), Lang.T("共 " + d.Lights.Count + " 區", d.Lights.Count + " zones"));
                tiles.Add(lightButton);
            }
            int gap = 8, each = Math.Min(172, (Width0 - 8 - gap * (tiles.Count - 1)) / tiles.Count);
            for (int t = 0; t < tiles.Count; t++)
                tiles[t].Bounds = new Rectangle(S(t * (each + gap)), 0, S(each), S(60));
            y += 68;

            int bottom = y;
            int artTop = y + 6;
            if (hasButtons)
            {
                rule1.Bounds = new Rectangle(S(4), S(y), S(Width0 - 8), 1);
                y += 6;
                int left = hasDiagram || hasPicture ? 206 : 4;
                int area = Width0 - 4 - left;
                if (hasDiagram)
                {
                    diagram.Bounds = new Rectangle(S(4), S(y), S(194), S(236));
                    bottom = y + 236;
                }
                buttonsTitle.Location = new Point(S(left), S(y + 4));
                // The title is wider in some languages, so the hint starts after its real width.
                int titleWidth = (int)Math.Ceiling(buttonsTitle.PreferredWidth * 100F / S(100));
                int hintLeft = left + titleWidth + 8;
                int hintRight = Width0 - 4 - (hasBackup ? 164 : 0);
                buttonsHint.Bounds = new Rectangle(S(hintLeft), S(y + 5), S(hintRight - hintLeft), S(18));
                message.Bounds = buttonsHint.Bounds;
                buttonsHint.Text = d.CanEditButtons
                    ? Lang.T("存在滑鼠裡，App 沒開也有效；寫入前會備份", "Saved in the mouse; works without the app; backed up first")
                    : d.WriteVerified ? Lang.T("這個設定檔不能改", "This profile can't be changed")
                    : Lang.T("這個型號還沒實機驗證，先不開放修改", "This model hasn't been verified yet, so editing is off");
                restore.Bounds = new Rectangle(S(Width0 - 4 - 156), S(y), S(156), S(26));
                y += 30;

                int rows = (d.Buttons.Count + 1) / 2;
                int columnWidth = (area - 12) / 2;
                var appKeys = new List<string>();
                for (int i = 0; i < d.Buttons.Count && i < rowCombos.Length; i++)
                {
                    int column = i < rows ? 0 : 1;
                    int row = i < rows ? i : i - rows;
                    int x = left + column * (columnWidth + 12);
                    rowNames[i].Bounds = new Rectangle(S(x), S(y + row * 30), S(112), S(26));
                    rowCombos[i].Bounds = new Rectangle(S(x + 114), S(y + row * 30), S(columnWidth - 114), S(26));
                    if (d.Buttons[i].AppButton.HasValue && (d.Wpid != "407F" || getApp(d.Wpid, i).IsPassThrough))
                    {
                        string[] label = OtherDevicesProtocol.ButtonName(d.Wpid, i);
                        appKeys.Add(Lang.T(label[0], label[1]));
                    }
                }
                y += rows * 30;
                if (appKeys.Count > 0)
                {
                    appWarning.Text = d.Wpid == "407F"
                        ? Lang.T(string.Join("、", appKeys.ToArray()) + " 還沒選 App 動作，目前按了沒有作用",
                            string.Join(", ", appKeys.ToArray()) + " have no app action yet, so pressing them does nothing")
                        : Lang.T(string.Join("、", appKeys.ToArray()) + " 設成「交給 App」，Windows 收不到，按了沒有作用",
                            string.Join(", ", appKeys.ToArray()) + " are set as app buttons, which Windows can't receive");
                    appWarning.Bounds = new Rectangle(S(left), S(y), S(area), S(22));
                    appWarning.Visible = true;
                    y += 24;
                }
                bottom = Math.Max(bottom, y);
            }
            if (hasPicture)
            {
                familyPicture.Bounds = new Rectangle(S(4), S(artTop), S(194), S(214));
                pictureNote.Bounds = new Rectangle(S(4), S(artTop + 216), S(194), S(18));
                bottom = Math.Max(bottom, artTop + 236);
            }
            if (!hasButtons)
            {
                int hintLeft = hasPicture ? 216 : 4;
                basicHint.Bounds = new Rectangle(S(hintLeft), S(artTop + 8), S(Width0 - hintLeft - 8), S(48));
                bottom = Math.Max(bottom, artTop + 60);
            }
            UpdateButtons();
            ApplyEnabled();
            Height = S(bottom + 2);
        }
    }

    // One device fills the page. With several, a tab row picks which one the card below shows.
    internal sealed class OtherDevicesPage : Panel
    {
        private const int TabsWidth = 628, TabGap = 6, ArrowWidth = 26;
        private readonly OtherDevicesManager manager;
        private readonly Func<int, int> S;
        private readonly Func<OtherDeviceSnapshot, bool> hasBackup;
        private readonly Func<string, int, ActionBinding> getApp;
        private readonly Action<string, int, ActionBinding> setApp;
        private readonly Dictionary<string, OtherDeviceCard> cards = new Dictionary<string, OtherDeviceCard>();
        private readonly List<OtherDeviceTab> tabs = new List<OtherDeviceTab>();
        private readonly Label empty = new Label(), note = new Label();
        private readonly Panel list = new Panel(), tabRow = new Panel();
        private readonly Button reload, previous, next;
        private List<OtherDeviceSnapshot> shown = new List<OtherDeviceSnapshot>();
        private string selectedId;
        private int firstTab;
        private bool revealSelected;

        public OtherDevicesPage(OtherDevicesManager manager, Func<int, int> scale, Func<OtherDeviceSnapshot, bool> hasBackup,
            Func<string, int, ActionBinding> getApp, Action<string, int, ActionBinding> setApp)
        {
            this.manager = manager;
            S = scale;
            this.hasBackup = hasBackup;
            this.getApp = getApp;
            this.setApp = setApp;
            BackColor = Theme.Card;
            list.BackColor = Theme.Card;
            list.AutoScroll = true;
            Controls.Add(list);
            tabRow.BackColor = Theme.Card;
            tabRow.Visible = false;
            tabRow.Bounds = new Rectangle(S(4), 0, S(TabsWidth), S(34));
            Controls.Add(tabRow);
            previous = Theme.CreateButton("‹", false);
            previous.Click += delegate { firstTab--; LayoutTabs(); };
            tabRow.Controls.Add(previous);
            next = Theme.CreateButton("›", false);
            next.Click += delegate { firstTab++; LayoutTabs(); };
            tabRow.Controls.Add(next);
            empty.ForeColor = Theme.Muted;
            empty.UseMnemonic = false;
            empty.Bounds = new Rectangle(S(8), S(8), S(716), S(120));
            empty.Text = Lang.T("沒有偵測到其他羅技裝置。\r\n\r\n把羅技接收器插到這台電腦並打開裝置，這一頁就會列出它的電量與狀態。目前支援 Lightspeed、Unifying 與 Nano 接收器上的羅技裝置（例如 G502 LIGHTSPEED、M331）。MX Master 4 與鍵盤在各自的頁面，Logi Bolt 接收器的配對在「接收器」頁。",
                "No other Logitech devices found.\r\n\r\nPlug a Logitech receiver into this computer and turn the device on to see its battery and status here. Supports devices on Lightspeed, Unifying and Nano receivers (for example G502 LIGHTSPEED or M331). MX Master 4 and the keyboard have their own pages; Logi Bolt pairing is on the Receiver page.");
            list.Controls.Add(empty);
            note.ForeColor = Theme.Muted;
            note.Font = new Font(Theme.UiFont, 8.5F);
            note.AutoEllipsis = true;
            note.TextAlign = ContentAlignment.MiddleLeft;
            note.Bounds = new Rectangle(S(8), S(372), S(600), S(28));
            note.Text = Lang.T("只回報電壓的裝置（例如 G502），電量是依電壓表換算的估計值。", "For devices that report only a voltage (such as G502), the battery level is an estimate.");
            Controls.Add(note);
            reload = Theme.CreateButton(Lang.T("重新整理", "Refresh"), false);
            reload.Click += delegate { if (manager != null) manager.Refresh(true); };
            Controls.Add(reload);
            Arrange(false);
        }

        // Several devices: tabs on top, Refresh beside them, and the note gives its row to the card.
        private void Arrange(bool several)
        {
            tabRow.Visible = several;
            note.Visible = !several;
            list.Bounds = several ? new Rectangle(0, S(40), S(744), S(362)) : new Rectangle(0, 0, S(744), S(368));
            reload.Bounds = several ? new Rectangle(S(638), S(2), S(96), S(30)) : new Rectangle(S(620), S(372), S(110), S(28));
        }

        public void SelectDevice(string id)
        {
            if (id == selectedId) return;
            selectedId = id;
            revealSelected = true;
            list.AutoScrollPosition = Point.Empty;
            ShowDevices(shown);
        }

        public void ShowDevices(List<OtherDeviceSnapshot> devices)
        {
            shown = devices;
            var keep = new HashSet<string>();
            foreach (OtherDeviceSnapshot d in devices) keep.Add(d.Id);
            foreach (string id in new List<string>(cards.Keys))
            {
                if (keep.Contains(id)) continue;
                list.Controls.Remove(cards[id]);
                cards[id].Dispose();
                cards.Remove(id);
            }
            if (selectedId == null || !keep.Contains(selectedId))
            {
                OtherDeviceSnapshot first = devices.Find(d => d.Link == OtherDeviceLink.Online);
                if (first == null && devices.Count > 0) first = devices[0];
                selectedId = first != null ? first.Id : null;
            }
            empty.Visible = devices.Count == 0;
            bool several = devices.Count > 1;
            Arrange(several);
            list.SuspendLayout();
            foreach (OtherDeviceSnapshot d in devices)
            {
                OtherDeviceCard card;
                if (!cards.TryGetValue(d.Id, out card))
                {
                    card = new OtherDeviceCard(manager, S, getApp, setApp);
                    cards[d.Id] = card;
                    list.Controls.Add(card);
                }
                bool current = d.Id == selectedId;
                if (current) card.Show(d, hasBackup(d));
                card.Location = new Point(S(4), list.AutoScrollPosition.Y + S(several ? 0 : 4));
                card.Visible = current;
            }
            list.ResumeLayout();
            if (several) LayoutTabs();
        }

        private static string TabTitle(OtherDeviceSnapshot d, List<OtherDeviceSnapshot> all)
        {
            string name = OtherText.ShortName(d);
            int same = 0;
            foreach (OtherDeviceSnapshot other in all) if (OtherText.ShortName(other) == name) same++;
            return same > 1 ? name + " #" + d.Slot : name;
        }

        private void LayoutTabs()
        {
            while (tabs.Count < shown.Count)
            {
                var tab = new OtherDeviceTab();
                tab.Click += delegate { SelectDevice(tab.DeviceId); };
                tabs.Add(tab);
                tabRow.Controls.Add(tab);
            }
            while (tabs.Count > shown.Count)
            {
                OtherDeviceTab extra = tabs[tabs.Count - 1];
                tabs.RemoveAt(tabs.Count - 1);
                tabRow.Controls.Remove(extra);
                extra.Dispose();
            }
            int count = shown.Count;
            if (count == 0) return;
            int width = Math.Max(104, Math.Min(150, (TabsWidth - TabGap * (count - 1)) / count));
            int fit = (TabsWidth + TabGap) / (width + TabGap);
            bool overflow = count > fit;
            if (overflow)
            {
                int room = TabsWidth - 2 * (ArrowWidth + TabGap);
                fit = Math.Max(1, (room + TabGap) / (104 + TabGap));
                width = (room - TabGap * (fit - 1)) / fit;
            }
            int selectedIndex = shown.FindIndex(d => d.Id == selectedId);
            // Only a new selection scrolls the tabs; the arrows must not snap back on the next refresh.
            if (revealSelected && selectedIndex >= 0)
            {
                if (selectedIndex < firstTab) firstTab = selectedIndex;
                else if (selectedIndex >= firstTab + fit) firstTab = selectedIndex - fit + 1;
            }
            revealSelected = false;
            firstTab = Math.Max(0, Math.Min(firstTab, count - fit));
            int x = 0;
            previous.Visible = overflow;
            next.Visible = overflow;
            if (overflow)
            {
                previous.Bounds = new Rectangle(0, S(2), S(ArrowWidth), S(30));
                next.Bounds = new Rectangle(S(TabsWidth - ArrowWidth), S(2), S(ArrowWidth), S(30));
                previous.Enabled = firstTab > 0;
                next.Enabled = firstTab + fit < count;
                x = ArrowWidth + TabGap;
            }
            for (int i = 0; i < count; i++)
            {
                OtherDeviceSnapshot d = shown[i];
                OtherDeviceTab tab = tabs[i];
                bool online = d.Link == OtherDeviceLink.Online;
                string state = online ? (d.BatteryPercent >= 0 ? d.BatteryPercent + "%" : "—") : Lang.T("離線", "Offline");
                tab.Set(d.Id, TabTitle(d, shown), state, online, i == selectedIndex);
                bool visible = i >= firstTab && i < firstTab + fit;
                tab.Visible = visible;
                if (visible)
                {
                    tab.Bounds = new Rectangle(S(x), S(2), S(width), S(30));
                    x += width + TabGap;
                }
            }
        }

        public void RefreshTimes() { if (shown.Count > 0) ShowDevices(shown); }
    }
    // ---- Other devices page end ----

    internal sealed class DeviceSnapshot
    {
        public string Transport = "";
        public int BatteryPercent = -1;
        public bool BatteryCharging;
        public bool BatteryFull;
        public string Firmware = "";
        public int DpiFeature, Dpi, DpiDefault, DpiMin, DpiMax, DpiStep;
        public int SmartShiftFeature, WheelMode, SmartShiftThreshold;
        public bool WheelInvertPresent, WheelInverted;
        public bool TorquePresent, ThumbInvertPresent, ThumbInverted, HiresResolution;
        public int RatchetTorque;
        public bool HapticPresent;
        public int HapticLevel;
        public readonly List<string> Raw = new List<string>();

        public string BatteryText
        {
            get
            {
                if (BatteryPercent < 0) return Lang.T("未知", "Unknown");
                return BatteryPercent + "%" + (BatteryFull ? Lang.T("（已充飽）", " (full)") : BatteryCharging ? Lang.T("（充電中）", " (charging)") : "");
            }
        }
    }

    // Device values the user applied from this app. They are kept in the INI and written back
    // at startup; -1 means "never set here, leave the mouse as it is".
    internal sealed class DeviceSettings
    {
        public int Dpi = -1;
        public int WheelMode = -1;
        public int SmartShiftThreshold = -1;
        public int WheelInvert = -1;
        public int RatchetTorque = -1, ThumbInvert = -1, HiresResolution = -1;
        public int Haptic = -1;

        public bool TryLoad(string key, string text)
        {
            if (!key.StartsWith("Device", StringComparison.OrdinalIgnoreCase)) return false;
            int value;
            if (!int.TryParse(text.Trim(), out value)) return true;
            if (key.Equals("DeviceDpi", StringComparison.OrdinalIgnoreCase))
            {
                if (value > 0 && value <= 0xFFFF) Dpi = value;
            }
            else if (key.Equals("DeviceWheelMode", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 1 || value == 2) WheelMode = value;
            }
            else if (key.Equals("DeviceSmartShift", StringComparison.OrdinalIgnoreCase))
            {
                if ((value >= 1 && value <= 50) || value == 255) SmartShiftThreshold = value;
            }
            else if (key.Equals("DeviceWheelInvert", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 0 || value == 1) WheelInvert = value;
            }
            else if (key.Equals("DeviceRatchetTorque", StringComparison.OrdinalIgnoreCase))
            {
                if (value >= 1 && value <= 100) RatchetTorque = value;
            }
            else if (key.Equals("DeviceThumbInvert", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 0 || value == 1) ThumbInvert = value;
            }
            else if (key.Equals("DeviceHiresResolution", StringComparison.OrdinalIgnoreCase))
            {
                if (value == 0 || value == 1) HiresResolution = value;
            }
            else if (key.Equals("DeviceHaptic", StringComparison.OrdinalIgnoreCase))
            {
                if (value >= 0 && value <= 100) Haptic = value;
            }
            return true;
        }

        public List<string> ToLines()
        {
            var lines = new List<string>();
            if (Dpi > 0) lines.Add("DeviceDpi=" + Dpi);
            if (WheelMode > 0) lines.Add("DeviceWheelMode=" + WheelMode);
            if (SmartShiftThreshold > 0) lines.Add("DeviceSmartShift=" + SmartShiftThreshold);
            if (WheelInvert >= 0) lines.Add("DeviceWheelInvert=" + WheelInvert);
            if (RatchetTorque > 0) lines.Add("DeviceRatchetTorque=" + RatchetTorque);
            if (ThumbInvert >= 0) lines.Add("DeviceThumbInvert=" + ThumbInvert);
            if (HiresResolution >= 0) lines.Add("DeviceHiresResolution=" + HiresResolution);
            if (Haptic >= 0) lines.Add("DeviceHaptic=" + Haptic);
            return lines;
        }

        // Writes only the values that differ from what the device reports. Returns true when
        // anything was written; each attempt is described in results.
        public bool Restore(HidppDiversionManager hidpp, DeviceSnapshot s, List<string> results)
        {
            return Restore(hidpp, s, results, true);
        }

        // includeDpi is false when a newer speed request (precision mode or the pointer page) owns the DPI.
        public bool Restore(HidppDiversionManager hidpp, DeviceSnapshot s, List<string> results, bool includeDpi)
        {
            bool wrote = false;
            bool dpiInRange = s.DpiMax <= s.DpiMin || (Dpi >= s.DpiMin && Dpi <= s.DpiMax);
            if (includeDpi && Dpi > 0 && s.DpiFeature == 0x2201 && s.Dpi != Dpi && dpiInRange)
            {
                results.Add("restore DPI " + Dpi + " verified=" + hidpp.SetDpi(Dpi));
                wrote = true;
            }
            if (WheelMode > 0 && SmartShiftThreshold > 0 && s.SmartShiftFeature != 0 &&
                (s.WheelMode != WheelMode || s.SmartShiftThreshold != SmartShiftThreshold))
            {
                results.Add("restore SmartShift " + WheelMode + "/" + SmartShiftThreshold +
                    " verified=" + hidpp.SetSmartShift(WheelMode, SmartShiftThreshold));
                wrote = true;
            }
            if (WheelInvert >= 0 && s.WheelInvertPresent && s.WheelInverted != (WheelInvert == 1))
            {
                results.Add("restore wheel invert " + WheelInvert + " verified=" + hidpp.SetWheelInvert(WheelInvert == 1));
                wrote = true;
            }
            if (RatchetTorque > 0 && s.TorquePresent && s.RatchetTorque != RatchetTorque)
            {
                results.Add("restore torque " + RatchetTorque + " verified=" + hidpp.SetRatchetTorque(RatchetTorque));
                wrote = true;
            }
            if (ThumbInvert >= 0 && s.ThumbInvertPresent && s.ThumbInverted != (ThumbInvert == 1))
            {
                results.Add("restore thumb invert " + ThumbInvert + " verified=" + hidpp.SetThumbInvert(ThumbInvert == 1));
                wrote = true;
            }
            if (HiresResolution >= 0 && s.WheelInvertPresent && s.HiresResolution != (HiresResolution == 1))
            {
                results.Add("restore high resolution " + HiresResolution + " verified=" + hidpp.SetHiresResolution(HiresResolution == 1));
                wrote = true;
            }
            if (Haptic >= 0 && s.HapticPresent && s.HapticLevel != Haptic)
            {
                results.Add("restore haptic " + Haptic + " verified=" + hidpp.SetHaptic(Haptic));
                wrote = true;
            }
            return wrote;
        }
    }

    internal sealed class DeviceDialog : Form
    {
        private readonly HidppDiversionManager hidpp;
        private readonly Action<IEnumerable<string>> log;
        private readonly DeviceSettings saved;
        private readonly Action save;
        private readonly Label connection = new Label();
        private readonly Label battery = new Label();
        private readonly Label firmware = new Label();
        private readonly SliderBar dpi = new SliderBar();
        private readonly Label dpiValue = new Label();
        private readonly Label dpiRange = new Label();
        private readonly ComboBox wheelMode = new ComboBox();
        private readonly NumericUpDown threshold = new NumericUpDown();
        private readonly CheckBox neverDisengage = new CheckBox();
        private readonly Label result = new Label();
        private readonly ComboBox wheelDirection = new ComboBox();
        private readonly Panel torqueRow = new Panel();
        private readonly SliderBar torque = new SliderBar();
        private readonly Label torqueValue = new Label();
        private readonly CheckBox thumbInvert = new ToggleSwitch(), hiresResolution = new ToggleSwitch();
        private bool fillingFeatures, featureBusy;
        private readonly Button dpiApply, shiftApply, directionApply, reload, close;
        private readonly float scale;
        private DeviceSnapshot current;

        // Set by MainForm. While precision mode is on the mouse reports the slow speed; this gives the
        // normal one to show instead (0 = use what the mouse reports).
        public Func<int> NormalDpi;
        // Raised after a new speed was applied here; it becomes the normal speed.
        public event Action<int> DpiApplied;
        // Set by MainForm so the write is ordered with precision-mode writes (null = write directly).
        public Func<int, bool> WriteDpi;

        public DeviceDialog(HidppDiversionManager manager, Action<IEnumerable<string>> logAction,
            DeviceSettings savedSettings, Action saveAction)
        {
            hidpp = manager;
            log = logAction;
            saved = savedSettings;
            save = saveAction;
            scale = Theme.GetScale();
            DialogStyle.Apply(this, Lang.T("裝置設定", "Device settings"), 480, 484, scale);

            // Layout: connection / battery / firmware in three columns, then the speed, then the wheel rows
            // (label column, control column at x=150, Apply at x=380); the optional wheel features follow
            // and are stacked by LayoutExtras so unsupported ones leave no gap.
            AddInfoColumn(Lang.T("連線", "Connection"), connection, 0);
            AddInfoColumn(Lang.T("電量", "Battery"), battery, 1);
            AddInfoColumn(Lang.T("韌體", "Firmware"), firmware, 2);
            AddDividerLine(56);

            AddSection(Lang.T("指標速度（DPI）", "Pointer speed (DPI)"), 66);
            dpi.Location = new Point(S(14), S(92));
            dpi.Size = new Size(S(270), S(30));
            dpi.ValueChanged += delegate { dpiValue.Text = dpi.Value + " DPI"; };
            Controls.Add(dpi);
            AddValueLabel(dpiValue, 92);
            dpiRange.AutoSize = true;
            dpiRange.ForeColor = Theme.Muted;
            dpiRange.Font = new Font(Theme.UiFont, 8.5F);
            dpiRange.Location = new Point(S(20), S(121));
            Controls.Add(dpiRange);
            dpiApply = AddApply(92);
            dpiApply.Click += delegate
            {
                DeviceSnapshot s = current;
                if (s == null || s.DpiStep <= 0) return;
                int steps = (int)Math.Round(((int)dpi.Value - s.DpiMin) / (double)s.DpiStep);
                int value = Math.Max(s.DpiMin, Math.Min(s.DpiMax, s.DpiMin + steps * s.DpiStep));
                Func<int, bool> writer = WriteDpi;
                RunWrite("DPI " + value, delegate { return writer != null ? writer(value) : hidpp.SetDpi(value); },
                    delegate
                    {
                        saved.Dpi = value;
                        Action<int> handler = DpiApplied;
                        if (handler != null) handler(value);
                    });
            };

            AddDividerLine(146);
            AddSection(Lang.T("滾輪", "Wheel"), 156);
            AddRowLabel(Lang.T("模式", "Mode"), 184);
            wheelMode.DropDownStyle = ComboBoxStyle.DropDownList;
            wheelMode.Items.Add(Lang.T("段落滾動（Ratchet）", "Ratchet"));
            wheelMode.Items.Add(Lang.T("自由滾動（Free spin）", "Free spin"));
            wheelMode.Location = new Point(S(150), S(184));
            wheelMode.Width = S(210);
            Controls.Add(wheelMode);
            shiftApply = AddApply(182);
            AddRowLabel(Lang.T("自動切換靈敏度", "Auto-shift sensitivity"), 218);
            threshold.Minimum = 1;
            threshold.Maximum = 50;
            threshold.Location = new Point(S(150), S(218));
            threshold.Width = S(70);
            Controls.Add(threshold);
            neverDisengage.Text = Lang.T("不自動切換", "Never auto-shift");
            neverDisengage.AutoSize = true;
            neverDisengage.Location = new Point(S(236), S(219));
            neverDisengage.CheckedChanged += delegate { threshold.Enabled = !neverDisengage.Checked; };
            Controls.Add(neverDisengage);
            shiftApply.Click += delegate
            {
                int mode = wheelMode.SelectedIndex == 1 ? 1 : 2;
                int value = neverDisengage.Checked ? 255 : (int)threshold.Value;
                RunWrite("SmartShift", delegate { return hidpp.SetSmartShift(mode, value); },
                    delegate { saved.WheelMode = mode; saved.SmartShiftThreshold = value; });
            };

            AddRowLabel(Lang.T("捲動方向", "Scroll direction"), 252);
            wheelDirection.DropDownStyle = ComboBoxStyle.DropDownList;
            // Index 0 is the mouse's own direction, index 1 the inverted one. Windows does not flip
            // scrolling itself, so the device direction is "standard" and the inverted one "natural"
            // (on macOS, where the system flips it, the two names are the other way round).
            wheelDirection.Items.Add(Lang.T("標準", "Standard"));
            wheelDirection.Items.Add(Lang.T("自然", "Natural"));
            wheelDirection.Location = new Point(S(150), S(252));
            wheelDirection.Width = S(210);
            Controls.Add(wheelDirection);
            directionApply = AddApply(250);
            directionApply.Click += delegate
            {
                bool inverted = wheelDirection.SelectedIndex == 1;
                RunWrite(Lang.T("捲動方向", "Scroll direction"), delegate { return hidpp.SetWheelInvert(inverted); },
                    delegate { saved.WheelInvert = inverted ? 1 : 0; });
            };

            torqueRow.Bounds = new Rectangle(S(20), S(284), S(440), S(32));
            torqueRow.Visible = false; Controls.Add(torqueRow);
            torqueRow.Controls.Add(new Label { Text = Lang.T("段落力道", "Ratchet torque"), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(0, S(1), S(126), S(28)) });
            torque.Minimum = 1; torque.Maximum = 100; torque.Bounds = new Rectangle(S(124), S(2), S(176), S(26));
            torque.ValueChanged += delegate { torqueValue.Text = torque.Value.ToString(); };
            torqueRow.Controls.Add(torque);
            torqueValue.AutoSize = false; torqueValue.TextAlign = ContentAlignment.MiddleCenter; torqueValue.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            torqueValue.Bounds = new Rectangle(S(300), S(1), S(52), S(28)); torqueRow.Controls.Add(torqueValue);
            Button torqueApply = Theme.CreateButton(Lang.T("套用", "Apply"), false);
            torqueApply.Bounds = new Rectangle(S(360), 0, S(80), S(30)); torqueRow.Controls.Add(torqueApply);
            torqueApply.Click += delegate
            {
                int value = torque.Value;
                RunWrite(Lang.T("段落力道", "Ratchet torque"), delegate { return hidpp.SetRatchetTorque(value); }, delegate { saved.RatchetTorque = value; });
            };
            thumbInvert.Text = Lang.T("拇指滾輪方向反轉", "Invert thumb wheel");
            thumbInvert.Bounds = new Rectangle(S(20), S(320), S(440), S(28)); thumbInvert.Visible = false; Controls.Add(thumbInvert);
            thumbInvert.Click += delegate
            {
                if (fillingFeatures || featureBusy) return; bool value = thumbInvert.Checked;
                RunWrite(thumbInvert.Text, delegate { return hidpp.SetThumbInvert(value); }, delegate { saved.ThumbInvert = value ? 1 : 0; });
            };
            hiresResolution.Text = Lang.T("主滾輪高解析度（捲動可能變快）", "High resolution wheel (may scroll faster)");
            hiresResolution.Bounds = new Rectangle(S(20), S(352), S(440), S(28)); hiresResolution.Visible = false; Controls.Add(hiresResolution);
            hiresResolution.Click += delegate
            {
                if (fillingFeatures || featureBusy) return; bool value = hiresResolution.Checked;
                RunWrite(hiresResolution.Text, delegate { return hidpp.SetHiresResolution(value); }, delegate { saved.HiresResolution = value ? 1 : 0; });
            };

            // Haptic strength moved to its own page with the haptic moments.
            result.AutoSize = false;
            result.ForeColor = Theme.Muted;
            result.Location = new Point(S(20), S(406));
            result.Size = new Size(S(440), S(32));
            Controls.Add(result);

            reload = Theme.CreateButton(Lang.T("重新讀取", "Reload"), false);
            reload.Location = new Point(S(250), S(442));
            reload.Size = new Size(S(100), S(34));
            reload.Click += delegate { Reload(); };
            Controls.Add(reload);
            close = Theme.CreateButton(Lang.T("關閉", "Close"), false);
            close.Location = new Point(S(360), S(442));
            close.Size = new Size(S(100), S(34));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            CancelButton = close;

            Shown += delegate { Reload(); };
        }

        private int S(int value)
        {
            return (int)Math.Round(value * scale);
        }

        // Muted name over the value, three columns across (like the Keyboard page).
        private void AddInfoColumn(string name, Label value, int column)
        {
            int x = 20 + column * 150;
            Controls.Add(new Label { Text = name, AutoSize = false, ForeColor = Theme.Muted, Font = new Font(Theme.UiFont, 9F),
                Bounds = new Rectangle(S(x), S(6), S(140), S(18)) });
            value.AutoSize = false;
            value.AutoEllipsis = true;
            value.UseMnemonic = false;
            value.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            value.Bounds = new Rectangle(S(x), S(26), S(column == 2 ? 140 : 145), S(22));
            Controls.Add(value);
        }

        private void AddRowLabel(string text, int y)
        {
            Controls.Add(new Label { Text = text, AutoSize = false, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft,
                Bounds = new Rectangle(S(20), S(y - 1), S(128), S(28)) });
        }

        private void AddDividerLine(int y)
        {
            Controls.Add(new Panel { BackColor = Theme.Divider, Bounds = new Rectangle(S(20), S(y), S(440), 1) });
        }

        // Ratchet torque, thumb-wheel inversion and high resolution, stacked from y=284 with no gaps.
        private void LayoutExtras(bool torqueShown, bool thumbShown, bool hiresShown)
        {
            int y = 284;
            if (torqueShown) { torqueRow.Top = S(y); y += 38; }
            if (thumbShown) { thumbInvert.Top = S(y); y += 34; }
            if (hiresShown) hiresResolution.Top = S(y);
        }

        private void AddSection(string title, int y)
        {
            Controls.Add(new Label
            {
                Text = title,
                AutoSize = true,
                Font = new Font(Theme.UiFont, 10F, FontStyle.Bold),
                Location = new Point(S(20), S(y))
            });
        }

        // The number shown between a slider and its apply button.
        private void AddValueLabel(Label label, int y)
        {
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = new Font(Theme.UiFont, 9.5F, FontStyle.Bold);
            label.Location = new Point(S(288), S(y));
            label.Size = new Size(S(88), S(30));
            Controls.Add(label);
        }

        private Button AddApply(int y)
        {
            Button button = Theme.CreateButton(Lang.T("套用", "Apply"), false);
            button.Location = new Point(S(380), S(y));
            button.Size = new Size(S(80), S(30));
            Controls.Add(button);
            return button;
        }

        private void SetBusy(string message)
        {
            featureBusy = true;
            torqueRow.Enabled = thumbInvert.Enabled = hiresResolution.Enabled = false;
            dpiApply.Enabled = shiftApply.Enabled = directionApply.Enabled = reload.Enabled = false;
            result.Text = message;
        }

        private void Post(Action action)
        {
            try
            {
                if (IsHandleCreated && !IsDisposed) BeginInvoke(action);
            }
            catch { }
        }

        // Turns the dialog into a borderless child of a page card: no close button, and the
        // reload button moves up beside the result text.
        public void Embed(Control host, Point location)
        {
            TopLevel = false;
            FormBorderStyle = FormBorderStyle.None;
            BackColor = Theme.Card;
            close.Visible = false;
            result.Size = new Size(S(330), S(32));
            reload.Location = new Point(S(360), S(406));
            ClientSize = new Size(S(480), S(444));
            Location = location;
            host.Controls.Add(this);
            Show();
        }

        public void Reload()
        {
            SetBusy(Lang.T("正在讀取裝置…", "Reading device…"));
            ThreadPool.QueueUserWorkItem(delegate
            {
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                Post(delegate { ShowSnapshot(snapshot, snapshot == null ? Lang.T("尚未連上 HID++ 裝置，無法讀取。", "Not connected to the mouse over HID++; nothing to read.") : Lang.T("已讀取裝置目前設定。", "Current device settings loaded.")); });
            });
        }

        // remember records the confirmed value so it is saved and re-applied on the next start.
        private void RunWrite(string name, Func<bool> write, Action remember)
        {
            SetBusy(Lang.T("正在套用 ", "Applying ") + name + "…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                try { ok = write(); }
                catch { }
                DeviceSnapshot snapshot = null;
                try { snapshot = hidpp.ReadDevice(); }
                catch { }
                Post(delegate
                {
                    log(new[] { "write " + name + " verified=" + ok });
                    if (ok)
                    {
                        remember();
                        save();
                    }
                    ShowSnapshot(snapshot, ok ? name + Lang.T(" 已套用並儲存，下次啟動會自動重新套用。", " applied and saved; it will be re-applied at the next start.") : name + Lang.T(" 套用失敗：裝置未確認新數值。", " failed: the device did not confirm the new value."));
                });
            });
        }

        private void ShowSnapshot(DeviceSnapshot s, string message)
        {
            current = s;
            featureBusy = false;
            fillingFeatures = true;
            bool torqueShown = s != null && s.TorquePresent, thumbShown = s != null && s.ThumbInvertPresent, hiresShown = s != null && s.WheelInvertPresent;
            torqueRow.Visible = torqueRow.Enabled = torqueShown;
            thumbInvert.Visible = thumbInvert.Enabled = thumbShown;
            hiresResolution.Visible = hiresResolution.Enabled = hiresShown;
            LayoutExtras(torqueShown, thumbShown, hiresShown);
            if (s != null)
            {
                if (s.TorquePresent) torque.Value = Math.Max(1, Math.Min(100, s.RatchetTorque));
                thumbInvert.Checked = s.ThumbInverted; hiresResolution.Checked = s.HiresResolution;
            }
            fillingFeatures = false;
            reload.Enabled = true;
            result.Text = message;
            if (s == null)
            {
                connection.Text = battery.Text = firmware.Text = "—";
                dpiRange.Text = "";
                dpi.Enabled = wheelMode.Enabled = threshold.Enabled = neverDisengage.Enabled = wheelDirection.Enabled = false;
                return;
            }
            log(s.Raw);

            connection.Text = s.Transport == "BOLT" ? Lang.T("Logi Bolt 接收器", "Logi Bolt receiver") : "Bluetooth LE";
            battery.Text = s.BatteryText;
            firmware.Text = s.Firmware.Length > 0 ? s.Firmware : Lang.T("未知", "Unknown");

            bool dpiWritable = s.DpiFeature == 0x2201 && s.DpiStep > 0 && s.DpiMax > s.DpiMin;
            dpi.Enabled = dpiApply.Enabled = dpiWritable;
            if (dpiWritable)
            {
                dpi.Minimum = s.DpiMin;
                dpi.Maximum = s.DpiMax;
                dpi.Increment = s.DpiStep;
                int normal = NormalDpi != null ? NormalDpi() : 0;
                dpi.Value = Math.Max(s.DpiMin, Math.Min(s.DpiMax, normal > 0 ? normal : s.Dpi));
                dpiRange.Text = Lang.T("範圍 ", "Range ") + s.DpiMin + "–" + s.DpiMax + Lang.T("，預設 ", ", default ") + s.DpiDefault;
            }
            else if (s.DpiFeature == 0x2201)
                dpiRange.Text = Lang.T("目前 ", "Current ") + s.Dpi + Lang.T("；裝置未回報可用範圍，暫不開放修改", "; the device reported no range, so it cannot be changed here");
            else if (s.DpiFeature == 0x2202)
                dpiRange.Text = Lang.T("裝置使用延伸 DPI（2202），目前僅能偵測", "This device uses extended DPI (2202), which is read-only here");
            else
                dpiRange.Text = Lang.T("此裝置未回報 DPI 功能", "This device reports no DPI feature");

            bool shift = s.SmartShiftFeature != 0;
            wheelMode.Enabled = neverDisengage.Enabled = shiftApply.Enabled = shift;
            if (shift)
            {
                wheelMode.SelectedIndex = s.WheelMode == 1 ? 1 : 0;
                bool never = s.SmartShiftThreshold < 1 || s.SmartShiftThreshold > 50;
                neverDisengage.Checked = never;
                if (!never) threshold.Value = s.SmartShiftThreshold;
            }
            threshold.Enabled = shift && !neverDisengage.Checked;

            wheelDirection.Enabled = directionApply.Enabled = s.WheelInvertPresent;
            if (s.WheelInvertPresent)
                wheelDirection.SelectedIndex = s.WheelInverted ? 1 : 0;
        }
    }

    internal sealed class RawInputMonitor : NativeWindow, IDisposable
    {
        private readonly Action<string> report;
        private readonly Action<string, int, int, bool> vendorButton;
        private readonly Dictionary<IntPtr, string> names = new Dictionary<IntPtr, string>();
        private readonly Dictionary<IntPtr, bool> lightspeedDevices = new Dictionary<IntPtr, bool>();
        private uint lastLightspeedRaw;
        private readonly Dictionary<int, HashSet<int>> divertedPressed = new Dictionary<int, HashSet<int>>();
        private volatile int divertedDeviceIndex = -1;
        private volatile int divertedFeatureIndex = -1;
        private readonly Action activity;
        private readonly Action<int, int> mouseMove;
        private readonly Action<byte[]> boltShortReport;
        private int lastMxInput;
        private bool sawMxInput;
        // A host switch or reconnect always leaves at least this long without any report.
        private const int ActivityGapMilliseconds = 2000;
        private const int WM_INPUT = 0x00FF;
        private const uint RID_INPUT = 0x10000003, RIDI_DEVICENAME = 0x20000007;
        private const uint RIDEV_INPUTSINK = 0x00000100, RIDEV_PAGEONLY = 0x00000020;

        public RawInputMonitor(IntPtr handle, Action<string> reportAction, Action<string, int, int, bool> vendorButtonAction,
            Action activityAction, Action<int, int> mouseMoveAction, Action<byte[]> boltShortReportAction)
        {
            AssignHandle(handle);
            report = reportAction;
            vendorButton = vendorButtonAction;
            activity = activityAction;
            mouseMove = mouseMoveAction;
            boltShortReport = boltShortReportAction;
            Register(handle);
        }

        // The mouse sent something; after a pause that may mean it came back from another host.
        // A Bolt receiver also carries the keyboard (and Options+ talking to it). With the mouse on Bolt only
        // HID++ reports from its slot count as mouse activity; otherwise typing or a keyboard read made the
        // mouse check run against a sleeping mouse. With the mouse on Bluetooth its index is 0xFF, the same
        // as the receiver's own, so nothing from the receiver counts (the mouse's raw mouse input still does).
        internal static bool CountsAsMouse(string transport, int mouseSlot, byte[] bytes)
        {
            if (transport != "BOLT" || mouseSlot <= 0) return true;
            if (mouseSlot > 6) return false;
            bool hidpp = bytes.Length >= 3 && (bytes[0] == 0x10 || bytes[0] == 0x11);
            return hidpp && bytes[1] == mouseSlot;
        }

        // Hex for the engineering log, with Bolt pairing data hidden: the passkey (0x4D), the discovered
        // device's address (0x4F) and paired devices' serial numbers (register 0x2B5).
        internal static string LogHex(byte[] bytes)
        {
            if (bytes.Length >= 4 && (bytes[0] == 0x10 || bytes[0] == 0x11) && bytes[1] == 0xFF &&
                (bytes[2] == 0x4D || bytes[2] == 0x4F || (bytes[2] == 0x83 && bytes[3] == 0xB5)))
                return BitConverter.ToString(bytes, 0, 4) + " (Bolt pairing data hidden)";
            return BitConverter.ToString(bytes);
        }

        private void NoteMouseInput()
        {
            int now = Environment.TickCount;
            bool resumed = !sawMxInput || unchecked(now - lastMxInput) >= ActivityGapMilliseconds;
            sawMxInput = true;
            lastMxInput = now;
            if (resumed) activity();
        }

        // Bolt slot (or 0xFF on Bluetooth) of the mouse whose buttons are diverted; -1 before that.
        public int MouseSlot { get { return divertedDeviceIndex; } }

        // Time since the mouse last sent anything; a large value when it never has.
        public int MillisecondsSinceInput
        {
            get { return sawMxInput ? unchecked(Environment.TickCount - lastMxInput) : int.MaxValue; }
        }

        public static bool IsMxMaster4Present()
        {
            uint count = 0;
            uint size = (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICELIST));
            GetRawInputDeviceList(IntPtr.Zero, ref count, size);
            IntPtr mem = Marshal.AllocHGlobal((int)(count * size));
            try
            {
                if (GetRawInputDeviceList(mem, ref count, size) == uint.MaxValue) return false;
                for (uint i = 0; i < count; i++)
                {
                    IntPtr item = new IntPtr(mem.ToInt64() + i * size);
                    RAWINPUTDEVICELIST d = (RAWINPUTDEVICELIST)Marshal.PtrToStructure(item, typeof(RAWINPUTDEVICELIST));
                    string name = DeviceName(d.hDevice);
                    if (IsMx(name)) return true;
                }
            }
            finally { Marshal.FreeHGlobal(mem); }
            return false;
        }

        public void ConfigureHidppDiversion(int deviceIndex, int featureIndex)
        {
            divertedDeviceIndex = deviceIndex;
            divertedFeatureIndex = featureIndex;
        }

        public void ResetPressed()
        {
            divertedPressed.Clear();
        }

        private static void Register(IntPtr handle)
        {
            var devices = new List<RAWINPUTDEVICE>();
            devices.Add(new RAWINPUTDEVICE { usUsagePage = 0x01, usUsage = 0x02, dwFlags = RIDEV_INPUTSINK, hwndTarget = handle });
            ushort[] pages = { 0x0C, 0xFF00, 0xFF01, 0xFF43 };
            foreach (ushort page in pages)
                devices.Add(new RAWINPUTDEVICE { usUsagePage = page, usUsage = 0, dwFlags = RIDEV_INPUTSINK | RIDEV_PAGEONLY, hwndTarget = handle });
            RegisterRawInputDevices(devices.ToArray(), (uint)devices.Count, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT) ProcessInput(m.LParam);
            base.WndProc(ref m);
        }

        private void ProcessInput(IntPtr rawHandle)
        {
            uint size = 0;
            GetRawInputData(rawHandle, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER)));
            if (size == 0) return;
            IntPtr mem = Marshal.AllocHGlobal((int)size);
            try
            {
                if (GetRawInputData(rawHandle, RID_INPUT, mem, ref size, (uint)Marshal.SizeOf(typeof(RAWINPUTHEADER))) != size) return;
                RAWINPUTHEADER header = (RAWINPUTHEADER)Marshal.PtrToStructure(mem, typeof(RAWINPUTHEADER));
                string name;
                if (!names.TryGetValue(header.hDevice, out name))
                {
                    name = DeviceName(header.hDevice);
                    names[header.hDevice] = name;
                }
                if (!IsMx(name))
                {
                    // Engineering log only: shows whether Windows reports buttons above 5 from a Lightspeed mouse.
                    if (header.dwType == 0 && IsLightspeed(header.hDevice, name))
                    {
                        RAWMOUSE other = (RAWMOUSE)Marshal.PtrToStructure(new IntPtr(mem.ToInt64() + Marshal.SizeOf(typeof(RAWINPUTHEADER))), typeof(RAWMOUSE));
                        if (other.usButtonFlags != 0 || other.ulRawButtons != lastLightspeedRaw)
                        {
                            lastLightspeedRaw = other.ulRawButtons;
                            report("OtherButton flags=0x" + other.usButtonFlags.ToString("X4") + " raw=0x" + other.ulRawButtons.ToString("X8"));
                        }
                    }
                    return;
                }
                string transport = TransportName(name);
                int offset = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                if (header.dwType == 0)
                {
                    NoteMouseInput();
                    RAWMOUSE mouse = (RAWMOUSE)Marshal.PtrToStructure(new IntPtr(mem.ToInt64() + offset), typeof(RAWMOUSE));
                    // Movement-only packets are too noisy for button mapping.
                    if (mouse.usButtonFlags != 0)
                        report(transport + " MOUSE flags=0x" + mouse.usButtonFlags.ToString("X4") + " data=" + mouse.usButtonData + " dx=" + mouse.lLastX + " dy=" + mouse.lLastY);
                    // Relative movement of this mouse only (bit 0 of usFlags marks absolute coordinates).
                    if ((mouse.usFlags & 1) == 0 && (mouse.lLastX != 0 || mouse.lLastY != 0))
                        mouseMove(mouse.lLastX, mouse.lLastY);
                }
                else if (header.dwType == 2)
                {
                    IntPtr hidPtr = new IntPtr(mem.ToInt64() + offset);
                    uint bytesPerReport = (uint)Marshal.ReadInt32(hidPtr);
                    uint reportCount = (uint)Marshal.ReadInt32(hidPtr, 4);
                    int total = (int)Math.Min(bytesPerReport * reportCount, 128);
                    byte[] bytes = new byte[total];
                    Marshal.Copy(new IntPtr(hidPtr.ToInt64() + 8), bytes, 0, total);
                    report(transport + " HID size=" + bytesPerReport + " count=" + reportCount + " hex=" + LogHex(bytes));

                    bool hidpp = bytes.Length >= 3 && (bytes[0] == 0x10 || bytes[0] == 0x11);
                    if (CountsAsMouse(transport, divertedDeviceIndex, bytes)) NoteMouseInput();
                    // Short HID++ reports from the receiver (connection notices, errors) go to the keyboard side.
                    if (transport == "BOLT" && hidpp && bytes[0] == 0x10 && bytes.Length >= 7 && boltShortReport != null)
                        boltShortReport(bytes);

                    // Captured MX Master 4 HID++ diverted-button notification:
                    // 11 [FF for BLE / 01-06 for Bolt] 0D 20
                    // [control high] [control low] [01 down / 00 up]
                    if (bytes.Length >= 7 && (bytes[0] == 0x11 || bytes[0] == 0x10) &&
                        bytes[2] == 0x0D && bytes[3] == 0x20)
                    {
                        int deviceIndex = bytes[1];
                        int controlCode = (bytes[4] << 8) | bytes[5];
                        bool pressed = bytes[6] != 0;
                        vendorButton(transport, deviceIndex, controlCode, pressed);
                    }
                    else if (bytes.Length >= 6 && bytes[0] == 0x11 &&
                        bytes[1] == divertedDeviceIndex && bytes[2] == divertedFeatureIndex && bytes[3] == 0x00)
                    {
                        DecodeDivertedButtonSet(transport, bytes);
                    }
                }
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        private void DecodeDivertedButtonSet(string transport, byte[] bytes)
        {
            int deviceIndex = bytes[1];
            var next = new HashSet<int>();
            for (int offset = 4; offset + 1 < bytes.Length && offset < 12; offset += 2)
            {
                int cid = (bytes[offset] << 8) | bytes[offset + 1];
                if (cid != 0) next.Add(cid);
            }

            HashSet<int> previous;
            if (!divertedPressed.TryGetValue(deviceIndex, out previous))
                previous = new HashSet<int>();

            int[] known = { 0x00C3, 0x01A0, 0x00C4 };
            foreach (int cid in known)
            {
                bool wasPressed = previous.Contains(cid);
                bool isPressed = next.Contains(cid);
                if (wasPressed != isPressed)
                    vendorButton(transport, deviceIndex, cid, isPressed);
            }
            divertedPressed[deviceIndex] = next;
        }

        private bool IsLightspeed(IntPtr device, string name)
        {
            bool known;
            if (lightspeedDevices.TryGetValue(device, out known)) return known;
            string upper = (name ?? "").ToUpperInvariant();
            foreach (int pid in OtherDevicesProtocol.LightspeedReceivers)
                if (upper.Contains("VID_046D&PID_" + pid.ToString("X4"))) known = true;
            lightspeedDevices[device] = known;
            return known;
        }

        private static bool IsMx(string name)
        {
            if (String.IsNullOrEmpty(name)) return false;
            string n = name.ToUpperInvariant();
            bool usbStyle = n.Contains("VID_046D") && n.Contains("PID_B042");
            bool bluetoothStyle = n.Contains("VID&02046D") && n.Contains("PID&B042");
            bool boltReceiver = n.Contains("VID_046D") && n.Contains("PID_C548");
            return usbStyle || bluetoothStyle || boltReceiver;
        }

        private static string TransportName(string name)
        {
            string n = (name ?? "").ToUpperInvariant();
            return n.Contains("PID_C548") ? "BOLT" : "BLE";
        }

        private static string DeviceName(IntPtr device)
        {
            uint length = 0;
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, IntPtr.Zero, ref length);
            if (length == 0) return "";
            var sb = new StringBuilder((int)length + 2);
            GetRawInputDeviceInfo(device, RIDI_DEVICENAME, sb, ref length);
            return sb.ToString();
        }

        public void Dispose() { ReleaseHandle(); }

        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICE
        { public ushort usUsagePage, usUsage; public uint dwFlags; public IntPtr hwndTarget; }
        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTDEVICELIST
        { public IntPtr hDevice; public uint dwType; }
        [StructLayout(LayoutKind.Sequential)] private struct RAWINPUTHEADER
        { public uint dwType, dwSize; public IntPtr hDevice, wParam; }
        [StructLayout(LayoutKind.Explicit, Size = 24)] private struct RAWMOUSE
        {
            [FieldOffset(0)] public ushort usFlags;
            [FieldOffset(4)] public uint ulButtons;
            [FieldOffset(4)] public ushort usButtonFlags;
            [FieldOffset(6)] public ushort usButtonData;
            [FieldOffset(8)] public uint ulRawButtons;
            [FieldOffset(12)] public int lLastX;
            [FieldOffset(16)] public int lLastY;
            [FieldOffset(20)] public uint ulExtraInformation;
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterRawInputDevices(RAWINPUTDEVICE[] devices, uint count, uint size);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputData(IntPtr raw, uint command, IntPtr data, ref uint size, uint headerSize);
        [DllImport("user32.dll", SetLastError = true)] private static extern uint GetRawInputDeviceList(IntPtr list, ref uint count, uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, IntPtr data, ref uint size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern uint GetRawInputDeviceInfo(IntPtr device, uint command, StringBuilder data, ref uint size);
    }
}
