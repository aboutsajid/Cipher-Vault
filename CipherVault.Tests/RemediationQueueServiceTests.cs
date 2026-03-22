using System;
using System.Collections.Generic;
using System.Linq;
using CipherVault.Core.Services;
using Xunit;

namespace CipherVault.Tests;

public class RemediationQueueServiceTests
{
	private readonly RemediationQueueService _service = new RemediationQueueService();

	[Fact]
	public void BuildQueue_CombinesSignalsAndPrioritizesBreachedEntries()
	{
		List<AuditResult> auditResults = new List<AuditResult>
		{
			new AuditResult
			{
				EntryId = 1,
				EntryTitle = "Weak A",
				Issues = new List<string> { "short", "missing symbol" }
			}
		};
		List<RemediationBreachResult> breachResults = new List<RemediationBreachResult>
		{
			new RemediationBreachResult
			{
				EntryId = 2,
				EntryTitle = "Breach B",
				BreachCount = 2000000
			}
		};
		List<DuplicateAccountGroupResult> duplicateGroups = new List<DuplicateAccountGroupResult>
		{
			new DuplicateAccountGroupResult
			{
				Site = "example.com",
				Username = "alice",
				Entries = new List<DuplicateAccountItem>
				{
					new DuplicateAccountItem
					{
						EntryId = 1,
						Title = "Weak A"
					},
					new DuplicateAccountItem
					{
						EntryId = 3,
						Title = "Duplicate C"
					}
				}
			}
		};
		List<RemediationQueueItem> list = _service.BuildQueue(auditResults, breachResults, duplicateGroups);
		Assert.Equal(3, list.Count);
		Assert.Equal(2, list[0].EntryId);
		RemediationQueueItem remediationQueueItem = list.Single((RemediationQueueItem x) => x.EntryId == 1);
		Assert.True(remediationQueueItem.HasWeakIssues);
		Assert.True(remediationQueueItem.IsDuplicate);
		Assert.False(remediationQueueItem.IsBreached);
		Assert.Equal("weak + duplicate", remediationQueueItem.RiskLabel);
	}

	[Fact]
	public void BuildQueue_DeduplicatesByEntryIdAcrossSignals()
	{
		List<AuditResult> auditResults = new List<AuditResult>
		{
			new AuditResult
			{
				EntryId = 10,
				EntryTitle = "Shared",
				Issues = new List<string> { "short" }
			}
		};
		List<RemediationBreachResult> breachResults = new List<RemediationBreachResult>
		{
			new RemediationBreachResult
			{
				EntryId = 10,
				EntryTitle = "Shared",
				BreachCount = 50000
			}
		};
		List<DuplicateAccountGroupResult> duplicateGroups = new List<DuplicateAccountGroupResult>
		{
			new DuplicateAccountGroupResult
			{
				Site = "acme.com",
				Username = "sam",
				Entries = new List<DuplicateAccountItem>
				{
					new DuplicateAccountItem
					{
						EntryId = 10,
						Title = "Shared"
					}
				}
			}
		};
		List<RemediationQueueItem> list = _service.BuildQueue(auditResults, breachResults, duplicateGroups);
		Assert.Single(list);
		Assert.True(list[0].HasWeakIssues);
		Assert.True(list[0].IsBreached);
		Assert.True(list[0].IsDuplicate);
		Assert.Contains("breached", list[0].RiskLabel, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RotateQueue_MovesFirstItemToEnd()
	{
		List<RemediationQueueItem> queue = new List<RemediationQueueItem>
		{
			new RemediationQueueItem
			{
				EntryId = 1,
				EntryTitle = "One"
			},
			new RemediationQueueItem
			{
				EntryId = 2,
				EntryTitle = "Two"
			},
			new RemediationQueueItem
			{
				EntryId = 3,
				EntryTitle = "Three"
			}
		};
		List<RemediationQueueItem> source = _service.RotateQueue(queue);
		Assert.Equal(new int[3] { 2, 3, 1 }, source.Select((RemediationQueueItem x) => x.EntryId).ToArray());
	}

	[Fact]
	public void RemoveEntry_RemovesOnlyMatchingEntry()
	{
		List<RemediationQueueItem> queue = new List<RemediationQueueItem>
		{
			new RemediationQueueItem
			{
				EntryId = 1,
				EntryTitle = "One"
			},
			new RemediationQueueItem
			{
				EntryId = 2,
				EntryTitle = "Two"
			},
			new RemediationQueueItem
			{
				EntryId = 3,
				EntryTitle = "Three"
			}
		};
		List<RemediationQueueItem> list = _service.RemoveEntry(queue, 2);
		Assert.Equal(2, list.Count);
		Assert.DoesNotContain((IEnumerable<RemediationQueueItem>)list, (Predicate<RemediationQueueItem>)((RemediationQueueItem x) => x.EntryId == 2));
		Assert.Contains((IEnumerable<RemediationQueueItem>)list, (Predicate<RemediationQueueItem>)((RemediationQueueItem x) => x.EntryId == 1));
		Assert.Contains((IEnumerable<RemediationQueueItem>)list, (Predicate<RemediationQueueItem>)((RemediationQueueItem x) => x.EntryId == 3));
	}

	[Fact]
	public void BuildQueue_IgnoresInvalidEntryIds()
	{
		List<AuditResult> auditResults = new List<AuditResult>
		{
			new AuditResult
			{
				EntryId = 0,
				EntryTitle = "Invalid Audit",
				Issues = new List<string> { "short" }
			},
			new AuditResult
			{
				EntryId = 5,
				EntryTitle = "Valid",
				Issues = new List<string> { "weak" }
			}
		};
		List<RemediationBreachResult> breachResults = new List<RemediationBreachResult>
		{
			new RemediationBreachResult
			{
				EntryId = -1,
				EntryTitle = "Invalid Breach",
				BreachCount = 12345
			}
		};
		List<DuplicateAccountGroupResult> duplicateGroups = new List<DuplicateAccountGroupResult>
		{
			new DuplicateAccountGroupResult
			{
				Site = "example.com",
				Username = "user",
				Entries = new List<DuplicateAccountItem>
				{
					new DuplicateAccountItem
					{
						EntryId = 0,
						Title = "Invalid Duplicate"
					}
				}
			}
		};
		List<RemediationQueueItem> list = _service.BuildQueue(auditResults, breachResults, duplicateGroups);
		Assert.Single(list);
		Assert.Equal(5, list[0].EntryId);
	}

	[Fact]
	public void BuildQueue_DuplicateGroupRepeatsSameEntry_ScoresDuplicateOnce()
	{
		List<AuditResult> auditResults = new List<AuditResult>
		{
			new AuditResult
			{
				EntryId = 1,
				EntryTitle = "Shared",
				Issues = new List<string> { "short" }
			}
		};
		List<DuplicateAccountGroupResult> duplicateGroups = new List<DuplicateAccountGroupResult>
		{
			new DuplicateAccountGroupResult
			{
				Site = "example.com",
				Username = "alice",
				Entries = new List<DuplicateAccountItem>
				{
					new DuplicateAccountItem
					{
						EntryId = 1,
						Title = "Shared"
					},
					new DuplicateAccountItem
					{
						EntryId = 1,
						Title = "Shared"
					}
				}
			}
		};
		List<RemediationQueueItem> list = _service.BuildQueue(auditResults, null, duplicateGroups);
		Assert.Single(list);
		Assert.Equal(3, list[0].Score);
	}
}
