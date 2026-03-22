using System.Reflection;
using CipherVault.UI;
using Xunit;

namespace CipherVault.Tests;

public sealed class AppThemeNormalizationTests
{
    [Theory]
    [InlineData("dark", "Dark")]
    [InlineData("light", "Light")]
    [InlineData("system", "System")]
    [InlineData("unexpected", "System")]
    [InlineData(null, "System")]
    public void NormalizeTheme_MapsExpectedValues(string? input, string expected)
    {
        MethodInfo? method = typeof(App).GetMethod("NormalizeTheme", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        string? actual = (string?)method.Invoke(null, new object?[] { input });
        Assert.NotNull(actual);
        Assert.Equal(expected, actual);
    }
}
