using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GunlukUygulamasi.Models;

public class DiaryEntry : INotifyPropertyChanged
{
    private static readonly CultureInfo TurkishCulture = new("tr-TR");

    private Guid _id = Guid.NewGuid();
    private string _title = string.Empty;
    private DateTime _date = DateTime.Today;
    private string _content = string.Empty;
    private DateTime _createdAt = DateTime.Now;
    private DateTime _updatedAt = DateTime.Now;
    private string _mood = "Mutlu";
    private List<string> _tags = [];
    private List<string> _photoPaths = [];
    private List<DiaryPhoto> _photoItems = [];
    private List<DiaryStroke> _contentStrokes = [];
    private bool _isPinned;

    public Guid Id
    {
        get => _id;
        set => SetField(ref _id, value);
    }

    public string Title
    {
        get => _title;
        set
        {
            if (SetField(ref _title, value))
            {
                OnPropertyChanged(nameof(DisplayTitle));
            }
        }
    }

    public DateTime Date
    {
        get => _date;
        set
        {
            if (SetField(ref _date, value))
            {
                OnPropertyChanged(nameof(FormattedDate));
                OnPropertyChanged(nameof(ShortDateBadge));
                OnPropertyChanged(nameof(DayName));
                OnPropertyChanged(nameof(ArchiveGroupKey));
            }
        }
    }

    public string Content
    {
        get => _content;
        set
        {
            if (SetField(ref _content, value))
            {
                OnPropertyChanged(nameof(ContentPreview));
                OnPropertyChanged(nameof(WordCount));
                OnPropertyChanged(nameof(CharacterCount));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetField(ref _createdAt, value);
    }

    public DateTime UpdatedAt
    {
        get => _updatedAt;
        set
        {
            if (SetField(ref _updatedAt, value))
            {
                OnPropertyChanged(nameof(FormattedUpdatedAt));
            }
        }
    }

    public string Mood
    {
        get => _mood;
        set => SetField(ref _mood, value);
    }

    public List<string> Tags
    {
        get => _tags;
        set
        {
            if (SetField(ref _tags, value ?? []))
            {
                OnPropertyChanged(nameof(TagsDisplay));
            }
        }
    }

    public List<string> PhotoPaths
    {
        get => _photoPaths;
        set => SetField(ref _photoPaths, value ?? []);
    }

    public List<DiaryPhoto> PhotoItems
    {
        get => _photoItems;
        set => SetField(ref _photoItems, value ?? []);
    }

    public List<DiaryStroke> ContentStrokes
    {
        get => _contentStrokes;
        set => SetField(ref _contentStrokes, value ?? []);
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetField(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(ArchiveGroupKey));
            }
        }
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Başlıksız Günlük" : Title.Trim();

    [JsonIgnore]
    public string ArchiveGroupKey => IsPinned
        ? "0|Sabitlenenler"
        : $"1|{9999 - Date.Year:D4}|{12 - Date.Month:D2}|{Date.ToString("yyyy-MM", CultureInfo.InvariantCulture)}";

    [JsonIgnore]
    public string FormattedDate => Date.ToString("dd MMMM yyyy, dddd", TurkishCulture);

    [JsonIgnore]
    public string ShortDateBadge => Date.ToString("dd MMM yyyy", TurkishCulture);

    [JsonIgnore]
    public string DayName => Date.ToString("dddd", TurkishCulture);

    [JsonIgnore]
    public string FormattedUpdatedAt => UpdatedAt.ToString("dd MMM yyyy HH:mm", TurkishCulture);

    [JsonIgnore]
    public string TagsDisplay => string.Join("  ", Tags.Select(tag => $"#{tag}"));

    [JsonIgnore]
    public string ContentPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Content))
            {
                return "Henüz içerik eklenmedi...";
            }

            var singleLine = Content
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\r", " ")
                .Trim();

            return singleLine.Length > 85 ? singleLine[..85] + "..." : singleLine;
        }
    }

    [JsonIgnore]
    public int WordCount => string.IsNullOrWhiteSpace(Content)
        ? 0
        : Content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    [JsonIgnore]
    public int CharacterCount => Content?.Length ?? 0;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
