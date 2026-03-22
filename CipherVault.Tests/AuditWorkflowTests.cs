using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CipherVault.Core.Interfaces;
using CipherVault.Core.Models;
using CipherVault.Core.Services;
using CipherVault.UI.Services;
using CipherVault.UI.ViewModels;
using Xunit;

namespace CipherVault.Tests;

public class AuditWorkflowTests
{
	private sealed class FakeAuditWorkflowHost : IAuditWorkflowHost
	{
		private readonly List<VaultEntryPlain> _entries;

		public AppSettings Settings { get; set; } = new AppSettings();

		public string StatusText { get; set; } = string.Empty;

		public FakeAuditWorkflowHost(IEnumerable<VaultEntryPlain> entries)
		{
			_entries = entries.Select(Clone).ToList();
		}

		public List<VaultEntryPlain> GetAllDecryptedEntries(bool includeDeleted = false)
		{
			return _entries.Where((VaultEntryPlain e) => includeDeleted || !e.IsDeleted).Select(Clone).ToList();
		}

		public Task SaveEntryAsync(VaultEntryPlain entry, bool refresh = true)
		{
			int num = _entries.FindIndex((VaultEntryPlain x) => x.Id == entry.Id);
			if (num >= 0)
			{
				_entries[num] = Clone(entry);
			}
			else
			{
				_entries.Add(Clone(entry));
			}
			return Task.CompletedTask;
		}

		public Task RefreshAsync()
		{
			return Task.CompletedTask;
		}

		public void RecordUserActivity()
		{
		}

		public Task RecordRemediationQueueCompletedAsync(int clearedItems)
		{
			return Task.CompletedTask;
		}

		public Task PersistRemediationQueueStateAsync(IEnumerable<int> dismissedEntryIds, IEnumerable<int> queueOrderEntryIds)
		{
			Settings.RemediationDismissedEntryIds = SerializeEntryIds(dismissedEntryIds);
			Settings.RemediationQueueOrderEntryIds = SerializeEntryIds(queueOrderEntryIds);
			return Task.CompletedTask;
		}

		public bool OpenEntryEditorById(int entryId, bool returnToAudit = false)
		{
			return _entries.Any((VaultEntryPlain e) => e.Id == entryId && !e.IsDeleted);
		}

		public VaultEntryPlain? GetEntry(int id)
		{
			return _entries.FirstOrDefault((VaultEntryPlain e) => e.Id == id);
		}

		private static string SerializeEntryIds(IEnumerable<int> entryIds)
		{
			List<int> list = new List<int>();
			HashSet<int> hashSet = new HashSet<int>();
			foreach (int entryId in entryIds)
			{
				if (entryId > 0 && hashSet.Add(entryId))
				{
					list.Add(entryId);
				}
			}
			return string.Join(",", list);
		}

		private static VaultEntryPlain Clone(VaultEntryPlain source)
		{
			return new VaultEntryPlain
			{
				Id = source.Id,
				Title = source.Title,
				Username = source.Username,
				Password = source.Password,
				Notes = source.Notes,
				Url = source.Url,
				Tags = source.Tags,
				TotpSecret = source.TotpSecret,
				FolderId = source.FolderId,
				Favorite = source.Favorite,
				PasswordReminderDays = source.PasswordReminderDays,
				PasswordLastChangedUtc = source.PasswordLastChangedUtc,
				IsDeleted = source.IsDeleted,
				DeletedAtUtc = source.DeletedAtUtc,
				CreatedAt = source.CreatedAt,
				UpdatedAt = source.UpdatedAt,
				PasswordHistory = source.PasswordHistory.Select((PasswordHistoryItem h) => new PasswordHistoryItem
				{
					Password = h.Password,
					ChangedAtUtc = h.ChangedAtUtc
				}).ToList()
			};
		}
	}

	private sealed class FakeAuditDialogService : IAuditDialogService
	{
		public bool DuplicateMergeDecision { get; set; } = true;

		public bool AutoSecureDecision { get; set; } = true;

		public bool ConfirmDuplicateMerge(string username, string site, int entryCount)
		{
			return DuplicateMergeDecision;
		}

		public bool ConfirmAutoSecure(string entryTitle)
		{
			return AutoSecureDecision;
		}
	}

