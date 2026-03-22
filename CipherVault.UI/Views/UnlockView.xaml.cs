using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CipherVault.UI.Views;

public partial class UnlockView : Page
{
    private readonly UnlockViewModel _vm;
    private bool _autoWindowsUnlockAttempted;

    public UnlockView(UnlockViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        Loaded += UnlockView_Loaded;

        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsNewVault))
                UpdateUI();
            if (e.PropertyName == nameof(vm.CanUseWindowsHello))
                _ = HandleWindowsHelloAvailabilityChangedAsync();
            if (e.PropertyName == nameof(vm.StatusMessage))
                UpdateStatus();
            if (e.PropertyName == nameof(vm.IsBusy))
                UpdateBusy();
        };

        UpdateUI();
    }

    private async void UnlockView_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateDefaultFocus();
        await TryAutoWindowsUnlockAsync();
    }

    private async Task HandleWindowsHelloAvailabilityChangedAsync()
    {
        UpdateUI();
        if (!IsLoaded)
            return;

        UpdateDefaultFocus();
        await TryAutoWindowsUnlockAsync();
    }

    private void UpdateUI()
    {
        ConfirmPanel.Visibility = _vm.IsNewVault ? Visibility.Visible : Visibility.Collapsed;

        if (_vm.IsNewVault)
        {
            SubtitleText.Text = "Create your master password";
            UnlockBtn.Content = "Create Vault";
            UnlockBtn.Style = (Style)FindResource("PrimaryButton");
            return;
        }

        SubtitleText.Text = _vm.CanUseWindowsHello
            ? "Use Windows sign-in first, or unlock manually"
            : "Enter your master password";

        UnlockBtn.Content = "Unlock Manually";
        UnlockBtn.Style = (Style)FindResource("SecondaryButton");
    }

    private void UpdateDefaultFocus()
    {
        if (_vm.IsBusy)
            return;

        if (!_vm.IsNewVault && _vm.CanUseWindowsHello && WindowsHelloBtn.Visibility == Visibility.Visible)
        {
            WindowsHelloBtn.Focus();
            return;
        }

        PasswordInput.Focus();
    }

    private async Task TryAutoWindowsUnlockAsync()
    {
        if (_autoWindowsUnlockAttempted)
            return;

        if (_vm.IsNewVault || !_vm.CanUseWindowsHello)
            return;

        _autoWindowsUnlockAttempted = true;
        await _vm.ExecuteUnlockWithWindowsHelloAsync();
    }

    private void UpdateStatus()
    {
        if (string.IsNullOrEmpty(_vm.StatusMessage))
        {
            StatusBlock.Visibility = Visibility.Collapsed;
        }
        else
        {
            StatusBlock.Text = _vm.StatusMessage;
            StatusBlock.Visibility = Visibility.Visible;
        }
    }

    private void UpdateBusy()
    {
        UnlockBtn.IsEnabled = !_vm.IsBusy;
        WindowsHelloBtn.IsEnabled = !_vm.IsBusy;
        PasswordInput.IsEnabled = !_vm.IsBusy;
        ConfirmInput.IsEnabled = !_vm.IsBusy;
        LoadingText.Visibility = _vm.IsBusy ? Visibility.Visible : Visibility.Collapsed;

        if (!_vm.IsBusy)
            UpdateDefaultFocus();
    }

    private async void UnlockBtn_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ExecuteUnlockWithPasswordAsync(PasswordInput.Password, ConfirmInput.Password);
    }

    private async void WindowsHelloBtn_Click(object sender, RoutedEventArgs e)
    {
        await _vm.ExecuteUnlockWithWindowsHelloAsync();
    }

    private async void PasswordInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Return)
            await _vm.ExecuteUnlockWithPasswordAsync(PasswordInput.Password, ConfirmInput.Password);
    }
}
