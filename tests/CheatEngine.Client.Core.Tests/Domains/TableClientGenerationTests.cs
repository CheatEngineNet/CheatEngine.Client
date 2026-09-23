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
///     identifier-taking operation refuses a stale identifier before any dispatch.
/// </summary>
public sealed class TableClientGenerationTests : IDisposable
{
	private static readonly MemoryRecordId _handedOut = new(41);

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

		bool succeeded = fixture.Client.TrySetActive(_handedOut, true, out MemoryRecordSnapshot record,
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
			fixture.Client.SetActive(_handedOut, true, TestContext.Current.CancellationToken));
	}

	[Fact]
	[Trait("Qualification", "Q34")]
	public void RecordIdReobservedAfterATrustedTableLoadIsAccepted()
	{
		Fixture fixture = CreateFixture();
		HandOutAndLoad(fixture);

		Assert.True(fixture.Client.TryGetRecord(0, out MemoryRecordSnapshot reobserved, out _,
			TestContext.Current.CancellationToken));
		bool succeeded = fixture.Client.TrySetActive(_handedOut, true, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.Equal(_handedOut, reobserved.Id);
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
		MemoryRecordId other = new(99);
		CancellationToken token = TestContext.Current.CancellationToken;

		(bool succeeded, CheatEngineFailure failure) = operation switch
		{
			"GetRecord" => (fixture.Client.TryGetRecord(_handedOut, out _, out CheatEngineFailure f, token), f),
			"Select" => (fixture.Client.TrySelect(_handedOut, out _, out CheatEngineFailure f, token), f),
			"Update" => (fixture.Client.TryUpdate(new MemoryRecordUpdate(_handedOut, "renamed"), out _,
				out CheatEngineFailure f, token), f),
			"Delete" => (fixture.Client.TryDelete(_handedOut, out CheatEngineFailure f, token), f),
			"SetActive" => (fixture.Client.TrySetActive(_handedOut, false, out _, out CheatEngineFailure f, token), f),
			"SetParentChild" => (fixture.Client.TrySetParent(_handedOut, other, out _, out CheatEngineFailure f, token),
				f),
			"SetParentParent" => (fixture.Client.TrySetParent(other, _handedOut, out _, out CheatEngineFailure f,
				token), f),
			"GetHierarchy" => (fixture.Client.TryGetHierarchy(_handedOut, new MemoryRecordHierarchyRequest(4, 16),
				out _, out CheatEngineFailure f, token), f),
			"CreateUnderParent" => (fixture.Client.TryCreate(
				new MemoryRecordDefinition("child", "game.exe+30", "1", VariableType.Dword, _handedOut), out _,
				out CheatEngineFailure f, token), f),
			_ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
		};

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(TableClient.StaleRecordIdentifierMessage, failure.Message);
		Assert.Equal(dispatchedBefore, fixture.Dispatcher.InvocationCount);
		Assert.Equal(0, fixture.Mutations.Calls);
	}

	[Fact]
	public void TrustedTableLoadThatReachedCheatEngineAdvancesTheGenerationEvenWhenItFails()
	{
		// The load may have cleared or replaced the table before it failed, so earlier identifiers are not trusted.
		LuaException fault = new("the table script failed");
		Fixture fixture = CreateFixture(fileFault: fault);
		Assert.True(fixture.Client.TryGetRecord(0, out _, out _, TestContext.Current.CancellationToken));

		bool loaded = fixture.Client.TryLoadTrustedTable(new TableLoadRequest(fixture.TableFile, Merge: true),
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(loaded);
		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(1, fixture.Client.TableGeneration);
		Assert.Equal([(fixture.TableFile.FullPath, true)], fixture.Files.Loads);
		Assert.False(fixture.Client.TryDelete(_handedOut, out CheatEngineFailure staleFailure,
			TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.InvalidState, staleFailure.Kind);
	}

	[Fact]
	public void RefusedTableLoadPathDoesNotAdvanceTheGenerationAndIsNeverRetriedThroughAnotherOverload()
	{
		// A14-27: the trust policy refuses the path before dispatch; the Client never retries it through another
		// loadTable overload or a stream (ITableClient exposes no stream or byte load path at all).
		Fixture fixture = CreateFixture();
		Assert.True(fixture.Client.TryGetRecord(0, out _, out _, TestContext.Current.CancellationToken));
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
		Assert.True(fixture.Client.TrySetActive(_handedOut, true, out _, out _, TestContext.Current.CancellationToken));
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

	private Fixture CreateFixture(Exception? fileFault = null)
	{
		string tablePath = Path.Combine(_root, "trusted.ct");
		File.WriteAllText(tablePath, "<CheatTable/>");
		CountingDispatcher dispatcher = new();
		CountingMutationPort mutations = new();
		RecordingFilePort files = new(fileFault);
		SingleRecordLookupPort lookups = new();
		TableClient client = new(dispatcher, new CoreClientPolicy([_root], false), mutations, null, lookups, files);
		return new Fixture(client, dispatcher, mutations, files, new TrustedTableFile(tablePath));
	}

	private static void HandOutAndLoad(Fixture fixture)
	{
		Assert.True(fixture.Client.TryGetRecord(0, out MemoryRecordSnapshot handedOut, out _,
			TestContext.Current.CancellationToken));
		Assert.Equal(_handedOut, handedOut.Id);
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
		RecordingFilePort Files,
		TrustedTableFile TableFile);

	private sealed class SingleRecordLookupPort : ITableRecordLookupPort
	{
		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			record = Snapshot(_handedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			record = Snapshot(id);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			record = Snapshot(_handedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			table = new AddressTableSnapshot([Snapshot(_handedOut)]);
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

		public TableRecordMutationStatus TryDelete(MemoryRecordId id)
		{
			Calls++;
			return TableRecordMutationStatus.Success;
		}

		public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(childId);
			return TableRecordMutationStatus.Success;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			Calls++;
			return new TableActivationObservation(TableActivationStatus.Applied, !requested, requested, false,
				Snapshot(id));
		}

		public TableRecordMutationStatus TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			Calls++;
			record = Snapshot(id);
			return TableRecordMutationStatus.Success;
		}
	}

	private sealed class RecordingFilePort(Exception? fault) : ITableFilePort
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

		public void LoadTable(string path, bool merge)
		{
			Loads.Add((path, merge));
			if (fault is not null)
			{
				throw fault;
			}
		}

		public void SaveTable(string path)
		{
			Saves++;
		}
	}

	private sealed class CountingDispatcher : ICheatEngineDispatcher
	{
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
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
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
