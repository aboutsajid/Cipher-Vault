using System;
using System.Collections.Generic;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class PasswordRiskAdvisorServiceTests
{
	private readonly PasswordRiskAdvisorService _advisor = new PasswordRiskAdvisorService();

	[Fact]
	public void EvaluateWarnings_ReturnsComplexityWarnings_ForWeakPassword()
	{
		List<string> collection = _advisor.EvaluateWarnings("abc", Array.Empty<string>());
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("too short", StringComparison.OrdinalIgnoreCase)));
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("uppercase", StringComparison.OrdinalIgnoreCase)));
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("numbers", StringComparison.OrdinalIgnoreCase)));
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("symbols", StringComparison.OrdinalIgnoreCase)));
	}

	[Fact]
	public void EvaluateWarnings_ReturnsReuseWarning_WhenPasswordIsReused()
	{
		List<string> collection = _advisor.EvaluateWarnings("P@ssword123!", new string[3] { "Different#1", "P@ssword123!", "P@ssword123!" });
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("reused", StringComparison.OrdinalIgnoreCase)));
		Assert.Contains((IEnumerable<string>)collection, (Predicate<string>)((string warning) => warning.Contains("2", StringComparison.Ordinal)));
	}

	[Fact]
	public void EvaluateWarnings_ReturnsEmpty_ForStrongUniquePassword()
	{
		List<string> collection = _advisor.EvaluateWarnings("StrongPassword!2026", new string[2] { "Different#1", "AnotherPassword$9" });
		Assert.Empty(collection);
	}
}
