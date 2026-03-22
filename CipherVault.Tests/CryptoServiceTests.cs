using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CipherVault.Core.Crypto;
using Xunit;

namespace CipherVault.Tests;

public class CryptoServiceTests
{
	private readonly CryptoService _crypto = new CryptoService();

	private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

	[Fact]
	public void EncryptDecryptRoundtrip()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("Hello, CipherVault!");
		byte[] encryptedBlob = _crypto.Encrypt(_key, bytes);
		byte[] actual = _crypto.Decrypt(_key, encryptedBlob);
		Assert.Equal(bytes, actual);
	}

	[Fact]
	public void EncryptDecryptStringRoundtrip()
	{
		string text = "my super secret password 123!@#";
		byte[] encryptedBlob = _crypto.EncryptString(_key, text);
		string actual = _crypto.DecryptString(_key, encryptedBlob);
		Assert.Equal(text, actual);
	}

	[Fact]
	public void TamperedCiphertextThrows()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("sensitive data");
		byte[] encrypted = _crypto.Encrypt(_key, bytes);
		encrypted[30] ^= byte.MaxValue;
		Assert.Throws<AuthenticationTagMismatchException>(() => _crypto.Decrypt(_key, encrypted));
	}

	[Fact]
	public void TamperedTagThrows()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("sensitive data");
		byte[] encrypted = _crypto.Encrypt(_key, bytes);
		encrypted[15] ^= byte.MaxValue;
		Assert.Throws<AuthenticationTagMismatchException>(() => _crypto.Decrypt(_key, encrypted));
	}

	[Fact]
	public void DifferentKeyCannotDecrypt()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("secret");
		byte[] encrypted = _crypto.Encrypt(_key, bytes);
		byte[] wrongKey = RandomNumberGenerator.GetBytes(32);
		Assert.Throws<AuthenticationTagMismatchException>(() => _crypto.Decrypt(wrongKey, encrypted));
	}

	[Fact]
	public void WrongKeySizeThrows()
	{
		Assert.Throws<ArgumentException>(() => _crypto.Encrypt(new byte[16], new byte[10]));
	}

	[Fact]
	public void EachEncryptionProducesUniqueNonce()
	{
		byte[] bytes = Encoding.UTF8.GetBytes("test");
		byte[] array = _crypto.Encrypt(_key, bytes);
		byte[] array2 = _crypto.Encrypt(_key, bytes);
		byte[] subArray = array[..12];
		byte[] subArray2 = array2[..12];
		Assert.NotEqual(subArray, subArray2);
	}

	[Fact]
	public void EncryptObjectRoundtrip()
	{
		var obj = new
		{
			Name = "Test",
			Value = 42
		};
		byte[] encryptedBlob = _crypto.EncryptObject(_key, obj);
		Assert.Equal("Test", _crypto.DecryptObject<JsonElement>(_key, encryptedBlob).GetProperty("Name").GetString());
	}
}
