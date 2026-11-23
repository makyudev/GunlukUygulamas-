using System.Globalization;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Threading;
using GunlukUygulamasi.Models;
using Microsoft.Win32;

namespace GunlukUygulamasi.Services;

public sealed class ReminderService : IDisposable
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "GunlukUygulamasi";

    private readonly Action _openWindow;
    private readonly Action _exitApplication;
    private readonly Func<DateTime, bool> _hasEntryForDate;
    private readonly DispatcherTimer _timer;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly Font _menuFont;
    private readonly Icon _trayIcon;
    private readonly TrayProfessionalColorTable _colorTable;
    private readonly ThemeService _themeService;
    private AppSettings _settings = new();
    private DateTime? _lastReminderDate;

    public ReminderService(
        Action openWindow,
        Action exitApplication,
        Func<DateTime, bool> hasEntryForDate,
        ThemeService themeService)
    {
        _openWindow = openWindow;
        _exitApplication = exitApplication;
        _hasEntryForDate = hasEntryForDate;
        _themeService = themeService;
        _menuFont = new Font("Segoe UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
        _trayIcon = LoadApplicationIcon();

        _colorTable = new TrayProfessionalColorTable();
        _contextMenu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = false,
            RenderMode = ToolStripRenderMode.Professional,
            Renderer = new ToolStripProfessionalRenderer(_colorTable),
            Padding = new Padding(5),
            Font = _menuFont,
            DropShadowEnabled = true
        };
        _contextMenu.Items.Add(CreateMenuItem("Günlüğü Aç", _openWindow));
        _contextMenu.Items.Add(CreateMenuItem("Çıkış", _exitApplication));
        ApplyTheme(_themeService.GetTrayThemeColors());
        _themeService.ThemeApplied += ApplyTheme;

        _notifyIcon = new NotifyIcon
        {
            Icon = _trayIcon,
            Text = "Kişisel Günlüğüm",
            ContextMenuStrip = _contextMenu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => _openWindow();
        _notifyIcon.BalloonTipClicked += (_, _) => _openWindow();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timer.Tick += CheckReminder;
    }

    public void SetStartWithWindows(bool enabled)
    {
        using var runKey = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("Windows başlangıç kayıt anahtarı açılamadı.");

        if (!enabled)
        {
            runKey.DeleteValue(RunValueName, throwOnMissingValue: false);
            return;
        }

        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Uygulamanın çalıştırılabilir dosya yolu bulunamadı.");
        }

        runKey.SetValue(RunValueName, $"\"{executablePath}\" --minimized", RegistryValueKind.String);
    }

    public void UpdateSettings(AppSettings settings)
    {
        _settings = settings;
        if (_settings.DailyReminderEnabled)
        {
            _timer.Start();
            CheckReminder(this, EventArgs.Empty);
        }
        else
        {
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        _themeService.ThemeApplied -= ApplyTheme;
        _timer.Stop();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _menuFont.Dispose();
        _trayIcon.Dispose();
    }

    private static ToolStripMenuItem CreateMenuItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            AutoSize = false,
            Size = new Size(190, 38),
            Margin = new Padding(2),
            Padding = new Padding(10, 7, 12, 7),
            TextAlign = ContentAlignment.MiddleLeft
        };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon LoadApplicationIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new InvalidOperationException("Uygulamanın simge dosyası için çalıştırılabilir yol bulunamadı.");
        }

        return Icon.ExtractAssociatedIcon(executablePath)
            ?? throw new InvalidOperationException("Uygulamanın çalıştırılabilir dosyasından simge alınamadı.");
    }

    private void ApplyTheme(ThemeService.TrayThemeColors colors)
    {
        var background = ToDrawingColor(colors.Background);
        var border = ToDrawingColor(colors.Border);
        var hoverBackground = ToDrawingColor(colors.HoverBackground);
        var foreground = ToDrawingColor(colors.Foreground);
        var accent = ToDrawingColor(colors.Accent);

        _colorTable.Update(background, border, hoverBackground, accent);
        _contextMenu.BackColor = background;
        _contextMenu.ForeColor = foreground;
        foreach (ToolStripMenuItem item in _contextMenu.Items)
        {
            item.BackColor = background;
            item.ForeColor = foreground;
        }

        _contextMenu.Invalidate();
    }

    private static Color ToDrawingColor(System.Windows.Media.Color color) =>
        Color.FromArgb(color.A, color.R, color.G, color.B);

    private void CheckReminder(object? sender, EventArgs e)
    {
        if (!_settings.DailyReminderEnabled ||
            !TimeOnly.TryParseExact(_settings.ReminderTime, "HH:mm",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var reminderTime))
        {
            return;
        }

        var now = DateTime.Now;
        if (now.TimeOfDay < reminderTime.ToTimeSpan() ||
            _lastReminderDate == now.Date ||
            _hasEntryForDate(now.Date))
        {
            return;
        }

        _lastReminderDate = now.Date;
        _notifyIcon.BalloonTipTitle = "Kişisel Günlüğüm";
        _notifyIcon.BalloonTipText = "Bugünü henüz not almadın, birkaç dakikanı ayırmak ister misin?";
        _notifyIcon.BalloonTipIcon = System.Windows.Forms.ToolTipIcon.Info;
        _notifyIcon.ShowBalloonTip(8000);
    }

    private sealed class TrayProfessionalColorTable : ProfessionalColorTable
    {
        private Color _background = Color.FromArgb(25, 31, 45);
        private Color _border = Color.FromArgb(42, 50, 71);
        private Color _hoverBackground = Color.FromArgb(36, 44, 63);
        private Color _accent = Color.FromArgb(99, 102, 241);

        public override Color ToolStripDropDownBackground => _background;
        public override Color ImageMarginGradientBegin => _background;
        public override Color ImageMarginGradientMiddle => _background;
        public override Color ImageMarginGradientEnd => _background;
        public override Color MenuBorder => _border;
        public override Color ToolStripBorder => _border;
        public override Color MenuItemSelected => _hoverBackground;
        public override Color MenuItemSelectedGradientBegin => _hoverBackground;
        public override Color MenuItemSelectedGradientEnd => _hoverBackground;
        public override Color MenuItemPressedGradientBegin => _hoverBackground;
        public override Color MenuItemPressedGradientMiddle => _hoverBackground;
        public override Color MenuItemPressedGradientEnd => _hoverBackground;
        public override Color MenuItemBorder => _accent;

        public void Update(Color background, Color border, Color hoverBackground, Color accent)
        {
            _background = background;
            _border = border;
            _hoverBackground = hoverBackground;
            _accent = accent;
        }
    }
}