	private sealed class NoOpVaultRepository : IVaultRepository
	{
		public Task<VaultMeta?> GetVaultMetaAsync()
		{
			return Task.FromResult<VaultMeta?>(null);
		}

		public Task SaveVaultMetaAsync(VaultMeta meta)
		{
			return Task.CompletedTask;
		}

		public Task<List<VaultEntryRecord>> GetAllEntriesAsync(bool includeDeleted = false)
		{
			return Task.FromResult(new List<VaultEntryRecord>());
		}

		public Task<VaultEntryRecord?> GetEntryByIdAsync(int id)
		{
			return Task.FromResult<VaultEntryRecord?>(null);
		}

		public Task<int> InsertEntryAsync(VaultEntryRecord record)
		{
			return Task.FromResult(0);
		}

		public Task UpdateEntryAsync(VaultEntryRecord record)
		{
			return Task.CompletedTask;
		}

		public Task DeleteEntryAsync(int id)
		{
			return Task.CompletedTask;
		}

		public Task RestoreEntryAsync(int id)
		{
			return Task.CompletedTask;
		}

		public Task DeleteEntryPermanentlyAsync(int id)
		{
			return Task.CompletedTask;
		}
	}

	private sealed class ImmediateEmptyBreachHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(string.Empty)
			});
		}
	}

	private sealed class DelayedBreachHandler : HttpMessageHandler
	{
		private readonly int _delayMs;

		public DelayedBreachHandler(int delayMs)
		{
			_delayMs = delayMs;
		}

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			await Task.Delay(_delayMs, cancellationToken);
			return new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(string.Empty)
			};
		}
	}

	private sealed class FlakyBreachHandler : HttpMessageHandler
	{
		private readonly int _failCountBeforeSuccess;

		private readonly HttpStatusCode _statusCode;

		public int RequestCount { get; private set; }

		public FlakyBreachHandler(int failCountBeforeSuccess, HttpStatusCode statusCode)
		{
			_failCountBeforeSuccess = failCountBeforeSuccess;
			_statusCode = statusCode;
		}

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestCount++;
			if (RequestCount <= _failCountBeforeSuccess)
			{
				return Task.FromResult(new HttpResponseMessage(_statusCode)
				{
					Content = new StringContent(string.Empty)
				});
			}
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
			{
				Content = new StringContent(string.Empty)
			});
		}
	}

	[Fact]
	public async Task FullScanThenAutoSecure_ClearsWeakQueueItem()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[2]
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "Email",
				Username = "alice@example.com",
				Password = "abc",
				Url = "https://mail.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 2,
				Title = "Bank",
				Username = "alice-bank",
				Password = "StrongPass!2026",
				Url = "https://bank.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService
		{
			AutoSecureDecision = true
		});
		await vm.RunFullScanForRemediationAsync();
		Assert.True(vm.RemediationQueueCount > 0);
		Assert.NotNull(vm.ActiveRemediationItem);
		VaultEntryPlain? before = host.GetEntry(1);
        Assert.NotNull(before);
        string oldPassword = before!.Password;
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.AutoSecureActiveRemediationItemCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		VaultEntryPlain? after = host.GetEntry(1);
        Assert.NotNull(after);
        string password = after!.Password;
		Assert.NotEqual(oldPassword, password);
		Assert.Equal(0, vm.RemediationQueueCount);
		Assert.Contains("Auto-secured", vm.Summary, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BreachScan_CanBeCanceled_ReportsProgress()
	{
		List<VaultEntryPlain> entries = (from i in Enumerable.Range(1, 8)
			select new VaultEntryPlain
			{
				Id = i,
				Title = $"Entry {i}",
				Username = $"user{i}",
				Password = $"P@ssw0rd-{i}-Unique!",
				Url = $"https://site{i}.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}).ToList();
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(entries)
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = true
			}
		};
		BreachCheckService breachService = new BreachCheckService(new HttpClient(new DelayedBreachHandler(180)));
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService(), breachService);
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.RunBreachScanCommand);
		Task runTask = asyncRelayCommand.ExecuteAsync(null);
		await WaitForConditionAsync(() => vm.IsBreachScanRunning, 2000);
		vm.CancelBreachScanCommand.Execute(null);
		await runTask;
		Assert.Contains("canceled", vm.BreachSummary, StringComparison.OrdinalIgnoreCase);
		Assert.False(vm.IsBreachScanRunning);
		Assert.True(vm.BreachCheckedCount < vm.BreachTotalCount);
	}

	[Fact]
	public async Task FullScan_CancelDuringBreach_StillRunsDuplicateScan()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[2]
		{
			new VaultEntryPlain
			{
				Id = 1,
				Title = "GitHub Main",
				Username = "alice",
				Password = "Strong#Pass-1",
				Url = "https://github.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 2,
				Title = "GitHub Alt",
				Username = "alice",
				Password = "Strong#Pass-2",
				Url = "https://github.com/login",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = true
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService(), new BreachCheckService(new HttpClient(new DelayedBreachHandler(200))));
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.RunFullScanCommand);
		Task runTask = asyncRelayCommand.ExecuteAsync(null);
		await WaitForConditionAsync(() => vm.IsBreachScanRunning, 2000);
		vm.CancelBreachScanCommand.Execute(null);
		await runTask;
		Assert.Contains("canceled", vm.BreachSummary, StringComparison.OrdinalIgnoreCase);
		Assert.True(vm.DuplicateGroups.Count > 0);
		Assert.Contains("duplicate account group", vm.DuplicateSummary, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BreachScan_RetriesTransientFailures_ThenSucceeds()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[1]
		{
			new VaultEntryPlain
			{
				Id = 10,
				Title = "Retry Entry",
				Username = "retry-user",
				Password = "Retry#Password!2026",
				Url = "https://retry.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = true
			}
		};
		FlakyBreachHandler flakyHandler = new FlakyBreachHandler(2, HttpStatusCode.ServiceUnavailable);
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService(), new BreachCheckService(new HttpClient(flakyHandler)));
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.RunBreachScanCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		Assert.Equal(3, flakyHandler.RequestCount);
		Assert.Contains("No breached passwords found", vm.BreachSummary, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task BreachScan_RateLimit_ShowsHelpfulMessage()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[1]
		{
			new VaultEntryPlain
			{
				Id = 11,
				Title = "Rate Limit Entry",
				Username = "ratelimit-user",
				Password = "Rate#Limit!2026",
				Url = "https://rate.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = true
			}
		};
		FlakyBreachHandler handler = new FlakyBreachHandler(int.MaxValue, HttpStatusCode.TooManyRequests);
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService(), new BreachCheckService(new HttpClient(handler)));
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.RunBreachScanCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		Assert.Contains("rate limit", vm.BreachSummary, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("rate-limited", vm.ScanStatusLabel, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task RemediationQueue_MarkDone_PersistsAcrossViewModelInstances()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[2]
		{
			new VaultEntryPlain
			{
				Id = 101,
				Title = "Alpha",
				Username = "alpha-user",
				Password = "short",
				Url = "https://alpha.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 102,
				Title = "Beta",
				Username = "beta-user",
				Password = "tiny",
				Url = "https://beta.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm1 = BuildViewModel(host, new FakeAuditDialogService());
		await vm1.RunFullScanForRemediationAsync();
		int dismissedId = Assert.IsType<RemediationQueueItem>(vm1.ActiveRemediationItem).EntryId;
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm1.MarkActiveRemediationDoneCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		AuditViewModel vm2 = BuildViewModel(host, new FakeAuditDialogService());
		await vm2.RunFullScanForRemediationAsync();
		Assert.DoesNotContain((IEnumerable<RemediationQueueItem>)vm2.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
		Assert.Contains(dismissedId.ToString(), host.Settings.RemediationDismissedEntryIds, StringComparison.Ordinal);
	}

	[Fact]
	public async Task RemediationQueue_SkipOrder_PersistsAcrossViewModelInstances()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[3]
		{
			new VaultEntryPlain
			{
				Id = 201,
				Title = "A Item",
				Username = "a-user",
				Password = "aaa",
				Url = "https://a.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 202,
				Title = "B Item",
				Username = "b-user",
				Password = "bbb",
				Url = "https://b.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 203,
				Title = "C Item",
				Username = "c-user",
				Password = "ccc",
				Url = "https://c.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm1 = BuildViewModel(host, new FakeAuditDialogService());
		await vm1.RunFullScanForRemediationAsync();
		List<int> initialOrder = vm1.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm1.SkipActiveRemediationItemCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		List<int> rotatedOrder = vm1.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		Assert.False(initialOrder.SequenceEqual(rotatedOrder));
		AuditViewModel vm2 = BuildViewModel(host, new FakeAuditDialogService());
		await vm2.RunFullScanForRemediationAsync();
		List<int> actual = vm2.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		Assert.Equal(rotatedOrder, actual);
	}

	[Fact]
	public async Task RemediationQueue_UndoMarkDone_RestoresDismissedItem()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[2]
		{
			new VaultEntryPlain
			{
				Id = 240,
				Title = "Undo A",
				Username = "undo-a",
				Password = "aaa",
				Url = "https://undo-a.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 241,
				Title = "Undo B",
				Username = "undo-b",
				Password = "bbb",
				Url = "https://undo-b.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService());
		await vm.RunFullScanForRemediationAsync();
		int dismissedId = Assert.IsType<RemediationQueueItem>(vm.ActiveRemediationItem).EntryId;
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.MarkActiveRemediationDoneCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		Assert.DoesNotContain((IEnumerable<RemediationQueueItem>)vm.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
		AsyncRelayCommand asyncRelayCommand2 = Assert.IsType<AsyncRelayCommand>(vm.UndoRemediationQueueActionCommand);
		await asyncRelayCommand2.ExecuteAsync(null);
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
	}

	[Fact]
	public async Task RemediationQueue_UndoSkip_RestoresPreviousOrder()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[3]
		{
			new VaultEntryPlain
			{
				Id = 250,
				Title = "Undo Skip A",
				Username = "undo-skip-a",
				Password = "aaa",
				Url = "https://undo-skip-a.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 251,
				Title = "Undo Skip B",
				Username = "undo-skip-b",
				Password = "bbb",
				Url = "https://undo-skip-b.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 252,
				Title = "Undo Skip C",
				Username = "undo-skip-c",
				Password = "ccc",
				Url = "https://undo-skip-c.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService());
		await vm.RunFullScanForRemediationAsync();
		List<int> initialOrder = vm.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.SkipActiveRemediationItemCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		List<int> second = vm.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		Assert.False(initialOrder.SequenceEqual(second));
		AsyncRelayCommand asyncRelayCommand2 = Assert.IsType<AsyncRelayCommand>(vm.UndoRemediationQueueActionCommand);
		await asyncRelayCommand2.ExecuteAsync(null);
		List<int> actual = vm.RemediationQueue.Select((RemediationQueueItem item) => item.EntryId).ToList();
		Assert.Equal(initialOrder, actual);
	}

	[Fact]
	public async Task RemediationQueue_RestoreDismissedItemCommand_ReaddsItemToQueue()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[2]
		{
			new VaultEntryPlain
			{
				Id = 270,
				Title = "Restore A",
				Username = "restore-a",
				Password = "aaa",
				Url = "https://restore-a.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 271,
				Title = "Restore B",
				Username = "restore-b",
				Password = "bbb",
				Url = "https://restore-b.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService());
		await vm.RunFullScanForRemediationAsync();
		int dismissedId = Assert.IsType<RemediationQueueItem>(vm.ActiveRemediationItem).EntryId;
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm.MarkActiveRemediationDoneCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm.DismissedRemediationItems, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
		AsyncRelayCommand asyncRelayCommand2 = Assert.IsType<AsyncRelayCommand>(vm.RestoreDismissedRemediationItemCommand);
		await asyncRelayCommand2.ExecuteAsync(dismissedId);
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
		Assert.DoesNotContain((IEnumerable<RemediationQueueItem>)vm.DismissedRemediationItems, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
	}

	[Fact]
	public async Task RemediationQueue_ResetQueue_ClearsPersistedStateAndRestoresDismissedItems()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[3]
		{
			new VaultEntryPlain
			{
				Id = 301,
				Title = "Reset A",
				Username = "reset-a",
				Password = "aaa",
				Url = "https://reset-a.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 302,
				Title = "Reset B",
				Username = "reset-b",
				Password = "bbb",
				Url = "https://reset-b.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			},
			new VaultEntryPlain
			{
				Id = 303,
				Title = "Reset C",
				Username = "reset-c",
				Password = "ccc",
				Url = "https://reset-c.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false
			}
		};
		AuditViewModel vm1 = BuildViewModel(host, new FakeAuditDialogService());
		await vm1.RunFullScanForRemediationAsync();
		AsyncRelayCommand asyncRelayCommand = Assert.IsType<AsyncRelayCommand>(vm1.SkipActiveRemediationItemCommand);
		await asyncRelayCommand.ExecuteAsync(null);
		int dismissedId = Assert.IsType<RemediationQueueItem>(vm1.ActiveRemediationItem).EntryId;
		AsyncRelayCommand asyncRelayCommand2 = Assert.IsType<AsyncRelayCommand>(vm1.MarkActiveRemediationDoneCommand);
		await asyncRelayCommand2.ExecuteAsync(null);
		Assert.False(string.IsNullOrWhiteSpace(host.Settings.RemediationDismissedEntryIds));
		Assert.False(string.IsNullOrWhiteSpace(host.Settings.RemediationQueueOrderEntryIds));
		Assert.True(vm1.HasDismissedRemediationItems);
		Assert.True(vm1.DismissedRemediationCount > 0);
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm1.DismissedRemediationItems, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
		AsyncRelayCommand asyncRelayCommand3 = Assert.IsType<AsyncRelayCommand>(vm1.ResetRemediationQueueCommand);
		await asyncRelayCommand3.ExecuteAsync(null);
		Assert.False(vm1.HasDismissedRemediationItems);
		Assert.Equal(0, vm1.DismissedRemediationCount);
		await WaitForConditionAsync(() => string.IsNullOrEmpty(host.Settings.RemediationDismissedEntryIds) && string.IsNullOrEmpty(host.Settings.RemediationQueueOrderEntryIds), 2000);
		AuditViewModel vm2 = BuildViewModel(host, new FakeAuditDialogService());
		await vm2.RunFullScanForRemediationAsync();
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm2.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == dismissedId));
	}

	[Fact]
	public async Task RemediationQueue_StalePersistedIds_ArePrunedAndSaved()
	{
		FakeAuditWorkflowHost host = new FakeAuditWorkflowHost(new VaultEntryPlain[1]
		{
			new VaultEntryPlain
			{
				Id = 410,
				Title = "Only Item",
				Username = "only-user",
				Password = "short",
				Url = "https://only.example.com",
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			}
		})
		{
			Settings = new AppSettings
			{
				AllowBreachCheck = false,
				RemediationDismissedEntryIds = "999,1000",
				RemediationQueueOrderEntryIds = "1000,999"
			}
		};
		AuditViewModel vm = BuildViewModel(host, new FakeAuditDialogService());
		await vm.RunFullScanForRemediationAsync();
		await WaitForConditionAsync(() => string.IsNullOrEmpty(host.Settings.RemediationDismissedEntryIds) && string.IsNullOrEmpty(host.Settings.RemediationQueueOrderEntryIds), 2000);
		Assert.Contains((IEnumerable<RemediationQueueItem>)vm.RemediationQueue, (Predicate<RemediationQueueItem>)((RemediationQueueItem item) => item.EntryId == 410));
	}

	private static async Task WaitForConditionAsync(Func<bool> condition, int timeoutMs)
	{
		DateTime started = DateTime.UtcNow;
		while (!condition())
		{
			if ((DateTime.UtcNow - started).TotalMilliseconds > (double)timeoutMs)
			{
				throw new TimeoutException("Condition was not met within the expected time window.");
			}
			await Task.Delay(25);
		}
	}

	private static AuditViewModel BuildViewModel(FakeAuditWorkflowHost host, FakeAuditDialogService dialogService, BreachCheckService? breachService = null)
	{
		if (breachService == null)
		{
			breachService = new BreachCheckService(new HttpClient(new ImmediateEmptyBreachHandler()));
		}
		return new AuditViewModel(new PasswordAuditService(), breachService, new DuplicateAccountService(), new PasswordGeneratorService(), new RemediationQueueService(), new NoOpVaultRepository(), dialogService, host);
	}
}

