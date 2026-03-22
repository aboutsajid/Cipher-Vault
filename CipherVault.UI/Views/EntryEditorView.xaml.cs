using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace CipherVault.UI.Views;

public partial class EntryEditorView : Page
{
    private readonly EntryEditorViewModel _vm;
    private bool _suppressPasswordChange;

    public EntryEditorView(EntryEditorViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // Sync PasswordBox with VM
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Password) && !_suppressPasswordChange)
            {
                _suppressPasswordChange = true;
                PasswordHidden.Password = vm.Password;
                _suppressPasswordChange = false;
            }
        };

        // Initialize PasswordBox with current value
        PasswordHidden.Password = vm.Password;
    }

    private void PasswordHidden_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (!_suppressPasswordChange)
        {
            _suppressPasswordChange = true;
            _vm.Password = PasswordHidden.Password;
            _suppressPasswordChange = false;
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

