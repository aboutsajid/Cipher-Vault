using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using CipherVault.UI.Services;
using System.IO;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace CipherVault.UI.ViewModels;


public class GeneratorViewModel : ViewModelBase
{
    private readonly PasswordGeneratorService _generator;
    private readonly SecureClipboardService _clipboard;

    private string _generatedPassword = string.Empty;
    private int _length = 20;
    private bool _includeUpper = true;
    private bool _includeLower = true;
    private bool _includeNumbers = true;
    private bool _includeSymbols = true;
    private bool _excludeSimilar;
    private int _clipboardSeconds = 25;

    public string GeneratedPassword { get => _generatedPassword; set => SetField(ref _generatedPassword, value); }
    public int Length { get => _length; set => SetField(ref _length, value); }
    public bool IncludeUpper { get => _includeUpper; set => SetField(ref _includeUpper, value); }
    public bool IncludeLower { get => _includeLower; set => SetField(ref _includeLower, value); }
    public bool IncludeNumbers { get => _includeNumbers; set => SetField(ref _includeNumbers, value); }
    public bool IncludeSymbols { get => _includeSymbols; set => SetField(ref _includeSymbols, value); }
    public bool ExcludeSimilar { get => _excludeSimilar; set => SetField(ref _excludeSimilar, value); }

    public ICommand GenerateCommand { get; }
    public ICommand CopyCommand { get; }

    public GeneratorViewModel(PasswordGeneratorService generator, SecureClipboardService clipboard)
    {
        _generator = generator;
        _clipboard = clipboard;
        GenerateCommand = new RelayCommand(_ => Generate());
        CopyCommand = new RelayCommand(_ => _clipboard.CopyAndScheduleClear(GeneratedPassword, _clipboardSeconds),
            _ => !string.IsNullOrEmpty(GeneratedPassword));
        Generate();
    }

    private void Generate()
    {
        try
        {
            GeneratedPassword = _generator.Generate(new PasswordGeneratorOptions
            {
                Length = Length,
                IncludeUppercase = IncludeUpper,
                IncludeLowercase = IncludeLower,
                IncludeNumbers = IncludeNumbers,
                IncludeSymbols = IncludeSymbols,
                ExcludeSimilarChars = ExcludeSimilar
            });
        }
        catch (InvalidOperationException ex)
        {
            GeneratedPassword = $"Error: {ex.Message}";
        }
    }
}
