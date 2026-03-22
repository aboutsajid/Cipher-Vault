using CipherVault.Core.Crypto;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Services;
using CipherVault.Data;
using CipherVault.Data.Repositories;
using CipherVault.UI.Services;
using CipherVault.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.IO;
using System.Windows;

namespace CipherVault.UI;

public partial class App : System.Windows.Application
{
    private const string DarkThemePath = "Theme/DarkTheme.xaml";
    private const string LightThemePath = "Theme/LightTheme.xaml";

    public static IServiceProvider? Services { get; private set; }
    public static string VaultPath { get; private set; } = string.Empty;
    public static string ConnectionString { get; private set; } = string.Empty;

    public static void ApplyTheme(string? themePreference)
    {
        if (Current is not App app) return;

        string effectiveTheme = ResolveEffectiveTheme(themePreference);
        string resourcePath = effectiveTheme == "Light" ? LightThemePath : DarkThemePath;
        var mergedDictionaries = app.Resources.MergedDictionaries;

        for (int i = mergedDictionaries.Count - 1; i >= 0; i--)
        {
            var source = mergedDictionaries[i].Source?.OriginalString ?? string.Empty;
            if (source.EndsWith(DarkThemePath, StringComparison.OrdinalIgnoreCase) ||
                source.EndsWith(LightThemePath, StringComparison.OrdinalIgnoreCase))
            {
                mergedDictionaries.RemoveAt(i);
            }
        }

        mergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri(resourcePath, UriKind.Relative)
        });
    }

    private static string ResolveEffectiveTheme(string? themePreference)
    {
        var normalized = NormalizeTheme(themePreference);
        return normalized == "System" ? GetSystemTheme() : normalized;
    }

    private static string NormalizeTheme(string? themePreference)
    {
        return themePreference?.Trim().ToLowerInvariant() switch
        {
            "dark" => "Dark",
            "light" => "Light",
            "system" => "System",
            _ => "System"
        };
    }

    private static string GetSystemTheme()
    {
        try
        {
            using var personalizeKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (personalizeKey?.GetValue("AppsUseLightTheme") is int isLightTheme)
                return isLightTheme == 0 ? "Dark" : "Light";
        }
        catch
        {
            // Fallback below
        }

        return "Light";
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ApplyTheme("System");

        // Default vault location: %APPDATA%\CipherVault\vault.cipherpw
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string dir = Path.Combine(appData, "CipherVault");
        Directory.CreateDirectory(dir);
        VaultPath = Path.Combine(dir, "vault.cipherpw");
        ConnectionString = $"Data Source={VaultPath}";

        // Initialize DB schema
        var initializer = new DatabaseInitializer(ConnectionString);
        await initializer.InitializeAsync();

        // Build DI container
        var services = new ServiceCollection();

        // Logging (no sensitive data ever goes to logs)
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Crypto
        services.AddSingleton<CryptoService>();
        services.AddSingleton<KeyDerivationService>();

        // Data
        services.AddSingleton<IVaultRepository>(_ => new VaultRepository(ConnectionString));
        services.AddSingleton<IFolderRepository>(_ => new FolderRepository(ConnectionString));
        services.AddSingleton<ISettingsRepository>(_ => new SettingsRepository(ConnectionString));

        // Core services
        services.AddSingleton<PasswordGeneratorService>();
        services.AddSingleton<PasswordAuditService>();
        services.AddSingleton<PasswordRiskAdvisorService>();
        services.AddSingleton<DuplicateAccountService>();
        services.AddSingleton<RemediationQueueService>();
        services.AddSingleton<SecureClipboardService>();
        services.AddSingleton<AutoLockService>();
        services.AddSingleton<BackupExportImportService>();
        services.AddSingleton<TotpCodeService>();
        services.AddSingleton<BreachCheckService>(_ => new BreachCheckService(new System.Net.Http.HttpClient()));
        services.AddSingleton<BrowserCaptureService>();
        services.AddSingleton<WindowsHelloUnlockService>();
        services.AddSingleton<BrowserDomainService>();
        services.AddSingleton<SecurityInsightsService>();
        services.AddSingleton<IAuditDialogService, AuditDialogService>();

        // Session (holds key in memory)
        services.AddSingleton<VaultSessionService>();

        // ViewModels
        services.AddTransient<UnlockViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<IAuditWorkflowHost>(sp => sp.GetRequiredService<MainViewModel>());
        services.AddTransient<EntryEditorViewModel>();
        services.AddTransient<GeneratorViewModel>();
        services.AddTransient<AuditViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<TrustViewModel>();
        services.AddTransient<ImportExportViewModel>();

        Services = services.BuildServiceProvider();

        try
        {
            var settingsRepo = Services.GetRequiredService<ISettingsRepository>();
            var settings = await settingsRepo.GetSettingsAsync();
            ApplyTheme(settings.Theme);
        }
        catch
        {
            ApplyTheme("System");
        }

        // Hook app exit to wipe key
        Exit += (_, _) =>
        {
            Services.GetService<BrowserCaptureService>()?.Stop();
            Services.GetService<VaultSessionService>()?.Lock();
        };

        // Show main window
        var win = new MainWindow();
        win.Show();
    }
}





