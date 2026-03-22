using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using CipherVault.Core.Crypto;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class ExportImportTests
{
	private readonly KeyDerivationService _kdf = new KeyDerivationService();

	private readonly CryptoService _crypto = new CryptoService();

	[Fact]
	public async Task ExportImportRoundtrip()
	{
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		string tempFile = Path.GetTempFileName() + ".cipherpw-backup";
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Title = "Google",
				Username = "user@gmail.com",
				Password = "secret123!",
				Url = "https://google.com",
				PasswordHistory = new List<PasswordHistoryItem>
				{
					new PasswordHistoryItem
					{
						Password = "old-secret!",
						ChangedAtUtc = DateTime.UtcNow.AddDays(-2.0)
					}
				},
				PasswordReminderDays = 90,
				PasswordLastChangedUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Title = "GitHub",
				Username = "devuser",
				Password = "gh_token_abc",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		};
		List<Folder> folders = new List<Folder>
		{
			new Folder
			{
				Name = "Work",
				CreatedAt = DateTime.UtcNow
			}
		};
		string exportPassword = "ExportPass123!";
		await service.ExportAsync(tempFile, exportPassword, entries, folders);
		BackupPayload backupPayload = await service.ImportAsync(tempFile, exportPassword);
		Assert.Equal(2, backupPayload.Entries.Count);
		Assert.Equal("Google", backupPayload.Entries[0].Title);
		Assert.Equal("secret123!", backupPayload.Entries[0].Password);
		Assert.Single(backupPayload.Entries[0].PasswordHistory);
		Assert.Equal("old-secret!", backupPayload.Entries[0].PasswordHistory[0].Password);
		Assert.Equal(90, backupPayload.Entries[0].PasswordReminderDays);
		Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc), backupPayload.Entries[0].PasswordLastChangedUtc);
		Assert.Single(backupPayload.Folders);
		File.Delete(tempFile);
	}

	[Fact]
	public async Task WrongPasswordFailsImport()
	{
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		string tempFile = Path.GetTempFileName() + ".cipherpw-backup";
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Title = "Test",
				Username = "u",
				Password = "p",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		};
		await service.ExportAsync(tempFile, "CorrectPassword!", entries, new List<Folder>());
		await Assert.ThrowsAsync<AuthenticationTagMismatchException>(() => service.ImportAsync(tempFile, "WrongPassword!"));
		File.Delete(tempFile);
	}

	[Fact]
	public async Task InvalidFileMagicThrows()
	{
		string tempFile = Path.GetTempFileName();
		await File.WriteAllBytesAsync(tempFile, new byte[10] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 });
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(tempFile, "anypass"));
		File.Delete(tempFile);
	}

	[Fact]
	public void ImportCsv_ParsesQuotedCommasQuotesAndMultilineNotes()
	{
		BackupExportImportService backupExportImportService = new BackupExportImportService(_kdf, _crypto);
		string csvContent = "name,username,password,url,notes\r\n\"Bank, Personal\",alice,\"p\"\"ass,123\",https://bank.example.com,\"Line 1\r\nLine 2\"";
		List<VaultEntryPlain> collection = backupExportImportService.ImportCsv(csvContent);
		VaultEntryPlain vaultEntryPlain = Assert.Single(collection);
		Assert.Equal("Bank, Personal", vaultEntryPlain.Title);
		Assert.Equal("alice", vaultEntryPlain.Username);
		Assert.Equal("p\"ass,123", vaultEntryPlain.Password);
		Assert.Equal("https://bank.example.com", vaultEntryPlain.Url);
		Assert.Equal("Line 1\r\nLine 2", vaultEntryPlain.Notes);
	}

	[Fact]
	public void ImportCsv_ThrowsOnMalformedQuotedData()
	{
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		string csv = "name,username,password\r\n\"Broken Entry,alice,secret\r\n";
		Assert.Throws<InvalidDataException>(() => service.ImportCsv(csv));
	}

	[Fact]
	public async Task ImportRejectsTruncatedBackup()
	{
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		string tempFile = Path.GetTempFileName() + ".cipherpw-backup";

		await File.WriteAllBytesAsync(tempFile, new byte[20]);

		await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(tempFile, "anypass"));
		File.Delete(tempFile);
	}

	[Fact]
	public async Task ImportRejectsInvalidArgonParameters()
	{
		BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
		string tempFile = Path.GetTempFileName() + ".cipherpw-backup";

		using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
		{
			await fs.WriteAsync(System.Text.Encoding.ASCII.GetBytes("CPHRBAK1"));
			await fs.WriteAsync(new byte[16]);
			await fs.WriteAsync(BitConverter.GetBytes(8));
			await fs.WriteAsync(BitConverter.GetBytes(3));
			await fs.WriteAsync(BitConverter.GetBytes(2));
			await fs.WriteAsync(new byte[28]);
		}

		await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(tempFile, "anypass"));
		File.Delete(tempFile);
	}

    [Fact]
    public async Task ImportRejectsOversizedBackup()
    {
        BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
        string tempFile = Path.GetTempFileName() + ".cipherpw-backup";

        using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
        {
            fs.SetLength((64L * 1024 * 1024) + 1);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(tempFile, "anypass"));
        File.Delete(tempFile);
    }

    [Theory]
    [InlineData(256, 0, 2)]
    [InlineData(256, 13, 2)]
    [InlineData(256, 3, 0)]
    [InlineData(256, 3, 99)]
    public async Task ImportRejectsOutOfRangeArgonIterationOrParallelism(int memoryMb, int iterations, int parallelism)
    {
        BackupExportImportService service = new BackupExportImportService(_kdf, _crypto);
        string tempFile = Path.GetTempFileName() + ".cipherpw-backup";

        using (FileStream fs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
        {
            await fs.WriteAsync(System.Text.Encoding.ASCII.GetBytes("CPHRBAK1"));
            await fs.WriteAsync(new byte[16]);
            await fs.WriteAsync(BitConverter.GetBytes(memoryMb));
            await fs.WriteAsync(BitConverter.GetBytes(iterations));
            await fs.WriteAsync(BitConverter.GetBytes(parallelism));
            await fs.WriteAsync(new byte[28]);
        }

        await Assert.ThrowsAsync<InvalidDataException>(() => service.ImportAsync(tempFile, "anypass"));
        File.Delete(tempFile);
    }}

