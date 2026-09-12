using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MonitorMirror;

internal sealed class MainForm : Form
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    private readonly Screen[] _screens = Screen.AllScreens;
    private readonly Panel[] _monitorPanels;
    private readonly PictureBox[] _pictureBoxes;
    private readonly Label[] _roleLabels;

    private int? _sourceIndex;
    private int? _destIndex;

    private readonly System.Windows.Forms.Timer _thumbTimer;
    private readonly TrackBar _scaleTrack;
    private readonly Label _scaleLabel;
    private readonly RadioButton _rbNone, _rbStretch, _rbFill;
    private readonly CheckBox _chkEnable;
    private readonly CheckBox _chkHold;
    private readonly Button _btnSetHotkey;
    private readonly Label _lblHotkey;
    private readonly Button _btnStart;
    private readonly Label _statusLabel;
    private readonly TrackBar _themeSlider;
    private readonly Label _lblThemeLight;
    private readonly Label _lblThemeDark;

    private readonly NotifyIcon _trayIcon;
    private readonly GlobalInputHook _inputHook;
    private MirrorEngine? _mirror;

    private readonly AppSettings _settings;
    private bool _loadingSettings;
    private bool _dark;

    public MainForm()
    {
        _settings = SettingsStore.Load();

        Text = "Monitor Mirror";
        try
        {
            var exeIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            if (exeIcon != null) Icon = exeIcon;
        }
        catch
        {
            // fall back to the WinForms default icon
        }
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;

        // --- Monitor row ---
        int n = _screens.Length;
        const int boxUnit = 160; // 150 box + 5+5 margin
        int flowWidth = n * boxUnit + 4; // small buffer so the panel's own border never forces a scrollbar
        ClientSize = new Size(Math.Max(900, flowWidth + 20), 450);

        var monitorFlow = new FlowLayoutPanel
        {
            Location = new Point((ClientSize.Width - flowWidth) / 2, 10),
            Size = new Size(flowWidth, 150),
            AutoScroll = false,
            WrapContents = false,
            BorderStyle = BorderStyle.FixedSingle
        };
        Controls.Add(monitorFlow);

        _monitorPanels = new Panel[n];
        _pictureBoxes = new PictureBox[n];
        _roleLabels = new Label[n];

        for (int i = 0; i < n; i++)
        {
            int idx = i;
            var panel = new Panel
            {
                Size = new Size(150, 130),
                Margin = new Padding(5),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = System.Drawing.Color.Gainsboro
            };

            var pic = new PictureBox
            {
                Location = new Point(5, 5),
                Size = new Size(138, 78),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = System.Drawing.Color.Black,
                BorderStyle = BorderStyle.FixedSingle
            };

            var infoLabel = new Label
            {
                Location = new Point(5, 86),
                Size = new Size(138, 16),
                Text = $"[{idx}] {_screens[idx].Bounds.Width}x{_screens[idx].Bounds.Height}",
                Font = new Font(Font.FontFamily, 7.5f)
            };

            var roleLabel = new Label
            {
                Location = new Point(5, 102),
                Size = new Size(138, 16),
                Text = _screens[idx].Primary ? "(primary)" : "",
                Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold),
                ForeColor = System.Drawing.Color.DarkSlateBlue
            };

            void ClickHandler(object? s, MouseEventArgs e) => SelectMonitor(idx, e.Button);
            panel.MouseDown += ClickHandler;
            pic.MouseDown += ClickHandler;
            infoLabel.MouseDown += ClickHandler;

            panel.Controls.Add(pic);
            panel.Controls.Add(infoLabel);
            panel.Controls.Add(roleLabel);
            monitorFlow.Controls.Add(panel);

            _monitorPanels[i] = panel;
            _pictureBoxes[i] = pic;
            _roleLabels[i] = roleLabel;
        }

        int themeRowX = ClientSize.Width - 165;

        var hint = new Label
        {
            Location = new Point(10, 165),
            Size = new Size(themeRowX - 25, 18),
            Text = "Left-click a monitor to set it as SOURCE (green). Right-click to set as OUTPUT (blue).",
            Font = new Font(Font.FontFamily, 8f, FontStyle.Italic)
        };
        Controls.Add(hint);

        _lblThemeLight = new Label { Location = new Point(themeRowX, 167), Size = new Size(35, 16), Text = "Light", Font = new Font(Font.FontFamily, 7.5f) };
        _themeSlider = new TrackBar
        {
            Location = new Point(themeRowX + 33, 159),
            Size = new Size(74, 28),
            Minimum = 0,
            Maximum = 1,
            TickStyle = TickStyle.None,
            Value = 1
        };
        _lblThemeDark = new Label { Location = new Point(themeRowX + 113, 167), Size = new Size(35, 16), Text = "Dark", Font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold) };
        _themeSlider.ValueChanged += (_, _) =>
        {
            UpdateThemeLabels();
            ApplyTheme(_themeSlider.Value == 1);
            SaveSettings();
        };
        Controls.Add(_lblThemeLight);
        Controls.Add(_themeSlider);
        Controls.Add(_lblThemeDark);

        // --- Centered settings block ---
        const int blockWidth = 640;
        var settingsBlock = new Panel
        {
            Size = new Size(blockWidth, 240),
            Location = new Point((ClientSize.Width - blockWidth) / 2, 193)
        };
        Controls.Add(settingsBlock);

        // Scale slider
        var scaleTitle = new Label { Location = new Point(0, 5), Size = new Size(90, 20), Text = "Crop size:" };
        _scaleTrack = new TrackBar
        {
            Location = new Point(90, 0),
            Size = new Size(320, 45),
            Minimum = 1,
            Maximum = 100,
            Value = 10,
            TickFrequency = 10
        };
        _scaleLabel = new Label { Location = new Point(420, 5), Size = new Size(60, 20), Text = "10%" };
        _scaleTrack.ValueChanged += (_, _) =>
        {
            _scaleLabel.Text = $"{_scaleTrack.Value}%";
            _mirror?.UpdateSettings(_scaleTrack.Value / 100f, SelectedWarp);
            SaveSettings();
        };
        settingsBlock.Controls.Add(scaleTitle);
        settingsBlock.Controls.Add(_scaleTrack);
        settingsBlock.Controls.Add(_scaleLabel);

        // Warp mode
        var warpGroup = new GroupBox { Location = new Point(0, 50), Size = new Size(280, 110), Text = "Warp mode (aspect mismatch)" };
        _rbNone = new RadioButton { Location = new Point(10, 25), Size = new Size(260, 20), Text = "None (letterbox, no distortion)" };
        _rbStretch = new RadioButton { Location = new Point(10, 50), Size = new Size(260, 20), Text = "Stretch (fill, may distort)" };
        _rbFill = new RadioButton { Location = new Point(10, 75), Size = new Size(260, 20), Text = "Fill (crop edges to fill)", Checked = true };
        void WarpChanged(object? s, EventArgs e)
        {
            _mirror?.UpdateSettings(_scaleTrack.Value / 100f, SelectedWarp);
            SaveSettings();
        }
        _rbNone.CheckedChanged += WarpChanged;
        _rbStretch.CheckedChanged += WarpChanged;
        _rbFill.CheckedChanged += WarpChanged;
        warpGroup.Controls.Add(_rbNone);
        warpGroup.Controls.Add(_rbStretch);
        warpGroup.Controls.Add(_rbFill);
        settingsBlock.Controls.Add(warpGroup);

        // Hotkey
        var hotkeyGroup = new GroupBox { Location = new Point(300, 50), Size = new Size(340, 110), Text = "Toggle" };
        _chkEnable = new CheckBox { Location = new Point(10, 22), Size = new Size(150, 20), Text = "Enable", Checked = true };
        _chkEnable.CheckedChanged += (_, _) => SaveSettings();
        _chkHold = new CheckBox { Location = new Point(10, 45), Size = new Size(220, 20), Text = "Hold (show while held)", Checked = true };
        _chkHold.CheckedChanged += (_, _) => SaveSettings();
        _btnSetHotkey = new Button { Location = new Point(10, 72), Size = new Size(110, 28), Text = "Set Hotkey" };
        _btnSetHotkey.Click += (_, _) => StartCapture();
        _lblHotkey = new Label { Location = new Point(130, 78), Size = new Size(200, 20), Text = "(none)" };
        hotkeyGroup.Controls.Add(_chkEnable);
        hotkeyGroup.Controls.Add(_chkHold);
        hotkeyGroup.Controls.Add(_btnSetHotkey);
        hotkeyGroup.Controls.Add(_lblHotkey);
        settingsBlock.Controls.Add(hotkeyGroup);

        var hotkeyHint = new Label
        {
            Location = new Point(300, 162),
            Size = new Size(340, 32),
            Text = "Hotkey can be a key combo (e.g. Ctrl+F9) or a mouse button (e.g. hold Right Click).",
            Font = new Font(Font.FontFamily, 7.5f, FontStyle.Italic)
        };
        settingsBlock.Controls.Add(hotkeyHint);

        // Start button + status
        _btnStart = new Button { Location = new Point(0, 198), Size = new Size(200, 40), Text = "Start Mirroring", Enabled = false };
        _btnStart.Click += (_, _) => ToggleMirroring();
        settingsBlock.Controls.Add(_btnStart);

        _statusLabel = new Label { Location = new Point(210, 208), Size = new Size(430, 20), Text = "Select a source and output monitor." };
        settingsBlock.Controls.Add(_statusLabel);

        // --- Tray icon ---
        var trayMenu = new ContextMenuStrip();
        trayMenu.Items.Add("Show Settings", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitApplication());
        _trayIcon = new NotifyIcon
        {
            Icon = this.Icon,
            Text = "Monitor Mirror",
            ContextMenuStrip = trayMenu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        // --- Thumbnail refresh timer ---
        _thumbTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _thumbTimer.Tick += (_, _) => RefreshThumbnails();
        _thumbTimer.Start();

        // --- Global input hook (hotkey capture + trigger matching) ---
        _inputHook = new GlobalInputHook();
        _inputHook.KeyCaptured += OnKeyCaptured;
        _inputHook.MouseCaptured += OnMouseCaptured;
        _inputHook.MatchDown += OnTriggerDown;
        _inputHook.MatchUp += OnTriggerUp;
        _inputHook.Install();

        ApplyLoadedSettings();
        RefreshThumbnails();
    }

    private WarpMode SelectedWarp => _rbStretch.Checked ? WarpMode.Stretch : _rbFill.Checked ? WarpMode.Fill : WarpMode.None;

    private void SelectMonitor(int idx, MouseButtons button)
    {
        if (button == MouseButtons.Left)
        {
            if (_destIndex == idx) _destIndex = null;
            _sourceIndex = idx;
        }
        else if (button == MouseButtons.Right)
        {
            if (_sourceIndex == idx) _sourceIndex = null;
            _destIndex = idx;
        }
        else return;

        UpdateMonitorHighlights();
        SaveSettings();
    }

    private void UpdateMonitorHighlights()
    {
        var neutral = _dark ? Color.FromArgb(60, 60, 63) : System.Drawing.Color.Gainsboro;

        for (int i = 0; i < _monitorPanels.Length; i++)
        {
            if (i == _sourceIndex)
            {
                _monitorPanels[i].BackColor = System.Drawing.Color.LightGreen;
                _roleLabels[i].Text = _screens[i].Primary ? "SOURCE (primary)" : "SOURCE";
            }
            else if (i == _destIndex)
            {
                _monitorPanels[i].BackColor = System.Drawing.Color.LightSkyBlue;
                _roleLabels[i].Text = _screens[i].Primary ? "OUTPUT (primary)" : "OUTPUT";
            }
            else
            {
                _monitorPanels[i].BackColor = neutral;
                _roleLabels[i].Text = _screens[i].Primary ? "(primary)" : "";
            }
        }

        bool valid = _sourceIndex.HasValue && _destIndex.HasValue && _sourceIndex != _destIndex;
        _btnStart.Enabled = valid;
        _statusLabel.Text = valid
            ? $"Source: monitor {_sourceIndex}   Output: monitor {_destIndex}"
            : "Select a source and output monitor.";
    }

    private volatile bool _thumbnailBusy;

    private void RefreshThumbnails()
    {
        if (!Visible || WindowState == FormWindowState.Minimized) return;
        if (_mirror != null && _mirror.IsRunning && _mirror.Visible) return; // don't fight the mirror's own GPU capture while it's on screen
        if (_thumbnailBusy) return;
        _thumbnailBusy = true;

        var screens = _screens;
        Task.Run(() =>
        {
            var thumbs = new Bitmap?[screens.Length];
            for (int i = 0; i < screens.Length; i++)
            {
                try
                {
                    var bounds = screens[i].Bounds;
                    using var full = new Bitmap(bounds.Width, bounds.Height);
                    using (var g = Graphics.FromImage(full))
                    {
                        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
                    }

                    var thumb = new Bitmap(138, 78);
                    using (var g = Graphics.FromImage(thumb))
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
                        g.DrawImage(full, 0, 0, 138, 78);
                    }
                    thumbs[i] = thumb;
                }
                catch
                {
                    // screen capture can transiently fail (DRM content, permissions); keep old thumbnail
                    thumbs[i] = null;
                }
            }

            try
            {
                BeginInvoke(() =>
                {
                    for (int i = 0; i < thumbs.Length; i++)
                    {
                        if (thumbs[i] == null) continue;
                        var old = _pictureBoxes[i].Image;
                        _pictureBoxes[i].Image = thumbs[i];
                        old?.Dispose();
                    }
                    _thumbnailBusy = false;
                });
            }
            catch
            {
                _thumbnailBusy = false;
            }
        });
    }

    private void ToggleMirroring()
    {
        if (_mirror == null || !_mirror.IsRunning)
        {
            if (!_sourceIndex.HasValue || !_destIndex.HasValue) return;
            _mirror?.Dispose();
            _mirror = new MirrorEngine(_screens[_sourceIndex.Value], _screens[_destIndex.Value], _scaleTrack.Value / 100f, SelectedWarp);
            _mirror.Visible = !_chkHold.Checked;
            _mirror.Start();
            _btnStart.Text = "Stop Mirroring";
            _statusLabel.Text = "Mirroring active.";
        }
        else
        {
            _mirror.Stop();
            _btnStart.Text = "Start Mirroring";
            _statusLabel.Text = "Mirroring stopped.";
        }
    }

    // --- Hotkey capture ---

    private void StartCapture()
    {
        _lblHotkey.Text = "Press keys or click a mouse button...";
        _inputHook.Capturing = true;
    }

    private void OnKeyCaptured(Keys key, uint modifiers)
    {
        _inputHook.Capturing = false;
        _inputHook.TriggerType = TriggerType.Keyboard;
        _inputHook.BoundKey = key;
        _inputHook.BoundModifiers = modifiers;
        BeginInvoke(() =>
        {
            _lblHotkey.Text = FormatHotkey(modifiers, key.ToString());
            SaveSettings();
        });
    }

    private void OnMouseCaptured(MouseButtons button, uint modifiers)
    {
        _inputHook.Capturing = false;
        _inputHook.TriggerType = TriggerType.Mouse;
        _inputHook.BoundMouseButton = button;
        _inputHook.BoundModifiers = modifiers;
        BeginInvoke(() =>
        {
            _lblHotkey.Text = FormatHotkey(modifiers, $"{button} Click");
            SaveSettings();
        });
    }

    private static string FormatHotkey(uint modifiers, string keyName)
    {
        var parts = new System.Collections.Generic.List<string>();
        if ((modifiers & GlobalInputHook.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((modifiers & GlobalInputHook.MOD_ALT) != 0) parts.Add("Alt");
        if ((modifiers & GlobalInputHook.MOD_SHIFT) != 0) parts.Add("Shift");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    private void OnTriggerDown()
    {
        if (!_chkEnable.Checked) return;
        if (_mirror == null || !_mirror.IsRunning) return;

        if (_chkHold.Checked)
            _mirror.Visible = true;
        else
            _mirror.Visible = !_mirror.Visible;
    }

    private void OnTriggerUp()
    {
        if (!_chkEnable.Checked) return;
        if (_mirror == null || !_mirror.IsRunning) return;

        if (_chkHold.Checked)
            _mirror.Visible = false;
    }

    // --- Settings persistence ---

    private void ApplyLoadedSettings()
    {
        _loadingSettings = true;
        try
        {
            int srcIdx = Array.FindIndex(_screens, s => s.DeviceName == _settings.SourceDeviceName);
            int dstIdx = Array.FindIndex(_screens, s => s.DeviceName == _settings.DestDeviceName);
            if (srcIdx >= 0) _sourceIndex = srcIdx;
            if (dstIdx >= 0) _destIndex = dstIdx;

            _scaleTrack.Value = Math.Clamp(_settings.ScalePercent, _scaleTrack.Minimum, _scaleTrack.Maximum);
            _scaleLabel.Text = $"{_scaleTrack.Value}%";

            switch (_settings.Warp)
            {
                case WarpMode.None: _rbNone.Checked = true; break;
                case WarpMode.Stretch: _rbStretch.Checked = true; break;
                default: _rbFill.Checked = true; break;
            }

            _chkEnable.Checked = _settings.HotkeyEnabled;
            _chkHold.Checked = _settings.HotkeyHold;

            _inputHook.TriggerType = _settings.TriggerType;
            _inputHook.BoundKey = (Keys)_settings.HotkeyKey;
            _inputHook.BoundMouseButton = (MouseButtons)_settings.HotkeyMouseButton;
            _inputHook.BoundModifiers = _settings.HotkeyModifiers;
            _lblHotkey.Text = _settings.TriggerType switch
            {
                TriggerType.Keyboard => FormatHotkey(_settings.HotkeyModifiers, ((Keys)_settings.HotkeyKey).ToString()),
                TriggerType.Mouse => FormatHotkey(_settings.HotkeyModifiers, $"{(MouseButtons)_settings.HotkeyMouseButton} Click"),
                _ => "(none)"
            };

            _themeSlider.Value = _settings.DarkTheme ? 1 : 0;
            UpdateThemeLabels();
            ApplyTheme(_settings.DarkTheme);

            UpdateMonitorHighlights();
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveSettings()
    {
        if (_loadingSettings) return;

        _settings.SourceDeviceName = _sourceIndex.HasValue ? _screens[_sourceIndex.Value].DeviceName : null;
        _settings.DestDeviceName = _destIndex.HasValue ? _screens[_destIndex.Value].DeviceName : null;
        _settings.ScalePercent = _scaleTrack.Value;
        _settings.Warp = SelectedWarp;
        _settings.HotkeyEnabled = _chkEnable.Checked;
        _settings.HotkeyHold = _chkHold.Checked;
        _settings.TriggerType = _inputHook.TriggerType;
        _settings.HotkeyKey = (int)_inputHook.BoundKey;
        _settings.HotkeyMouseButton = (int)_inputHook.BoundMouseButton;
        _settings.HotkeyModifiers = _inputHook.BoundModifiers;
        _settings.DarkTheme = _themeSlider.Value == 1;

        SettingsStore.Save(_settings);
    }

    // --- Theme ---

    private void UpdateThemeLabels()
    {
        bool dark = _themeSlider.Value == 1;
        _lblThemeLight.Font = new Font(_lblThemeLight.Font, dark ? FontStyle.Regular : FontStyle.Bold);
        _lblThemeDark.Font = new Font(_lblThemeDark.Font, dark ? FontStyle.Bold : FontStyle.Regular);
    }

    private void ApplyTheme(bool dark)
    {
        _dark = dark;

        var back = dark ? Color.FromArgb(32, 32, 32) : SystemColors.Control;
        var fore = dark ? System.Drawing.Color.Gainsboro : SystemColors.ControlText;
        ApplyThemeRecursive(this, back, fore, dark);
        UpdateMonitorHighlights();
        TrySetDarkTitleBar(dark);
    }

    private static void ApplyThemeRecursive(Control root, Color back, Color fore, bool dark)
    {
        root.BackColor = back;
        root.ForeColor = fore;

        foreach (Control c in root.Controls)
        {
            if (c is PictureBox)
            {
                // thumbnails keep their own black background regardless of theme
            }
            else if (c is Button btn)
            {
                btn.FlatStyle = dark ? FlatStyle.Flat : FlatStyle.Standard;
                btn.BackColor = dark ? Color.FromArgb(60, 60, 63) : SystemColors.Control;
                btn.ForeColor = fore;
                if (dark) btn.FlatAppearance.BorderColor = Color.FromArgb(90, 90, 95);
            }
            else
            {
                c.BackColor = back;
                c.ForeColor = fore;
            }

            if (c.HasChildren) ApplyThemeRecursive(c, back, fore, dark);
        }
    }

    private void TrySetDarkTitleBar(bool dark)
    {
        try
        {
            int value = dark ? 1 : 0;
            DwmSetWindowAttribute(Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        }
        catch
        {
            // unsupported OS version; title bar just stays light
        }
    }

    // --- Tray / lifecycle ---

    private void RestoreFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    private void ExitApplication()
    {
        Close();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            Hide();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        SaveSettings();
        _inputHook.Dispose();
        _trayIcon.Visible = false;
        _mirror?.Dispose();
        base.OnFormClosing(e);
    }
}
