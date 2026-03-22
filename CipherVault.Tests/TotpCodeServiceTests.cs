using System;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class TotpCodeServiceTests
{
	private readonly TotpCodeService _totp = new TotpCodeService();

	[Fact]
	public void RawBase32SecretReturnsValidCodeAndCountdown()
	{
		DateTime value = new DateTime(2026, 1, 1, 0, 0, 15, DateTimeKind.Utc);
		TotpCodeResult currentCode = _totp.GetCurrentCode("JBSWY3DPEHPK3PXP", value);
		Assert.True(currentCode.IsValid);
		Assert.Matches("^[0-9]{6}$", currentCode.Code);
		Assert.Equal(15, currentCode.SecondsRemaining);
	}

	[Fact]
	public void OtpAuthUriIsParsedAndReturnsCode()
	{
		DateTime value = new DateTime(2026, 1, 1, 0, 0, 10, DateTimeKind.Utc);
		string secretOrUri = "otpauth://totp/CipherVault:test@example.com?secret=JBSWY3DPEHPK3PXP&issuer=CipherVault&period=30&digits=6&algorithm=SHA1";
		TotpCodeResult currentCode = _totp.GetCurrentCode(secretOrUri, value);
		Assert.True(currentCode.IsValid);
		Assert.Matches("^[0-9]{6}$", currentCode.Code);
		Assert.Equal(20, currentCode.SecondsRemaining);
	}

	[Fact]
	public void CountdownRollsOverAtBoundary()
	{
		TotpCodeResult currentCode = _totp.GetCurrentCode("JBSWY3DPEHPK3PXP", new DateTime(2026, 1, 1, 0, 0, 29, DateTimeKind.Utc));
		TotpCodeResult currentCode2 = _totp.GetCurrentCode("JBSWY3DPEHPK3PXP", new DateTime(2026, 1, 1, 0, 0, 30, DateTimeKind.Utc));
		Assert.True(currentCode.IsValid);
		Assert.True(currentCode2.IsValid);
		Assert.Equal(1, currentCode.SecondsRemaining);
		Assert.Equal(30, currentCode2.SecondsRemaining);
	}

	[Fact]
	public void InvalidSecretReturnsError()
	{
		TotpCodeResult currentCode = _totp.GetCurrentCode("otpauth://totp/CipherVault:test@example.com?issuer=CipherVault");
		Assert.False(currentCode.IsValid);
		Assert.True(string.IsNullOrWhiteSpace(currentCode.Code));
		Assert.False(string.IsNullOrWhiteSpace(currentCode.ErrorMessage));
	}
}
