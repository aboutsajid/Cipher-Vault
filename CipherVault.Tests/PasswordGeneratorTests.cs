using System;
using System.Collections.Generic;
using System.Linq;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class PasswordGeneratorTests
{
	private readonly PasswordGeneratorService _gen = new PasswordGeneratorService();

	[Theory]
	[InlineData(new object[] { 8 })]
	[InlineData(new object[] { 16 })]
	[InlineData(new object[] { 32 })]
	[InlineData(new object[] { 64 })]
	public void GeneratesCorrectLength(int length)
	{
		string text = _gen.Generate(new PasswordGeneratorOptions
		{
			Length = length
		});
		Assert.Equal(length, text.Length);
	}

	[Fact]
	public void ContainsRequiredCharClasses()
	{
		string collection = _gen.Generate(new PasswordGeneratorOptions
		{
			Length = 32,
			IncludeUppercase = true,
			IncludeLowercase = true,
			IncludeNumbers = true,
			IncludeSymbols = true
		});
		Assert.Contains((IEnumerable<char>)collection, (Predicate<char>)char.IsUpper);
		Assert.Contains((IEnumerable<char>)collection, (Predicate<char>)char.IsLower);
		Assert.Contains((IEnumerable<char>)collection, (Predicate<char>)char.IsDigit);
		Assert.Contains((IEnumerable<char>)collection, (Predicate<char>)((char c) => !char.IsLetterOrDigit(c)));
	}

	[Fact]
	public void NoDuplicateGenerations()
	{
		List<string> source = (from _ in Enumerable.Range(0, 10)
			select _gen.Generate(new PasswordGeneratorOptions
			{
				Length = 16
			})).ToList();
		Assert.Equal(10, source.Distinct().Count());
	}
}
