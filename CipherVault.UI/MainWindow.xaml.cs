using CipherVault.UI.ViewModels;
using CipherVault.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Input;
using WinForms = System.Windows.Forms;

namespace CipherVault.UI;

public partial class MainWindow : Window
{
    private MainViewModel? _mainVm;
    private bool _mainVmEventsWired;
    private bool _allowClose;
    private bool _trayHintShown;
    private WinForms.NotifyIcon? _trayIcon;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTrayIcon();
        Closing += MainWindow_Closing;
        PreviewKeyDown += Window_PreviewKeyDown;
        PreviewMouseDown += Window_PreviewMouseDown;
        StateChanged += Window_StateChanged;
        NavigateToUnlock();
    }

    private void NavigateToUnlock()
    {
        var vm = App.Services!.GetRequiredService<UnlockViewModel>();
        vm.OnUnlocked = async () =>
        {
            _mainVm = App.Services!.GetRequiredService<MainViewModel>();
            if (!_mainVmEventsWired)
            {
                WireMainVmEvents();
                _mainVmEventsWired = true;
            }
            await _mainVm.LoadAsync();
            if (_mainVm.IsOnboardingComplete)
                NavigateToMain();
            else
                NavigateToTrust(isMandatory: true);
        };
        MainFrame.Navigate(new UnlockView(vm));
    }

    private void NavigateToMain()
    {
        MainFrame.Navigate(new MainView(_mainVm!));
    }

    private void NavigateToAudit()
    {
        var vm = App.Services!.GetRequiredService<AuditViewModel>();
        MainFrame.Navigate(new AuditView(vm));
    }

    private void NavigateToTrust(bool isMandatory)
    {
        var vm = App.Services!.GetRequiredService<TrustViewModel>();
        vm.IsMandatory = isMandatory;
        vm.OnSaved += () =>
        {
            if (_mainVm?.IsOnboardingComplete == true)
                NavigateToMain();
        };
        MainFrame.Navigate(new TrustView(vm));
    }

    private void WireMainVmEvents()
    {
        if (_mainVm == null) return;
        _mainVm.RequestOpenEntryEditor += entry =>
        {
            bool returnToAudit = _mainVm.ConsumeEntryEditorReturnToAudit();

            var editorVm = App.Services!.GetRequiredService<EntryEditorViewModel>();
            editorVm.LoadEntry(entry);
            editorVm.SaveCallback = async e => await _mainVm.SaveEntryAsync(e);
            editorVm.OnSave += () =>
            {
                if (returnToAudit)
                {
                    NavigateToAudit();
                    return;
                }

                NavigateToMain();
            };
            editorVm.OnCancel += () =>
            {
                if (returnToAudit)
                {
                    NavigateToAudit();
                    return;
                }

                NavigateToMain();
            };
            MainFrame.Navigate(new EntryEditorView(editorVm));
        };

        _mainVm.RequestOpenSettings += () =>
        {
            var vm = App.Services!.GetRequiredService<SettingsViewModel>();
            vm.OnSaved += async () =>
            {
                if (_mainVm != null)
                    await _mainVm.RefreshSettingsAsync();
                NavigateToMain();
            };
            MainFrame.Navigate(new SettingsView(vm));
        };

        _mainVm.RequestOpenTrust += () =>
        {
            NavigateToTrust(isMandatory: false);
        };

        _mainVm.RequestOpenGenerator += () =>
        {
            var vm = App.Services!.GetRequiredService<GeneratorViewModel>();
            MainFrame.Navigate(new PasswordGeneratorView(vm));
        };

        _mainVm.RequestOpenAudit += () => NavigateToAudit();

        _mainVm.RequestOpenImportExport += () =>
        {
            var vm = App.Services!.GetRequiredService<ImportExportViewModel>();
            MainFrame.Navigate(new ImportExportView(vm));
        };

        _mainVm.RequestLock += () => NavigateToUnlock();
    }

    private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        RecordActivity();

        if (!IsVaultUnlocked() || _mainVm == null)
            return;

        if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _mainVm.TriggerQuickSave();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.B && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _mainVm.OpenImportExportCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (MainFrame.Content is MainView mainView)
            {
                mainView.FocusSearch();
                e.Handled = true;
            }
            return;
        }

        if (e.Key == Key.L && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _mainVm.LockCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Window_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        RecordActivity();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        RecordActivity();
    }

    private void Window_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized
            && _mainVm?.Settings.LockOnMinimize == true
            && IsVaultUnlocked())
        {
            _mainVm.LockCommand.Execute(null);
        }

        if (WindowState == WindowState.Minimized)
            ShowToTray();
    }

    private void RecordActivity()
    {
        if (IsVaultUnlocked())
            _mainVm?.RecordUserActivity();
    }

    private static bool IsVaultUnlocked()
        => App.Services?.GetService<VaultSessionService>()?.IsUnlocked == true;

    public void GoBackOrMain()
    {
        if (MainFrame.CanGoBack)
        {
            MainFrame.GoBack();
            return;
        }

        if (_mainVm != null && IsVaultUnlocked())
        {
            if (_mainVm.IsOnboardingComplete)
                NavigateToMain();
            else
                NavigateToTrust(isMandatory: true);

            return;
        }

        NavigateToUnlock();
    }

    public void ExitMandatoryOnboarding()
    {
        if (_mainVm != null && IsVaultUnlocked())
        {
            _mainVm.LockCommand.Execute(null);
            return;
        }

        NavigateToUnlock();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        ShowToTray();
    }

    private void InitializeTrayIcon()
    {
        System.Drawing.Icon trayIcon;
        try
        {
            var processPath = Environment.ProcessPath;
            trayIcon = !string.IsNullOrWhiteSpace(processPath)
                ? (System.Drawing.Icon.ExtractAssociatedIcon(processPath) ?? System.Drawing.SystemIcons.Application)
                : System.Drawing.SystemIcons.Application;
        }
        catch
        {
            trayIcon = System.Drawing.SystemIcons.Application;
        }

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "Cipher™ Vault",
            Visible = false,
            Icon = trayIcon
        };

        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Lock Vault", null, (_, _) =>
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _mainVm?.LockCommand.Execute(null);
                RestoreFromTray();
            });
        });
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        _trayIcon.ContextMenuStrip = menu;
    }

    private void ShowToTray()
    {
        if (_trayIcon == null) return;

        Hide();
        ShowInTaskbar = false;
        _trayIcon.Visible = true;

        if (!_trayHintShown)
        {
            _trayIcon.BalloonTipTitle = "Cipher™ Vault";
            _trayIcon.BalloonTipText = "Running in system tray. Double-click to reopen.";
            _trayIcon.ShowBalloonTip(1200);
            _trayHintShown = true;
        }
    }

    private void RestoreFromTray()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ShowInTaskbar = true;
            Show();
            if (WindowState == WindowState.Minimized)
                WindowState = WindowState.Normal;
            Activate();
            Focus();
        });
    }

    private void ExitFromTray()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _allowClose = true;
            _trayIcon?.Dispose();
            _trayIcon = null;
            Close();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _trayIcon?.Dispose();
        _trayIcon = null;
        App.Services?.GetService<VaultSessionService>()?.Lock();
        base.OnClosed(e);
    }
}





