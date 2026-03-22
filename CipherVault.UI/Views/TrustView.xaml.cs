using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace CipherVault.UI.Views;

public partial class TrustView : Page
{
    public TrustView(TrustViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow mainWindow)
        {
            if (DataContext is TrustViewModel vm && vm.IsMandatory && NavigationService?.CanGoBack != true)
            {
                mainWindow.ExitMandatoryOnboarding();
                return;
            }

            mainWindow.GoBackOrMain();
            return;
        }

        if (NavigationService?.CanGoBack == true)
            NavigationService.GoBack();
    }
}
