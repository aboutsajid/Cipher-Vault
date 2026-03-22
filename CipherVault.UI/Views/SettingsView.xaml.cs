using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace CipherVault.UI.Views;

public partial class SettingsView : Page
{
    public SettingsView(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
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

