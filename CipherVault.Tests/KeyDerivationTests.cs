using System;
using CipherVault.Core.Crypto;
using Xunit;

namespace CipherVault.Tests;

public class KeyDerivationTests
{
	private readonly KeyDerivationService _kdf = new KeyDerivationService();

	[Fact]
	public void SamePasswordAndSaltProducesSameKey()
	{
		byte[] salt = _kdf.GenerateSalt();
		byte[] array = _kdf.DeriveKey("TestPassword123!", salt, 64, 1);
		byte[] array2 = _kdf.DeriveKey("TestPassword123!", salt, 64, 1);
		Assert.Equal(array, array2);
		Array.Clear(array, 0, array.Length);
		Array.Clear(array2, 0, array2.Length);
	}

	[Fact]
	public void DifferentPasswordsProduceDifferentKeys()
	{
		byte[] salt = _kdf.GenerateSalt();
		byte[] array = _kdf.DeriveKey("Password1!", salt, 64, 1);
		byte[] array2 = _kdf.DeriveKey("Password2!", salt, 64, 1);
		Assert.NotEqual(array, array2);
		Array.Clear(array, 0, array.Length);
		Array.Clear(array2, 0, array2.Length);
	}

	[Fact]
	public void DifferentSaltsProduceDifferentKeys()
	{
		byte[] salt = _kdf.GenerateSalt();
		byte[] salt2 = _kdf.GenerateSalt();
		byte[] array = _kdf.DeriveKey("SamePassword!", salt, 64, 1);
		byte[] array2 = _kdf.DeriveKey("SamePassword!", salt2, 64, 1);
		Assert.NotEqual(array, array2);
		Array.Clear(array, 0, array.Length);
		Array.Clear(array2, 0, array2.Length);
	}

	[Fact]
	public void KeyIs32Bytes()
	{
		byte[] salt = _kdf.GenerateSalt();
		byte[] array = _kdf.DeriveKey("test", salt, 64, 1);
		Assert.Equal(32, array.Length);
		Array.Clear(array, 0, array.Length);
	}

	[Fact]
	public void EmptyPasswordThrows()
	{
		byte[] salt = _kdf.GenerateSalt();
		Assert.Throws<ArgumentNullException>(() => _kdf.DeriveKey("", salt, 64, 1));
	}

	[Fact]
	public void ShortSaltThrows()
	{
		Assert.Throws<ArgumentException>(() => _kdf.DeriveKey("pass", new byte[8], 64, 1));
	}
}
