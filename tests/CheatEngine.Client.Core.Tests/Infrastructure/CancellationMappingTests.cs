using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     Proves the cancellation vocabulary: before the native call a cancellation is
///     <see cref="CheatEngineHostEffect.NotStarted" />; after it returned, <see cref="CheatEngineHostEffect.Completed" />,
///     and nothing copied from the call is published; between two native calls of one operation,
///     <see cref="CheatEngineHostEffect.Started" />.
/// </summary>
public sealed class CancellationMappingTests
{
	[Fact]
	public void ACancellationBeforeTheNativeCallIsNotStarted()
	{
		CheatEngineFailure failure = CancellationMapping.BeforeNativeCall("Memory.ReadBytes");

		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Memory.ReadBytes", failure.Operation);
		Assert.Equal(CancellationMapping.BeforeNativeCallMessage, failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void ACancellationAfterTheNativeCallIsCompleted()
	{
		CheatEngineFailure failure = CancellationMapping.AfterNativeCall("Patterns.Scan");

		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(CancellationMapping.AfterNativeCallMessage, failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void ACancellationBetweenTwoNativeCallsIsStarted()
	{
		CheatEngineFailure failure = CancellationMapping.BetweenNativeCalls("Scans.FirstScan");

		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Equal("Scans.FirstScan", failure.Operation);
		Assert.Equal(CancellationMapping.BetweenNativeCallsMessage, failure.Message);
		Assert.Null(failure.Exception);
	}

	[Fact]
	public void AnOperationSpecificMessageKeepsTheCancellationEffect()
	{
		CheatEngineFailure before = CancellationMapping.BeforeNativeCall("Patterns.Scan", "Cancelled before the scan.");
		CheatEngineFailure after = CancellationMapping.AfterNativeCall("Patterns.Scan", "Cancelled after the scan.");
		CheatEngineFailure between = CancellationMapping.BetweenNativeCalls("Scans.NextScan", "Cancelled before the wait.");

		Assert.Equal("Cancelled before the scan.", before.Message);
		Assert.Equal(CheatEngineHostEffect.NotStarted, before.HostEffect);
		Assert.Equal("Cancelled after the scan.", after.Message);
		Assert.Equal(CheatEngineHostEffect.Completed, after.HostEffect);
		Assert.Equal("Cancelled before the wait.", between.Message);
		Assert.Equal(CheatEngineHostEffect.Started, between.HostEffect);
	}

	[Fact]
	public void ACancelledTableSearchIsCompletedAndPublishesNothing()
	{
		using CancellationTokenSource cancellation = new();
		CancellingLookupPort lookups = new(cancellation.Cancel);
		CoreLifetime lifetime = InertCoreLifetime.Create();
		TableClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()),
			CoreClientPolicy.SafeDefaults, lifetime: lifetime, recordLookups: lookups);

		bool succeeded = client.TryFind(new MemoryRecordSearch("Ammo"), new MemoryRecordCollectionRequest(8),
			out ImmutableArray<MemoryRecordSnapshot> records, out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.True(records.IsEmpty);
		Assert.Equal(1, lookups.TableReads);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Tables.Find", failure.Operation);
	}

	/// <summary>Copies one matching record and cancels the caller's token while the snapshot is taken.</summary>
	private sealed class CancellingLookupPort(Action onTableRead) : ITableRecordLookupPort
	{
		internal int TableReads
		{
			get;
			private set;
		}

		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			throw new InvalidOperationException("Not used by the search.");
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			throw new InvalidOperationException("Not used by the search.");
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			throw new InvalidOperationException("Not used by the search.");
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			TableReads++;
			onTableRead();
			table = new AddressTableSnapshot([
				new MemoryRecordSnapshot(new MemoryRecordId(1), 0,
					new MemoryRecordContentSnapshot("Ammo", "game.exe+24", "50", VariableType.Dword),
					new MemoryRecordStateSnapshot(null))
			]);
			return RecordLookupStatus.Success;
		}
	}
}
