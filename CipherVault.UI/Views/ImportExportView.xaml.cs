using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace CipherVault.UI.Views;

public partial class ImportExportView : Page
{
    private readonly ImportExportViewModel _vm;

    public ImportExportView(ImportExportViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void ExportPwBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        => _vm.ExportPassword = ExportPwBox.Password;

    private void ImportPwBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
        => _vm.ImportPassword = ImportPwBox.Password;

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

