using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

using SymbolRegistrationLease = CheatEngine.Client.Core.Domains.SymbolRegistrationLease;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     The Core diagnostic events (audit ch.24, A24-12 to A24-17, A24-22): emitted after the dispatched Cheat Engine work
///     returned, bounded to closed names, counts and epochs, and never able to change an operation result.
/// </summary>
public sealed partial class CoreDiagnosticsTests : IDisposable
{
	private const string SensitiveSymbol = "player_health";
	private const string ExistingSymbol = "existing_symbol";
	private const string SensitiveScript = "return readInteger('player_health')";
	private const ulong SensitiveAddress = 0x7FF6_1234_5678;

	private static readonly MemoryRecordId HandedOut = new(41);

	private readonly string _root = Directory.CreateTempSubdirectory("ce-client-diagnostics-").FullName;

	public void Dispose()
	{
		Directory.Delete(_root, recursive: true);
	}

	[Fact]
	public void CoreDiagnosticsAreNeverEmittedInsideADispatchedCallback()
	{
		CallbackTracker tracker = new();
		RecordingCoreDiagnostics diagnostics = new(tracker);

		RunScenario(diagnostics, tracker);

		Assert.True(tracker.CallbackCount > 0, "The scripted run dispatched no callback; the test would pass vacuously.");
		Assert.Equal(
			[
				nameof(ICoreDiagnostics.RuntimeSnapshotCaptured), nameof(ICoreDiagnostics.TargetSelectionAdvanced),
				nameof(ICoreDiagnostics.TargetSelectionAdvanced), nameof(ICoreDiagnostics.PointerWidthMismatchRefused),
				nameof(ICoreDiagnostics.MemoryBatchCompleted), nameof(ICoreDiagnostics.TableGenerationAdvanced),
				nameof(ICoreDiagnostics.StaleRecordIdentifierRefused),
				nameof(ICoreDiagnostics.RecordActivationNotApplied),
				nameof(ICoreDiagnostics.SymbolRegistrationRejected), nameof(ICoreDiagnostics.LeaseReleased),
				nameof(ICoreDiagnostics.PatternScanCompleted), nameof(ICoreDiagnostics.LuaOperationCompleted),
				nameof(ICoreDiagnostics.CapabilityRefused),
				nameof(ICoreDiagnostics.CoreResourceCleanupFailed)
			],
			diagnostics.Emissions.Select(static emission => emission.Event));
		Assert.All(diagnostics.Emissions, static emission => Assert.False(emission.InsideCallback,
			$"{emission.Event} was emitted inside a dispatched callback."));
	}

