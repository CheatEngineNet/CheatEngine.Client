using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class TableClientLookupTests
{
	[Fact]
	public void TryGetRecordMapsAnUnavailableAddressListToCapabilityUnavailable()
	{
		FakeRecordLookupPort lookups = new()
		{
			IndexStatus = RecordLookupStatus.AddressListUnavailable
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetRecordAt(3, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Tables.GetRecordAt", failure.Operation);
		Assert.Equal("Cheat Engine's Address List capability is unavailable.", failure.Message);
		Assert.Equal(3, lookups.LastIndex);
	}

	/// <summary>A copy of the top-level records fails like every record lookup, named after its method.</summary>
	[Theory]
	[InlineData(nameof(RecordLookupStatus.AddressListUnavailable), CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(nameof(RecordLookupStatus.InvalidRecord), CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.Unknown)]
	public void ACopyOfTheTopLevelRecordsFailsLikeEveryRecordLookup(string status,
		CheatEngineFailureKind expectedKind, CheatEngineHostEffect expectedEffect)
	{
		FakeRecordLookupPort lookups = new()
		{
			TableStatus = Enum.Parse<RecordLookupStatus>(status)
		};
		TableClient client = CreateClient(lookups);
		CancellationToken token = TestContext.Current.CancellationToken;

		bool copied = client.TryGetSnapshot(new MemoryRecordCollectionRequest(8), out AddressTableSnapshot table,
			out CheatEngineFailure snapshotFailure, token);
		bool found = client.TryFind(new MemoryRecordSearch("Ammo"), new MemoryRecordCollectionRequest(8),
			out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure findFailure, token);

		Assert.False(copied);
		Assert.Equal(default, table);
		Assert.Equal(expectedKind, snapshotFailure.Kind);
		Assert.Equal(expectedEffect, snapshotFailure.HostEffect);
		Assert.Equal("Tables.GetSnapshot", snapshotFailure.Operation);
		Assert.False(found);
		Assert.True(records.IsEmpty);
		Assert.Equal(expectedKind, findFailure.Kind);
		Assert.Equal(expectedEffect, findFailure.HostEffect);
		Assert.Equal("Tables.Find", findFailure.Operation);
	}

	[Fact]
	public void FindNamesItselfWhenTheCopyExceedsTheLimit()
	{
		FakeRecordLookupPort lookups = new()
		{
			Table = [Snapshot(1, "Ammo"), Snapshot(2, "Health")]
		};
		TableClient client = CreateClient(lookups);

		bool found = client.TryFind(new MemoryRecordSearch("Ammo"), new MemoryRecordCollectionRequest(1),
			out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(found);
		Assert.True(records.IsEmpty);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, failure.Kind);
		Assert.Equal("Tables.Find", failure.Operation);
	}

	[Fact]
	public void TryGetRecordPreservesNotFoundForAnAbsentRecord()
	{
		FakeRecordLookupPort lookups = new()
		{
			IdStatus = RecordLookupStatus.NotFound
		};
		TableClient client = CreateClient(lookups);
		MemoryRecordId id = new(42);

		bool succeeded = client.TryGetRecord(id, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal("Tables.GetRecord", failure.Operation);
		Assert.Equal("The requested Cheat Engine memory record was not found.", failure.Message);
		Assert.Equal(id, lookups.LastId);
	}

	[Fact]
	public void TryGetSelectedRecordMapsAMalformedRecordToInvalidHostResult()
	{
		FakeRecordLookupPort lookups = new()
		{
			SelectedStatus = RecordLookupStatus.InvalidRecord
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetSelectedRecord(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Tables.GetSelectedRecord", failure.Operation);
		Assert.Equal("Cheat Engine did not return the expected Address List contract.", failure.Message);
		Assert.Equal(1, lookups.SelectedCalls);
	}

	[Fact]
	public void TryGetRecordReturnsThePortSnapshotWhenTheLookupSucceeds()
	{
		MemoryRecordSnapshot expected = Snapshot(42, "Health");
		FakeRecordLookupPort lookups = new()
		{
			IdStatus = RecordLookupStatus.Success,
			IdRecord = expected
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetRecord(new MemoryRecordId(42), out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(expected, record);
		Assert.Equal(default, failure);
	}

	[Fact]
	public void FindReturnsEveryRecordThatSharesADuplicatedDescription()
	{
		// A14-30: the Client has no single lookup by description; a search returns every match, never an arbitrary first.
		FakeRecordLookupPort lookups = new()
		{
			Table = [Snapshot(1, "Ammo"), Snapshot(2, "Health"), Snapshot(3, "Ammo")]
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryFind(new MemoryRecordSearch("Ammo"), new MemoryRecordCollectionRequest(8),
			out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal([new MemoryRecordId(1), new MemoryRecordId(3)], records.Select(static record => record.Id));
	}

	[Fact]
	public void GetSnapshotReportsTheMaterializationLimitWithoutCopyingRecords()
	{
		FakeRecordLookupPort lookups = new()
		{
			Table = [Snapshot(1, "Ammo"), Snapshot(2, "Health")]
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetSnapshot(new MemoryRecordCollectionRequest(1), out AddressTableSnapshot table,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, table);
		Assert.Equal(CheatEngineFailureKind.ResultLimitExceeded, failure.Kind);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void GetRecordWithAnIndexBeyondTheTableReportsNotFoundWithoutALuaError()
	{
		// A14-31: an index past the end is an absent record, not a Lua error and not an exception.
		FakeRecordLookupPort lookups = new()
		{
			IndexStatus = RecordLookupStatus.NotFound
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetRecordAt(1000, out MemoryRecordSnapshot record, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.NotEqual(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Null(failure.Exception);
		Assert.Equal(1000, lookups.LastIndex);
	}

	/// <summary>A cancelled throwing lookup raises the cancellation exception before any Address List read.</summary>
	[Fact]
	public void GetRecordAtThrowsOperationCanceledExceptionWhenTheDispatchIsCancelled()
	{
		FakeRecordLookupPort lookups = new()
		{
			IndexRecord = Snapshot(7, "Health")
		};
		TableClient client = CreateClient(lookups);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			client.GetRecordAt(3, cancellation.Token));

		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(CheatEngineFailureKind.Cancelled, exception.Failure.Kind);
		Assert.Equal(0, lookups.LastIndex);
	}

	/// <summary>
	///     A hierarchy copy reads the child positions of each record below its reported count, and stops at the first
	///     position where Cheat Engine returns no child: that position is reported with the record identifier and the
	///     count, apart from a malformed record.
	/// </summary>
	[Fact]
	public void AChildMissingBelowTheReportedCountIsReportedAtItsPosition()
	{
		FakeHierarchyRecord root = new(12, childCount: 3)
		{
			Children =
			{
				[0] = new FakeHierarchyRecord(13),
				[2] = new FakeHierarchyRecord(15)
			}
		};
		TableClient client = CreateHierarchyClient(root);

		bool succeeded = client.TryGetHierarchy(new MemoryRecordId(12), new MemoryRecordHierarchyRequest(16, 4),
			out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, hierarchy);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Equal("Tables.GetHierarchy", failure.Operation);
		Assert.Equal("Cheat Engine did not return child 1 of memory record 12, which reports 3 children.",
			failure.Message);
		Assert.NotEqual(TableMapping.InvalidContractMessage, failure.Message);
		Assert.Equal([0, 1], root.RequestedChildren);
	}

	[Fact]
	public void AHierarchyCopyReadsEveryChildPositionBelowEachReportedCount()
	{
		FakeHierarchyRecord grandchild = new(14);
		FakeHierarchyRecord first = new(13, childCount: 1)
		{
			Children =
			{
				[0] = grandchild
			}
		};
		FakeHierarchyRecord second = new(15);
		FakeHierarchyRecord root = new(12, childCount: 2)
		{
			Children =
			{
				[0] = first,
				[1] = second
			}
		};
		TableClient client = CreateHierarchyClient(root);

		bool succeeded = client.TryGetHierarchy(new MemoryRecordId(12), new MemoryRecordHierarchyRequest(16, 4),
			out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(new MemoryRecordId(12), hierarchy.Record.Id);
		Assert.Equal([new MemoryRecordId(13), new MemoryRecordId(15)],
			hierarchy.Children.Select(static child => child.Record.Id));
		Assert.Equal(new MemoryRecordId(14), Assert.Single(hierarchy.Children[0].Children).Record.Id);
		Assert.Empty(hierarchy.Children[1].Children);
		Assert.Equal([0, 1], root.RequestedChildren);
		Assert.Equal([0], first.RequestedChildren);
		Assert.Empty(grandchild.RequestedChildren);
		Assert.Empty(second.RequestedChildren);
	}

	/// <summary>The root lookup of a hierarchy fails the way every other record lookup does.</summary>
	[Theory]
	[InlineData(nameof(RecordLookupStatus.AddressListUnavailable), CheatEngineFailureKind.CapabilityUnavailable,
		"Cheat Engine's Address List capability is unavailable.")]
	[InlineData(nameof(RecordLookupStatus.NotFound), CheatEngineFailureKind.NotFound,
		"The requested Cheat Engine memory record was not found.")]
	[InlineData(nameof(RecordLookupStatus.InvalidRecord), CheatEngineFailureKind.InvalidHostResult,
		"Cheat Engine did not return the expected Address List contract.")]
	public void AHierarchyRootLookupFailsLikeEveryRecordLookup(string status, CheatEngineFailureKind expectedKind,
		string expectedMessage)
	{
		TableClient client = new(new InlineDispatcher(), CoreClientPolicy.SafeDefaults,
			hierarchy: new FakeHierarchyPort(new FakeHierarchyRecord(12))
			{
				Status = Enum.Parse<RecordLookupStatus>(status)
			});

		bool succeeded = client.TryGetHierarchy(new MemoryRecordId(12), new MemoryRecordHierarchyRequest(16, 4),
			out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, hierarchy);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal("Tables.GetHierarchy", failure.Operation);
		Assert.Equal(expectedMessage, failure.Message);
	}

	private static TableClient CreateClient(FakeRecordLookupPort lookups)
	{
		return new TableClient(new InlineDispatcher(), CoreClientPolicy.SafeDefaults, recordLookups: lookups);
	}

	private static TableClient CreateHierarchyClient(FakeHierarchyRecord root)
	{
		return new TableClient(new InlineDispatcher(), CoreClientPolicy.SafeDefaults,
			hierarchy: new FakeHierarchyPort(root));
	}

	private static MemoryRecordSnapshot Snapshot(int id, string description)
	{
		return new MemoryRecordSnapshot(
			new MemoryRecordId(id),
			0,
			new MemoryRecordContentSnapshot(description, "game.exe+24", "50", VariableType.Dword),
			new MemoryRecordStateSnapshot(null));
	}

	private sealed class FakeRecordLookupPort : ITableRecordLookupPort
	{
		internal RecordLookupStatus IndexStatus
		{
			get;
			init;
		} = RecordLookupStatus.Success;

		internal RecordLookupStatus IdStatus
		{
			get;
			init;
		} = RecordLookupStatus.Success;

		internal RecordLookupStatus SelectedStatus
		{
			get;
			init;
		} = RecordLookupStatus.Success;

		internal MemoryRecordSnapshot IdRecord
		{
			get;
			init;
		}

		internal MemoryRecordSnapshot IndexRecord
		{
			get;
			init;
		}

		internal MemoryRecordSnapshot[] Table
		{
			get;
			init;
		} = [];

		/// <summary>Gets a failed status that the table copy reports instead of copying <see cref="Table" />.</summary>
		internal RecordLookupStatus TableStatus
		{
			get;
			init;
		} = RecordLookupStatus.Success;

		internal int LastIndex
		{
			get;
			private set;
		}

		internal MemoryRecordId LastId
		{
			get;
			private set;
		}

		internal int SelectedCalls
		{
			get;
			private set;
		}

		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			LastIndex = index;
			record = IndexStatus == RecordLookupStatus.Success ? IndexRecord : default;
			return IndexStatus;
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			LastId = id;
			record = IdRecord;
			return IdStatus;
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			SelectedCalls++;
			record = default;
			return SelectedStatus;
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			if (TableStatus != RecordLookupStatus.Success)
			{
				table = default;
				return TableStatus;
			}

			if (Table.Length > maximumItems)
			{
				table = default;
				return RecordLookupStatus.LimitExceeded;
			}

			table = new AddressTableSnapshot([.. Table]);
			return RecordLookupStatus.Success;
		}
	}

	private sealed class FakeHierarchyPort(FakeHierarchyRecord record) : ITableHierarchyPort
	{
		/// <summary>Gets or sets a failed status to report instead of the lookup, or <c>Success</c> to look up.</summary>
		internal RecordLookupStatus Status
		{
			get;
			init;
		} = RecordLookupStatus.Success;

		public RecordLookupStatus TryGetRoot(MemoryRecordId id, out ITableHierarchyRecord? root)
		{
			if (Status != RecordLookupStatus.Success)
			{
				root = null;
				return Status;
			}

			root = id == record.Id ? record : null;
			return root is null ? RecordLookupStatus.NotFound : RecordLookupStatus.Success;
		}
	}

	/// <summary>A record that reports a child count and returns the children it holds, recording each position read.</summary>
	private sealed class FakeHierarchyRecord(int id, int childCount = 0) : ITableHierarchyRecord
	{
		internal MemoryRecordId Id
		{
			get;
		} = new(id);

		internal Dictionary<int, FakeHierarchyRecord> Children
		{
			get;
		} = [];

		internal List<int> RequestedChildren
		{
			get;
		} = [];

		public bool TrySnapshot(out MemoryRecordSnapshot snapshot)
		{
			snapshot = new MemoryRecordSnapshot(Id, 0,
				new MemoryRecordContentSnapshot("Group", "game.exe+24", "50", VariableType.Dword),
				new MemoryRecordStateSnapshot(null, childCount: childCount));
			return true;
		}

		public bool TryGetChild(int index, [NotNullWhen(true)] out ITableHierarchyRecord? child)
		{
			RequestedChildren.Add(index);
			child = Children.GetValueOrDefault(index);
			return child is not null;
		}
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default;
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatcher", "Cancelled.");
				return false;
			}

			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw(cancellationToken);
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}
	}
}
