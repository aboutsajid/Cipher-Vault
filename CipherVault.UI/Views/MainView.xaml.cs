using CipherVault.UI.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CipherVault.UI.Views;

public partial class MainView : Page
{
    private readonly MainViewModel _vm;

    public MainView(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        FocusSearch();
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void AllEntries_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedFolderId = null;
        _vm.ShowFavoritesOnly = false;
        _vm.ShowRecycleBinOnly = false;
    }

    private void Favorites_Click(object sender, RoutedEventArgs e)
    {
        _vm.ShowRecycleBinOnly = false;
        _vm.ShowFavoritesOnly = !_vm.ShowFavoritesOnly;
    }

    private void RecycleBin_Click(object sender, RoutedEventArgs e)
    {
        _vm.SelectedFolderId = null;
        _vm.ShowFavoritesOnly = false;
        _vm.ShowRecycleBinOnly = true;
    }

    private void CopyHistoryPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        object? historyItem = element.DataContext;
        if (_vm.CopyHistoryPasswordCommand.CanExecute(historyItem))
            _vm.CopyHistoryPasswordCommand.Execute(historyItem);
    }

    private async void RestoreHistoryPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        object? historyItem = element.DataContext;
        var command = _vm.RestoreHistoryPasswordCommand;
        if (!command.CanExecute(historyItem))
            return;

        if (command is IAsyncCommand asyncCommand)
            await asyncCommand.ExecuteAsync(historyItem);
        else
            command.Execute(historyItem);
    }

    private async void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        var selected = _vm.SelectedEntry;
        if (selected == null) return;

        bool isPermanentDelete = selected.IsDeleted;
        string message = isPermanentDelete
            ? $"Permanently delete '{selected.Title}' from Recycle Bin? This cannot be undone."
            : $"Move '{selected.Title}' to Recycle Bin? You can restore it later.";
        string caption = isPermanentDelete ? "Confirm Permanent Delete" : "Move to Recycle Bin";

        var result = MessageBox.Show(
            message,
            caption,
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        ICommand command = isPermanentDelete
            ? _vm.DeleteEntryPermanentlyCommand
            : _vm.DeleteEntryCommand;

        if (command is IAsyncCommand asyncCommand)
            await asyncCommand.ExecuteAsync(null);
        else
            command.Execute(null);
    }
}

