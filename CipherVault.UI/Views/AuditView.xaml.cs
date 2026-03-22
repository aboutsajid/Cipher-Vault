using CipherVault.UI.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace CipherVault.UI.Views;

public partial class AuditView : Page
{
    private bool _autoScanTriggered;

    public AuditView(AuditViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        Loaded += AuditView_Loaded;
    }

    private async void AuditView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_autoScanTriggered)
            return;

        _autoScanTriggered = true;

        if (DataContext is not AuditViewModel vm)
            return;

        bool hasExistingResults = vm.Results.Count > 0
            || vm.BreachResults.Count > 0
            || vm.DuplicateGroups.Count > 0;

        if (hasExistingResults || vm.IsBusy)
            return;

        try
        {
            await vm.RunFullScanForRemediationAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Audit refresh failed: {ex.Message}",
                "Audit",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            mainWindow.GoBackOrMain();
            return;
        }

        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
    }
}
