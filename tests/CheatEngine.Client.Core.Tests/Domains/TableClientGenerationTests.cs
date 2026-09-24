using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Record identifiers are bound to the table load in which this activation observed them (audit ch.14, A14-01,
///     A14-05, A14-27, A14-29, Q34): a trusted load that reached Cheat Engine makes earlier identifiers stale, and every
///     identifier-taking operation refuses a stale identifier before any dispatch and again on the main thread. The
///     scripted interleavings model concurrent workers whose dispatched callbacks the main thread runs one at a time.
/// </summary>
public sealed class TableClientGenerationTests : IDisposable
{
	private static readonly MemoryRecordId HandedOut = new(41);

	private readonly string _root = Directory.CreateTempSubdirectory("ce-client-table-generation-").FullName;

	public static TheoryData<string> IdentifierTakingOperations => new()
	{
		"GetRecord",
		"Select",
		"Update",
		"Delete",
		"SetActive",
		"SetParentChild",
		"SetParentParent",
		"GetHierarchy",
		"CreateUnderParent"
	};

	public static TheoryData<string> MutationsWithoutAnSdkCommand => new()
	{
		"Create",
		"CreateUnderParent",
		"Update",
		"Select"
	};

	public void Dispose()
	{
		Directory.Delete(_root, recursive: true);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SetActiveRefusesARecordIdCapturedBeforeATrustedTableLoad()
	{
		Fixture fixture = CreateFixture();
		HandOutAndLoad(fixture);
		int dispatchedBefore = fixture.Dispatcher.InvocationCount;

		bool succeeded = fixture.Client.TrySetActive(HandedOut, true, out MemoryRecordSnapshot record,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, record);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("Tables.SetActive", failure.Operation);
		Assert.Equal(TableClient.StaleRecordIdentifierMessage, failure.Message);
		Assert.Equal(dispatchedBefore, fixture.Dispatcher.InvocationCount);
		Assert.Equal(0, fixture.Mutations.Calls);
		Assert.Throws<CheatEngineClientLifecycleException>(() =>
			fixture.Client.SetActive(HandedOut, true, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void RecordIdReobservedAfterATrustedTableLoadIsAccepted()
	{
		Fixture fixture = CreateFixture();
		HandOutAndLoad(fixture);

		Assert.True(fixture.Client.TryGetRecordAt(0, out MemoryRecordSnapshot reobserved, out _,
			TestContext.Current.CancellationToken));
		bool succeeded = fixture.Client.TrySetActive(HandedOut, true, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(HandedOut, reobserved.Id);
		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(1, fixture.Mutations.Calls);
	}

	[Theory]
	[Trait("Qualification", "Q34")]
	[MemberData(nameof(IdentifierTakingOperations))]
	public void EveryIdTakingOperationRefusesAStaleRecordIdBeforeDispatch(string operation)
	{
		Fixture fixture = CreateFixture();
		HandOutAndLoad(fixture);
		int dispatchedBefore = fixture.Dispatcher.InvocationCount;

		(bool succeeded, CheatEngineFailure failure) = InvokeIdTakingOperation(fixture, operation);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(TableClient.StaleRecordIdentifierMessage, failure.Message);
		Assert.Equal(dispatchedBefore, fixture.Dispatcher.InvocationCount);
		Assert.Equal(0, fixture.Mutations.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SnapshotCopiedBeforeAConcurrentTrustedLoadHandsOutARefusedRecordId()
	{
		// Review interleaving: worker B's snapshot is copied on the main thread; worker A's trusted load then runs on the
		// main thread and advances the generation; only then does B resume and observe its copy. B must not hand out the
		// pre-load identifier as current.
		Fixture fixture = CreateFixture();
		fixture.Dispatcher.AfterNextCallback(() =>
			Assert.True(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
				TestContext.Current.CancellationToken)));

		Assert.True(fixture.Client.TryGetRecordAt(0, out MemoryRecordSnapshot copiedBeforeLoad, out _,
			TestContext.Current.CancellationToken));
		bool succeeded = fixture.Client.TrySetActive(copiedBeforeLoad.Id, true, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(HandedOut, copiedBeforeLoad.Id);
		Assert.Equal(1, fixture.Client.TableGeneration);
		Assert.Single(fixture.Files.Loads);
		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(TableClient.StaleRecordIdentifierMessage, failure.Message);
		Assert.Equal(0, fixture.Mutations.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void SnapshotCopiedAfterAConcurrentTrustedLoadKeepsItsRecordIdCurrent()
	{
		// Reverse interleaving: the load runs on the main thread, another worker's snapshot is copied and observed after
		// it, and the loading worker resumes last. The generation advanced inside the load, so the later snapshot is
		// current and its identifier is accepted instead of being turned stale by the loading worker.
		Fixture fixture = CreateFixture();
		Assert.True(fixture.Client.TryGetRecordAt(0, out _, out _, TestContext.Current.CancellationToken));
		MemoryRecordSnapshot copiedAfterLoad = default;
		fixture.Dispatcher.AfterNextCallback(() =>
			Assert.True(fixture.Client.TryGetRecordAt(0, out copiedAfterLoad, out _,
				TestContext.Current.CancellationToken)));

		Assert.True(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
			TestContext.Current.CancellationToken));
		bool succeeded = fixture.Client.TrySetActive(copiedAfterLoad.Id, true, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(HandedOut, copiedAfterLoad.Id);
		Assert.Equal(1, fixture.Client.TableGeneration);
		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal(1, fixture.Mutations.Calls);
	}

	[Theory]
	[Trait("Qualification", "Q34")]
	[MemberData(nameof(IdentifierTakingOperations))]
	public void EveryIdTakingOperationQueuedBehindAnInFlightTrustedLoadIsRefusedOnTheMainThread(string operation)
	{
		// The identifier is current when the caller checks it, but a trusted load dispatched by another worker runs on
		// the main thread before this operation's callback: the callback checks again and never calls Cheat Engine.
		Fixture fixture = CreateFixture();
		Assert.True(fixture.Client.TryGetRecordAt(0, out _, out _, TestContext.Current.CancellationToken));
		fixture.Dispatcher.BeforeNextCallback(() =>
			Assert.True(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
				TestContext.Current.CancellationToken)));

		(bool succeeded, CheatEngineFailure failure) = InvokeIdTakingOperation(fixture, operation);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(TableClient.StaleRecordIdentifierMessage, failure.Message);
		Assert.Equal(1, fixture.Client.TableGeneration);
		Assert.Single(fixture.Files.Loads);
		Assert.Equal(0, fixture.Mutations.Calls);
		Assert.Equal(1, fixture.Lookups.Calls);
	}

	[Fact]
	public void TrustedTableLoadThatReachedCheatEngineAdvancesTheGenerationEvenWhenItFails()
	{
		// The load may have cleared or replaced the table before it failed, so earlier identifiers are not trusted.
		Fixture fixture = CreateFixture(fileStatus: LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError));
		Assert.True(fixture.Client.TryGetRecordAt(0, out _, out _, TestContext.Current.CancellationToken));

		bool loaded = fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile, Merge: true),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(loaded);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.DoesNotContain(fixture.TableFile.FullPath, failure.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(1, fixture.Client.TableGeneration);
		Assert.Equal([(fixture.TableFile.FullPath, true)], fixture.Files.Loads);
		Assert.False(fixture.Client.TryDelete(HandedOut, out CheatEngineFailure staleFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.InvalidState, staleFailure.Kind);
	}

	[Fact]
	public void AFaultedTrustedTableLoadIsTranslatedAndStillAdvancesTheGeneration()
	{
		// No SDK exception crosses TryLoadTrustedTable; the load may have run before CheatEngine.SDK raised.
		InvalidOperationException fault = new("detached runtime");
		Fixture fixture = CreateFixture(fileFault: fault);

		bool loaded = fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(loaded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(1, fixture.Client.TableGeneration);
	}

	[Fact]
	public void RefusedTableSavePathNeverReachesCheatEngine()
	{
		// The trust policy refuses the path before dispatch: CheatTableFiles.TrySave is never called.
		Fixture fixture = CreateFixture();
		int dispatchedBefore = fixture.Dispatcher.InvocationCount;
		string outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"), "table.ct");

		bool saved = fixture.Client.TrySaveTable(new TableSaveRequest(new TrustedTableFile(outside)),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(saved);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, fixture.Files.Saves);
		Assert.Equal(dispatchedBefore, fixture.Dispatcher.InvocationCount);
	}

	[Theory]
	[InlineData(LuaOperationStatusKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(LuaOperationStatusKind.LuaFailure, CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Started)]
	public void TrustedTableSaveReportsTheStatusOfCheatTableFiles(LuaOperationStatusKind kind,
		CheatEngineFailureKind expectedKind, CheatEngineHostEffect expectedEffect)
	{
		LuaOperationStatus status = kind == LuaOperationStatusKind.GlobalUnavailable
			? LuaOperationStatus.GlobalUnavailable
			: LuaOperationStatus.LuaFailure(LuaStatus.FileError);
		Fixture fixture = CreateFixture(fileStatus: status);

		bool saved = fixture.Client.TrySaveTable(new TableSaveRequest(fixture.TableFile),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(saved);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal("Tables.SaveTable", failure.Operation);
		Assert.Equal(1, fixture.Files.Saves);
	}

	[Fact]
	public void RefusedTableLoadPathDoesNotAdvanceTheGenerationAndIsNeverRetriedThroughAnotherOverload()
	{
		// A14-27: the trust policy refuses the path before dispatch; the Client never retries it through another
		// loadTable overload or a stream (ITableClient exposes no stream or byte load path at all).
		Fixture fixture = CreateFixture();
		Assert.True(fixture.Client.TryGetRecordAt(0, out _, out _, TestContext.Current.CancellationToken));
		int dispatchedBefore = fixture.Dispatcher.InvocationCount;
		string outside = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N"), "table.ct");

		bool loaded = fixture.Client.TryLoadTrustedTable(new TableLoadRequest(new TrustedTableFile(outside)),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(loaded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, fixture.Client.TableGeneration);
		Assert.Empty(fixture.Files.Loads);
		Assert.Equal(0, fixture.Files.Saves);
		Assert.Equal(dispatchedBefore, fixture.Dispatcher.InvocationCount);
		Assert.True(fixture.Client.TrySetActive(HandedOut, true, out _, out _, TestContext.Current.CancellationToken));
	}

	[Theory]
	[MemberData(nameof(MutationsWithoutAnSdkCommand))]
	public void AMutationWithoutAnSdkCommandIsRefusedWhileATrustedLoadRuns(string operation)
	{
		// CheatEngine.SDK refuses its own Address List commands while CheatTableFiles.TryLoad runs on the calling thread
		// (a script of the table being loaded calling the Client); creation, update and selection have no SDK command,
		// so the Client refuses them the same way. An identifier handed out before the load is not stale yet: the
		// generation advances when the load returns.
		Fixture fixture = CreateFixture();
		Assert.True(fixture.Client.TryGetRecordAt(0, out _, out _, TestContext.Current.CancellationToken));
		(bool Succeeded, CheatEngineFailure Failure) duringLoad = default;
		fixture.Files.OnLoad = () => duringLoad = InvokeMutationWithoutAnSdkCommand(fixture, operation);

		Assert.True(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
			TestContext.Current.CancellationToken));

		Assert.False(duringLoad.Succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, duringLoad.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, duringLoad.Failure.HostEffect);
		Assert.Equal(operation switch
		{
			"Update" => "Tables.Update",
			"Select" => "Tables.SelectRecord",
			_ => "Tables.Create"
		}, duringLoad.Failure.Operation);
		Assert.StartsWith("A table file is loading on Cheat Engine's main thread", duringLoad.Failure.Message,
			StringComparison.Ordinal);
		Assert.Null(duringLoad.Failure.Exception);
		Assert.Equal(0, fixture.Mutations.Calls);
		Assert.True(fixture.Client.TryCreate(
				new MemoryRecordDefinition("added", "game.exe+30", "1", VariableType.Dword), out _, out _,
				TestContext.Current.CancellationToken),
			"The refusal ends with the load.");
		Assert.Equal(1, fixture.Mutations.Calls);
	}

	[Fact]
	public void TheTrustedLoadRefusalEndsWhenTheLoadFaults()
	{
		Fixture fixture = CreateFixture(fileFault: new InvalidOperationException("detached runtime"));
		(bool Succeeded, CheatEngineFailure Failure) duringLoad = default;
		fixture.Files.OnLoad = () => duringLoad = InvokeMutationWithoutAnSdkCommand(fixture, "Create");

		Assert.False(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
			TestContext.Current.CancellationToken));
		bool afterLoad = fixture.Client.TryCreate(
			new MemoryRecordDefinition("added", "game.exe+30", "1", VariableType.Dword), out _,
			out CheatEngineFailure afterFailure, TestContext.Current.CancellationToken);

		Assert.False(duringLoad.Succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, duringLoad.Failure.Kind);
		Assert.True(afterLoad);
		Assert.Equal(default, afterFailure);
		Assert.Equal(1, fixture.Mutations.Calls);
	}

	[Fact]
	public void AnIdentifierThatWasNeverHandedOutIsNotJudged()
	{
		// Unknown provenance: the Client refuses only identifiers it handed out before the last trusted load.
		Fixture fixture = CreateFixture();
		HandOutAndLoad(fixture);

		bool succeeded = fixture.Client.TrySetActive(new MemoryRecordId(7), true, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
	}

	private Fixture CreateFixture(Exception? fileFault = null, LuaOperationStatus? fileStatus = null)
	{
		string tablePath = Path.Combine(_root, "trusted.ct");
		File.WriteAllText(tablePath, "<CheatTable/>");
		CountingDispatcher dispatcher = new();
		CountingMutationPort mutations = new();
		RecordingFilePort files = new(fileFault, fileStatus);
		SingleRecordLookupPort lookups = new();
		TableClient client = new(dispatcher, new CoreClientPolicy([_root], false), mutations, null, lookups, files);
		return new Fixture(client, dispatcher, mutations, lookups, files, new TrustedTableFile(tablePath));
	}

	private static (bool Succeeded, CheatEngineFailure Failure) InvokeIdTakingOperation(Fixture fixture,
		string operation)
	{
		MemoryRecordId other = new(99);
		CancellationToken token = TestContext.Current.CancellationToken;
		return operation switch
		{
			"GetRecord" => (fixture.Client.TryGetRecord(HandedOut, out _, out CheatEngineFailure f, token), f),
			"Select" => (fixture.Client.TrySelectRecord(HandedOut, out _, out CheatEngineFailure f, token), f),
			"Update" => (fixture.Client.TryUpdate(new MemoryRecordUpdate(HandedOut, "renamed"), out _,
				out CheatEngineFailure f, token), f),
			"Delete" => (fixture.Client.TryDelete(HandedOut, out CheatEngineFailure f, token), f),
			"SetActive" => (fixture.Client.TrySetActive(HandedOut, false, out _, out CheatEngineFailure f, token), f),
			"SetParentChild" => (fixture.Client.TrySetParent(HandedOut, other, out _, out CheatEngineFailure f, token),
				f),
			"SetParentParent" => (fixture.Client.TrySetParent(other, HandedOut, out _, out CheatEngineFailure f,
				token), f),
			"GetHierarchy" => (fixture.Client.TryGetHierarchy(HandedOut, new MemoryRecordHierarchyRequest(4, 16),
				out _, out CheatEngineFailure f, token), f),
			"CreateUnderParent" => (fixture.Client.TryCreate(
				new MemoryRecordDefinition("child", "game.exe+30", "1", VariableType.Dword, HandedOut), out _,
				out CheatEngineFailure f, token), f),
			_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
		};
	}

	private static (bool Succeeded, CheatEngineFailure Failure) InvokeMutationWithoutAnSdkCommand(Fixture fixture,
		string operation)
	{
		CancellationToken token = TestContext.Current.CancellationToken;
		return operation switch
		{
			"Create" => (fixture.Client.TryCreate(
				new MemoryRecordDefinition("added", "game.exe+30", "1", VariableType.Dword), out _,
				out CheatEngineFailure f, token), f),
			"CreateUnderParent" => (fixture.Client.TryCreate(
				new MemoryRecordDefinition("child", "game.exe+30", "1", VariableType.Dword, HandedOut), out _,
				out CheatEngineFailure f, token), f),
			"Update" => (fixture.Client.TryUpdate(new MemoryRecordUpdate(HandedOut, "renamed"), out _,
				out CheatEngineFailure f, token), f),
			"Select" => (fixture.Client.TrySelectRecord(HandedOut, out _, out CheatEngineFailure f, token), f),
			_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
		};
	}

	private static void HandOutAndLoad(Fixture fixture)
	{
		Assert.True(fixture.Client.TryGetRecordAt(0, out MemoryRecordSnapshot handedOut, out _,
			TestContext.Current.CancellationToken));
		Assert.Equal(HandedOut, handedOut.Id);
		Assert.True(fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile), out _,
			TestContext.Current.CancellationToken));
		Assert.Equal(1, fixture.Client.TableGeneration);
	}

	private static MemoryRecordSnapshot Snapshot(MemoryRecordId id)
	{
		return new MemoryRecordSnapshot(id, 0,
			new MemoryRecordContentSnapshot("Health", "game.exe+24", "100", VariableType.Dword),
			new MemoryRecordStateSnapshot(null));
	}

	private sealed record Fixture(
		TableClient Client,
		CountingDispatcher Dispatcher,
		CountingMutationPort Mutations,
		SingleRecordLookupPort Lookups,
		RecordingFilePort Files,
		TrustedTableFile TableFile);

	private sealed class SingleRecordLookupPort : ITableRecordLookupPort
	{
		internal int Calls
		{
			get;
			private set;
		}

		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(HandedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(id);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(HandedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			Calls++;
			table = new AddressTableSnapshot([Snapshot(HandedOut)]);
			return RecordLookupStatus.Success;
		}
	}

	private sealed class CountingMutationPort : ITableRecordMutationPort
	{
		internal int Calls
		{
			get;
			private set;
		}

		public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(new MemoryRecordId(50));
			return TableRecordCreation.Created;
		}

		public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
		{
			Calls++;
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(childId);
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			Calls++;
			return new TableActivationObservation(MemoryRecordActivationOutcomeKind.Applied,
				MemoryRecordMutationProblem.None, Snapshot(id));
		}

		public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(id);
			return TableRecordMutationOutcome.Succeeded;
		}
	}

	private sealed class RecordingFilePort(Exception? fault, LuaOperationStatus? status) : ITableFilePort
	{
		internal List<(string Path, bool Merge)> Loads
		{
			get;
		} = [];

		internal int Saves
		{
			get;
			private set;
		}

		/// <summary>Runs inside the load, like a script of the table being loaded calling the Client.</summary>
		internal Action? OnLoad
		{
			get;
			set;
		}

		public LuaOperationStatus TryLoad(string path, bool merge)
		{
			Loads.Add((path, merge));
			OnLoad?.Invoke();
			if (fault is not null)
			{
				throw fault;
			}

			return status ?? LuaOperationStatus.Success;
		}

		public LuaOperationStatus TrySave(string path)
		{
			Saves++;
			return status ?? LuaOperationStatus.Success;
		}
	}

	/// <summary>
	///     Runs callbacks inline, like Cheat Engine's main thread runs dispatched work one at a time, and can script another
	///     dispatched operation right before or right after the next callback. The scripted operation is what another
	///     worker thread's call would run on the main thread between two steps of this caller.
	/// </summary>
	private sealed class CountingDispatcher : ICheatEngineDispatcher
	{
		private Action? _afterNextCallback;
		private Action? _beforeNextCallback;

		internal int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			RunOnce(ref _beforeNextCallback);
			callback();
			RunOnce(ref _afterNextCallback);
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			RunOnce(ref _beforeNextCallback);
			result = callback();
			RunOnce(ref _afterNextCallback);
			failure = default;
			return true;
		}

		/// <summary>Runs <paramref name="interleaved" /> on the "main thread" right before the next callback.</summary>
		internal void BeforeNextCallback(Action interleaved)
		{
			_beforeNextCallback = interleaved;
		}

		/// <summary>
		///     Runs <paramref name="interleaved" /> on the "main thread" right after the next callback, before the caller of
		///     that callback resumes.
		/// </summary>
		internal void AfterNextCallback(Action interleaved)
		{
			_afterNextCallback = interleaved;
		}

		private static void RunOnce(ref Action? interleaved)
		{
			// Cleared first: the interleaved operation dispatches through this dispatcher too.
			Action? action = interleaved;
			interleaved = null;
			action?.Invoke();
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
