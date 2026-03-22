using System.Windows;

namespace CipherVault.UI.Services;

public interface IAuditDialogService
{
    bool ConfirmDuplicateMerge(string username, string site, int entryCount);
    bool ConfirmAutoSecure(string entryTitle);
}

public sealed class AuditDialogService : IAuditDialogService
{
    public bool ConfirmDuplicateMerge(string username, string site, int entryCount)
    {
        var decision = MessageBox.Show(
            $"Merge {entryCount} entries for username '{username}' on site '{site}'?\n\nThe best candidate will be kept and the others will be moved to Recycle Bin.",
            "Merge Duplicate Accounts",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return decision == MessageBoxResult.Yes;
    }

    public bool ConfirmAutoSecure(string entryTitle)
    {
        var decision = MessageBox.Show(
            $"Generate and apply a new strong password for '{entryTitle}' now?\n\nThe current password will be kept in history so you can restore it later.",
            "Auto Secure Entry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        return decision == MessageBoxResult.Yes;
    }
}
