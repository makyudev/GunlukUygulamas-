using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using GunlukUygulamasi.Services;

namespace GunlukUygulamasi;

public partial class UpdateWindow : Window
{
    private readonly UpdateRelease _release;

    public UpdateWindow(UpdateRelease release, bool showInstallButton)
    {
        InitializeComponent();
        _release = release;

        WindowHeadingText.Text = showInstallButton
            ? $"Yeni Güncelleme Mevcut ({release.TagName})"
            : $"Son Sürüm Notları ({release.TagName})";
        VersionText.Text = $"Mevcut sürüm: v{UpdateService.CurrentVersion.ToString(3)}";
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(release.ReleaseNotes)
            ? "Bu sürüm için güncelleme notu eklenmemiş."
            : release.ReleaseNotes.Trim();

        InstallButton.Visibility = showInstallButton ? Visibility.Visible : Visibility.Collapsed;
        LaterButton.Content = showInstallButton ? "Daha Sonra" : "Kapat";
        if (showInstallButton && release.InstallerDownloadUrl is null)
        {
            InstallButton.IsEnabled = false;
            InstallerStatusText.Text = "Bu sürüm için GitHub Release'e .exe kurulum dosyası eklenmemiş.";
            InstallerStatusText.Visibility = Visibility.Visible;
        }
    }

    private void LaterButton_Click(object sender, RoutedEventArgs e) => Close();

    private async void InstallButton_Click(object sender, RoutedEventArgs e)
    {
        InstallButton.IsEnabled = false;
        LaterButton.IsEnabled = false;
        DownloadProgressBar.Visibility = Visibility.Visible;
        DownloadProgressBar.IsIndeterminate = true;
        DownloadStatusText.Visibility = Visibility.Visible;
        DownloadStatusText.Text = "Güncelleme indiriliyor...";

        try
        {
            var progress = new Progress<DownloadProgress>(UpdateDownloadProgress);
            var installerPath = await new UpdateService().DownloadInstallerAsync(_release, progress);
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS",
                UseShellExecute = true
            });

            if (process is null)
            {
                throw new InvalidOperationException("Güncelleme kurulumu başlatılamadı.");
            }

            Application.Current.Shutdown();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or IOException or
                UnauthorizedAccessException or InvalidOperationException or Win32Exception)
        {
            DownloadStatusText.Text = $"Güncelleme indirilemedi veya başlatılamadı: {exception.Message}";
            InstallButton.IsEnabled = _release.InstallerDownloadUrl is not null;
            LaterButton.IsEnabled = true;
        }
    }

    private void UpdateDownloadProgress(DownloadProgress progress)
    {
        if (progress.TotalBytes is > 0)
        {
            DownloadProgressBar.IsIndeterminate = false;
            DownloadProgressBar.Value = progress.BytesReceived * 100d / progress.TotalBytes.Value;
            DownloadStatusText.Text =
                $"İndiriliyor: {progress.BytesReceived / 1024d / 1024d:0.0} / " +
                $"{progress.TotalBytes.Value / 1024d / 1024d:0.0} MB";
        }
        else
        {
            DownloadStatusText.Text =
                $"İndiriliyor: {progress.BytesReceived / 1024d / 1024d:0.0} MB";
        }
    }
}
