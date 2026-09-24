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
		Assert.Equal("Tables.GetRecordAt", failure.Operation);
		Assert.Equal("Cheat Engine's Address List capability is unavailable.", failure.Message);
		Assert.Equal(3, lookups.LastIndex);
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
	public void TryGetSelectedMapsAMalformedRecordToInvalidHostResult()
	{
		FakeRecordLookupPort lookups = new()
		{
			SelectedStatus = RecordLookupStatus.InvalidRecord
		};
		TableClient client = CreateClient(lookups);

		bool succeeded = client.TryGetSelected(out MemoryRecordSnapshot record, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Tables.GetSelected", failure.Operation);
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

	private static TableClient CreateClient(FakeRecordLookupPort lookups)
	{
		return new TableClient(new InlineDispatcher(), CoreClientPolicy.SafeDefaults, recordLookups: lookups);
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
			if (Table.Length > maximumItems)
			{
				table = default;
				return RecordLookupStatus.LimitExceeded;
			}

			table = new AddressTableSnapshot([.. Table]);
			return RecordLookupStatus.Success;
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
				failure.Throw();
			}
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
