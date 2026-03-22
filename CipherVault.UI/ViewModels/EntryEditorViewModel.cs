using CipherVault.Core.Models;
using CipherVault.Core.Services;
using System.Windows.Input;

namespace CipherVault.UI.ViewModels;

public class EntryEditorViewModel : ViewModelBase
{
    private readonly PasswordGeneratorService _generator;

    private string _title = string.Empty;
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _url = string.Empty;
    private string _notes = string.Empty;
    private string _tags = string.Empty;
    private string _totpSecret = string.Empty;
    private bool _favorite;
    private int? _folderId;
    private bool _isPasswordVisible;
    private string _generatedStrength = string.Empty;
    private int _entryId;
    private DateTime _createdAt;
    private List<PasswordHistoryItem> _passwordHistory = new();
    private int _passwordReminderDays = 90;
    private DateTime? _passwordLastChangedUtc;

    public string Title { get => _title; set { SetField(ref _title, value); UpdateStrength(); } }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string Password { get => _password; set { SetField(ref _password, value); UpdateStrength(); } }
    public string Url { get => _url; set => SetField(ref _url, value); }
    public string Notes { get => _notes; set => SetField(ref _notes, value); }
    public string Tags { get => _tags; set => SetField(ref _tags, value); }
    public string TotpSecret { get => _totpSecret; set => SetField(ref _totpSecret, value); }
    public bool Favorite { get => _favorite; set => SetField(ref _favorite, value); }
    public int? FolderId { get => _folderId; set => SetField(ref _folderId, value); }
    public bool IsPasswordVisible { get => _isPasswordVisible; set => SetField(ref _isPasswordVisible, value); }
    public string GeneratedStrength { get => _generatedStrength; set => SetField(ref _generatedStrength, value); }
    public int PasswordReminderDays { get => _passwordReminderDays; set => SetField(ref _passwordReminderDays, Math.Clamp(value, 0, 3650)); }
    public IReadOnlyList<PasswordReminderOption> PasswordReminderOptions { get; } =
    [
        new PasswordReminderOption(0, "Off"),
        new PasswordReminderOption(30, "30 days"),
        new PasswordReminderOption(60, "60 days"),
        new PasswordReminderOption(90, "90 days (recommended)"),
        new PasswordReminderOption(180, "180 days"),
        new PasswordReminderOption(365, "365 days")
    ];

    public ICommand GeneratePasswordCommand { get; }
    public ICommand TogglePasswordVisibilityCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    public event Action? OnSave;
    public event Action? OnCancel;

    public Func<VaultEntryPlain, Task>? SaveCallback { get; set; }

    public EntryEditorViewModel(PasswordGeneratorService generator)
    {
        _generator = generator;
        GeneratePasswordCommand = new RelayCommand(_ => GeneratePassword());
        TogglePasswordVisibilityCommand = new RelayCommand(_ => IsPasswordVisible = !IsPasswordVisible);
        SaveCommand = new AsyncRelayCommand(async _ => await SaveAsync(), _ => !string.IsNullOrWhiteSpace(Title));
        CancelCommand = new RelayCommand(_ => OnCancel?.Invoke());
    }

    public void LoadEntry(VaultEntryPlain? entry)
    {
        if (entry == null)
        {
            _entryId = 0;
            _createdAt = default;
            Title = string.Empty;
            Username = string.Empty;
            Password = string.Empty;
            Url = string.Empty;
            Notes = string.Empty;
            Tags = string.Empty;
            TotpSecret = string.Empty;
            Favorite = false;
            FolderId = null;
            _passwordHistory = new List<PasswordHistoryItem>();
            _passwordLastChangedUtc = null;
            PasswordReminderDays = 90;
        }
        else
        {
            _entryId = entry.Id;
            _createdAt = entry.CreatedAt;
            Title = entry.Title;
            Username = entry.Username;
            Password = entry.Password;
            Url = entry.Url;
            Notes = entry.Notes;
            Tags = entry.Tags;
            TotpSecret = entry.TotpSecret;
            Favorite = entry.Favorite;
            FolderId = entry.FolderId;
            _passwordLastChangedUtc = entry.PasswordLastChangedUtc;
            PasswordReminderDays = Math.Clamp(entry.PasswordReminderDays, 0, 3650);
            _passwordHistory = entry.PasswordHistory.Select(x => new PasswordHistoryItem
            {
                Password = x.Password,
                ChangedAtUtc = x.ChangedAtUtc
            }).ToList();
        }

        UpdateStrength();
    }

    private void GeneratePassword()
    {
        Password = _generator.Generate(new PasswordGeneratorOptions
        {
            Length = 20,
            IncludeUppercase = true,
            IncludeLowercase = true,
            IncludeNumbers = true,
            IncludeSymbols = true,
            ExcludeSimilarChars = false
        });
    }

    private void UpdateStrength()
    {
        if (string.IsNullOrEmpty(Password)) { GeneratedStrength = string.Empty; return; }
        int score = 0;
        if (Password.Length >= 12) score++;
        if (Password.Length >= 16) score++;
        if (Password.Any(char.IsUpper)) score++;
        if (Password.Any(char.IsLower)) score++;
        if (Password.Any(char.IsDigit)) score++;
        if (Password.Any(c => !char.IsLetterOrDigit(c))) score++;
        GeneratedStrength = score switch { >= 6 => "Strong", >= 4 => "Good", >= 2 => "Fair", _ => "Weak" };
    }

    private async Task SaveAsync()
    {
        if (SaveCallback == null) return;

        var entry = new VaultEntryPlain
        {
            Id = _entryId,
            Title = Title,
            Username = Username,
            Password = Password,
            Url = Url,
            Notes = Notes,
            Tags = Tags,
            TotpSecret = TotpSecret,
            PasswordHistory = _passwordHistory.Select(x => new PasswordHistoryItem
            {
                Password = x.Password,
                ChangedAtUtc = x.ChangedAtUtc
            }).ToList(),
            PasswordReminderDays = Math.Clamp(PasswordReminderDays, 0, 3650),
            PasswordLastChangedUtc = _passwordLastChangedUtc,
            Favorite = Favorite,
            FolderId = FolderId,
            CreatedAt = _entryId == 0 ? DateTime.UtcNow : (_createdAt == default ? DateTime.UtcNow : _createdAt),
            UpdatedAt = DateTime.UtcNow
        };

        await SaveCallback(entry);
        OnSave?.Invoke();
    }
}

public sealed record PasswordReminderOption(int Days, string Label);