	[Fact]
	public void ScriptedRunEmitsEachDomainEventWithItsBoundedFields()
	{
		CallbackTracker tracker = new();
		RecordingCoreDiagnostics diagnostics = new(tracker);

		RunScenario(diagnostics, tracker);

		Assert.Equal(Fields(1L, CheatEngineArchitecture.X64, 8, 8, false),
			diagnostics.Single(nameof(ICoreDiagnostics.RuntimeSnapshotCaptured)));
		Assert.Equal(
			[
				Fields(1L, 1L, "Processes.GetCurrent", "PidChanged"),
				Fields(1L, 2L, "Processes.GetCurrent", "TargetDetached")
			],
			diagnostics.All(nameof(ICoreDiagnostics.TargetSelectionAdvanced)));
		Assert.Equal(Fields("Memory.ReadPrimitive", 8, 4),
			diagnostics.Single(nameof(ICoreDiagnostics.PointerWidthMismatchRefused)));
		Assert.Equal(Fields("Memory.ReadPrimitiveBatch", 2, 2, "ReadOnly"),
			diagnostics.Single(nameof(ICoreDiagnostics.MemoryBatchCompleted)));
		Assert.Equal(Fields(1L, 1L), diagnostics.Single(nameof(ICoreDiagnostics.TableGenerationAdvanced)));
		Assert.Equal(Fields("Tables.SetActive", 1L),
			diagnostics.Single(nameof(ICoreDiagnostics.StaleRecordIdentifierRefused)));
		Assert.Equal(Fields("Tables.SetActive", true, "RefusedByHost"),
			diagnostics.Single(nameof(ICoreDiagnostics.RecordActivationNotApplied)));
		Assert.Equal(Fields("Inspection.RegisterSymbol", "AlreadyResolves"),
			diagnostics.Single(nameof(ICoreDiagnostics.SymbolRegistrationRejected)));
		Assert.Equal(Fields(SymbolRegistrationLease.ReleaseOperation, LeaseReleaseKind.Released,
				CheatEngineHostEffect.Completed),
			diagnostics.Single(nameof(ICoreDiagnostics.LeaseReleased)));
		object?[] scan = diagnostics.Single(nameof(ICoreDiagnostics.PatternScanCompleted));
		Assert.Equal(Fields(PatternScanScope.GlobalHostScan, 1L, 1, false), scan[..4]);
		object?[] lua = diagnostics.Single(nameof(ICoreDiagnostics.LuaOperationCompleted));
		Assert.Equal(Fields("Lua.Execute", "None", 0), Fields(lua[0], lua[1], lua[3]));
		Assert.Equal(
			[
				Fields(ClientCapabilityId.UnsafeLuaExecution.Value, "UnsafeLua.Execute",
					ClientCapabilityEvidenceReasonCode.Policy, ClientCapabilityEvidenceState.Missing)
			],
			diagnostics.All(nameof(ICoreDiagnostics.CapabilityRefused)));
		Assert.Equal(Fields(nameof(ThrowingDisposable), typeof(InvalidOperationException).FullName),
			diagnostics.Single(nameof(ICoreDiagnostics.CoreResourceCleanupFailed)));
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void CoreDiagnosticsCarryOnlyClosedNamesCountsAndEpochs()
	{
		CallbackTracker tracker = new();
		RecordingCoreDiagnostics diagnostics = new(tracker);

		RunScenario(diagnostics, tracker);

		Assert.NotEmpty(diagnostics.Emissions);
		foreach (Emission emission in diagnostics.Emissions)
		{
			foreach (object? argument in emission.Arguments)
			{
				Assert.True(argument is string or long or int or bool or Enum,
					$"{emission.Event} carries a {argument?.GetType().FullName ?? "null"} argument.");
				if (argument is string text)
				{
					Assert.Matches(ClosedName(), text);
					Assert.DoesNotContain(SensitiveSymbol, text, StringComparison.OrdinalIgnoreCase);
					Assert.DoesNotContain(ExistingSymbol, text, StringComparison.OrdinalIgnoreCase);
					Assert.DoesNotContain("readInteger", text, StringComparison.Ordinal);
					Assert.DoesNotContain("secret", text, StringComparison.OrdinalIgnoreCase);
					Assert.DoesNotContain(_root, text, StringComparison.OrdinalIgnoreCase);
				}

				long? number = argument switch
				{
					long value => value,
					int value => value,
					_ => null
				};
				Assert.NotEqual(unchecked((long) SensitiveAddress), number);
			}
		}
	}

	[Fact]
	public void ThrowingCoreDiagnosticsSinkDoesNotChangeOperationResults()
	{
		CallbackTracker silentTracker = new();
		CallbackTracker throwingTracker = new();
		RecordingCoreDiagnostics throwing = new(throwingTracker, throwAfterRecording: true);

		IReadOnlyList<string> withoutDiagnostics = RunScenario(null, silentTracker);
		IReadOnlyList<string> withThrowingDiagnostics = RunScenario(throwing, throwingTracker);

		Assert.Equal(withoutDiagnostics, withThrowingDiagnostics);
		Assert.Equal(14, throwing.Emissions.Count);
		Assert.Contains("UnsafeLua.Execute:False:CapabilityUnavailable", withThrowingDiagnostics);
		Assert.Contains("Cleanup:InvalidOperationException", withThrowingDiagnostics);
	}

	[Fact]
	public void GuardedDiagnosticsWrapsOnceAndMapsNullToTheNullSink()
	{
		RecordingCoreDiagnostics inner = new(new CallbackTracker(), throwAfterRecording: true);

		ICoreDiagnostics guarded = GuardedCoreDiagnostics.Wrap(inner);

		Assert.Same(NullCoreDiagnostics.Instance, GuardedCoreDiagnostics.Wrap(null));
		Assert.IsType<GuardedCoreDiagnostics>(guarded);
		Assert.Same(guarded, GuardedCoreDiagnostics.Wrap(guarded));
		guarded.LeaseReleased(SymbolRegistrationLease.ReleaseOperation, LeaseReleaseKind.Replaced,
			CheatEngineHostEffect.NotStarted);
		Assert.Single(inner.Emissions);
	}

	/// <summary>
	///     Runs one operation of every domain that emits an event and returns the observable results; the Cheat Engine
	///     ports are fakes and the dispatchers run callbacks inline while <paramref name="tracker" /> marks them.
	/// </summary>
	private List<string> RunScenario(ICoreDiagnostics? diagnostics, CallbackTracker tracker)
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		List<string> outcomes = [];
		FakeTarget target = new()
		{
			ProcessId = 100
		};
		CoreLifetime lifetime = InertCoreLifetime.Create(diagnostics);
		FlaggingDispatcher dispatcher = new(tracker);
		SdkMainThreadDispatcher mainThreadDispatcher = new(lifetime, new FlaggingInvoker(tracker));

		RuntimeClient runtime = new(dispatcher, target, () => lifetime.Epoch, diagnostics: diagnostics);
		outcomes.Add(Describe("Runtime.GetSnapshot",
			runtime.TryGetSnapshot(out _, out CheatEngineFailure failure, cancellationToken), failure));

		ProcessClient processes = new(dispatcher, target, target, target, lifetime);
		outcomes.Add(Describe("Processes.First", processes.TryGetCurrent(out _, out failure, cancellationToken),
			failure));
		target.ProcessId = 200;
		outcomes.Add(Describe("Processes.PidChanged", processes.TryGetCurrent(out _, out failure, cancellationToken),
			failure));
		target.ProcessId = 0;
		outcomes.Add(Describe("Processes.Detached", processes.TryGetCurrent(out _, out failure, cancellationToken),
			failure));
		target.ProcessId = 200;

		MemoryClient memory = new(dispatcher, lifetime, target);
		target.ConfiguredPointerSize = sizeof(uint);
		outcomes.Add(Describe("Memory.ReadPointer",
			memory.TryReadPrimitive(new Address(SensitiveAddress), out Address _, out failure, cancellationToken),
			failure));
		target.ConfiguredPointerSize = sizeof(ulong);
		MemoryPrimitiveBatchReadOutcome<int> batch = memory.ReadPrimitiveBatchDetailed(
			new MemoryPrimitiveBatchReadRequest<int>([new Address(SensitiveAddress), new Address(SensitiveAddress + 4)]),
			cancellationToken);
		outcomes.Add($"Memory.Batch:{batch.IsSuccess}:{batch.CompletedCount}");

		string tablePath = Path.Combine(_root, "trusted.ct");
		File.WriteAllText(tablePath, "<CheatTable/>");
		TableClient tables = new(dispatcher, new CoreClientPolicy([_root], false), new RefusingMutationPort(), lifetime,
			new SingleRecordLookupPort(), new AcceptingFilePort());
		outcomes.Add(Describe("Tables.HandOut", tables.TryGetRecordAt(0, out _, out failure, cancellationToken), failure));
		outcomes.Add(Describe("Tables.Load",
			tables.TryLoadTrustedTable(new TableLoadRequest(new TrustedTableFile(tablePath)), out failure,
				cancellationToken), failure));
		outcomes.Add(Describe("Tables.Stale",
			tables.TrySetActive(HandedOut, true, out _, out failure, cancellationToken), failure));
		outcomes.Add(Describe("Tables.Reobserve", tables.TryGetRecordAt(0, out _, out failure, cancellationToken),
			failure));
		outcomes.Add(Describe("Tables.Refused",
			tables.TrySetActive(HandedOut, true, out _, out failure, cancellationToken), failure));

		InspectionClient inspection = new(mainThreadDispatcher, lifetime, new SymbolPort());
		outcomes.Add(Describe("Inspection.Collision",
			inspection.TryRegisterSymbol(new SymbolRegistration(ExistingSymbol, new Address(0x2000)), out _,
				out failure, cancellationToken), failure));
		bool registered = inspection.TryRegisterSymbol(
			new SymbolRegistration(SensitiveSymbol, new Address(SensitiveAddress)), out ISymbolRegistrationLease? lease,
			out failure, cancellationToken);
		outcomes.Add(Describe("Inspection.Register", registered, failure));
		lease?.Dispose();

		PatternScanner patterns = new(mainThreadDispatcher, new SingleMatchScanPort());
		outcomes.Add(Describe("Patterns.Scan",
			patterns.TryScan(new AobScanRequest(new AobPattern("90"), 10, null, null), out _,
				out failure, cancellationToken), failure));

		LuaClient lua = new(dispatcher, () => lifetime.Epoch, () => true, diagnostics: diagnostics);
		outcomes.Add(Describe("Lua.Execute",
			lua.TryExecute<ConstantOperation, int>(new ConstantOperation(), out _, out failure, cancellationToken),
			failure));

		UnsafeLuaClient unsafeLua = new(dispatcher, new CoreClientPolicy([_root], false), lifetime);
		outcomes.Add(Describe("UnsafeLua.Execute",
			unsafeLua.TryExecute(new LuaScript(SensitiveScript), out failure, cancellationToken), failure));

		lifetime.Track(new ThrowingDisposable());
		Exception cleanup = Assert.ThrowsAny<Exception>(lifetime.Dispose);
		outcomes.Add($"Cleanup:{cleanup.GetType().Name}");
		return outcomes;
	}

