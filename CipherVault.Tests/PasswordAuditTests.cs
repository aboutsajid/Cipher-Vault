using System;
using System.Collections.Generic;
using System.Linq;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class PasswordAuditTests
{
	private readonly PasswordAuditService _audit = new PasswordAuditService();

	[Fact]
	public void DetectsWeakPassword()
	{
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "Weak",
				Password = "123"
			}
		};
		List<AuditResult> collection = _audit.Audit(entries);
		Assert.Contains((IEnumerable<AuditResult>)collection, (Predicate<AuditResult>)((AuditResult r) => r.EntryId == 1 && r.Issues.Any((string i) => i.Contains("short") || i.Contains("Weak"))));
	}

	[Fact]
	public void DetectsReusedPasswords()
	{
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "Site A",
				Password = "SamePassword123!"
			},
			new VaultEntryPlain
			{
				Id = 2,
				Title = "Site B",
				Password = "SamePassword123!"
			}
		};
		List<AuditResult> collection = _audit.Audit(entries);
		Assert.Contains((IEnumerable<AuditResult>)collection, (Predicate<AuditResult>)((AuditResult r) => r.Issues.Any((string i) => i.Contains("reused"))));
	}

	[Fact]
	public void StrongUniquePasswordHasNoIssues()
	{
		List<VaultEntryPlain> entries = new List<VaultEntryPlain>
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "Good",
				Password = "Tr0ub4dor&3!xYzQ9"
			}
		};
		List<AuditResult> collection = _audit.Audit(entries);
		Assert.DoesNotContain((IEnumerable<AuditResult>)collection, (Predicate<AuditResult>)((AuditResult r) => r.EntryId == 1));
	}
}
