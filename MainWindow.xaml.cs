using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using GunlukUygulamasi.Models;
using GunlukUygulamasi.Services;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace GunlukUygulamasi;

public partial class MainWindow : Window
{
    private static readonly MoodDefinition[] MoodDefinitions =
    [
        new("Mutlu", "#FBBF24", 0, 0),
        new("Sakin", "#34D399", 1, 5),
        new("Odaklı", "#60A5FA", 5, 0),
        new("Yorgun", "#A78BFA", 3, 5),
        new("Stresli", "#FB7185", 6, 1),
        new("Üzgün", "#38BDF8", 1, 1)
    ];
    private static readonly Dictionary<string, BitmapSource> MoodIconCache = new(StringComparer.Ordinal);
    private static readonly object MoodIconCacheLock = new();

    private static readonly string[] FontFamilies =
    [
        "Segoe UI", "Arial", "Georgia", "Consolas", "Times New Roman"
    ];

    private static readonly BrushOption[] BrushOptions =
    [
        new("Beyaz", "#FFFFFF"),
        new("Sarı", "#FDE047"),
        new("Kırmızı", "#FB7185"),
        new("Mavi", "#60A5FA"),
        new("Yeşil", "#4ADE80"),
        new("Mor", "#A78BFA"),
        new("Turkuaz", "#22D3EE"),
        new("Turuncu", "#FB923C")
    ];

    private const int MaximumStrokeHistory = 100;
    private const int MaximumDecodedPhotoWidth = 1200;

    private static readonly HashSet<string> SupportedImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    private readonly DiaryStorageService _storage = new();
    private readonly DiarySecurityService _security;
    private readonly ObservableCollection<DiaryEntry> _entries = [];
    private readonly Dictionary<DiaryPhoto, PhotoVisual> _photoVisuals = [];
    private readonly Dictionary<DiaryPhoto, double> _photoAspectRatios = [];
    private readonly Stack<StrokeHistoryAction> _strokeUndoStack = [];
    private readonly Stack<StrokeHistoryAction> _strokeRedoStack = [];
    private readonly Dictionary<string, bool> _archiveGroupExpansionStates = new(StringComparer.Ordinal);
    private readonly MoodOption[] _moods;
    private readonly DispatcherTimer _autoSaveTimer;
    private readonly DispatcherTimer _savedNotificationTimer;
    private readonly UpdateService _updateService = new();
    private AppSettings _settings = new();
    private DiaryEntry? _selectedEntry;
    private ICollectionView? _entriesView;
    private byte[]? _encryptionKey;
    private bool _isLoadingEntry;
    private bool _hasUnsavedChanges;
    private bool _isFirstRunSetup;
    private bool _isUpdatingList;
    private bool _isRefreshingEntryView;
    private bool _isArchiveSearchActive;
    private bool _isSettingArchiveExpansion;
    private bool _isClosingModal;
    private bool _brushMode;
    private bool _isEraserGesture;
    private bool _isSidebarCollapsed;
    private bool _isSidebarAnimating;
    private bool _isCompletingAuthentication;
    private bool _isCheckingForUpdates;
    private bool _hasShownAutomaticUpdate;
    private bool _hasStartedAutomaticUpdateCheck;
    private int _authenticationAttemptVersion;
    private int _authenticationScreenGeneration;
    private DrawingTool _drawingTool;
    private string _brushColor = "#FFFFFF";
    private List<DiaryStroke>? _activeStrokeCollection;
    private DiaryStroke[]? _strokeGestureBefore;
    private Point? _lastEraserPoint;
    private DiaryPhoto? _draggedPhoto;
    private Border? _draggedPhotoBorder;
    private DiaryPhoto? _selectedPhoto;
    private DiaryPhoto? _resizingPhoto;
    private Thumb? _resizingPhotoThumb;
    private Border? _photoResizePreviewBorder;
    private ScaleTransform? _photoResizePreviewTransform;
    private Point _resizeStartMousePoint;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private double _resizePreviewWidth;
    private double _resizePreviewHeight;
    private Point _photoDragOffset;
    private DiaryPhoto? _drawingPhoto;
    private Border? _drawingPhotoBorder;
    private DiaryStroke? _activeStroke;
    private Polyline? _activeStrokeLine;
    private Ellipse? _activeStrokeDot;
    private DiaryStroke? _activeWritingStroke;
    private Polyline? _activeWritingStrokeLine;
    private Ellipse? _activeWritingStrokeDot;
    private string _activeModal = string.Empty;

