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

public sealed class TableClientCoverageTests
{
	[Fact]
	public void TryFindRejectsAnUninitializedSearchWithoutDispatching()
	{
		RejectingDispatcher dispatcher = new(Failure());
		TableClient client = CreateClient(dispatcher);

		bool succeeded = client.TryFind(default, new MemoryRecordCollectionRequest(8),
			out ImmutableArray<MemoryRecordSnapshot> records,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.True(records.IsEmpty);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Tables.Find", failure.Operation);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void FindConvertsTheUninitializedSearchRejectionToAnOperationException()
	{
		RejectingDispatcher dispatcher = new(Failure());
		TableClient client = CreateClient(dispatcher);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Find(default, new MemoryRecordCollectionRequest(8), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, exception.Failure.Kind);
		Assert.Equal("Tables.Find", exception.Failure.Operation);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void NegativeRecordIndexIsRejectedBeforeTheAddressListIsRead()
	{
		RejectingDispatcher dispatcher = new(Failure());
		TableClient client = CreateClient(dispatcher);

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.TryGetRecordAt(-1, out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void TableImportAndExportFailClosedWhenNoTrustedRootsAreConfigured()
	{
		RejectingDispatcher dispatcher = new(Failure());
		TableClient client = CreateClient(dispatcher);
		TrustedTableFile file = new(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", "profile.ct"));

		bool loadSucceeded = client.TryLoadTrustedTable(new TableLoadRequest(file), out CheatEngineFailure loadFailure,
			TestContext.Current.CancellationToken);
		bool saveSucceeded = client.TrySaveTable(new TableSaveRequest(file), out CheatEngineFailure saveFailure,
			TestContext.Current.CancellationToken);

		Assert.False(loadSucceeded);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, loadFailure.Kind);
		Assert.Equal("Tables.LoadTrustedTable", loadFailure.Operation);
		Assert.False(saveSucceeded);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, saveFailure.Kind);
		Assert.Equal("Tables.SaveTable", saveFailure.Operation);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void ThrowingTableImportAndExportPreserveTheFailClosedPolicyFailure()
	{
		RejectingDispatcher dispatcher = new(Failure());
		TableClient client = CreateClient(dispatcher);
		TrustedTableFile file = new(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", "profile.ct"));

		CheatEngineOperationException loadException = Assert.Throws<CheatEngineOperationException>(() =>
			client.LoadTrustedTable(new TableLoadRequest(file), TestContext.Current.CancellationToken));
		CheatEngineOperationException saveException = Assert.Throws<CheatEngineOperationException>(() =>
			client.SaveTable(new TableSaveRequest(file), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, loadException.Failure.Kind);
		Assert.Equal("Tables.LoadTrustedTable", loadException.Failure.Operation);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, saveException.Failure.Kind);
		Assert.Equal("Tables.SaveTable", saveException.Failure.Operation);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void ReadOperationsPropagateTheExactDispatcherFailureWithoutReadingSdkStatics()
	{
		CheatEngineFailure expected = Failure();
		RejectingDispatcher dispatcher = new(expected);
		TableClient client = CreateClient(dispatcher);
		MemoryRecordId id = new(42);

		Assert.False(client.TryGetRecordCount(out int count, out CheatEngineFailure countFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(0, count);
		Assert.Equal(expected, countFailure);
		Assert.False(client.TryGetSnapshot(new MemoryRecordCollectionRequest(8), out AddressTableSnapshot snapshot,
			out CheatEngineFailure snapshotFailure, TestContext.Current.CancellationToken));
		Assert.Equal(default, snapshot);
		Assert.Equal(expected, snapshotFailure);
		Assert.False(client.TryGetRecordAt(0, out MemoryRecordSnapshot indexedRecord,
			out CheatEngineFailure indexedFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, indexedRecord);
		Assert.Equal(expected, indexedFailure);
		Assert.False(client.TryGetRecord(id, out MemoryRecordSnapshot identifiedRecord,
			out CheatEngineFailure identifiedFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, identifiedRecord);
		Assert.Equal(expected, identifiedFailure);
		Assert.False(client.TryGetSelected(out MemoryRecordSnapshot selectedRecord,
			out CheatEngineFailure selectedFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, selectedRecord);
		Assert.Equal(expected, selectedFailure);
		Assert.Equal(5, dispatcher.InvocationCount);
	}

	[Fact]
	public void MutatingOperationsPropagateTheExactDispatcherFailureWithoutInvokingTheMutationPort()
	{
		CheatEngineFailure expected = Failure();
		RejectingDispatcher dispatcher = new(expected);
		RecordingMutationPort mutations = new();
		TableClient client = new(dispatcher, CoreClientPolicy.SafeDefaults, mutations);
		MemoryRecordId id = new(42);
		MemoryRecordDefinition definition = new("Health", "game.exe+10", "100", VariableType.Dword);
		MemoryRecordUpdate update = new(id, value: "101");

		Assert.False(client.TryCreate(definition, out MemoryRecordSnapshot created,
			out CheatEngineFailure createFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, created);
		Assert.Equal(expected, createFailure);
		Assert.False(client.TryUpdate(update, out MemoryRecordSnapshot updated, out CheatEngineFailure updateFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(default, updated);
		Assert.Equal(expected, updateFailure);
		Assert.False(client.TryDelete(id, out CheatEngineFailure deleteFailure, TestContext.Current.CancellationToken));
		Assert.Equal(expected, deleteFailure);
		Assert.False(client.TrySetActive(id, true, out MemoryRecordSnapshot activeRecord,
			out CheatEngineFailure activeFailure, TestContext.Current.CancellationToken));
		Assert.Equal(default, activeRecord);
		Assert.Equal(expected, activeFailure);
		Assert.False(client.TrySetParent(id, new MemoryRecordId(12), out MemoryRecordSnapshot parentRecord,
			out CheatEngineFailure parentFailure, TestContext.Current.CancellationToken));
		Assert.Equal(default, parentRecord);
		Assert.Equal(expected, parentFailure);
		Assert.Equal(5, dispatcher.InvocationCount);
		Assert.Equal(0, mutations.InvocationCount);
	}

	[Fact]
	public void HierarchyOperationsPropagateTheExactDispatcherFailure()
	{
		CheatEngineFailure expected = Failure();
		RejectingDispatcher dispatcher = new(expected);
		TableClient client = CreateClient(dispatcher);

		Assert.False(client.TryGetHierarchy(new MemoryRecordId(42), new MemoryRecordHierarchyRequest(8, 2),
			out MemoryRecordHierarchySnapshot hierarchy, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(default, hierarchy);
		Assert.Equal(expected, failure);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	private static TableClient CreateClient(RejectingDispatcher dispatcher)
	{
		return new TableClient(dispatcher, CoreClientPolicy.SafeDefaults);
	}

	private static CheatEngineFailure Failure()
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Test.TableDispatcher",
			"The host dispatcher denied the operation.");
	}

	private sealed class RecordingMutationPort : ITableRecordMutationPort
	{
		internal int InvocationCount
		{
			get;
			private set;
		}

		public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
		{
			InvocationCount++;
			record = default;
			return TableRecordCreation.Created;
		}

		public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
		{
			InvocationCount++;
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			InvocationCount++;
			record = default;
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			InvocationCount++;
			return TableActivationObservation.Of(MemoryRecordActivationOutcomeKind.Applied);
		}

		public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			InvocationCount++;
			record = default;
			return TableRecordMutationOutcome.Succeeded;
		}
	}

	private sealed class RejectingDispatcher : ICheatEngineDispatcher
	{
		private readonly CheatEngineFailure _failure;

		internal RejectingDispatcher(CheatEngineFailure failure)
		{
			_failure = failure;
		}

		internal int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => false;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			failure = _failure;
			return false;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			result = default;
			failure = _failure;
			return false;
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