	private static object?[] Fields(params object?[] values)
	{
		return values;
	}

	private static string Describe(string step, bool succeeded, CheatEngineFailure failure)
	{
		return succeeded ? $"{step}:True" : $"{step}:False:{failure.Kind}";
	}

	private static MemoryRecordSnapshot Snapshot(MemoryRecordId id)
	{
		return new MemoryRecordSnapshot(id, 0,
			new MemoryRecordContentSnapshot("Health", "game.exe+24", "100", VariableType.Dword),
			new MemoryRecordStateSnapshot(null));
	}

	[GeneratedRegex(@"^[A-Z][A-Za-z]*(\.[A-Z][A-Za-z]*)*$")]
	private static partial Regex ClosedName();

	private sealed record Emission(string Event, bool InsideCallback, object?[] Arguments);

	/// <summary>Marks the dynamic extent of every dispatched callback.</summary>
	private sealed class CallbackTracker
	{
		private int _depth;

		internal bool InCallback => _depth > 0;

		internal int CallbackCount
		{
			get;
			private set;
		}

		internal T Run<T>(Func<T> callback)
		{
			CallbackCount++;
			_depth++;
			try
			{
				return callback();
			}
			finally
			{
				_depth--;
			}
		}
	}

	private sealed class FlaggingDispatcher(CallbackTracker tracker) : ICheatEngineDispatcher
	{
		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			tracker.Run(() =>
			{
				callback();
				return 0;
			});
			failure = default;
			return true;
		}