    public MainWindow()
    {
        InitializeComponent();
        BrushSizeSlider.ValueChanged += BrushSizeSlider_ValueChanged;
        _security = new DiarySecurityService(_storage.DataDirectory);
        _moods = CreateMoodOptions();
        EntriesListBox.ItemsSource = _entries;
        _entriesView = CollectionViewSource.GetDefaultView(_entries);
        _entriesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DiaryEntry.ArchiveGroupKey))
        {
            CustomSort = StringComparer.Ordinal
        });
        EntryMoodComboBox.ItemsSource = _moods;
        NewEntryMoodComboBox.ItemsSource = _moods;
        EntryMoodComboBox.SelectedValuePath = nameof(MoodOption.Name);
        NewEntryMoodComboBox.SelectedValuePath = nameof(MoodOption.Name);
        SettingsFontFamilyComboBox.ItemsSource = FontFamilies;
        BuildBrushPalette();
        UpdateDrawingToolButtons();
        UpdateBrushSizePreview();
        UpdateStrokeHistoryButtons();

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _autoSaveTimer.Tick += AutoSaveTimer_Tick;
        _savedNotificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _savedNotificationTimer.Tick += (_, _) =>
        {
            _savedNotificationTimer.Stop();
            if (_selectedEntry is not null)
            {
                LastSavedText.Text = $"Son kaydetme: {_selectedEntry.FormattedUpdatedAt}";
            }
        };

        try
        {
            _settings = _storage.LoadSettings();
            if (!_security.HasConfiguration)
            {
                ShowAuthenticationScreen(isFirstRunSetup: true);
            }
            else if (_security.PasswordEnabled)
            {
                ShowAuthenticationScreen(isFirstRunSetup: false);
            }
            else
            {
                CompleteAuthentication(_security.UnlockWithoutPassword());
            }
        }
        catch (Exception exception)
        {
            ShowError("Günlük güvenlik bilgileri açılamadı.", exception);
            Close();
        }
    }

    private void ShowAuthenticationScreen(bool isFirstRunSetup)
    {
        Interlocked.Increment(ref _authenticationScreenGeneration);
        Interlocked.Increment(ref _authenticationAttemptVersion);
        _isCompletingAuthentication = false;
        _isFirstRunSetup = isFirstRunSetup;
        MainWorkspaceGrid.Visibility = Visibility.Visible;
        AuthenticationBlurEffect.BeginAnimation(BlurEffect.RadiusProperty, null);
        AuthenticationBlurEffect.Radius = 8;
        AuthenticationOverlayGrid.Visibility = Visibility.Visible;
        AuthenticationOverlayGrid.Opacity = 0;
        AuthenticationOverlayGrid.IsHitTestVisible = true;
        AuthenticationCard.Opacity = 0;
        AuthenticationTitleText.Text = isFirstRunSetup ? "Günlüğünü koru" : "Günlüğünüz güvende";
        var now = DateTime.Now;
        AuthenticationGreetingText.Text = now.Hour is >= 5 and < 12
            ? "Günaydın"
            : now.Hour is >= 12 and < 18 ? "İyi Günler" : "İyi Akşamlar";
        AuthenticationDateText.Text = now.ToString("dddd, d MMMM yyyy", CultureInfo.GetCultureInfo("tr-TR"));
        AuthenticationSubtitleText.Text = isFirstRunSetup
            ? "Günlüğünüz için bir PIN veya parola belirleyin. İsterseniz korumayı daha sonra Ayarlar’dan değiştirebilirsiniz."
            : _security.PasswordEnabled
                ? "Devam etmek için PIN veya parolanızı girin."
                : "Günlüğünüze dönmek için Devam Et düğmesine basın.";
        AuthenticationPasswordBox.Password = string.Empty;
        AuthenticationVisiblePasswordTextBox.Clear();
        AuthenticationVisiblePasswordTextBox.Visibility = Visibility.Collapsed;
        AuthenticationPasswordBox.Visibility = isFirstRunSetup || _security.PasswordEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        AuthenticationPasswordVisibilityButton.Visibility = AuthenticationPasswordBox.Visibility;
        AuthenticationEyePath.Data = Geometry.Parse(
            "M12,5C7,5 2.73,8.11 1,12C2.73,15.89 7,19 12,19C17,19 21.27,15.89 23,12C21.27,8.11 17,5 12,5M12,16.5A4.5,4.5 0 1,1 12,7.5A4.5,4.5 0 0,1 12,16.5M12,14A2,2 0 1,0 12,10A2,2 0 0,0 12,14");
        AuthenticationConfirmationBox.Password = string.Empty;
        AuthenticationConfirmationBox.Visibility = isFirstRunSetup ? Visibility.Visible : Visibility.Collapsed;
        AuthenticationCapsLockText.Visibility = Visibility.Collapsed;
        var hint = !isFirstRunSetup && _security.PasswordEnabled ? _settings.PasswordHint?.Trim() : null;
        AuthenticationHintText.Text = string.IsNullOrWhiteSpace(hint) ? string.Empty : $"İpucu: {hint}";
        AuthenticationHintText.Visibility = string.IsNullOrWhiteSpace(hint)
            ? Visibility.Collapsed
            : Visibility.Visible;
        AuthenticationActionButton.Content = isFirstRunSetup
            ? "Güvenli Günlüğü Oluştur"
            : _security.PasswordEnabled ? "Kilidi Aç" : "Devam Et";
        AuthenticationActionButton.Visibility = isFirstRunSetup || !_security.PasswordEnabled
            ? Visibility.Visible
            : Visibility.Collapsed;
        AuthenticationActionButton.IsDefault = AuthenticationActionButton.Visibility == Visibility.Visible;
        SkipPasswordSetupButton.Visibility = isFirstRunSetup ? Visibility.Visible : Visibility.Collapsed;
        AuthenticationErrorText.Text = string.Empty;

        AuthenticationCardScaleTransform.ScaleX = 0.96;
        AuthenticationCardScaleTransform.ScaleY = 0.96;
        AuthenticationCardTranslateTransform.X = 0;
        AuthenticationCardTranslateTransform.Y = 12;
        if (IsLoaded)
        {
            AnimateAuthenticationScreen(this, new RoutedEventArgs());
        }
        else
        {
            Loaded += AnimateAuthenticationScreen;
        }
    }

    private void AnimateAuthenticationScreen(object sender, RoutedEventArgs e)
    {
        Loaded -= AnimateAuthenticationScreen;
        AuthenticationOverlayGrid.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        AuthenticationCard.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        AuthenticationCardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        AuthenticationCardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        AuthenticationCardTranslateTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        AuthenticationBlurEffect.BeginAnimation(BlurEffect.RadiusProperty,
            new DoubleAnimation(0, 8, TimeSpan.FromMilliseconds(320))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        if (AuthenticationVisiblePasswordTextBox.Visibility == Visibility.Visible)
        {
            Keyboard.Focus(AuthenticationVisiblePasswordTextBox);
        }
        else if (AuthenticationPasswordBox.Visibility == Visibility.Visible)
        {
            Keyboard.Focus(AuthenticationPasswordBox);
        }
        else
        {
            AuthenticationActionButton.Focus();
        }
        UpdateCapsLockWarning();
    }

    private void AuthenticationPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _ = CheckAuthenticationPasswordAsync();

    private void AuthenticationVisiblePasswordTextBox_TextChanged(object sender, TextChangedEventArgs e) =>
        _ = CheckAuthenticationPasswordAsync();

    private async Task CheckAuthenticationPasswordAsync()
    {
        if (_isFirstRunSetup || AuthenticationOverlayGrid.Visibility != Visibility.Visible ||
            _isCompletingAuthentication ||
            (AuthenticationPasswordBox.Visibility != Visibility.Visible &&
             AuthenticationVisiblePasswordTextBox.Visibility != Visibility.Visible))
        {
            return;
        }

        var password = AuthenticationPasswordBox.Visibility == Visibility.Visible
            ? AuthenticationPasswordBox.Password
            : AuthenticationVisiblePasswordTextBox.Text;
        var attemptVersion = Interlocked.Increment(ref _authenticationAttemptVersion);
        var screenGeneration = Volatile.Read(ref _authenticationScreenGeneration);
        if (password.Length == 0)
        {
            return;
        }

        try
        {
            await Task.Delay(90);
            if (attemptVersion != Volatile.Read(ref _authenticationAttemptVersion))
            {
                return;
            }

            var attempt = await Task.Run(() =>
            {
                var success = _security.TryUnlock(password, out var key);
                return (Success: success, DataKey: key);
            });

            if (attempt.DataKey is null)
            {
                if (!attempt.Success)
                {
                    await Task.Delay(420);
                    if (attemptVersion == Volatile.Read(ref _authenticationAttemptVersion) &&
                        screenGeneration == Volatile.Read(ref _authenticationScreenGeneration) &&
                        AuthenticationOverlayGrid.Visibility == Visibility.Visible &&
                        password.Length >= 6)
                    {
                        ShakeAuthenticationCard();
                    }
                }

                return;
            }

            if (!attempt.Success ||
                _isCompletingAuthentication ||
                screenGeneration != Volatile.Read(ref _authenticationScreenGeneration) ||
                AuthenticationOverlayGrid.Visibility != Visibility.Visible ||
                _isFirstRunSetup)
            {
                CryptographicOperations.ZeroMemory(attempt.DataKey);
                return;
            }

            CompleteAuthentication(attempt.DataKey);
        }
        catch (Exception exception)
        {
            if (screenGeneration == Volatile.Read(ref _authenticationScreenGeneration) &&
                AuthenticationOverlayGrid.Visibility == Visibility.Visible)
            {
                ShowError("Günlük kilidi açılamadı.", exception);
            }
        }
    }

    private void AuthenticationPasswordVisibilityButton_Click(object sender, RoutedEventArgs e)
    {
        if (AuthenticationPasswordBox.Visibility == Visibility.Visible)
        {
            AuthenticationVisiblePasswordTextBox.Text = AuthenticationPasswordBox.Password;
            AuthenticationPasswordBox.Visibility = Visibility.Collapsed;
            AuthenticationVisiblePasswordTextBox.Visibility = Visibility.Visible;
            AuthenticationEyePath.Data = Geometry.Parse(
                "M3,3L21,21 M10.58,10.59A2,2 0 0,0 13.41,13.42 M9.88,5.09A10.94,10.94 0 0,1 12,4.88C17,4.88 21.27,8 23,11.88A11.83,11.83 0 0,1 18.84,16.8 M6.61,6.61A11.78,11.78 0 0,0 1,11.88C2.73,15.76 7,18.88 12,18.88C13.27,18.88 14.48,18.65 15.58,18.24");
            Keyboard.Focus(AuthenticationVisiblePasswordTextBox);
            AuthenticationVisiblePasswordTextBox.CaretIndex = AuthenticationVisiblePasswordTextBox.Text.Length;
        }
        else
        {
            AuthenticationPasswordBox.Password = AuthenticationVisiblePasswordTextBox.Text;
            AuthenticationVisiblePasswordTextBox.Visibility = Visibility.Collapsed;
            AuthenticationPasswordBox.Visibility = Visibility.Visible;
            AuthenticationEyePath.Data = Geometry.Parse(
                "M12,5C7,5 2.73,8.11 1,12C2.73,15.89 7,19 12,19C17,19 21.27,15.89 23,12C21.27,8.11 17,5 12,5M12,16.5A4.5,4.5 0 1,1 12,7.5A4.5,4.5 0 0,1 12,16.5M12,14A2,2 0 1,0 12,10A2,2 0 0,0 12,14");
            Keyboard.Focus(AuthenticationPasswordBox);
        }
    }

    private void AuthenticationInput_PreviewKeyDown(object sender, KeyEventArgs e) => UpdateCapsLockWarning();

    private void AuthenticationInput_PreviewKeyUp(object sender, KeyEventArgs e) => UpdateCapsLockWarning();

    private void AuthenticationInput_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        UpdateCapsLockWarning();

    private void UpdateCapsLockWarning()
    {
        AuthenticationCapsLockText.Visibility = Keyboard.IsKeyToggled(Key.CapsLock)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ShakeAuthenticationCard()
    {
        var shake = new DoubleAnimation(-8, 8, TimeSpan.FromMilliseconds(55))
        {
            AutoReverse = true,
            RepeatBehavior = new RepeatBehavior(2),
            FillBehavior = FillBehavior.Stop
        };
        AuthenticationCardTranslateTransform.BeginAnimation(TranslateTransform.XProperty, shake);
    }

    private void AuthenticationActionButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var password = AuthenticationPasswordBox.Password;
            if (!_isFirstRunSetup && !_security.PasswordEnabled)
            {
                CompleteAuthentication(_security.UnlockWithoutPassword());
                return;
            }

            if (_isFirstRunSetup)
            {
                if (password.Length < 6)
                {
                    AuthenticationErrorText.Text = "PIN/parola en az 6 karakter olmalıdır.";
                    return;
                }

                if (!string.Equals(password, AuthenticationConfirmationBox.Password, StringComparison.Ordinal))
                {
                    AuthenticationErrorText.Text = "PIN/parola doğrulaması eşleşmiyor.";
                    AuthenticationConfirmationBox.Clear();
                    AuthenticationConfirmationBox.Focus();
                    return;
                }

                CompleteAuthentication(_security.InitializeWithPassword(password));
                return;
            }

            if (_security.TryUnlock(password, out var key) && key is not null)
            {
                CompleteAuthentication(key);
                return;
            }

            return;
        }
        catch (Exception exception)
        {
            AuthenticationErrorText.Text = "Güvenlik bilgileri işlenemedi.";
            ShowError("Günlük kilidi açılamadı.", exception);
        }
    }

    private void SkipPasswordSetupButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            CompleteAuthentication(_security.InitializeWithoutPassword());
        }
        catch (Exception exception)
        {
            ShowError("PIN koruması olmadan güvenli günlük başlatılamadı.", exception);
        }
    }

    private void ExitAuthenticationButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void CompleteAuthentication(byte[] key)
    {
        if (_isCompletingAuthentication)
        {
            CryptographicOperations.ZeroMemory(key);
            return;
        }

        _isCompletingAuthentication = true;
        _encryptionKey = key;
        try
        {
            if (AuthenticationOverlayGrid.Visibility == Visibility.Visible)
            {
                AuthenticationPasswordBox.Clear();
                AuthenticationVisiblePasswordTextBox.Clear();
                AuthenticationConfirmationBox.Clear();
                AuthenticationOverlayGrid.IsHitTestVisible = false;

                var cardFade = AnimateAsync(AuthenticationCard, OpacityProperty,
                    new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(210))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
                var cardSlide = AnimateAsync(AuthenticationCardTranslateTransform, TranslateTransform.YProperty,
                    new DoubleAnimation(0, -16, TimeSpan.FromMilliseconds(250))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
                var overlayFade = AnimateAsync(AuthenticationOverlayGrid, OpacityProperty,
                    new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                    });
                var blurFade = AnimateAsync(AuthenticationBlurEffect, BlurEffect.RadiusProperty,
                    new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(300))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                    });
                await Task.WhenAll(cardFade, cardSlide, overlayFade, blurFade);

                AuthenticationOverlayGrid.Visibility = Visibility.Collapsed;
                AuthenticationOverlayGrid.IsHitTestVisible = true;
                AuthenticationOverlayGrid.BeginAnimation(OpacityProperty, null);
                AuthenticationCard.BeginAnimation(OpacityProperty, null);
                AuthenticationCardTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
                AuthenticationCardScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                AuthenticationCardScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                AuthenticationCardTranslateTransform.X = 0;
                AuthenticationCardTranslateTransform.Y = 0;
                AuthenticationCardScaleTransform.ScaleX = 1;
                AuthenticationCardScaleTransform.ScaleY = 1;
                AuthenticationCard.Opacity = 1;
                AuthenticationOverlayGrid.Opacity = 0;
                AuthenticationBlurEffect.BeginAnimation(BlurEffect.RadiusProperty, null);
                AuthenticationBlurEffect.Radius = 0;
            }

            MainWorkspaceGrid.Visibility = Visibility.Visible;
            LoadApplicationData();
            _autoSaveTimer.Start();
            _ = Dispatcher.BeginInvoke(
                new Action(() => _ = CheckForUpdatesAutomaticallyAsync()), DispatcherPriority.Background);
        }
        catch (Exception exception)
        {
            if (_encryptionKey is not null)
            {
                CryptographicOperations.ZeroMemory(_encryptionKey);
                _encryptionKey = null;
            }

            ShowError("Günlük ekranı açılamadı.", exception);
        }
        finally
        {
            _isCompletingAuthentication = false;
        }
    }

    private static Task AnimateAsync(IAnimatable target, DependencyProperty property, DoubleAnimation animation)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        animation.Completed += (_, _) => completion.TrySetResult();
        target.BeginAnimation(property, animation);
        return completion.Task;
    }

    private void LoadApplicationData()
    {
        try
        {
            _entries.Clear();
            _selectedEntry = null;
            _photoVisuals.Clear();
            _photoAspectRatios.Clear();
            PhotoCanvas.Children.Clear();
            WritingInkCanvas.Children.Clear();
            EntriesListBox.SelectedItem = null;
            UpdateEditorState();

            _settings = _storage.LoadSettings();
            _settings.DiaryName = string.IsNullOrWhiteSpace(_settings.DiaryName)
                ? "Kişisel Günlüğüm"
                : _settings.DiaryName.Trim();
            _settings.PasswordHint = _settings.PasswordHint?.Trim() ?? string.Empty;
            if (!FontFamilies.Contains(_settings.EditorFontFamily, StringComparer.Ordinal))
            {
                _settings.EditorFontFamily = "Segoe UI";
            }

            if (_settings.EditorFontSize is < 10 or > 32)
            {
                _settings.EditorFontSize = 15;
            }

            SidebarDiaryTitleText.Text = _settings.DiaryName;
            Title = _settings.DiaryName;
            EntryContentTextBox.FontSize = _settings.EditorFontSize;
            EntryContentTextBox.FontFamily = new FontFamily(_settings.EditorFontFamily);

            var entries = _storage.LoadEntries(_encryptionKey!);
            foreach (var entry in entries)
            {
                entry.Tags ??= [];
                entry.PhotoPaths ??= [];
                entry.PhotoItems ??= [];
                entry.ContentStrokes ??= [];
                foreach (var stroke in entry.ContentStrokes)
                {
                    NormalizeStroke(stroke);
                }

                foreach (var photo in entry.PhotoItems)
                {
                    photo.Strokes ??= [];
                    foreach (var stroke in photo.Strokes)
                    {
                        NormalizeStroke(stroke);
                    }
                }

                if (entry.PhotoItems.Count == 0)
                {
                    for (var index = 0; index < entry.PhotoPaths.Count; index++)
                    {
                        entry.PhotoItems.Add(new DiaryPhoto
                        {
                            Path = entry.PhotoPaths[index],
                            X = 40 + (index % 4) * 28,
                            Y = 40 + (index % 4) * 28
                        });
                    }
                }

                entry.PhotoPaths = entry.PhotoItems.Select(photo => photo.Path).ToList();
                entry.Mood = NormalizeMood(entry.Mood);
                _entries.Add(entry);
            }

            ApplySortOrder();
            UpdateEntryListState();
        }
        catch (Exception exception)
        {
            ShowError("Günlük verileri yüklenemedi veya çözülemedi.", exception);
        }
    }

    private void NewEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_autoSaveTimer.IsEnabled)
        {
            return;
        }

        NewEntryTitleTextBox.Text = string.Empty;
        NewEntryDatePicker.SelectedDate = DateTime.Today;
        NewEntryMoodComboBox.SelectedValue = _moods[0].Name;
        NewEntryTagsTextBox.Text = string.Empty;
        NewEntryContentTextBox.Text = string.Empty;
        ShowModal("new-entry");
        NewEntryTitleTextBox.Focus();
    }

    private void SaveNewEntryButton_Click(object sender, RoutedEventArgs e)
    {
        var entry = new DiaryEntry
        {
            Title = NewEntryTitleTextBox.Text.Trim(),
            Date = NewEntryDatePicker.SelectedDate ?? DateTime.Today,
            Mood = NewEntryMoodComboBox.SelectedValue as string ?? _moods[0].Name,
            Tags = ParseTags(NewEntryTagsTextBox.Text),
            Content = NewEntryContentTextBox.Text.Trim(),
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _entries.Add(entry);
        ApplySortOrder();
        EntriesListBox.SelectedItem = entry;
        _hasUnsavedChanges = true;
        if (!SaveCurrentEntry())
        {
            _entries.Remove(entry);
            EntriesListBox.SelectedItem = null;
            return;
        }

        CloseModal();
        UpdateEntryListState();
    }

    private void CancelModalButton_Click(object sender, RoutedEventArgs e) => CloseModal();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsDiaryNameTextBox.Text = _settings.DiaryName;
        SettingsPasswordHintTextBox.Text = _settings.PasswordHint ?? string.Empty;
        SettingsFontSizeTextBox.Text = _settings.EditorFontSize.ToString("0.#", CultureInfo.InvariantCulture);
        SettingsFontFamilyComboBox.SelectedItem = _settings.EditorFontFamily;
        SettingsSortNewestFirstCheckBox.IsChecked = _settings.SortNewestFirst;
        SettingsConfirmDeleteCheckBox.IsChecked = _settings.ConfirmDelete;
        DisablePasswordCheckBox.IsChecked = false;
        SettingsNewPasswordBox.Clear();
        SettingsConfirmPasswordBox.Clear();
        CurrentVersionTextBlock.Text = $"Mevcut sürüm: v{UpdateService.CurrentVersion.ToString(3)}";
        ShowModal("settings");
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginUpdateCheck())
        {
            return;
        }

        try
        {
            var release = await _updateService.GetLatestReleaseAsync();
            if (release is null)
            {
                MessageBox.Show(this, "GitHub'da henüz yayımlanmış bir sürüm bulunmuyor.",
                    "Güncelleme denetimi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else if (release.Version > UpdateService.CurrentVersion)
            {
                ShowUpdateWindow(release, showInstallButton: true);
            }
            else
            {
                MessageBox.Show(this, $"Uygulama güncel. Son yayınlanan sürüm: {release.TagName}",
                    "Güncelleme denetimi", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            ShowError("Güncellemeler denetlenemedi.", exception);
        }
        finally
        {
            EndUpdateCheck();
        }
    }

    private async void ViewReleaseNotesButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryBeginUpdateCheck())
        {
            return;
        }

        try
        {
            var release = await _updateService.GetLatestReleaseAsync();
            if (release is null)
            {
                MessageBox.Show(this, "GitHub'da görüntülenecek yayımlanmış bir sürüm bulunmuyor.",
                    "Güncelleme notları", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ShowUpdateWindow(release, showInstallButton: false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            ShowError("Güncelleme notları alınamadı.", exception);
        }
        finally
        {
            EndUpdateCheck();
        }
    }

    private async Task CheckForUpdatesAutomaticallyAsync()
    {
        if (_hasStartedAutomaticUpdateCheck || !TryBeginUpdateCheck())
        {
            return;
        }

        _hasStartedAutomaticUpdateCheck = true;
        try
        {
            var release = await _updateService.GetAvailableUpdateAsync();
            if (release is not null && IsLoaded && !_hasShownAutomaticUpdate)
            {
                _hasShownAutomaticUpdate = true;
                ShowUpdateWindow(release, showInstallButton: true);
            }
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
        }
        finally
        {
            EndUpdateCheck();
        }
    }

    private bool TryBeginUpdateCheck()
    {
        if (_isCheckingForUpdates)
        {
            return false;
        }

        _isCheckingForUpdates = true;
        CheckForUpdatesButton.IsEnabled = false;
        ViewReleaseNotesButton.IsEnabled = false;
        return true;
    }

    private void EndUpdateCheck()
    {
        _isCheckingForUpdates = false;
        CheckForUpdatesButton.IsEnabled = true;
        ViewReleaseNotesButton.IsEnabled = true;
    }

    private void ShowUpdateWindow(UpdateRelease release, bool showInstallButton)
    {
        new UpdateWindow(release, showInstallButton) { Owner = this }.ShowDialog();
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var diaryName = SettingsDiaryNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(diaryName))
        {
            ShowValidationError("Günlük adı boş bırakılamaz.", SettingsDiaryNameTextBox);
            return;
        }

        if (!double.TryParse(SettingsFontSizeTextBox.Text, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var fontSize) || fontSize is < 10 or > 32)
        {
            ShowValidationError("Yazı boyutu 10 ile 32 arasında olmalıdır.", SettingsFontSizeTextBox);
            return;
        }

        var newPassword = SettingsNewPasswordBox.Password;
        var confirmPassword = SettingsConfirmPasswordBox.Password;
        if (DisablePasswordCheckBox.IsChecked != true &&
            (newPassword.Length > 0 || confirmPassword.Length > 0) &&
            (newPassword.Length < 6 || !string.Equals(newPassword, confirmPassword, StringComparison.Ordinal)))
        {
            ShowValidationError("Yeni PIN/parola en az 6 karakter olmalı ve doğrulama alanıyla eşleşmelidir.",
                SettingsNewPasswordBox);
            return;
        }

        var previousSettings = _settings;
        _settings = new AppSettings
        {
            DiaryName = diaryName,
            PasswordHint = SettingsPasswordHintTextBox.Text.Trim(),
            EditorFontSize = fontSize,
            EditorFontFamily = SettingsFontFamilyComboBox.SelectedItem as string ?? "Segoe UI",
            SortNewestFirst = SettingsSortNewestFirstCheckBox.IsChecked == true,
            ConfirmDelete = SettingsConfirmDeleteCheckBox.IsChecked == true
        };

        try
        {
            _storage.SaveSettings(_settings);
            if (DisablePasswordCheckBox.IsChecked == true)
            {
                _security.SetPassword(_encryptionKey!, null);
            }
            else if (newPassword.Length > 0)
            {
                _security.SetPassword(_encryptionKey!, newPassword);
            }
        }
        catch (Exception exception)
        {
            _settings = previousSettings;
            ShowError("Ayarlar kaydedilemedi.", exception);
            return;
        }

        SidebarDiaryTitleText.Text = _settings.DiaryName;
        Title = _settings.DiaryName;
        EntryContentTextBox.FontSize = _settings.EditorFontSize;
        EntryContentTextBox.FontFamily = new FontFamily(_settings.EditorFontFamily);
        ApplySortOrder();
        SettingsNewPasswordBox.Clear();
        SettingsConfirmPasswordBox.Clear();
        CloseModal();
    }

    private void ShowValidationError(string message, Control control)
    {
        MessageBox.Show(this, message, "Geçersiz bilgi", MessageBoxButton.OK, MessageBoxImage.Warning);
        control.Focus();
    }

    private void DeleteEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        if (!_settings.ConfirmDelete)
        {
            DeleteSelectedEntry();
            return;
        }

        DeleteEntryConfirmationText.Text = $"“{_selectedEntry.DisplayTitle}” başlıklı günlük kalıcı olarak silinecek.";
        ShowModal("delete");
    }

    private void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e) => DeleteSelectedEntry();

    private void DeleteSelectedEntry()
    {
        if (_selectedEntry is null)
        {
            CloseModal();
            return;
        }

        var entry = _selectedEntry;
        var oldIndex = _entries.IndexOf(entry);
        _entries.Remove(entry);
        _selectedEntry = null;
        EntriesListBox.SelectedItem = null;

        if (!SaveEntries())
        {
            _entries.Insert(Math.Clamp(oldIndex, 0, _entries.Count), entry);
            EntriesListBox.SelectedItem = entry;
            return;
        }

        CloseModal();
        LoadSelectedEntry();
        UpdateEditorState();
        UpdateEntryListState();
    }

    private void EntriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingList || _isRefreshingEntryView)
        {
            return;
        }

        if (_hasUnsavedChanges && !SaveCurrentEntry())
        {
            _isUpdatingList = true;
            EntriesListBox.SelectedItem = _selectedEntry;
            _isUpdatingList = false;
            return;
        }

        _selectedEntry = EntriesListBox.SelectedItem as DiaryEntry;
        LoadSelectedEntry();
        UpdateEditorState();
    }

    private void LoadSelectedEntry()
    {
        _savedNotificationTimer.Stop();
        ClearSelectedPhoto();
        _strokeUndoStack.Clear();
        _strokeRedoStack.Clear();
        UpdateStrokeHistoryButtons();
        _isLoadingEntry = true;
        try
        {
            if (_selectedEntry is null)
            {
                EntryTitleTextBox.Text = string.Empty;
                EntryDatePicker.SelectedDate = null;
                EntryMoodComboBox.SelectedItem = null;
                EntryTagsTextBox.Text = string.Empty;
                EntryContentTextBox.Text = string.Empty;
                PhotoCanvas.Children.Clear();
                _photoVisuals.Clear();
                _photoAspectRatios.Clear();
                WritingInkCanvas.Children.Clear();
                WordCountText.Text = "0 kelime";
                CharacterCountText.Text = "0 karakter";
                LastSavedText.Text = string.Empty;
                _hasUnsavedChanges = false;
                return;
            }

            EntryTitleTextBox.Text = _selectedEntry.Title;
            EntryDatePicker.SelectedDate = _selectedEntry.Date;
            EntryMoodComboBox.SelectedValue = _selectedEntry.Mood;
            EntryTagsTextBox.Text = string.Join(" ", _selectedEntry.Tags.Select(tag => $"#{tag}"));
            EntryContentTextBox.Text = _selectedEntry.Content;
            EntryContentTextBox.FontSize = _settings.EditorFontSize;
            EntryContentTextBox.FontFamily = new FontFamily(_settings.EditorFontFamily);
            RenderEntryPhotos();
            RenderWritingStrokes();
            PinEntryLabelText.Text = _selectedEntry.IsPinned ? "Sabitlemeyi Kaldır" : "Sabitle";
            PinEntryButton.Foreground = (Brush)FindResource(
                _selectedEntry.IsPinned ? "AccentHoverBrush" : "TextPrimaryBrush");
            BrushModeLabelText.Text = _brushMode ? "Çizim: Açık" : "Çizim: Kapalı";
            UpdateEditorStatistics();
            LastSavedText.Text = $"Son kaydetme: {_selectedEntry.FormattedUpdatedAt}";
            _hasUnsavedChanges = false;
        }
        finally
        {
            _isLoadingEntry = false;
        }
    }

    private void EntryTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingEntry || _selectedEntry is null)
        {
            return;
        }

        _selectedEntry.Title = EntryTitleTextBox.Text;
        _selectedEntry.Content = EntryContentTextBox.Text;
        MarkEntryChanged();
        UpdateEditorStatistics();
    }

    private void EntryDatePicker_SelectedDateChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingEntry || _selectedEntry is null || EntryDatePicker.SelectedDate is not DateTime date)
        {
            return;
        }

        _selectedEntry.Date = date;
        MarkEntryChanged();
        ApplySortOrder();
    }

    private void EntryMoodComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingEntry || _selectedEntry is null || EntryMoodComboBox.SelectedValue is not string mood)
        {
            return;
        }

        _selectedEntry.Mood = mood;
        MarkEntryChanged();
        RefreshEntryFilter();
    }

    private void EntryTagsTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_isLoadingEntry || _selectedEntry is null)
        {
            return;
        }

        _selectedEntry.Tags = ParseTags(EntryTagsTextBox.Text);
        MarkEntryChanged();
        RefreshEntryFilter();
    }

    private void PinEntryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        _selectedEntry.IsPinned = !_selectedEntry.IsPinned;
        PinEntryLabelText.Text = _selectedEntry.IsPinned ? "Sabitlemeyi Kaldır" : "Sabitle";
        PinEntryButton.Foreground = (Brush)FindResource(
            _selectedEntry.IsPinned ? "AccentHoverBrush" : "TextPrimaryBrush");
        MarkEntryChanged();
        ApplySortOrder();
    }

    private void AddPhotoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Günlüğe fotoğraf ekle",
            Filter = "Resim dosyaları|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tüm dosyalar|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var start = GetDefaultPhotoPosition();
        AddPhotoFiles(dialog.FileNames, start);
    }

    private void BrushModeButton_Click(object sender, RoutedEventArgs e)
    {
        _brushMode = !_brushMode;
        BrushModeLabelText.Text = _brushMode ? "Çizim: Açık" : "Çizim: Kapalı";
        BrushModeButton.Background = (Brush)FindResource(_brushMode ? "AccentBrush" : "BgElevatedBrush");
        BrushModeButton.Foreground = _brushMode
            ? Brushes.White
            : (Brush)FindResource("TextPrimaryBrush");
        DrawingToolsPanel.Visibility = _brushMode ? Visibility.Visible : Visibility.Collapsed;
        var toolCursor = _drawingTool == DrawingTool.Eraser ? Cursors.Cross : Cursors.Pen;
        DiaryWritingSurface.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        EntryContentTextBox.Cursor = _brushMode ? toolCursor : Cursors.IBeam;
        WritingInkCanvas.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        WritingInkCanvas.IsHitTestVisible = _brushMode;
        PhotoCanvas.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        foreach (var visual in _photoVisuals.Values)
        {
            visual.Frame.Cursor = _brushMode ? toolCursor : Cursors.SizeAll;
        }
    }

    private void BuildBrushPalette()
    {
        foreach (var option in BrushOptions)
        {
            var button = new Button
            {
                Style = (Style)FindResource("BrushSwatchButtonStyle"),
                Background = (Brush)new BrushConverter().ConvertFromString(option.Hex)!,
                Tag = option.Hex,
                ToolTip = option.Name,
                Margin = new Thickness(0, 0, 6, 0)
            };
            button.Click += BrushColorButton_Click;
            BrushPalettePanel.Children.Add(button);
        }

        UpdateBrushColorSelection();
    }

    private void BrushColorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string color })
        {
            return;
        }

        _brushColor = color;
        UpdateBrushColorSelection();
        UpdateBrushSizePreview();
    }

    private void DrawingToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string tool } ||
            !Enum.TryParse(tool, out DrawingTool drawingTool))
        {
            return;
        }

        _drawingTool = drawingTool;
        UpdateDrawingToolButtons();
        UpdateBrushSizePreview();

        var toolCursor = _drawingTool == DrawingTool.Eraser ? Cursors.Cross : Cursors.Pen;
        DiaryWritingSurface.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        EntryContentTextBox.Cursor = _brushMode ? toolCursor : Cursors.IBeam;
        WritingInkCanvas.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        PhotoCanvas.Cursor = _brushMode ? toolCursor : Cursors.Arrow;
        foreach (var visual in _photoVisuals.Values)
        {
            visual.Frame.Cursor = _brushMode ? toolCursor : Cursors.SizeAll;
        }
    }

    private void UpdateDrawingToolButtons()
    {
        SetToolButtonState(PenToolButton, DrawingTool.Pen);
        SetToolButtonState(HighlighterToolButton, DrawingTool.Highlighter);
        SetToolButtonState(EraserToolButton, DrawingTool.Eraser);
    }

    private void SetToolButtonState(Button button, DrawingTool tool)
    {
        var selected = _drawingTool == tool;
        button.Background = (Brush)FindResource(selected ? "AccentBrush" : "BgElevatedBrush");
        button.BorderBrush = (Brush)FindResource(selected ? "AccentBrush" : "BorderSubtleBrush");
        button.Foreground = (Brush)FindResource(selected ? "TextPrimaryBrush" : "TextSecondaryBrush");
    }

    private void UpdateBrushColorSelection()
    {
        foreach (var button in BrushPalettePanel.Children.OfType<Button>())
        {
            var selected = string.Equals(button.Tag as string, _brushColor, StringComparison.OrdinalIgnoreCase);
            button.BorderBrush = (Brush)FindResource(selected ? "AccentBrush" : "BorderSubtleBrush");
            button.BorderThickness = new Thickness(selected ? 2 : 1);
        }
    }

    private void BrushSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e) =>
        UpdateBrushSizePreview();

    private void UpdateBrushSizePreview()
    {
        var size = _drawingTool switch
        {
            DrawingTool.Highlighter => BrushSizeSlider.Value * 2,
            DrawingTool.Eraser => BrushSizeSlider.Value * 2,
            _ => BrushSizeSlider.Value
        };
        size = Math.Clamp(size, 6, 32);
        BrushSizePreviewEllipse.Width = size;
        BrushSizePreviewEllipse.Height = size;

        var color = (Color)ColorConverter.ConvertFromString(_brushColor)!;
        if (_drawingTool == DrawingTool.Highlighter)
        {
            color.A = 150;
        }

        BrushSizePreviewEllipse.Fill = _drawingTool == DrawingTool.Eraser
            ? (Brush)FindResource("BgElevatedBrush")
            : new SolidColorBrush(color);
        BrushSizePreviewEllipse.Stroke = _drawingTool == DrawingTool.Eraser
            ? (Brush)FindResource("TextSecondaryBrush")
            : Brushes.Transparent;
        BrushSizePreviewEllipse.ToolTip =
            $"{(_drawingTool == DrawingTool.Eraser ? "Silgi" : "Araç")} boyutu: {BrushSizeSlider.Value:0.#}";
    }

    private void WritingInkCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_brushMode || _selectedEntry is null)
        {
            return;
        }

        _isEraserGesture = false;
        BeginStrokeGesture(_selectedEntry.ContentStrokes);
        if (_drawingTool == DrawingTool.Eraser)
        {
            _isEraserGesture = true;
            _lastEraserPoint = null;
            if (EraseAlongPath(_selectedEntry.ContentStrokes,
                    e.GetPosition(WritingInkCanvas), WritingInkCanvas.ActualWidth, WritingInkCanvas.ActualHeight))
            {
                RenderWritingStrokes();
                MarkEntryChanged();
            }

            WritingInkCanvas.CaptureMouse();
            WritingInkCanvas.Focus();
            e.Handled = true;
            return;
        }

        _activeWritingStroke = CreateCurrentStroke();
        _selectedEntry.ContentStrokes.Add(_activeWritingStroke);
        var point = ToNormalizedPoint(e.GetPosition(WritingInkCanvas),
            WritingInkCanvas.ActualWidth, WritingInkCanvas.ActualHeight);
        _activeWritingStroke.Points.Add(point);
        _activeWritingStrokeDot = CreateStrokeDot(
            _activeWritingStroke, point, WritingInkCanvas.ActualWidth, WritingInkCanvas.ActualHeight);
        WritingInkCanvas.Children.Add(_activeWritingStrokeDot);
        WritingInkCanvas.CaptureMouse();
        WritingInkCanvas.Focus();
        MarkEntryChanged();
        e.Handled = true;
    }

    private void WritingInkCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_isEraserGesture)
        {
            if (e.LeftButton == MouseButtonState.Pressed && _selectedEntry is not null &&
                EraseAlongPath(_selectedEntry.ContentStrokes, e.GetPosition(WritingInkCanvas),
                    WritingInkCanvas.ActualWidth, WritingInkCanvas.ActualHeight))
            {
                RenderWritingStrokes();
                MarkEntryChanged();
            }

            e.Handled = true;
            return;
        }

        if (_activeWritingStroke is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var width = WritingInkCanvas.ActualWidth;
        var height = WritingInkCanvas.ActualHeight;
        var point = ToNormalizedPoint(e.GetPosition(WritingInkCanvas), width, height);
        _activeWritingStroke.Points.Add(point);
        if (_activeWritingStrokeLine is null && _activeWritingStrokeDot is not null)
        {
            WritingInkCanvas.Children.Remove(_activeWritingStrokeDot);
            _activeWritingStrokeDot = null;
            _activeWritingStrokeLine = CreateStrokeLine(_activeWritingStroke);
            foreach (var strokePoint in _activeWritingStroke.Points)
            {
                _activeWritingStrokeLine.Points.Add(new Point(strokePoint.X * width, strokePoint.Y * height));
            }

            WritingInkCanvas.Children.Add(_activeWritingStrokeLine);
        }
        else
        {
            _activeWritingStrokeLine?.Points.Add(new Point(point.X * width, point.Y * height));
        }

        MarkEntryChanged();
        e.Handled = true;
    }

    private void WritingInkCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeWritingStroke is null)
        {
            if (!_isEraserGesture)
            {
                return;
            }
        }

        if (WritingInkCanvas.IsMouseCaptured)
        {
            WritingInkCanvas.ReleaseMouseCapture();
        }

        CompleteStrokeGesture();
        _isEraserGesture = false;
        _lastEraserPoint = null;
        _activeWritingStroke = null;
        _activeWritingStrokeLine = null;
        _activeWritingStrokeDot = null;
        e.Handled = true;
    }

    private void WritingInkCanvas_SizeChanged(object sender, SizeChangedEventArgs e) =>
        RenderWritingStrokes();

    private void RenderWritingStrokes()
    {
        WritingInkCanvas.Children.Clear();
        if (_selectedEntry is null || WritingInkCanvas.ActualWidth <= 0 || WritingInkCanvas.ActualHeight <= 0)
        {
            return;
        }

        RenderStrokes(_selectedEntry.ContentStrokes, WritingInkCanvas,
            WritingInkCanvas.ActualWidth, WritingInkCanvas.ActualHeight);
    }

    private void DiaryWritingSurface_PreviewDragOver(object sender, DragEventArgs e)
    {
        var files = e.Data.GetData(DataFormats.FileDrop) as string[];
        e.Effects = files is not null && files.Any(IsSupportedImage)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DiaryWritingSurface_PreviewDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPhotoFiles(files, e.GetPosition(PhotoCanvas));
        }

        e.Handled = true;
    }

    private void AddPhotoFiles(IEnumerable<string> paths, Point location)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        var offset = 0;
        foreach (var sourcePath in paths)
        {
            if (!IsSupportedImage(sourcePath))
            {
                continue;
            }

            try
            {
                var photo = new DiaryPhoto
                {
                    Path = _storage.CopyPhotoToStorage(sourcePath),
                    Width = 260,
                    X = Math.Max(0, location.X - 130 + offset),
                    Y = Math.Max(0, location.Y - 90 + offset)
                };
                _selectedEntry.PhotoItems.Add(photo);
                SyncPhotoPaths();
                RenderPhoto(photo);
                if (_photoVisuals.ContainsKey(photo))
                {
                    SelectPhoto(photo);
                }

                MarkEntryChanged();
                offset += 24;
            }
            catch (Exception exception)
            {
                ShowError($"“{Path.GetFileName(sourcePath)}” fotoğrafı eklenemedi.", exception);
            }
        }
    }

    private static bool IsSupportedImage(string path) =>
        SupportedImageExtensions.Contains(Path.GetExtension(path));

    private Point GetDefaultPhotoPosition()
    {
        var width = PhotoCanvas.ActualWidth > 0 ? PhotoCanvas.ActualWidth : 600;
        var height = PhotoCanvas.ActualHeight > 0 ? PhotoCanvas.ActualHeight : 400;
        var offset = (_selectedEntry?.PhotoItems.Count ?? 0) % 5 * 24;
        return new Point(width * 0.6 + offset, height * 0.35 + offset);
    }

    private void SyncPhotoPaths()
    {
        if (_selectedEntry is not null)
        {
            _selectedEntry.PhotoPaths = _selectedEntry.PhotoItems.Select(photo => photo.Path).ToList();
        }
    }

    private void RenderEntryPhotos()
    {
        PhotoCanvas.Children.Clear();
        _photoVisuals.Clear();
        _photoAspectRatios.Clear();
        if (_selectedEntry is null)
        {
            return;
        }

        foreach (var photo in _selectedEntry.PhotoItems)
        {
            RenderPhoto(photo);
        }
    }

    private void RenderPhoto(DiaryPhoto photo)
    {
        if (!File.Exists(photo.Path))
        {
            return;
        }

        BitmapImage bitmap;
        int sourcePixelWidth;
        int sourcePixelHeight;
        try
        {
            using (var stream = File.OpenRead(photo.Path))
            {
                var decoder = BitmapDecoder.Create(
                    stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                var sourceFrame = decoder.Frames[0];
                sourcePixelWidth = sourceFrame.PixelWidth;
                sourcePixelHeight = sourceFrame.PixelHeight;
            }

            bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (sourcePixelWidth > MaximumDecodedPhotoWidth)
            {
                bitmap.DecodePixelWidth = MaximumDecodedPhotoWidth;
            }

            bitmap.UriSource = new Uri(photo.Path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
                                         or NotSupportedException or UriFormatException or FileFormatException)
        {
            ShowError($"“{Path.GetFileName(photo.Path)}” fotoğrafının önizlemesi yüklenemedi.", exception);
            return;
        }

        var aspectRatio = sourcePixelWidth / (double)sourcePixelHeight;
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            aspectRatio = 4.0 / 3.0;
        }

        _photoAspectRatios[photo] = aspectRatio;
        NormalizePhotoDimensions(photo, aspectRatio, photo.KeepAspectRatio);
        ClampPhotoPosition(photo);
        var image = new Image
        {
            Source = bitmap,
            Stretch = photo.KeepAspectRatio ? Stretch.Uniform : Stretch.Fill,
            IsHitTestVisible = false
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var inkLayer = new Canvas { IsHitTestVisible = false };
        var content = new Grid();
        content.Children.Add(image);
        content.Children.Add(inkLayer);

        var aspectIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(photo.KeepAspectRatio
                ? "M7,10V7A5,5 0 0,1 17,7V10 M6,10H18V21H6Z M12,14V17"
                : "M8,10V7A4,4 0 0,1 15.7,5 M6,10H18V21H6Z M12,14V17 M17,5L21,9 M21,5L17,9"),
            Fill = Brushes.Transparent,
            Stroke = (Brush)FindResource(photo.KeepAspectRatio ? "AccentHoverBrush" : "TextSecondaryBrush"),
            StrokeThickness = 1.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 16,
            Height = 16,
            Stretch = Stretch.Uniform
        };
        var aspectRatioToggle = new ToggleButton
        {
            Style = (Style)FindResource("PhotoAspectRatioToggleStyle"),
            Width = 32,
            Height = 30,
            Padding = new Thickness(6),
            Margin = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Visibility = Visibility.Collapsed,
            IsChecked = photo.KeepAspectRatio,
            Content = aspectIcon,
            ToolTip = photo.KeepAspectRatio
                ? "En-boy oranı kilitli. Serbest boyutlandırmak için tıklayın."
                : "Serbest boyutlandırma açık. Oranı korumak için tıklayın."
        };
        aspectRatioToggle.Checked += (_, _) => SetPhotoAspectRatio(photo, keepAspectRatio: true);
        aspectRatioToggle.Unchecked += (_, _) => SetPhotoAspectRatio(photo, keepAspectRatio: false);
        content.Children.Add(aspectRatioToggle);

        var bounds = GetPhotoSizeBounds(aspectRatio, photo.KeepAspectRatio);
        var frame = new Border
        {
            Width = photo.Width,
            Height = photo.Height,
            MinWidth = bounds.MinWidth,
            MaxWidth = bounds.MaxWidth,
            MinHeight = bounds.MinHeight,
            MaxHeight = bounds.MaxHeight,
            Tag = photo,
            Background = new SolidColorBrush(Color.FromRgb(25, 31, 45)),
            BorderBrush = (Brush)FindResource("BorderSubtleBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            ClipToBounds = false,
            Focusable = true,
            Child = content,
            Cursor = _brushMode
                ? _drawingTool == DrawingTool.Eraser ? Cursors.Cross : Cursors.Pen
                : Cursors.SizeAll
        };
        frame.PreviewMouseLeftButtonDown += PhotoFrame_PreviewMouseLeftButtonDown;
        frame.PreviewMouseMove += PhotoFrame_PreviewMouseMove;
        frame.PreviewMouseLeftButtonUp += PhotoFrame_PreviewMouseLeftButtonUp;

        var removeButton = new Button
        {
            Content = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M6,6L18,18 M18,6L6,18"),
                Stroke = (Brush)FindResource("TextSecondaryBrush"),
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform
            },
            Width = 24,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Style = (Style)FindResource("GhostIconButtonStyle"),
            ToolTip = "Fotoğrafı günlükten kaldır"
        };
        removeButton.Click += (_, _) => RemovePhoto(photo);
        content.Children.Add(removeButton);

        var resizeThumb = new Thumb
        {
            Width = 16,
            Height = 16,
            Style = (Style)FindResource("PhotoResizeThumbStyle"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Cursor = Cursors.SizeNWSE,
            ToolTip = "Köşeyi sürükleyerek fotoğrafı boyutlandır",
            Visibility = Visibility.Collapsed
        };
        resizeThumb.DragDelta += (_, _) => UpdatePhotoResizePreview(photo);
        resizeThumb.DragStarted += (_, _) => BeginPhotoResize(photo, resizeThumb);
        resizeThumb.DragCompleted += (_, _) => CompletePhotoResize(photo);
        content.Children.Add(resizeThumb);

        var visual = new PhotoVisual(frame, inkLayer, image, aspectRatioToggle, aspectIcon, resizeThumb);
        _photoVisuals[photo] = visual;
        Canvas.SetLeft(frame, photo.X);
        Canvas.SetTop(frame, photo.Y);
        Panel.SetZIndex(frame, PhotoCanvas.Children.Count);
        PhotoCanvas.Children.Add(frame);
        RenderPhotoStrokes(photo, inkLayer);
    }

    private void SelectPhoto(DiaryPhoto photo)
    {
        if (!_photoVisuals.ContainsKey(photo))
        {
            return;
        }

        _selectedPhoto = photo;
        foreach (var (candidate, visual) in _photoVisuals)
        {
            var isSelected = ReferenceEquals(candidate, photo);
            visual.AspectRatioToggle.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            visual.ResizeThumb.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;
            visual.Frame.BorderBrush = (Brush)FindResource(isSelected ? "AccentBrush" : "BorderSubtleBrush");
            visual.Frame.BorderThickness = new Thickness(isSelected ? 1.5 : 1);
        }
    }

    private void ClearSelectedPhoto()
    {
        _selectedPhoto = null;
        foreach (var visual in _photoVisuals.Values)
        {
            visual.AspectRatioToggle.Visibility = Visibility.Collapsed;
            visual.ResizeThumb.Visibility = Visibility.Collapsed;
            visual.Frame.BorderBrush = (Brush)FindResource("BorderSubtleBrush");
            visual.Frame.BorderThickness = new Thickness(1);
        }
    }

    private void SetPhotoAspectRatio(DiaryPhoto photo, bool keepAspectRatio)
    {
        if (!ReferenceEquals(_selectedPhoto, photo) ||
            !_photoVisuals.TryGetValue(photo, out var visual) ||
            photo.KeepAspectRatio == keepAspectRatio)
        {
            return;
        }

        photo.KeepAspectRatio = keepAspectRatio;
        var aspectRatio = _photoAspectRatios.GetValueOrDefault(photo, 4.0 / 3.0);
        NormalizePhotoDimensions(photo, aspectRatio, keepAspectRatio);
        ClampPhotoPosition(photo);
        visual.Image.Stretch = keepAspectRatio ? Stretch.Uniform : Stretch.Fill;
        visual.Frame.Width = photo.Width;
        visual.Frame.Height = photo.Height;
        var bounds = GetPhotoSizeBounds(aspectRatio, keepAspectRatio);
        visual.Frame.MinWidth = bounds.MinWidth;
        visual.Frame.MaxWidth = bounds.MaxWidth;
        visual.Frame.MinHeight = bounds.MinHeight;
        visual.Frame.MaxHeight = bounds.MaxHeight;
        Canvas.SetLeft(visual.Frame, photo.X);
        Canvas.SetTop(visual.Frame, photo.Y);
        RenderPhotoStrokes(photo, visual.InkLayer);
        visual.AspectRatioIcon.Data = Geometry.Parse(keepAspectRatio
            ? "M7,10V7A5,5 0 0,1 17,7V10 M6,10H18V21H6Z M12,14V17"
            : "M8,10V7A4,4 0 0,1 15.7,5 M6,10H18V21H6Z M12,14V17 M17,5L21,9 M21,5L17,9");
        visual.AspectRatioIcon.Stroke = (Brush)FindResource(
            keepAspectRatio ? "AccentHoverBrush" : "TextSecondaryBrush");
        visual.AspectRatioToggle.ToolTip = keepAspectRatio
            ? "En-boy oranı kilitli. Serbest boyutlandırmak için tıklayın."
            : "Serbest boyutlandırma açık. Oranı korumak için tıklayın.";
        MarkEntryChanged();
        SaveCurrentEntry();
    }

    private void BeginPhotoResize(DiaryPhoto photo, Thumb resizeThumb)
    {
        if (_resizingPhoto is not null || !_photoVisuals.TryGetValue(photo, out var visual))
        {
            return;
        }

        _resizingPhoto = photo;
        _resizingPhotoThumb = resizeThumb;
        resizeThumb.Opacity = 0;
        SelectPhoto(photo);
        _resizeStartWidth = photo.Width;
        _resizeStartHeight = photo.Height;
        _resizePreviewWidth = _resizeStartWidth;
        _resizePreviewHeight = _resizeStartHeight;
        var mainWindow = Application.Current?.MainWindow ?? this;
        _resizeStartMousePoint = Mouse.GetPosition(mainWindow);

        var transform = new ScaleTransform();
        var preview = new Border
        {
            Width = photo.Width,
            Height = photo.Height,
            BorderBrush = new SolidColorBrush(Color.FromArgb(235, 129, 140, 248)),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(28, 99, 102, 241)),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            RenderTransform = transform,
            RenderTransformOrigin = new Point(0, 0)
        };
        Canvas.SetLeft(preview, photo.X);
        Canvas.SetTop(preview, photo.Y);
        Panel.SetZIndex(preview, int.MaxValue);
        PhotoCanvas.Children.Add(preview);
        _photoResizePreviewBorder = preview;
        _photoResizePreviewTransform = transform;
    }

    private void CompletePhotoResize(DiaryPhoto photo)
    {
        if (!ReferenceEquals(_resizingPhoto, photo))
        {
            return;
        }

        var visual = _photoVisuals.GetValueOrDefault(photo);
        if (visual is not null)
        {
            photo.Width = _resizePreviewWidth;
            photo.Height = _resizePreviewHeight;
            ClampPhotoPosition(photo);
            visual.Frame.Width = photo.Width;
            visual.Frame.Height = photo.Height;
            Canvas.SetLeft(visual.Frame, photo.X);
            Canvas.SetTop(visual.Frame, photo.Y);
            RenderPhotoStrokes(photo, visual.InkLayer);
        }

        if (_photoResizePreviewBorder is not null)
        {
            PhotoCanvas.Children.Remove(_photoResizePreviewBorder);
            _photoResizePreviewBorder = null;
        }

        _photoResizePreviewTransform = null;
        if (_resizingPhotoThumb is not null)
        {
            _resizingPhotoThumb.Opacity = 1;
            _resizingPhotoThumb = null;
        }

        _resizingPhoto = null;
        if (visual is not null)
        {
            MarkEntryChanged();
            SaveCurrentEntry();
        }
    }

    private void DiaryWritingSurface_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (FindVisualAncestor<TextBox>(e.OriginalSource as DependencyObject) is not null)
        {
            ClearSelectedPhoto();
        }
    }

    private void RenderPhotoStrokes(DiaryPhoto photo, Canvas inkLayer)
    {
        RenderStrokes(photo.Strokes, inkLayer, photo.Width, photo.Height);
    }

    private static void RenderStrokes(IEnumerable<DiaryStroke> strokes, Canvas inkLayer, double width, double height)
    {
        inkLayer.Children.Clear();
        if (width <= 0 || height <= 0)
        {
            return;
        }

        foreach (var stroke in strokes)
        {
            if (stroke.Points.Count == 0)
            {
                continue;
            }

            if (stroke.Points.Count == 1)
            {
                inkLayer.Children.Add(CreateStrokeDot(stroke, stroke.Points[0], width, height));
                continue;
            }

            var line = CreateStrokeLine(stroke);
            line.Points = new PointCollection(stroke.Points.Select(point =>
                new Point(point.X * width, point.Y * height)));
            inkLayer.Children.Add(line);
        }
    }

    private DiaryStroke CreateCurrentStroke() => new()
    {
        Tool = _drawingTool.ToString(),
        Color = _brushColor,
        Thickness = _drawingTool == DrawingTool.Highlighter
            ? BrushSizeSlider.Value * 2
            : BrushSizeSlider.Value
    };

    private void BeginStrokeGesture(List<DiaryStroke> collection)
    {
        _activeStrokeCollection = collection;
        _strokeGestureBefore = collection.ToArray();
    }

    private void CompleteStrokeGesture()
    {
        if (_activeStrokeCollection is not null && _strokeGestureBefore is not null)
        {
            var before = new HashSet<DiaryStroke>(_strokeGestureBefore);
            var after = new HashSet<DiaryStroke>(_activeStrokeCollection);
            var removed = _strokeGestureBefore
                .Select((stroke, index) => (stroke, index))
                .Where(item => !after.Contains(item.stroke))
                .Select(item => new StrokeMutation(_activeStrokeCollection, item.stroke, item.index))
                .ToArray();
            var added = _activeStrokeCollection
                .Select((stroke, index) => (stroke, index))
                .Where(item => !before.Contains(item.stroke))
                .Select(item => new StrokeMutation(_activeStrokeCollection, item.stroke, item.index))
                .ToArray();
            if (removed.Length > 0 || added.Length > 0)
            {
                PushStrokeHistory(new StrokeHistoryAction(removed, added));
            }
        }

        _activeStrokeCollection = null;
        _strokeGestureBefore = null;
        UpdateStrokeHistoryButtons();
    }

    private void PushStrokeHistory(StrokeHistoryAction action)
    {
        TrimHistoryStack(_strokeUndoStack);
        _strokeUndoStack.Push(action);
        _strokeRedoStack.Clear();
        UpdateStrokeHistoryButtons();
    }

    private static void TrimHistoryStack(Stack<StrokeHistoryAction> history)
    {
        if (history.Count < MaximumStrokeHistory)
        {
            return;
        }

        var retained = history.Take(MaximumStrokeHistory - 1).Reverse().ToArray();
        history.Clear();
        foreach (var action in retained)
        {
            history.Push(action);
        }
    }

    private void UndoStrokeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokeUndoStack.Count == 0)
        {
            return;
        }

        var action = _strokeUndoStack.Pop();
        ApplyStrokeHistory(action, undo: true);
        TrimHistoryStack(_strokeRedoStack);
        _strokeRedoStack.Push(action);
        RenderChangedStrokeCollections(action);
        MarkEntryChanged();
        UpdateStrokeHistoryButtons();
    }

    private void RedoStrokeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strokeRedoStack.Count == 0)
        {
            return;
        }

        var action = _strokeRedoStack.Pop();
        ApplyStrokeHistory(action, undo: false);
        TrimHistoryStack(_strokeUndoStack);
        _strokeUndoStack.Push(action);
        RenderChangedStrokeCollections(action);
        MarkEntryChanged();
        UpdateStrokeHistoryButtons();
    }

    private void ClearDrawingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is null || _activeStrokeCollection is not null)
        {
            return;
        }

        var collections = new List<List<DiaryStroke>> { _selectedEntry.ContentStrokes };
        collections.AddRange(_selectedEntry.PhotoItems.Select(photo => photo.Strokes));
        var removed = collections
            .Distinct()
            .SelectMany(collection => collection
                .Select((stroke, index) => new StrokeMutation(collection, stroke, index)))
            .ToArray();
        if (removed.Length == 0)
        {
            return;
        }

        foreach (var collection in collections.Distinct())
        {
            collection.Clear();
        }

        var action = new StrokeHistoryAction(removed, []);
        PushStrokeHistory(action);
        RenderChangedStrokeCollections(action);
        MarkEntryChanged();
    }

    private void ApplyStrokeHistory(StrokeHistoryAction action, bool undo)
    {
        var remove = undo ? action.Added : action.Removed;
        foreach (var group in remove.GroupBy(mutation => mutation.Collection))
        {
            foreach (var mutation in group.OrderByDescending(mutation => mutation.Index))
            {
                mutation.Collection.Remove(mutation.Stroke);
            }
        }

        var add = undo ? action.Removed : action.Added;
        foreach (var group in add.GroupBy(mutation => mutation.Collection))
        {
            foreach (var mutation in group.OrderBy(mutation => mutation.Index))
            {
                if (!mutation.Collection.Contains(mutation.Stroke))
                {
                    mutation.Collection.Insert(Math.Clamp(mutation.Index, 0, mutation.Collection.Count),
                        mutation.Stroke);
                }
            }
        }
    }

    private void RenderChangedStrokeCollections(StrokeHistoryAction action)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        foreach (var collection in action.Removed.Concat(action.Added)
                     .Select(mutation => mutation.Collection).Distinct())
        {
            if (ReferenceEquals(collection, _selectedEntry.ContentStrokes))
            {
                RenderWritingStrokes();
            }

            foreach (var photo in _selectedEntry.PhotoItems.Where(photo => ReferenceEquals(photo.Strokes, collection)))
            {
                if (_photoVisuals.TryGetValue(photo, out var visual))
                {
                    RenderPhotoStrokes(photo, visual.InkLayer);
                }
            }
        }
    }

    private bool EraseAtPoint(List<DiaryStroke> strokes, Point point, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var changed = false;
        for (var index = strokes.Count - 1; index >= 0; index--)
        {
            var segments = SplitStrokeAroundPoint(strokes[index], point, width, height);
            if (segments is null)
            {
                continue;
            }

            strokes.RemoveAt(index);
            strokes.InsertRange(index, segments);
            changed = true;
        }

        return changed;
    }

    private bool EraseAlongPath(List<DiaryStroke> strokes, Point point, double width, double height)
    {
        var previous = _lastEraserPoint ?? point;
        var deltaX = point.X - previous.X;
        var deltaY = point.Y - previous.Y;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var stepLength = Math.Max(2, BrushSizeSlider.Value / 2);
        var steps = Math.Max(1, (int)Math.Ceiling(distance / stepLength));
        var changed = false;
        for (var step = 1; step <= steps; step++)
        {
            var amount = (double)step / steps;
            var sample = new Point(previous.X + deltaX * amount, previous.Y + deltaY * amount);
            changed |= EraseAtPoint(strokes, sample, width, height);
        }

        _lastEraserPoint = point;
        return changed;
    }

    private List<DiaryStroke>? SplitStrokeAroundPoint(DiaryStroke stroke, Point point, double width, double height)
    {
        var eraseRadius = BrushSizeSlider.Value + stroke.Thickness / 2;
        var eraseRadiusSquared = eraseRadius * eraseRadius;
        var segments = new List<DiaryStroke>();
        var retainedPoints = new List<DiaryPoint>();
        if (stroke.Points.Count == 0)
        {
            return null;
        }

        if (stroke.Points.Count == 1)
        {
            var dot = stroke.Points[0];
            var dx = dot.X * width - point.X;
            var dy = dot.Y * height - point.Y;
            if (dx * dx + dy * dy > eraseRadiusSquared)
            {
                return null;
            }

            return [];
        }

        var erasedAnyPoint = false;
        var start = stroke.Points[0];
        if (!IsWithinEraser(start, point, width, height, eraseRadiusSquared))
        {
            retainedPoints.Add(start);
        }

        for (var index = 1; index < stroke.Points.Count; index++)
        {
            var end = stroke.Points[index];
            if (TryGetEraserIntersection(start, end, point, width, height, eraseRadiusSquared,
                    out var entry, out var exit))
            {
                erasedAnyPoint = true;
                if (entry > 0)
                {
                    if (retainedPoints.Count == 0 &&
                        !IsWithinEraser(start, point, width, height, eraseRadiusSquared))
                    {
                        retainedPoints.Add(start);
                    }

                    retainedPoints.Add(Interpolate(start, end, entry));
                }

                AddStrokeSegment(stroke, retainedPoints, segments);
                retainedPoints.Clear();
                if (exit < 1)
                {
                    retainedPoints.Add(Interpolate(start, end, exit));
                    retainedPoints.Add(end);
                }
            }
            else
            {
                if (retainedPoints.Count == 0 &&
                    !IsWithinEraser(start, point, width, height, eraseRadiusSquared))
                {
                    retainedPoints.Add(start);
                }

                retainedPoints.Add(end);
            }

            start = end;
        }

        if (!erasedAnyPoint)
        {
            return null;
        }

        AddStrokeSegment(stroke, retainedPoints, segments);
        return segments;
    }

    private static bool IsWithinEraser(
        DiaryPoint point, Point center, double width, double height, double radiusSquared)
    {
        var dx = point.X * width - center.X;
        var dy = point.Y * height - center.Y;
        return dx * dx + dy * dy <= radiusSquared;
    }

    private static bool TryGetEraserIntersection(
        DiaryPoint start,
        DiaryPoint end,
        Point center,
        double width,
        double height,
        double radiusSquared,
        out double entry,
        out double exit)
    {
        var startX = start.X * width - center.X;
        var startY = start.Y * height - center.Y;
        var deltaX = (end.X - start.X) * width;
        var deltaY = (end.Y - start.Y) * height;
        var a = deltaX * deltaX + deltaY * deltaY;
        if (a <= double.Epsilon)
        {
            entry = 0;
            exit = 1;
            return startX * startX + startY * startY <= radiusSquared;
        }

        var b = 2 * (startX * deltaX + startY * deltaY);
        var c = startX * startX + startY * startY - radiusSquared;
        var discriminant = b * b - 4 * a * c;
        if (discriminant < 0)
        {
            entry = 0;
            exit = 0;
            return false;
        }

        var root = Math.Sqrt(discriminant);
        var first = (-b - root) / (2 * a);
        var second = (-b + root) / (2 * a);
        if (second < 0 || first > 1)
        {
            entry = 0;
            exit = 0;
            return false;
        }

        entry = Math.Clamp(first, 0, 1);
        exit = Math.Clamp(second, 0, 1);
        return entry <= exit;
    }

    private static DiaryPoint Interpolate(DiaryPoint start, DiaryPoint end, double amount) => new()
    {
        X = start.X + (end.X - start.X) * amount,
        Y = start.Y + (end.Y - start.Y) * amount
    };

    private static void AddStrokeSegment(DiaryStroke source, List<DiaryPoint> points, List<DiaryStroke> segments)
    {
        if (points.Count == 0)
        {
            return;
        }

        segments.Add(new DiaryStroke
        {
            Tool = source.Tool,
            Color = source.Color,
            Thickness = source.Thickness,
            Points = [.. points]
        });
    }

    private static Ellipse CreateStrokeDot(DiaryStroke stroke, DiaryPoint point, double width, double height)
    {
        var dot = new Ellipse
        {
            Width = stroke.Thickness,
            Height = stroke.Thickness,
            Fill = CreateStrokeBrush(stroke),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(dot, point.X * width - stroke.Thickness / 2);
        Canvas.SetTop(dot, point.Y * height - stroke.Thickness / 2);
        return dot;
    }

    private void PhotoFrame_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border frame || frame.Tag is not DiaryPhoto photo ||
            FindVisualAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null ||
            FindVisualAncestor<Thumb>(e.OriginalSource as DependencyObject) is not null)
        {
            return;
        }

        SelectPhoto(photo);
        if (_brushMode)
        {
            _drawingPhoto = photo;
            _drawingPhotoBorder = frame;
            _isEraserGesture = _drawingTool == DrawingTool.Eraser;
            _lastEraserPoint = null;
            BeginStrokeGesture(photo.Strokes);
            if (_isEraserGesture)
            {
                if (EraseAlongPath(photo.Strokes, e.GetPosition(frame), photo.Width, photo.Height) &&
                    _photoVisuals.TryGetValue(photo, out var erasedVisual))
                {
                    RenderPhotoStrokes(photo, erasedVisual.InkLayer);
                    MarkEntryChanged();
                }
            }
            else
            {
                _activeStroke = CreateCurrentStroke();
                photo.Strokes.Add(_activeStroke);
                var normalizedPoint = ToNormalizedPoint(e.GetPosition(frame), photo);
                _activeStroke.Points.Add(normalizedPoint);
                if (_photoVisuals.TryGetValue(photo, out var visual))
                {
                    _activeStrokeDot = CreateStrokeDot(
                        _activeStroke, normalizedPoint, photo.Width, photo.Height);
                    visual.InkLayer.Children.Add(_activeStrokeDot);
                }

                MarkEntryChanged();
            }

            frame.CaptureMouse();
            frame.Focus();
            e.Handled = true;
            return;
        }

        _draggedPhoto = photo;
        _draggedPhotoBorder = frame;
        var canvasPoint = e.GetPosition(PhotoCanvas);
        _photoDragOffset = new Point(canvasPoint.X - photo.X, canvasPoint.Y - photo.Y);
        frame.CaptureMouse();
        e.Handled = true;
    }

    private void PhotoFrame_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_drawingPhoto is not null && _drawingPhotoBorder is not null)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                if (_isEraserGesture)
                {
                    if (EraseAlongPath(_drawingPhoto.Strokes, e.GetPosition(_drawingPhotoBorder),
                            _drawingPhoto.Width, _drawingPhoto.Height) &&
                        _photoVisuals.TryGetValue(_drawingPhoto, out var erasedVisual))
                    {
                        RenderPhotoStrokes(_drawingPhoto, erasedVisual.InkLayer);
                        MarkEntryChanged();
                    }
                }
                else
                {
                    var point = ToNormalizedPoint(e.GetPosition(_drawingPhotoBorder), _drawingPhoto);
                    _activeStroke?.Points.Add(point);
                    if (_activeStrokeLine is null && _activeStrokeDot is not null &&
                        _photoVisuals.TryGetValue(_drawingPhoto, out var visual))
                    {
                        visual.InkLayer.Children.Remove(_activeStrokeDot);
                        _activeStrokeDot = null;
                        _activeStrokeLine = CreateStrokeLine(_activeStroke!);
                        foreach (var strokePoint in _activeStroke!.Points)
                        {
                            _activeStrokeLine.Points.Add(new Point(
                                strokePoint.X * _drawingPhoto.Width, strokePoint.Y * _drawingPhoto.Height));
                        }

                        visual.InkLayer.Children.Add(_activeStrokeLine);
                    }
                    else
                    {
                        _activeStrokeLine?.Points.Add(new Point(
                            point.X * _drawingPhoto.Width, point.Y * _drawingPhoto.Height));
                    }

                    MarkEntryChanged();
                }
            }

            return;
        }

        if (_draggedPhoto is null || _draggedPhotoBorder is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var position = e.GetPosition(PhotoCanvas);
        _draggedPhoto.X = position.X - _photoDragOffset.X;
        _draggedPhoto.Y = position.Y - _photoDragOffset.Y;
        ClampPhotoPosition(_draggedPhoto);
        Canvas.SetLeft(_draggedPhotoBorder, _draggedPhoto.X);
        Canvas.SetTop(_draggedPhotoBorder, _draggedPhoto.Y);
        MarkEntryChanged();
    }

    private void PhotoFrame_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border frame)
        {
            return;
        }

        if (ReferenceEquals(frame, _drawingPhotoBorder))
        {
            frame.ReleaseMouseCapture();
            CompleteStrokeGesture();
            _drawingPhoto = null;
            _drawingPhotoBorder = null;
            _activeStroke = null;
            _activeStrokeLine = null;
            _activeStrokeDot = null;
            _isEraserGesture = false;
            _lastEraserPoint = null;
            e.Handled = true;
        }

        if (ReferenceEquals(frame, _draggedPhotoBorder))
        {
            frame.ReleaseMouseCapture();
            _draggedPhoto = null;
            _draggedPhotoBorder = null;
            e.Handled = true;
        }
    }

    private void UpdatePhotoResizePreview(DiaryPhoto photo)
    {
        if (!ReferenceEquals(_resizingPhoto, photo) ||
            !_photoAspectRatios.TryGetValue(photo, out var aspectRatio) ||
            _photoResizePreviewTransform is null)
        {
            return;
        }

        var mainWindow = Application.Current?.MainWindow ?? this;
        var currentPoint = Mouse.GetPosition(mainWindow);
        var deltaX = currentPoint.X - _resizeStartMousePoint.X;
        var deltaY = currentPoint.Y - _resizeStartMousePoint.Y;
        _resizePreviewWidth = Math.Clamp(_resizeStartWidth + deltaX, 60, 900);
        if (photo.KeepAspectRatio)
        {
            _resizePreviewHeight = _resizePreviewWidth / aspectRatio;
        }
        else
        {
            _resizePreviewHeight = Math.Clamp(_resizeStartHeight + deltaY, 60, 900);
        }

        var scaleX = _resizePreviewWidth / _resizeStartWidth;
        _photoResizePreviewTransform.ScaleX = scaleX;
        _photoResizePreviewTransform.ScaleY = photo.KeepAspectRatio
            ? scaleX
            : _resizePreviewHeight / _resizeStartHeight;
    }

    private void RemovePhoto(DiaryPhoto photo)
    {
        if (_selectedEntry is null)
        {
            return;
        }

        _selectedEntry.PhotoItems.Remove(photo);
        if (ReferenceEquals(_selectedPhoto, photo))
        {
            ClearSelectedPhoto();
        }

        if (_photoVisuals.Remove(photo, out var visual))
        {
            PhotoCanvas.Children.Remove(visual.Frame);
        }

        _photoAspectRatios.Remove(photo);
        _strokeUndoStack.Clear();
        _strokeRedoStack.Clear();
        SyncPhotoPaths();
        UpdateStrokeHistoryButtons();
        MarkEntryChanged();
    }

    private static DiaryPoint ToNormalizedPoint(Point point, DiaryPhoto photo) =>
        ToNormalizedPoint(point, photo.Width, photo.Height);

    private static DiaryPoint ToNormalizedPoint(Point point, double width, double height) => new()
    {
        X = Math.Clamp(point.X / Math.Max(1, width), 0, 1),
        Y = Math.Clamp(point.Y / Math.Max(1, height), 0, 1)
    };

    private static Polyline CreateStrokeLine(DiaryStroke stroke) => new()
    {
        Stroke = CreateStrokeBrush(stroke),
        StrokeThickness = stroke.Thickness,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        IsHitTestVisible = false
    };

    private static Brush CreateStrokeBrush(DiaryStroke stroke)
    {
        var color = (Color)ColorConverter.ConvertFromString(stroke.Color)!;
        if (string.Equals(stroke.Tool, nameof(DrawingTool.Highlighter), StringComparison.OrdinalIgnoreCase))
        {
            color.A = 145;
        }

        return new SolidColorBrush(color);
    }

    private static void NormalizeStroke(DiaryStroke stroke)
    {
        stroke.Tool = Enum.TryParse<DrawingTool>(stroke.Tool, true, out var tool) && tool != DrawingTool.Eraser
            ? tool.ToString()
            : nameof(DrawingTool.Pen);
        stroke.Points ??= [];
    }

    private void ClampPhotoPosition(DiaryPhoto photo)
    {
        if (!double.IsFinite(photo.X) || photo.X < 0)
        {
            photo.X = 0;
        }

        if (!double.IsFinite(photo.Y) || photo.Y < 0)
        {
            photo.Y = 0;
        }

        var aspectRatio = _photoAspectRatios.TryGetValue(photo, out var sourceAspectRatio)
            ? sourceAspectRatio
            : double.IsFinite(photo.Width) && double.IsFinite(photo.Height) && photo.Height > 0
                ? photo.Width / photo.Height
                : 4.0 / 3.0;
        NormalizePhotoDimensions(photo, aspectRatio, photo.KeepAspectRatio);

        var width = PhotoCanvas.ActualWidth > 0 ? PhotoCanvas.ActualWidth : double.MaxValue;
        var height = PhotoCanvas.ActualHeight > 0 ? PhotoCanvas.ActualHeight : double.MaxValue;
        photo.X = Math.Clamp(photo.X, 0, Math.Max(0, width - photo.Width));
        photo.Y = Math.Clamp(photo.Y, 0, Math.Max(0, height - photo.Height));
    }

    private void NormalizePhotoDimensions(DiaryPhoto photo, double aspectRatio, bool keepAspectRatio)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            aspectRatio = 4.0 / 3.0;
        }

        var bounds = GetPhotoSizeBounds(aspectRatio, keepAspectRatio);
        var width = double.IsFinite(photo.Width) ? photo.Width : 260;
        photo.Width = Math.Clamp(width, bounds.MinWidth, bounds.MaxWidth);
        if (keepAspectRatio)
        {
            photo.Height = photo.Width / aspectRatio;
        }
        else
        {
            var height = double.IsFinite(photo.Height) ? photo.Height : 170;
            photo.Height = Math.Clamp(height, bounds.MinHeight, bounds.MaxHeight);
        }
    }

    private (double MinWidth, double MaxWidth, double MinHeight, double MaxHeight) GetPhotoSizeBounds(
        double aspectRatio, bool keepAspectRatio)
    {
        if (!double.IsFinite(aspectRatio) || aspectRatio <= 0)
        {
            aspectRatio = 4.0 / 3.0;
        }

        if (keepAspectRatio)
        {
            return (60, 900, 60 / aspectRatio, 900 / aspectRatio);
        }

        return (60, 900, 60, 900);
    }

    private static T? FindVisualAncestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T target)
            {
                return target;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }

    private void MarkEntryChanged()
    {
        if (_selectedEntry is null)
        {
            return;
        }

        _savedNotificationTimer.Stop();
        _selectedEntry.UpdatedAt = DateTime.Now;
        _hasUnsavedChanges = true;
        LastSavedText.Text = "Taslak — 10 saniye içinde kaydedilecek";
    }

    private void AutoSaveTimer_Tick(object? sender, EventArgs e)
    {
        if (_hasUnsavedChanges && _resizingPhoto is null)
        {
            SaveCurrentEntry();
        }
    }

    private bool SaveCurrentEntry()
    {
        if (_resizingPhoto is not null)
        {
            return false;
        }

        if (!_hasUnsavedChanges || _selectedEntry is null)
        {
            return true;
        }

        _selectedEntry.UpdatedAt = DateTime.Now;
        if (!SaveEntries())
        {
            return false;
        }

        _hasUnsavedChanges = false;
        LastSavedText.Text = $"Son kaydetme: {_selectedEntry.FormattedUpdatedAt}";
        return true;
    }

    private bool SaveEntries()
    {
        if (_resizingPhoto is not null)
        {
            return false;
        }

        try
        {
            _storage.SaveEntries(_entries, _encryptionKey!);
            RefreshEntryFilter();
            return true;
        }
        catch (Exception exception)
        {
            ShowError($"Şifreli günlük dosyası kaydedilemedi.\nKonum: {_storage.EntriesFilePath}", exception);
            return false;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshEntryFilter();

    private void RefreshEntryFilter()
    {
        var terms = SearchBox.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        _isArchiveSearchActive = terms.Length > 0;
        if (_entriesView is not null)
        {
            var selectedListItemBeforeRefresh = EntriesListBox.SelectedItem;
            _isRefreshingEntryView = true;
            try
            {
                _entriesView.Filter = item => item is DiaryEntry entry && terms.All(term =>
                {
                    if (term.StartsWith('#'))
                    {
                        var tag = term[1..];
                        return entry.Tags.Any(existing => existing.Contains(tag, StringComparison.CurrentCultureIgnoreCase));
                    }

                    return entry.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                           entry.Content.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                           entry.Mood.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                           entry.Tags.Any(existing => existing.Contains(term, StringComparison.CurrentCultureIgnoreCase));
                });
                _entriesView.Refresh();

                if ((selectedListItemBeforeRefresh is null ||
                     ReferenceEquals(selectedListItemBeforeRefresh, _selectedEntry)) &&
                    _selectedEntry is not null && _entriesView.Contains(_selectedEntry) &&
                    !ReferenceEquals(EntriesListBox.SelectedItem, _selectedEntry))
                {
                    var wasUpdatingList = _isUpdatingList;
                    _isUpdatingList = true;
                    EntriesListBox.SelectedItem = _selectedEntry;
                    _isUpdatingList = wasUpdatingList;
                }
            }
            finally
            {
                _isRefreshingEntryView = false;
            }
        }

        EntriesListBox.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded, new Action(ApplyArchiveGroupExpansionStates));

        var anyMatches = _entries.Any(item => terms.All(term =>
            term.StartsWith('#')
                ? item.Tags.Any(tag => tag.Contains(term[1..], StringComparison.CurrentCultureIgnoreCase))
                : item.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                  item.Content.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                  item.Mood.Contains(term, StringComparison.CurrentCultureIgnoreCase) ||
                  item.Tags.Any(tag => tag.Contains(term, StringComparison.CurrentCultureIgnoreCase))));
        EmptyListPlaceholder.Visibility = anyMatches ? Visibility.Collapsed : Visibility.Visible;
        if (!anyMatches)
        {
            EmptyListPlaceholder.Children.OfType<TextBlock>().First().Text =
                _entries.Count == 0 ? "Henüz günlük kaydı yok" : "Aramanızla eşleşen günlük bulunamadı";
        }
    }

    private void ArchiveGroupExpander_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander)
        {
            ApplyArchiveGroupExpansion(expander);
        }
    }

    private void ArchiveGroupExpander_Expanded(object sender, RoutedEventArgs e) =>
        SaveArchiveGroupExpansion(sender as Expander, isExpanded: true);

    private void ArchiveGroupExpander_Collapsed(object sender, RoutedEventArgs e) =>
        SaveArchiveGroupExpansion(sender as Expander, isExpanded: false);

    private void ApplyArchiveGroupExpansionStates()
    {
        foreach (var expander in FindVisualChildren<Expander>(EntriesListBox))
        {
            ApplyArchiveGroupExpansion(expander);
        }
    }

    private void ApplyArchiveGroupExpansion(Expander expander)
    {
        if (expander.DataContext is not CollectionViewGroup { Name: string groupKey })
        {
            return;
        }

        var isExpanded = _isArchiveSearchActive ||
            (_archiveGroupExpansionStates.TryGetValue(groupKey, out var savedState)
                ? savedState
                : IsDefaultArchiveGroupExpanded(groupKey));
        _isSettingArchiveExpansion = true;
        expander.IsExpanded = isExpanded;
        _isSettingArchiveExpansion = false;
    }

    private void SaveArchiveGroupExpansion(Expander? expander, bool isExpanded)
    {
        if (_isSettingArchiveExpansion || _isArchiveSearchActive ||
            expander?.DataContext is not CollectionViewGroup { Name: string groupKey })
        {
            return;
        }

        _archiveGroupExpansionStates[groupKey] = isExpanded;
    }

    private bool IsDefaultArchiveGroupExpanded(string groupKey)
    {
        if (string.Equals(groupKey, "0|Sabitlenenler", StringComparison.Ordinal))
        {
            return true;
        }

        var newestMonthKey = _entries
            .Where(entry => !entry.IsPinned)
            .OrderByDescending(entry => entry.Date)
            .ThenByDescending(entry => entry.CreatedAt)
            .Select(entry => entry.ArchiveGroupKey)
            .FirstOrDefault();
        return string.Equals(groupKey, newestMonthKey, StringComparison.Ordinal);
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T target)
            {
                yield return target;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static List<string> ParseTags(string text) => text
        .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
        .Select(tag => tag.Trim().TrimStart('#').Trim(',', ';'))
        .Where(tag => tag.Length is > 0 and <= 32)
        .Distinct(StringComparer.CurrentCultureIgnoreCase)
        .Take(20)
        .ToList();

    private void UpdateEditorStatistics()
    {
        if (_selectedEntry is null)
        {
            return;
        }

        WordCountText.Text = $"{_selectedEntry.WordCount} kelime";
        CharacterCountText.Text = $"{_selectedEntry.CharacterCount} karakter";
    }

    private void UpdateEditorState()
    {
        var hasEntry = _selectedEntry is not null;
        NoSelectionState.Visibility = hasEntry ? Visibility.Collapsed : Visibility.Visible;
        EditorContainerGrid.Visibility = hasEntry ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEntryListState()
    {
        EntryCountText.Text = $"{_entries.Count} kayıt";
        if (_entries.Count == 0)
        {
            EmptyListPlaceholder.Visibility = Visibility.Visible;
            EmptyListPlaceholder.Children.OfType<TextBlock>().First().Text = "Henüz günlük kaydı yok";
        }
        else
        {
            RefreshEntryFilter();
        }
    }

    private void ApplySortOrder()
    {
        var selectedEntry = _selectedEntry;
        var sorted = _settings.SortNewestFirst
            ? _entries.OrderByDescending(entry => entry.IsPinned)
                .ThenByDescending(entry => entry.Date).ThenByDescending(entry => entry.CreatedAt).ToArray()
            : _entries.OrderByDescending(entry => entry.IsPinned)
                .ThenBy(entry => entry.Date).ThenBy(entry => entry.CreatedAt).ToArray();

        _isUpdatingList = true;
        _entries.Clear();
        foreach (var entry in sorted)
        {
            _entries.Add(entry);
        }

        EntriesListBox.SelectedItem = selectedEntry is not null && _entries.Contains(selectedEntry)
            ? selectedEntry
            : null;
        _isUpdatingList = false;
        RefreshEntryFilter();
    }

    private void ShowModal(string modalName)
    {
        _isClosingModal = false;
        _activeModal = modalName;
        ModalOverlayGrid.Visibility = Visibility.Visible;
        var card = GetModalCard(modalName);
        NewEntryModalCard.Visibility = modalName == "new-entry" ? Visibility.Visible : Visibility.Collapsed;
        SettingsModalCard.Visibility = modalName == "settings" ? Visibility.Visible : Visibility.Collapsed;
        DeleteModalCard.Visibility = modalName == "delete" ? Visibility.Visible : Visibility.Collapsed;
        ShortcutHelpModalCard.Visibility = modalName == "shortcuts" ? Visibility.Visible : Visibility.Collapsed;
        ModalOverlayGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
        card.Opacity = 0;
        card.RenderTransformOrigin = new Point(0.5, 0.5);
        card.RenderTransform = new ScaleTransform(0.96, 0.96);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        });
        var scale = (ScaleTransform)card.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, TimeSpan.FromMilliseconds(220)));
    }

    private Border GetModalCard(string modalName) => modalName switch
    {
        "new-entry" => NewEntryModalCard,
        "settings" => SettingsModalCard,
        "delete" => DeleteModalCard,
        "shortcuts" => ShortcutHelpModalCard,
        _ => throw new ArgumentOutOfRangeException(nameof(modalName))
    };

    private void CloseModal()
    {
        if (ModalOverlayGrid.Visibility != Visibility.Visible || _isClosingModal)
        {
            return;
        }

        _isClosingModal = true;
        var card = GetModalCard(_activeModal);
        card.BeginAnimation(OpacityProperty, new DoubleAnimation(card.Opacity, 0, TimeSpan.FromMilliseconds(140)));
        ModalOverlayGrid.BeginAnimation(OpacityProperty, new DoubleAnimation(ModalOverlayGrid.Opacity, 0,
            TimeSpan.FromMilliseconds(170)));
        var scale = card.RenderTransform as ScaleTransform;
        scale?.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(150)));
        scale?.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, 0.97, TimeSpan.FromMilliseconds(150)));
        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            ModalOverlayGrid.Visibility = Visibility.Collapsed;
            _activeModal = string.Empty;
            _isClosingModal = false;
        };
        closeTimer.Start();
    }

    private void ExportSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedEntry is not null)
        {
            ExportEntries([_selectedEntry], $"{SafeFileName(_selectedEntry.DisplayTitle)}.txt");
        }
    }

    private void ExportAllButton_Click(object sender, RoutedEventArgs e)
    {
        ExportEntries(_entries.OrderByDescending(entry => entry.IsPinned)
            .ThenByDescending(entry => entry.Date).ToArray(), "Gunlukler.txt");
    }

    private void ExportEntries(IEnumerable<DiaryEntry> entries, string fileName)
    {
        var entriesToExport = entries.ToArray();
        if (entriesToExport.Length == 0)
        {
            MessageBox.Show(this, "Dışa aktarılacak günlük bulunamadı.", "Dışa Aktar",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Günlükleri metin dosyası olarak dışa aktar",
            Filter = "Metin dosyası (*.txt)|*.txt",
            FileName = fileName,
            AddExtension = true,
            DefaultExt = ".txt"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            var text = string.Join(
                $"{Environment.NewLine}{new string('─', 64)}{Environment.NewLine}{Environment.NewLine}",
                entriesToExport.Select(FormatEntryForExport));
            File.WriteAllText(dialog.FileName, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            MessageBox.Show(this, "Günlük(ler) dışa aktarıldı.", "Dışa Aktar",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception exception)
        {
            ShowError("Dışa aktarma tamamlanamadı.", exception);
        }
    }

    private static string FormatEntryForExport(DiaryEntry entry) => string.Join(Environment.NewLine,
    new[]
    {
        entry.DisplayTitle,
        entry.FormattedDate,
        $"Ruh hali: {entry.Mood}",
        $"Etiketler: {(entry.Tags.Count == 0 ? "Yok" : entry.TagsDisplay)}",
        entry.IsPinned ? "Sabitlenmiş" : string.Empty,
        string.Empty,
        entry.Content,
        entry.PhotoPaths.Count == 0 ? string.Empty : $"Fotoğraflar: {string.Join(", ", entry.PhotoPaths.Select(Path.GetFileName))}"
    }.Where(line => line.Length > 0));

    private static string SafeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var safeName = new string(fileName.Where(character => !invalidChars.Contains(character)).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safeName) ? "Gunluk" : safeName;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AuthenticationOverlayGrid.Visibility == Visibility.Visible)
        {
            return;
        }

        var modifiers = Keyboard.Modifiers;
        var controlOnly = modifiers == ModifierKeys.Control;
        var modalOpen = ModalOverlayGrid.Visibility == Visibility.Visible;

        if (e.Key == Key.Escape && modalOpen)
        {
            CloseModal();
            e.Handled = true;
            return;
        }

        if (controlOnly && e.Key == Key.K)
        {
            LockApplication();
            e.Handled = true;
            return;
        }

        var isUndo = controlOnly && e.Key == Key.Z;
        var isRedo = controlOnly && e.Key == Key.Y ||
                     modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.Z;
        if (isUndo || isRedo)
        {
            e.Handled = TryHandleUndoRedo(isRedo);
            return;
        }

        if (modalOpen)
        {
            if (_activeModal == "new-entry" && controlOnly &&
                (e.Key == Key.D || e.Key == Key.L) && GetFocusedDiaryContentTextBox() is not null)
            {
                InsertIntoFocusedDiaryContent(e.Key == Key.D
                    ? $"[{DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}]"
                    : "• ");
                e.Handled = true;
                return;
            }
            return;
        }

        if (controlOnly)
        {
            switch (e.Key)
            {
                case Key.N:
                    NewEntryButton_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.S:
                    if (SaveCurrentEntry() && _selectedEntry is not null)
                    {
                        LastSavedText.Text = "Kaydedildi";
                        _savedNotificationTimer.Stop();
                        _savedNotificationTimer.Start();
                    }
                    e.Handled = true;
                    return;
                case Key.F:
                    SearchBox.Focus();
                    e.Handled = true;
                    return;
                case Key.P:
                    if (_selectedEntry is not null)
                    {
                        PinEntryButton_Click(sender, e);
                    }
                    e.Handled = true;
                    return;
                case Key.E:
                    ExportSelectedButton_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.OemComma:
                    SettingsButton_Click(sender, e);
                    e.Handled = true;
                    return;
                case Key.B:
                    ToggleSidebar();
                    e.Handled = true;
                    return;
                case Key.D:
                    if (GetFocusedDiaryContentTextBox() is not null)
                    {
                        InsertIntoFocusedDiaryContent(
                            $"[{DateTime.Now.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)}]");
                        e.Handled = true;
                    }
                    return;
                case Key.L:
                    if (GetFocusedDiaryContentTextBox() is not null)
                    {
                        InsertIntoFocusedDiaryContent("• ");
                        e.Handled = true;
                    }
                    return;
            }
        }

        if (modifiers == ModifierKeys.Control ||
            modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            if (e.Key == Key.Add || e.Key == Key.OemPlus)
            {
                ChangeEditorFontSize(1);
                e.Handled = true;
                return;
            }

            if (modifiers == ModifierKeys.Control && (e.Key == Key.Subtract || e.Key == Key.OemMinus))
            {
                ChangeEditorFontSize(-1);
                e.Handled = true;
                return;
            }
        }

        if (modifiers == ModifierKeys.None && e.Key == Key.F1)
        {
            ShowModal("shortcuts");
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.None && e.Key == Key.Delete &&
            EntriesListBox.IsKeyboardFocusWithin && _selectedEntry is not null)
        {
            DeleteEntryButton_Click(sender, e);
            e.Handled = true;
        }
    }

    private bool TryHandleUndoRedo(bool redo)
    {
        var textBox = GetFocusedTextBox();
        if (textBox is not null)
        {
            if (redo && textBox.CanRedo)
            {
                textBox.Redo();
            }
            else if (!redo && textBox.CanUndo)
            {
                textBox.Undo();
            }

            return true;
        }

        if (_brushMode || textBox is null)
        {
            if (redo)
            {
                RedoStrokeButton_Click(this, new RoutedEventArgs());
            }
            else
            {
                UndoStrokeButton_Click(this, new RoutedEventArgs());
            }

            return true;
        }

        return false;
    }

    private TextBox? GetFocusedTextBox()
    {
        if (Keyboard.FocusedElement is TextBox focusedTextBox)
        {
            return focusedTextBox;
        }

        TextBox[] textBoxes =
        [
            EntryTitleTextBox,
            EntryContentTextBox,
            EntryTagsTextBox,
            NewEntryTitleTextBox,
            NewEntryContentTextBox,
            NewEntryTagsTextBox,
            SearchBox,
            SettingsDiaryNameTextBox,
            SettingsFontSizeTextBox
        ];
        return textBoxes.FirstOrDefault(textBox => textBox.IsKeyboardFocusWithin);
    }

    private TextBox? GetFocusedDiaryContentTextBox()
    {
        if (EntryContentTextBox.IsKeyboardFocusWithin)
        {
            return EntryContentTextBox;
        }

        return NewEntryContentTextBox.IsKeyboardFocusWithin ? NewEntryContentTextBox : null;
    }

    private void InsertIntoFocusedDiaryContent(string text)
    {
        var textBox = GetFocusedDiaryContentTextBox();
        if (textBox is null)
        {
            return;
        }

        var selectionStart = textBox.SelectionStart;
        textBox.SelectedText = text;
        textBox.CaretIndex = selectionStart + text.Length;
        textBox.Focus();
    }

    private void ChangeEditorFontSize(double amount)
    {
        var newSize = Math.Clamp(_settings.EditorFontSize + amount, 10, 32);
        if (newSize == _settings.EditorFontSize)
        {
            return;
        }

        var previousSize = _settings.EditorFontSize;
        _settings.EditorFontSize = newSize;
        try
        {
            _storage.SaveSettings(_settings);
        }
        catch (Exception exception)
        {
            _settings.EditorFontSize = previousSize;
            ShowError("Yazı boyutu kaydedilemedi.", exception);
            return;
        }

        EntryContentTextBox.FontSize = newSize;
    }

    private void ToggleSidebar()
    {
        if (_isSidebarAnimating)
        {
            return;
        }

        var collapsing = !_isSidebarCollapsed;
        _isSidebarCollapsed = collapsing;
        _isSidebarAnimating = true;
        var column = SidebarColumnDefinition;
        var currentWidth = column.ActualWidth;

        if (collapsing)
        {
            var animation = new DoubleAnimation(currentWidth, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            animation.Completed += (_, _) =>
            {
                column.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
                column.Width = new GridLength(0);
                column.MaxWidth = 420;
                _isSidebarAnimating = false;
            };
            column.BeginAnimation(ColumnDefinition.MaxWidthProperty, animation);
            column.MaxWidth = 0;
        }
        else
        {
            column.Width = new GridLength(330);
            column.MaxWidth = 0;
            var animation = new DoubleAnimation(0, 420, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            animation.Completed += (_, _) =>
            {
                column.BeginAnimation(ColumnDefinition.MaxWidthProperty, null);
                column.MaxWidth = 420;
                _isSidebarAnimating = false;
            };
            column.BeginAnimation(ColumnDefinition.MaxWidthProperty, animation);
        }
    }

    private void LockApplication()
    {
        if (!SaveCurrentEntry())
        {
            return;
        }

        _autoSaveTimer.Stop();
        _savedNotificationTimer.Stop();
        ModalOverlayGrid.Visibility = Visibility.Collapsed;
        _activeModal = string.Empty;
        _isClosingModal = false;
        if (_encryptionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_encryptionKey);
            _encryptionKey = null;
        }

        _entries.Clear();
        _selectedEntry = null;
        _hasUnsavedChanges = false;
        _strokeUndoStack.Clear();
        _strokeRedoStack.Clear();
        ClearSelectedPhoto();
        _draggedPhoto = null;
        _draggedPhotoBorder = null;
        _drawingPhoto = null;
        _drawingPhotoBorder = null;
        _activeStroke = null;
        _activeStrokeLine = null;
        _activeStrokeDot = null;
        _activeWritingStroke = null;
        _activeWritingStrokeLine = null;
        _activeWritingStrokeDot = null;
        _activeStrokeCollection = null;
        _strokeGestureBefore = null;
        _isEraserGesture = false;
        _lastEraserPoint = null;
        _brushMode = false;
        BrushModeLabelText.Text = "Çizim: Kapalı";
        BrushModeButton.Background = (Brush)FindResource("BgElevatedBrush");
        BrushModeButton.Foreground = (Brush)FindResource("TextPrimaryBrush");
        DrawingToolsPanel.Visibility = Visibility.Collapsed;
        DiaryWritingSurface.Cursor = Cursors.Arrow;
        EntryContentTextBox.Cursor = Cursors.IBeam;
        WritingInkCanvas.Cursor = Cursors.Arrow;
        WritingInkCanvas.IsHitTestVisible = false;
        PhotoCanvas.Cursor = Cursors.Arrow;
        _photoVisuals.Clear();
        _photoAspectRatios.Clear();
        PhotoCanvas.Children.Clear();
        WritingInkCanvas.Children.Clear();
        UpdateEditorState();
        UpdateEntryListState();
        UpdateStrokeHistoryButtons();
        ShowAuthenticationScreen(isFirstRunSetup: false);
    }

    private void UpdateStrokeHistoryButtons()
    {
        UndoStrokeButton.IsEnabled = _selectedEntry is not null &&
            _activeStrokeCollection is null && _strokeUndoStack.Count > 0;
        RedoStrokeButton.IsEnabled = _selectedEntry is not null &&
            _activeStrokeCollection is null && _strokeRedoStack.Count > 0;
        ClearDrawingButton.IsEnabled = _selectedEntry is not null && _activeStrokeCollection is null &&
            (_selectedEntry.ContentStrokes.Count > 0 || _selectedEntry.PhotoItems.Any(photo => photo.Strokes.Count > 0));
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        Interlocked.Increment(ref _authenticationScreenGeneration);
        Interlocked.Increment(ref _authenticationAttemptVersion);
        _autoSaveTimer.Stop();
        _savedNotificationTimer.Stop();
        if (!SaveCurrentEntry())
        {
            e.Cancel = true;
            _autoSaveTimer.Start();
            return;
        }

        if (_encryptionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_encryptionKey);
            _encryptionKey = null;
        }
    }

    private void ShowError(string message, Exception exception)
    {
        MessageBox.Show(this, $"{message}\n\nAyrıntı: {exception.Message}", "Günlük Uygulaması",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private enum DrawingTool
    {
        Pen,
        Highlighter,
        Eraser
    }

    private MoodOption[] CreateMoodOptions() => MoodDefinitions
        .Select(definition =>
        {
            var color = (Color)ColorConverter.ConvertFromString(definition.Hex)!;
            var accent = new SolidColorBrush(color);
            accent.Freeze();
            var badge = new SolidColorBrush(Color.FromArgb(38, color.R, color.G, color.B));
            badge.Freeze();
            return new MoodOption(
                definition.Name,
                accent,
                badge,
                GetMoodIcon(definition.Name)!);
        })
        .ToArray();

    internal static BitmapSource? GetMoodIcon(string? name)
    {
        var definition = MoodDefinitions.FirstOrDefault(option =>
            string.Equals(option.Name, name, StringComparison.Ordinal));
        if (definition is null)
        {
            return null;
        }

        lock (MoodIconCacheLock)
        {
            if (!MoodIconCache.TryGetValue(definition.Name, out var icon))
            {
                icon = CropEmoji(definition.Column, definition.Row);
                MoodIconCache.Add(definition.Name, icon);
            }

            return icon;
        }
    }

    private static BitmapSource CropEmoji(int column, int row)
    {
        const int spriteSize = 16;
        const int gridSize = 8;
        if (column is < 0 or >= gridSize || row is < 0 or >= gridSize)
        {
            throw new ArgumentOutOfRangeException(nameof(column), "Emoji sprite koordinatları 8×8 sınırında olmalıdır.");
        }

        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.UriSource = new Uri(
            "pack://application:,,,/GunlukUygulamasi;component/Assets/16x16_emoji_asset_pack_v1.1.png",
            UriKind.Absolute);
        bitmap.EndInit();
        bitmap.Freeze();

        if (bitmap.PixelWidth < gridSize * spriteSize || bitmap.PixelHeight < gridSize * spriteSize)
        {
            throw new InvalidDataException("Emoji sprite sheet en az 128×128 piksel olmalıdır.");
        }

        var crop = new CroppedBitmap(bitmap, new Int32Rect(
            column * spriteSize, row * spriteSize, spriteSize, spriteSize));
        crop.Freeze();
        return crop;
    }

    private static string NormalizeMood(string? mood)
    {
        var matchingMood = MoodDefinitions.FirstOrDefault(option =>
            string.Equals(option.Name, mood, StringComparison.OrdinalIgnoreCase));
        if (matchingMood is not null)
        {
            return matchingMood.Name;
        }

        var legacyMood = mood?.ToLowerInvariant() ?? string.Empty;
        if (legacyMood.Contains("sakin") || legacyMood.Contains("relaxed") || legacyMood.Contains("😌"))
        {
            return "Sakin";
        }

        if (legacyMood.Contains("iyi") || legacyMood.Contains("harika") || legacyMood.Contains("mutlu") ||
            legacyMood.Contains("happy") || legacyMood.Contains("😊") || legacyMood.Contains("😍"))
        {
            return "Mutlu";
        }

        if (legacyMood.Contains("odak") || legacyMood.Contains("normal") || legacyMood.Contains("focused") ||
            legacyMood.Contains("😐"))
        {
            return "Odaklı";
        }

        if (legacyMood.Contains("yorg") || legacyMood.Contains("tired") || legacyMood.Contains("😴"))
        {
            return "Yorgun";
        }

        if (legacyMood.Contains("stres") || legacyMood.Contains("zor") || legacyMood.Contains("öfk") ||
            legacyMood.Contains("angry") || legacyMood.Contains("😠") || legacyMood.Contains("😣"))
        {
            return "Stresli";
        }

        if (legacyMood.Contains("üzg") || legacyMood.Contains("sad") || legacyMood.Contains("😔") ||
            legacyMood.Contains("😢"))
        {
            return "Üzgün";
        }

        return MoodDefinitions[0].Name;
    }

    private sealed record MoodDefinition(string Name, string Hex, int Column, int Row);
    private sealed record MoodOption(string Name, Brush AccentBrush, Brush BadgeBrush, BitmapSource Icon);
    private sealed record BrushOption(string Name, string Hex);
    private sealed record StrokeMutation(List<DiaryStroke> Collection, DiaryStroke Stroke, int Index);
    private sealed record StrokeHistoryAction(
        IReadOnlyList<StrokeMutation> Removed,
        IReadOnlyList<StrokeMutation> Added);
    private sealed record PhotoVisual(
        Border Frame,
        Canvas InkLayer,
        Image Image,
        ToggleButton AspectRatioToggle,
        System.Windows.Shapes.Path AspectRatioIcon,
        Thumb ResizeThumb);
}

public sealed class MoodIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        MainWindow.GetMoodIcon(value as string) ?? DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ArchiveGroupHeaderConverter : IValueConverter
{
    private static readonly CultureInfo TurkishCulture = CultureInfo.GetCultureInfo("tr-TR");

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string groupKey)
        {
            return string.Empty;
        }

        if (groupKey.StartsWith("0|", StringComparison.Ordinal))
        {
            return "Sabitlenenler";
        }

        var dateText = groupKey[(groupKey.LastIndexOf('|') + 1)..];
        return DateTime.TryParseExact(
            dateText,
            "yyyy-MM",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var month)
            ? month.ToString("MMMM yyyy", TurkishCulture)
            : groupKey;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
