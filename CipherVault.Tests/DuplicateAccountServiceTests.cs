using System;
using System.Collections.Generic;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class DuplicateAccountServiceTests
{
	private readonly DuplicateAccountService _service = new DuplicateAccountService();

	[Fact]
	public void FindDuplicateGroups_GroupsByNormalizedSiteAndUsername()
	{
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "GitHub Main",
				Username = "alice",
				Url = "https://github.com",
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 2,
				Title = "GitHub Alt",
				Username = "ALICE",
				Url = "https://github.com/login",
				UpdatedAt = DateTime.UtcNow.AddMinutes(-5.0)
			},
			new VaultEntryPlain
			{
				Id = 3,
				Title = "GitHub Bob",
				Username = "bob",
				Url = "https://github.com",
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 4,
				Title = "GitLab Alice",
				Username = "alice",
				Url = "https://gitlab.com",
				UpdatedAt = DateTime.UtcNow
			}
		};
		List<DuplicateAccountGroupResult> list = _service.FindDuplicateGroups(entries);
		Assert.Single(list);
		Assert.Equal("github.com", list[0].Site);
		Assert.Equal("alice", list[0].Username);
		Assert.Equal(2, list[0].Entries.Count);
	}

	[Fact]
	public void MergeDuplicateGroup_CombinesDataAndReturnsMergedIds()
	{
		VaultEntryPlain vaultEntryPlain = new VaultEntryPlain
		{
			Id = 10,
			Title = "Example",
			Username = "alice",
			Password = "Primary!123",
			Url = "https://example.com",
			Notes = "primary note",
			Tags = "work",
			Favorite = true,
			PasswordReminderDays = 0,
			PasswordLastChangedUtc = null,
			PasswordHistory = new List<PasswordHistoryItem>(),
			CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
			UpdatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
		};
		VaultEntryPlain vaultEntryPlain2 = new VaultEntryPlain
		{
			Id = 11,
			Title = "Example 2",
			Username = "alice",
			Password = "Secondary!456",
			Url = "https://example.com/login",
			Notes = "secondary note",
			Tags = "personal, work",
			Favorite = false,
			PasswordReminderDays = 90,
			PasswordLastChangedUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc),
			PasswordHistory = new List<PasswordHistoryItem>
			{
				new PasswordHistoryItem
				{
					Password = "Legacy!789",
					ChangedAtUtc = new DateTime(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc)
				}
			},
			CreatedAt = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc),
			UpdatedAt = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc)
		};
		DuplicateMergeResult duplicateMergeResult = _service.MergeDuplicateGroup(new VaultEntryPlain[2] { vaultEntryPlain, vaultEntryPlain2 });
		Assert.Equal(10, duplicateMergeResult.Keeper.Id);
		Assert.Single(duplicateMergeResult.MergedEntryIds);
		Assert.Contains(11, (IEnumerable<int>)duplicateMergeResult.MergedEntryIds);
		Assert.Equal("Primary!123", duplicateMergeResult.Keeper.Password);
		Assert.Contains((IEnumerable<PasswordHistoryItem>)duplicateMergeResult.Keeper.PasswordHistory, (Predicate<PasswordHistoryItem>)((PasswordHistoryItem h) => h.Password == "Secondary!456"));
		Assert.Contains((IEnumerable<PasswordHistoryItem>)duplicateMergeResult.Keeper.PasswordHistory, (Predicate<PasswordHistoryItem>)((PasswordHistoryItem h) => h.Password == "Legacy!789"));
		Assert.Contains("secondary note", duplicateMergeResult.Keeper.Notes, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("work", duplicateMergeResult.Keeper.Tags, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("personal", duplicateMergeResult.Keeper.Tags, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(90, duplicateMergeResult.Keeper.PasswordReminderDays);
		Assert.Equal(new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), duplicateMergeResult.Keeper.PasswordLastChangedUtc);
	}
}