		public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result,
			out CheatEngineFailure failure, CancellationToken cancellationToken = default)
		{
			result = tracker.Run(callback);
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out _, cancellationToken);
		}

		public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out T? result, out _, cancellationToken);
			return result!;
		}
	}

	private sealed class FlaggingInvoker(CallbackTracker tracker) : IMainThreadInvoker
	{
		public Exception? Invoke(Action callback)
		{
			try
			{
				tracker.Run(() =>
				{
					callback();
					return 0;
				});
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			try
			{
				return new MainThreadInvocationResult<T>(tracker.Run(callback), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<T>(default!, exception);
			}
		}
	}

	/// <summary>Records every event with its arguments and whether a dispatched callback was running.</summary>
	private sealed class RecordingCoreDiagnostics(CallbackTracker tracker, bool throwAfterRecording = false)
		: ICoreDiagnostics
	{
		internal List<Emission> Emissions
		{
			get;
		} = [];

		public void RuntimeSnapshotCaptured(long activationEpoch, CheatEngineArchitecture targetArchitecture,
			int processPointerBytes, int configuredPointerBytes, bool pointerSizeMismatch)
		{
			Record(nameof(RuntimeSnapshotCaptured), activationEpoch, targetArchitecture, processPointerBytes,
				configuredPointerBytes, pointerSizeMismatch);
		}

		public void CapabilityRefused(string capability, string operation, ClientCapabilityEvidenceReasonCode gate,
			ClientCapabilityEvidenceState gateState)
		{
			Record(nameof(CapabilityRefused), capability, operation, gate, gateState);
		}

		public void TargetSelectionAdvanced(long activationEpoch, long selectionEpoch, string operation, string reason)
		{
			Record(nameof(TargetSelectionAdvanced), activationEpoch, selectionEpoch, operation, reason);
		}

		public void PointerWidthMismatchRefused(string operation, int processPointerBytes, int configuredPointerBytes)
		{
			Record(nameof(PointerWidthMismatchRefused), operation, processPointerBytes, configuredPointerBytes);
		}

		public void MemoryBatchCompleted(string operation, int requested, int completed, string effectState)
		{
			Record(nameof(MemoryBatchCompleted), operation, requested, completed, effectState);
		}

		public void TableGenerationAdvanced(long activationEpoch, long tableGeneration)
		{
			Record(nameof(TableGenerationAdvanced), activationEpoch, tableGeneration);
		}

		public void StaleRecordIdentifierRefused(string operation, long tableGeneration)
		{
			Record(nameof(StaleRecordIdentifierRefused), operation, tableGeneration);
		}

		public void RecordActivationNotApplied(string operation, bool requestedState, string status)
		{
			Record(nameof(RecordActivationNotApplied), operation, requestedState, status);
		}

		public void SymbolRegistrationRejected(string operation, string reason)
		{
			Record(nameof(SymbolRegistrationRejected), operation, reason);
		}

		public void PatternScanCompleted(PatternScanScope scope, long hostResultCount, int materializedCount,
			bool truncated, long hostScanMilliseconds, long copyMilliseconds)
		{
			Record(nameof(PatternScanCompleted), scope, hostResultCount, materializedCount, truncated,
				hostScanMilliseconds, copyMilliseconds);
		}

		public void LuaOperationCompleted(string operation, string outcome, long elapsedMilliseconds, int scriptLength)
		{
			Record(nameof(LuaOperationCompleted), operation, outcome, elapsedMilliseconds, scriptLength);
		}

		public void CoreResourceCleanupFailed(string componentType, string exceptionType)
		{
			Record(nameof(CoreResourceCleanupFailed), componentType, exceptionType);
		}

		public void LeaseReleased(string operation, LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
		{
			Record(nameof(LeaseReleased), operation, kind, hostEffect);
		}

		public void AutoAssemblerPatchAppliedAfterTargetChange(string operation, long selectionEpoch)
		{
			Record(nameof(AutoAssemblerPatchAppliedAfterTargetChange), operation, selectionEpoch);
		}

		internal object?[] Single(string eventName)
		{
			return Assert.Single(All(eventName));
		}

		internal object?[][] All(string eventName)
		{
			return Emissions.Where(emission => emission.Event == eventName).Select(static emission => emission.Arguments)
				.ToArray();
		}

		private void Record(string eventName, params object?[] arguments)
		{
			Emissions.Add(new Emission(eventName, tracker.InCallback, arguments));
			if (throwAfterRecording)
			{
				throw new InvalidOperationException("The diagnostics sink failed.");
			}
		}
	}

	/// <summary>
	///     One selected 64-bit x86 target: the process host, the memory port and the runtime observation port at once.
	/// </summary>
	private sealed class FakeTarget : FakeRuntimeObservationPort, IProcessHost, IMemoryCodecContextPort
	{
		private int _configuredPointerSize = sizeof(ulong);
		private long _processId;

		internal long ProcessId
		{
			get => _processId;
			set
			{
				_processId = value;
				Synchronize();
			}
		}

		internal int ConfiguredPointerSize
		{
			get => _configuredPointerSize;
			set
			{
				_configuredPointerSize = value;
				Synchronize();
			}
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			process = default;
			return false;
		}

		public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
		{
			return [];
		}

		public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
		{
			return [];
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			destination.Clear();
			written = destination.Length;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
		{
			value = default!;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
			out MemoryAccessFailure failure)
		{
			value = default;
			failure = MemoryAccessFailure.None;
			return true;
		}

		public bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
			out MemoryAccessFailure failure)
		{
			failure = MemoryAccessFailure.None;
			return true;
		}

		private void Synchronize()
		{
			TargetStatus = _processId == 0 ? ProcessOperationStatus.TargetNotAttached : ProcessOperationStatus.Success;
			Target = TargetObservations.Create((int) Math.Max(_processId, 1),
				configuredPointerSizeBytes: _configuredPointerSize);
		}
	}

	private sealed class SingleRecordLookupPort : ITableRecordLookupPort
	{
		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			record = Snapshot(HandedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			record = Snapshot(id);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			record = Snapshot(HandedOut);
			return RecordLookupStatus.Success;
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			table = new AddressTableSnapshot([Snapshot(HandedOut)]);
			return RecordLookupStatus.Success;
		}
	}

	/// <summary>A record whose activation callback refuses every change.</summary>
	private sealed class RefusingMutationPort : ITableRecordMutationPort
	{
		public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
		{
			record = default;
			return TableRecordCreation.Created;
		}

		public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
		{
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			record = Snapshot(childId);
			return TableRecordMutationOutcome.Succeeded;
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			return new TableActivationObservation(MemoryRecordActivationOutcomeKind.RefusedByHost,
				MemoryRecordMutationProblem.None, Snapshot(id));
		}

		public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			record = Snapshot(id);
			return TableRecordMutationOutcome.Succeeded;
		}
	}

	private sealed class AcceptingFilePort : ITableFilePort
	{
		public LuaOperationStatus TryLoad(string path, bool merge)
		{
			return LuaOperationStatus.Success;
		}

		public LuaOperationStatus TrySave(string path)
		{
			return LuaOperationStatus.Success;
		}
	}

	/// <summary>A symbol table in which <see cref="ExistingSymbol" /> already resolves.</summary>
	private sealed class SymbolPort : IInspectionPort
	{
		private readonly Dictionary<string, Address> _symbols = new(StringComparer.Ordinal)
		{
			[ExistingSymbol] = new Address(0x1000)
		};

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			written = 0;
			return InspectionStatus.Success;
		}

		public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written)
		{
			written = 0;
			return InspectionStatus.Success;
		}

		public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination,
			out int written)
		{
			written = 0;
			return InspectionStatus.Success;
		}

		public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written)
		{
			written = 0;
			return InspectionStatus.Success;
		}

		public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region)
		{
			region = default;
			return InspectionStatus.NotFound;
		}

		public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol)
		{
			symbol = default;
			return InspectionStatus.NotFound;
		}

		public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
			out Address address)
		{
			return _symbols.TryGetValue(expression.Value, out address)
				? InspectionStatus.Success
				: InspectionStatus.NotFound;
		}

		public LuaOperationStatus TryGetName(Address address, out string? name)
		{
			name = null;
			return LuaOperationStatus.NilResult;
		}

		public SymbolRegistrationAttempt TryRegisterOwned(SymbolName name, Address address,
			SymbolRegistrationOptions options)
		{
			_symbols[name.Value] = address;
			return new SymbolRegistrationAttempt(LuaOperationStatus.Success, new Registration(_symbols, name.Value));
		}

		/// <summary>Unregisters the name once, as the SDK lease does when the name still maps to the address.</summary>
		private sealed class Registration(Dictionary<string, Address> symbols, string name) : ISymbolRegistrationHandle
		{
			public SymbolRegistrationReleaseKind Release()
			{
				return symbols.Remove(name)
					? SymbolRegistrationReleaseKind.Released
					: SymbolRegistrationReleaseKind.AlreadyReleased;
			}
		}
	}

	private sealed class SingleMatchScanPort : IAobScanPort
	{
		public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
		{
			matches = new SingleMatchList();
			return AobHosts.Outcome(AobScanOutcomeKind.Matches, 1);
		}

		public AobBoundedHostResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
			Span<Address> destination, CancellationToken cancellationToken)
		{
			throw new NotSupportedException("The diagnostics scan is unscoped.");
		}

		public TargetSelectionFacts ObserveSelection()
		{
			throw new NotSupportedException("The diagnostics scan is unscoped.");
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			written = 0;
			return InspectionStatus.Success;
		}
	}

	private sealed class SingleMatchList : IAobMatchList
	{
		public bool TryGetCount(out int count)
		{
			count = 1;
			return true;
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			value = index == 0 ? "7FF612345678" : null;
			return value is not null;
		}

		public TargetReleaseStatus Release()
		{
			return TargetReleaseStatus.Released;
		}
	}

	private readonly record struct ConstantOperation : ILuaOperation<int>
	{
		public bool TryExecute(ILuaExecutionContext context, out int result, out CheatEngineFailure failure)
		{
			result = 42;
			failure = default;
			return true;
		}
	}

	private sealed class ThrowingDisposable : IDisposable
	{
		public void Dispose()
		{
			throw new InvalidOperationException("The release of C:\\Users\\player\\secret.ct failed.");
		}
	}
}
