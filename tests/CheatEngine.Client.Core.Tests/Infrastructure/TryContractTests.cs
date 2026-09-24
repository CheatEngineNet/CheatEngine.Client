using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Runtime;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     Proves the per-family Try / exception / cancellation contract (audit F15, A10-20, A10-21, A11-31): which refusals
///     become failures, which lifecycle faults are thrown, that no SDK exception crosses a Try method, and the host effect
///     reported when cancellation or a partial result is observed.
/// </summary>
/// <remarks>
///     Every test uses the real <see cref="SdkMainThreadDispatcher" /> with an inline invoker: a fake dispatcher could not
///     prove the same-instance rethrow rule.
/// </remarks>
public sealed class TryContractTests
{
	private static readonly Address Target = new(0x401000);

	public static TheoryData<string> Families => new()
	{
		"Patterns",
		"MemoryPrimitive",
		"MemoryBatchDetailed",
		"MemoryBytesDetailed",
		"Inspection",
		"Tables",
		"LuaTypedOperation",
		"UnsafeLua",
		"UnavailableCapability",
		"Processes",
		"Runtime"
	};

	public static TheoryData<string, string> SdkFaultCases
	{
		get
		{
			TheoryData<string, string> data = [];
			foreach (string fault in SdkFaultKinds.Keys)
			{
				foreach (string entryPoint in SdkFaultEntryPoints)
				{
					data.Add(fault, entryPoint);
				}
			}

			return data;
		}
	}

	private static Dictionary<string, CheatEngineFailureKind> SdkFaultKinds => new(StringComparer.Ordinal)
	{
		[nameof(EngineBindingException)] = CheatEngineFailureKind.BindingError,
		[nameof(EngineCapabilityUnavailableException)] = CheatEngineFailureKind.CapabilityUnavailable,
		[nameof(EngineGlobalUnavailableException)] = CheatEngineFailureKind.CapabilityUnavailable,
		[nameof(EngineLuaException)] = CheatEngineFailureKind.LuaError,
		[nameof(EngineMarshallingException)] = CheatEngineFailureKind.InvalidHostResult,
		[nameof(EngineOperationFailedException)] = CheatEngineFailureKind.OperationRejected,
		[nameof(LuaException)] = CheatEngineFailureKind.LuaError,
		[nameof(InvalidOperationException)] = CheatEngineFailureKind.OperationRejected
	};

	private static string[] SdkFaultEntryPoints =>
	[
		"Patterns.Scan",
		"Patterns.InModule",
		"Inspection.GetSymbol",
		"Inspection.RegisterSymbol",
		"Tables.GetRecord",
		"Tables.Delete",
		"Memory.ReadPrimitive",
		"Memory.ReadBytes",
		"Memory.WritePrimitiveBatch",
		"Memory.ResolvePointerChain",
		"Processes.GetCurrent",
		"Processes.Attach",
		"Runtime.GetSnapshot"
	];

	[Theory]
	[MemberData(nameof(Families))]
	public void ActivationExpiredExceptionIsNeverMappedToCancelledOrCapabilityUnavailable(string family)
	{
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		CancellationToken cancelled = new(true);
		ThrowingPorts ports = new(new InvalidOperationException("never reached"));
		CoreClientPolicy policy = new([], enableUnsafeLuaExecution: true);

		Action entryPoint = family switch
		{
			"Patterns" => () => new PatternScanner(dispatcher, ports).TryScan(Request(), out _, out _, cancelled),
			"MemoryPrimitive" => () => new MemoryClient(dispatcher, lifetime, ports)
				.TryReadPrimitive(Target, out int _, out _, cancelled),
			"MemoryBatchDetailed" => () => new MemoryClient(dispatcher, lifetime, ports).WritePrimitiveBatchDetailed(
				new MemoryPrimitiveBatchWriteRequest<int>([new MemoryAddressValue<int>(Target, 1)]), cancelled),
			"MemoryBytesDetailed" => () => new MemoryClient(dispatcher, lifetime, ports)
				.ReadBytesDetailed(new MemoryBytesReadRequest(Target, 4), cancelled),
			"Inspection" => () => new InspectionClient(dispatcher, lifetime, ports)
				.TryGetSymbol(new SymbolExpression("game.exe+10"), out _, out _, cancelled),
			"Tables" => () => new TableClient(dispatcher, policy, ports, lifetime, ports)
				.TryGetRecord(new MemoryRecordId(1), out _, out _, cancelled),
			"LuaTypedOperation" => () => new LuaClient(dispatcher, lifetime)
				.TryExecute(new ConstantOperation(), out _, out _, cancelled),
			"UnsafeLua" => () => new UnsafeLuaClient(dispatcher, policy, lifetime)
				.TryExecute(new LuaScript("return 1"), out _, cancelled),
			"UnavailableCapability" => () => new UnavailableAllocationClient(lifetime)
				.TryAllocate(new TargetAllocationRequest(4096), out _, out _, cancelled),
			"Processes" => () => new ProcessClient(dispatcher, ports, ports, ports, lifetime)
				.TryGetCurrent(out _, out _, cancelled),
			"Runtime" => () => new RuntimeClient(dispatcher, ports, static () => 1).TryGetSnapshot(out _, out _, cancelled),
			_ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
		};

		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(entryPoint);

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
		Assert.Equal(0, ports.Calls);
	}

	[Theory]
	[MemberData(nameof(SdkFaultCases))]
	public void SdkExceptionsNeverCrossATryMethod(string faultType, string entryPoint)
	{
		Exception fault = CreateSdkFault(faultType);
		CheatEngineFailureKind expectedKind = SdkFaultKinds[faultType];
		ThrowingPorts ports = new(fault)
		{
			SucceedingWrites = 1
		};
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		CoreClientPolicy policy = new([], false);
		CancellationToken token = TestContext.Current.CancellationToken;

		(bool succeeded, CheatEngineFailure failure, Action throwingForm) = entryPoint switch
		{
			"Patterns.Scan" => Run(new PatternScanner(dispatcher, ports), Request(),
				static (scanner, request, t) => (scanner.TryScan(request, out _, out CheatEngineFailure f, t), f),
				static (scanner, request, t) => scanner.Scan(request, t), token),
			"Patterns.InModule" => Run(new PatternScanner(dispatcher, ports), Request(new ModuleName("game.exe")),
				static (scanner, request, t) => (scanner.TryScan(request, out _, out CheatEngineFailure f, t), f),
				static (scanner, request, t) => scanner.Scan(request, t), token),
			"Inspection.GetSymbol" => Run(new InspectionClient(dispatcher, lifetime, ports),
				new SymbolExpression("game.exe+10"),
				static (client, expression, t) =>
					(client.TryGetSymbol(expression, out _, out CheatEngineFailure f, t), f),
				static (client, expression, t) => client.GetSymbol(expression, t), token),
			"Inspection.RegisterSymbol" => Run(new InspectionClient(dispatcher, lifetime, ports),
				new SymbolRegistration("contractSymbol", Target),
				static (client, registration, t) =>
					(client.TryRegisterSymbol(registration, out _, out CheatEngineFailure f, t), f),
				static (client, registration, t) => client.RegisterSymbol(registration, t), token),
			"Tables.GetRecord" => Run(new TableClient(dispatcher, policy, ports, lifetime, ports),
				new MemoryRecordId(7),
				static (client, id, t) => (client.TryGetRecord(id, out _, out CheatEngineFailure f, t), f),
				static (client, id, t) => client.GetRecord(id, t), token),
			"Tables.Delete" => Run(new TableClient(dispatcher, policy, ports, lifetime, ports),
				new MemoryRecordId(7),
				static (client, id, t) => (client.TryDelete(id, out CheatEngineFailure f, t), f),
				static (client, id, t) => client.Delete(id, t), token),
			"Memory.ReadPrimitive" => Run(new MemoryClient(dispatcher, lifetime, ports), Target,
				static (client, address, t) => (client.TryReadPrimitive(address, out int _, out CheatEngineFailure f, t), f),
				static (client, address, t) => client.ReadPrimitive<int>(address, t), token),
			"Memory.ReadBytes" => Run(new MemoryClient(dispatcher, lifetime, ports), new MemoryBytesReadRequest(Target, 4),
				static (client, request, t) => (client.TryReadBytes(request, out _, out CheatEngineFailure f, t), f),
				static (client, request, t) => client.ReadBytes(request, t), token),
			"Memory.WritePrimitiveBatch" => Run(new MemoryClient(dispatcher, lifetime, ports),
				new MemoryPrimitiveBatchWriteRequest<int>([
					new MemoryAddressValue<int>(Target, 1), new MemoryAddressValue<int>(Target + 4, 2)
				]),
				static (client, request, t) => (client.TryWritePrimitiveBatch(request, out CheatEngineFailure f, t), f),
				static (client, request, t) => client.WritePrimitiveBatch(request, t), token),
			"Memory.ResolvePointerChain" => Run(new MemoryClient(dispatcher, lifetime, ports),
				new PointerChainRequest(Target, [0x10L, 0x8L]),
				static (client, request, t) =>
					(client.TryResolvePointerChain(request, out _, out CheatEngineFailure f, t), f),
				static (client, request, t) => client.ResolvePointerChain(request, t), token),
			"Processes.GetCurrent" => Run(new ProcessClient(dispatcher, ports, ports, ports, lifetime), 0,
				static (client, _, t) => (client.TryGetCurrent(out ProcessSnapshot _, out CheatEngineFailure f, t), f),
				static (client, _, t) => client.GetCurrent(t), token),
			"Processes.Attach" => Run(new ProcessClient(dispatcher, ports, ports, ports, lifetime), new TargetProcessId(43),
				static (client, processId, t) => (client.TryAttach(processId, out _, out CheatEngineFailure f, t), f),
				static (client, processId, t) => client.Attach(processId, t), token),
			"Runtime.GetSnapshot" => Run(new RuntimeClient(dispatcher, ports, static () => 1), 0,
				static (client, _, t) => (client.TryGetSnapshot(out CheatEngineRuntimeSnapshot _, out CheatEngineFailure f, t), f),
				static (client, _, t) => client.GetSnapshot(t), token),
			_ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
		};

		Assert.False(succeeded);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(entryPoint, failure.Operation);
		Assert.Same(fault, failure.Exception);
		Assert.NotEqual(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		CheatEngineClientException thrown = Assert.ThrowsAny<CheatEngineClientException>(throwingForm);
		Assert.Same(fault, thrown.Failure.Exception);
		Assert.Equal(expectedKind, thrown.Failure.Kind);
	}

	[Fact]
	public void AnInterruptedBatchWriteKeepsItsCompletedPrefixAndAnUnknownEffect()
	{
		InvalidOperationException fault = new("detached during the second write");
		ThrowingPorts ports = new(fault)
		{
			SucceedingWrites = 1
		};
		CoreLifetime lifetime = InertCoreLifetime.Create();
		MemoryClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime,
			ports);

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(Target, 1), new MemoryAddressValue<int>(Target + 4, 2),
				new MemoryAddressValue<int>(Target + 8, 3)
			]), TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(1, outcome.CompletedCount);
		Assert.Equal(1, outcome.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.Unknown, outcome.EffectState);
		Assert.Same(fault, outcome.Failure!.Value.Exception);
		Assert.Equal(CheatEngineHostEffect.Unknown, outcome.Failure.Value.HostEffect);
	}

	[Fact]
	public void StaticSdkBindingFailuresNeverCrossATryMethod()
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		string root = Directory.CreateTempSubdirectory("ce-client-try-contract-").FullName;
		try
		{
			string tablePath = Path.Combine(root, "trusted.ct");
			File.WriteAllText(tablePath, "<CheatTable/>");
			TableClient tables = new(dispatcher, new CoreClientPolicy([root], false), lifetime: lifetime);
			InspectionClient inspection = new(dispatcher, lifetime);
			MemoryClient memory = new(dispatcher, lifetime);
			PatternScanner patterns = new(dispatcher);
			UnsafeLuaClient unsafeLua = new(dispatcher, new CoreClientPolicy([], true), lifetime);
			CancellationToken token = TestContext.Current.CancellationToken;

			// No Lua runtime is attached in unit tests: every SDK static below throws InvalidOperationException, and the
			// SDK's own admission reports Detached.
			Assert.False(tables.TryLoadTrustedTable(new TableLoadRequest(new TrustedTableFile(tablePath)),
				out CheatEngineFailure loadFailure, token));
			Assert.False(inspection.TryRegisterSymbol(new SymbolRegistration("contractSymbol", Target),
				out ISymbolRegistrationLease? lease, out CheatEngineFailure registerFailure, token));
			Assert.False(memory.TryReadBytes(new MemoryBytesReadRequest(Target, 8), out ImmutableArray<byte> bytes,
				out CheatEngineFailure readFailure, token));
			// The counted TargetMemory.TryReadBytes overload behind the prefix-reporting read.
			MemoryBytesReadOutcome detailed = memory.ReadBytesDetailed(new MemoryBytesReadRequest(Target, 8), token);
			Assert.False(patterns.TryScan(Request(), out _, out CheatEngineFailure scanFailure, token));
			Assert.False(unsafeLua.TryExecute(new LuaScript("return 1"), out CheatEngineFailure luaFailure, token));

			Assert.Null(lease);
			Assert.True(bytes.IsEmpty);
			Assert.Equal(0, detailed.ConfirmedLength);
			CheatEngineFailure[] failures =
				[loadFailure, registerFailure, readFailure, detailed.Failure!.Value, scanFailure];
			Assert.All(
				failures,
				static failure =>
				{
					Assert.IsType<InvalidOperationException>(failure.Exception);
					Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
					Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
				});
			// Unsafe Lua asks for its admission through LuaAdmission: a detached runtime is an expired activation that
			// never reached Cheat Engine, not a rejection.
			Assert.Null(luaFailure.Exception);
			Assert.Equal(CheatEngineFailureKind.ActivationExpired, luaFailure.Kind);
			Assert.Equal(CheatEngineHostEffect.NotStarted, luaFailure.HostEffect);
			Assert.Equal("Lua.ExecuteUnsafe", luaFailure.Operation);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Theory]
	[InlineData(LuaAdmissionStatus.Detached, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.TransitionInProgress, CheatEngineFailureKind.ActivationExpired)]
	[InlineData(LuaAdmissionStatus.ExternalStateReset, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(LuaAdmissionStatus.ThreadNotAdmitted, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.NoStateForThread, CheatEngineFailureKind.InvalidState)]
	[InlineData(LuaAdmissionStatus.Unknown, CheatEngineFailureKind.InvalidState)]
	[InlineData((LuaAdmissionStatus) 99, CheatEngineFailureKind.InvalidState)]
	public void ARefusedLuaAdmissionInsidePortWorkIsNeverReportedAsARejection(LuaAdmissionStatus status,
		CheatEngineFailureKind expectedKind)
	{
		Assert.False(LuaAdmission.TryClassify(status, "Tables.ReadParent", out CheatEngineFailure refusal));
		LuaAdmissionRefusedException fault = new(refusal);
		ThrowingPorts ports = new(fault);
		CoreLifetime lifetime = InertCoreLifetime.Create();
		TableClient tables = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()),
			new CoreClientPolicy([], false), ports, lifetime, ports);

		bool succeeded = tables.TrySetParent(new MemoryRecordId(7), new MemoryRecordId(8), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);
		CheatEngineClientException thrown = Assert.ThrowsAny<CheatEngineClientException>(() =>
			tables.SetParent(new MemoryRecordId(7), new MemoryRecordId(8), TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.True(failure.Kind is CheatEngineFailureKind.ActivationExpired or CheatEngineFailureKind.InvalidState
			or CheatEngineFailureKind.RuntimeChanged);
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(expectedKind, thrown.Failure.Kind);
	}

	[Fact]
	public void InvalidOperationExceptionAfterAnExternalResetIsReportedAsRuntimeChanged()
	{
		InvalidOperationException fault = new("The host replaced its Lua state outside this SDK's controlled reset path.");

		CheatEngineFailure afterReset = SdkBoundary.Classify("Memory.ReadBytes", fault, CheatEngineHostEffect.Unknown,
			externalStateResetDetected: true);
		CheatEngineFailure withoutReset = SdkBoundary.Classify("Memory.ReadBytes", fault, CheatEngineHostEffect.Unknown,
			externalStateResetDetected: false);

		Assert.Equal(CheatEngineFailureKind.RuntimeChanged, afterReset.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, afterReset.HostEffect);
		Assert.Equal("Memory.ReadBytes", afterReset.Operation);
		Assert.Same(fault, afterReset.Exception);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, withoutReset.Kind);
	}

	[Fact]
	public void SdkFaultsWithTheirOwnCategoryKeepItAfterAnExternalReset()
	{
		Exception[] faults =
		[
			new ObjectDisposedException("contract.disposed"),
			(Exception) RuntimeHelpers.GetUninitializedObject(typeof(MemoryScanStateException)),
			new EngineGlobalUnavailableException("contract.global", "global fault"),
			new ArgumentException("contract argument")
		];

		foreach (Exception fault in faults)
		{
			Assert.Equal(CoreFailureFactory.GetKind(fault),
				SdkBoundary.Classify("Memory.ReadBytes", fault, CheatEngineHostEffect.Unknown, true).Kind);
		}
	}

	[Fact]
	public void SdkFaultObservedAfterTheActivationEndedIsReportedAsActivationExpired()
	{
		using ControlledCoreLifetimeContext context = new();
		CoreLifetime lifetime = new(context);
		ThrowingPorts ports = new(new InvalidOperationException("the runtime detached"))
		{
			BeforeFault = () => context.IsCurrent = false
		};
		PatternScanner scanner = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), ports);

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			scanner.TryScan(Request(), out _, out _, TestContext.Current.CancellationToken));

		Assert.IsType<InvalidOperationException>(exception.InnerException);
	}

	[Fact]
	public void ConsumerCallbackExceptionsAreRethrownAsTheSameInstance()
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		ConsumerException codecFault = new("codec");
		CheatEngineOperationException codecClientFault = new(new CheatEngineFailure(
			CheatEngineFailureKind.OperationRejected, "Application.Codec", "application-owned client exception"));
		ConsumerException operationFault = new("operation");
		ConsumerException callbackFault = new("callback");
		MemoryClient memory = new(dispatcher, lifetime, new ThrowingPorts(new InvalidOperationException("unused")));
		LuaClient lua = new(dispatcher, lifetime);

		Assert.Same(codecFault, Assert.Throws<ConsumerException>(() => memory.TryRead(
			new MemoryReadRequest<int>(Target, new ThrowingCodec(codecFault)), out _, out _,
			TestContext.Current.CancellationToken)));
		Assert.Same(codecClientFault, Assert.Throws<CheatEngineOperationException>(() => memory.TryWrite(
			new MemoryWriteRequest<int>(Target, 3, new ThrowingCodec(codecClientFault)), out _,
			TestContext.Current.CancellationToken)));
		Assert.Same(operationFault, Assert.Throws<ConsumerException>(() => lua.TryExecute(
			new ThrowingOperation(operationFault), out _, out _, TestContext.Current.CancellationToken)));
		Assert.Same(callbackFault, Assert.Throws<ConsumerException>(() => dispatcher.TryInvoke(
			() => throw callbackFault, out _, TestContext.Current.CancellationToken)));
	}

	[Theory]
	[InlineData("PatternsPreDispatchCancellation")]
	[InlineData("PatternsInvalidRequest")]
	[InlineData("MemoryBudget")]
	[InlineData("MemoryBatchPreDispatchCancellation")]
	[InlineData("MemoryBytesDetailedBudget")]
	[InlineData("TablesPolicy")]
	[InlineData("UnsafeLuaPolicy")]
	[InlineData("UnavailableCapability")]
	[InlineData("LuaPreDispatchCancellation")]
	[InlineData("ProcessesAttachExactNameCancellation")]
	public void RefusalBeforeStartReportsNotStarted(string refusal)
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		ThrowingPorts ports = new(new InvalidOperationException("must not be reached"));
		CancellationToken cancelled = new(true);
		CancellationToken token = TestContext.Current.CancellationToken;

		CheatEngineFailure failure = refusal switch
		{
			"PatternsPreDispatchCancellation" => TryFailure(() =>
				(new PatternScanner(dispatcher, ports).TryScan(Request(), out _, out CheatEngineFailure f, cancelled), f)),
			"PatternsInvalidRequest" => TryFailure(() =>
				(new PatternScanner(dispatcher, ports).TryScan(default, out _, out CheatEngineFailure f, token), f)),
			"MemoryBudget" => TryFailure(() =>
				(new MemoryClient(dispatcher, lifetime, ports, new MemoryResourceLimits(1, 1, 1, 64, 2))
					.TryReadBytes(new MemoryBytesReadRequest(Target, 2), out _, out CheatEngineFailure f, token), f)),
			"MemoryBatchPreDispatchCancellation" => new MemoryClient(dispatcher, lifetime, ports)
				.WritePrimitiveBatchDetailed(
					new MemoryPrimitiveBatchWriteRequest<int>([new MemoryAddressValue<int>(Target, 1)]), cancelled)
				.Failure!.Value,
			"MemoryBytesDetailedBudget" => new MemoryClient(dispatcher, lifetime, ports,
					new MemoryResourceLimits(1, 1, 1, 64, 2))
				.ReadBytesDetailed(new MemoryBytesReadRequest(Target, 2), token).Failure!.Value,
			"TablesPolicy" => TryFailure(() => (new TableClient(dispatcher, new CoreClientPolicy([], false), ports,
					lifetime, ports).TryLoadTrustedTable(new TableLoadRequest(new TrustedTableFile(
					Path.Combine(Path.GetTempPath(), "untrusted.ct"))), out CheatEngineFailure f, token), f)),
			"UnsafeLuaPolicy" => TryFailure(() =>
				(new UnsafeLuaClient(dispatcher, new CoreClientPolicy([], false), lifetime)
					.TryExecute(new LuaScript("return 1"), out CheatEngineFailure f, token), f)),
			"UnavailableCapability" => TryFailure(() =>
				(new UnavailableAllocationClient(lifetime).TryAllocate(new TargetAllocationRequest(4096), out _,
					out CheatEngineFailure f, token), f)),
			"LuaPreDispatchCancellation" => TryFailure(() =>
				(new LuaClient(dispatcher, lifetime).TryExecute(new ConstantOperation(), out _,
					out CheatEngineFailure f, cancelled), f)),
			"ProcessesAttachExactNameCancellation" => TryFailure(() =>
				(new ProcessClient(dispatcher, ports, ports, ports, lifetime).TryAttachExactName("fixture.exe", out _,
					out CheatEngineFailure f, cancelled), f)),
			_ => throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null)
		};

		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, ports.Calls);
	}

	/// <summary>
	///     The throwing form of each family raises the cancellation exception that carries its Try form's failure and the
	///     caller's token, never a Client operation exception (AUD-12).
	/// </summary>
	[Theory]
	[InlineData("Patterns")]
	[InlineData("Memory")]
	[InlineData("Tables")]
	[InlineData("Lua")]
	[InlineData("Dispatcher")]
	[InlineData("Processes")]
	[InlineData("Runtime")]
	public void ThrowingFormsRaiseTheCancellationExceptionOfTheirTryForm(string family)
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		ThrowingPorts ports = new(new InvalidOperationException("must not be reached"));
		PatternScanner patterns = new(dispatcher, ports);
		MemoryClient memory = new(dispatcher, lifetime, ports);
		TableClient tables = new(dispatcher, CoreClientPolicy.SafeDefaults, ports, lifetime, ports);
		LuaClient lua = new(dispatcher, lifetime);
		ProcessClient processes = new(dispatcher, ports, ports, ports, lifetime);
		RuntimeClient runtime = new(dispatcher, ports, static () => 1);
		CancellationToken cancelled = new(true);

		(CheatEngineFailure Expected, Action ThrowingForm) scenario = family switch
		{
			"Patterns" => (TryFailure(() => (patterns.TryScan(Request(), out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = patterns.Scan(Request(), cancelled)),
			"Memory" => (TryFailure(() => (memory.TryReadPrimitive(Target, out int _, out CheatEngineFailure f,
				cancelled), f)), () => _ = memory.ReadPrimitive<int>(Target, cancelled)),
			"Tables" => (TryFailure(() => (tables.TryGetCurrent(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = tables.GetCurrent(cancelled)),
			"Lua" => (TryFailure(() => (lua.TryExecute(new ConstantOperation(), out _, out CheatEngineFailure f,
				cancelled), f)), () => _ = lua.Execute(new ConstantOperation(), cancelled)),
			"Dispatcher" => (TryFailure(() => (dispatcher.TryInvoke(static () =>
				{
				}, out CheatEngineFailure f, cancelled), f)), () => dispatcher.Invoke(static () =>
				{
				}, cancelled)),
			"Processes" => (TryFailure(() => (processes.TryRefresh(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = processes.Refresh(cancelled)),
			"Runtime" => (TryFailure(() => (runtime.TryGetSnapshot(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = runtime.GetSnapshot(cancelled)),
			_ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
		};

		CheatEngineOperationCanceledException exception =
			Assert.Throws<CheatEngineOperationCanceledException>(scenario.ThrowingForm);

		Assert.Equal(CheatEngineFailureKind.Cancelled, scenario.Expected.Kind);
		Assert.Equal(scenario.Expected, exception.Failure);
		Assert.Equal(cancelled, exception.CancellationToken);
		Assert.Equal(0, ports.Calls);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void EffectStartedThenCancelledReportsCompleted()
	{
		using CancellationTokenSource cancellation = new();
		ThrowingPorts ports = new(new InvalidOperationException("unused"))
		{
			ScanResult = ["400000"],
			OnScan = cancellation.Cancel
		};
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), ports);

		bool succeeded = scanner.TryScan(Request(), out AobScanResult result, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(1, ports.ReleasedLists);
	}

	[Fact]
	[Trait("Qualification", "Q33")]
	public void IncompleteBatchReportsPartialWithCount()
	{
		ThrowingPorts ports = new(new InvalidOperationException("unused"))
		{
			SucceedingWrites = 2,
			RejectAfterSucceedingWrites = true
		};
		CoreLifetime lifetime = InertCoreLifetime.Create();
		MemoryClient client = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime,
			ports);

		MemoryPrimitiveBatchWriteOutcome outcome = client.WritePrimitiveBatchDetailed(
			new MemoryPrimitiveBatchWriteRequest<int>([
				new MemoryAddressValue<int>(Target, 1), new MemoryAddressValue<int>(Target + 4, 2),
				new MemoryAddressValue<int>(Target + 8, 3), new MemoryAddressValue<int>(Target + 12, 4)
			]), TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(4, outcome.AttemptedCount);
		Assert.Equal(2, outcome.CompletedCount);
		Assert.Equal(2, outcome.FailedIndex);
		Assert.Equal(MemoryBatchWriteEffectState.Partial, outcome.EffectState);
		Assert.Equal(CheatEngineFailureKind.MemoryWriteFailed, outcome.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, outcome.Failure.Value.HostEffect);
	}

	[Fact]
	public void ConsumerExceptionIsRethrownUnchanged()
	{
		ConsumerException applicationFault = new("application");
		SdkMainThreadDispatcher dispatcher = new(InertCoreLifetime.Create(), new InlineMainThreadInvoker());

		ConsumerException thrown = Assert.Throws<ConsumerException>(() =>
			dispatcher.TryInvoke<int>(() => throw applicationFault, out _, out _,
				TestContext.Current.CancellationToken));

		Assert.Same(applicationFault, thrown);
	}

	private static (bool Succeeded, CheatEngineFailure Failure, Action ThrowingForm) Run<TClient, TInput>(
		TClient client,
		TInput input,
		Func<TClient, TInput, CancellationToken, (bool, CheatEngineFailure)> tryForm,
		Action<TClient, TInput, CancellationToken> throwingForm,
		CancellationToken cancellationToken)
	{
		(bool succeeded, CheatEngineFailure failure) = tryForm(client, input, cancellationToken);
		return (succeeded, failure, () => throwingForm(client, input, cancellationToken));
	}

	private static CheatEngineFailure TryFailure(Func<(bool Succeeded, CheatEngineFailure Failure)> attempt)
	{
		(bool succeeded, CheatEngineFailure failure) = attempt();
		Assert.False(succeeded);
		return failure;
	}

	private static AobScanRequest Request(ModuleName? module = null)
	{
		return new AobScanRequest(new AobPattern("90"), AobScanOptions.Default, 2, module);
	}

	private static Exception CreateSdkFault(string faultType)
	{
		return faultType switch
		{
			nameof(EngineBindingException) => new EngineBindingException("contract.binding", "binding fault"),
			nameof(EngineCapabilityUnavailableException) =>
				new EngineCapabilityUnavailableException("contract.capability", "capability fault"),
			nameof(EngineGlobalUnavailableException) =>
				new EngineGlobalUnavailableException("contract.global", "global fault"),
			nameof(EngineLuaException) => new EngineLuaException("contract.lua", LuaStatus.RuntimeError, "lua fault"),
			nameof(EngineMarshallingException) => new EngineMarshallingException("contract.marshal",
				EngineMarshallingDirection.Result, "integer", "string", "marshalling fault"),
			nameof(EngineOperationFailedException) =>
				new EngineOperationFailedException("contract.operation", "operation fault"),
			nameof(LuaException) => new LuaException("lua state fault"),
			nameof(InvalidOperationException) => new InvalidOperationException("detached runtime"),
			_ => throw new ArgumentOutOfRangeException(nameof(faultType), faultType, null)
		};
	}

	/// <summary>One fake for every Core port; each call throws the configured SDK fault unless configured otherwise.</summary>
	private sealed class ThrowingPorts(Exception fault)
		: IAobScanPort, IInspectionPort, ITableRecordLookupPort, ITableRecordMutationPort, IMemoryCodecContextPort,
			IRuntimeObservationPort, IProcessSelectionPort, IProcessHost
	{
		private int _writes;

		internal int Calls
		{
			get;
			private set;
		}

		internal int ReleasedLists
		{
			get;
			private set;
		}

		internal int SucceedingWrites
		{
			get;
			init;
		}

		internal bool RejectAfterSucceedingWrites
		{
			get;
			init;
		}

		internal string[]? ScanResult
		{
			get;
			init;
		}

		internal Action? OnScan
		{
			get;
			init;
		}

		internal Action? BeforeFault
		{
			get;
			init;
		}

		public AobScanHostStatus TryScan(string pattern, AobScanOptions options,
			[NotNullWhen(true)] out IAobMatchList? matches)
		{
			Calls++;
			OnScan?.Invoke();
			if (ScanResult is { } entries)
			{
				matches = new ListDouble(entries, () => ReleasedLists++);
				return AobScanHostStatus.Success;
			}

			throw Fault();
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			throw Fault();
		}

		public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination,
			out int written)
		{
			throw Fault();
		}

		public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination,
			out int written)
		{
			throw Fault();
		}

		public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written)
		{
			throw Fault();
		}

		public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region)
		{
			throw Fault();
		}

		public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol)
		{
			throw Fault();
		}

		public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
			out Address address)
		{
			throw Fault();
		}

		public bool TryResolveName(nuint address, out string? name)
		{
			throw Fault();
		}

		public void RegisterSymbol(string name, nuint address, bool doNotSave)
		{
			throw Fault();
		}

		public void UnregisterSymbol(string name)
		{
			throw Fault();
		}

		public RecordLookupStatus TryGetRecord(int index, out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public RecordLookupStatus TryGetRecord(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public RecordLookupStatus TryGetSelected(out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public TableRecordCreation TryCreate(MemoryRecordDefinition definition, out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public TableRecordMutationStatus TryDelete(MemoryRecordId id)
		{
			throw Fault();
		}

		public TableRecordMutationStatus TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			throw Fault();
		}

		public TableRecordMutationStatus TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public RecordLookupStatus TryGetTable(int maximumItems, out AddressTableSnapshot table)
		{
			throw Fault();
		}

		public ProcessOperationStatus ObserveCurrent(out CurrentProcessObservation observation)
		{
			throw Fault();
		}

		public ProcessOperationStatus ObserveTargetArchitecture(out TargetArchitectureObservation observation)
		{
			throw Fault();
		}

		public ProcessOperationStatus TryGetConfiguredPointerSize(out int rawBytes, out PointerSize pointerSize)
		{
			throw Fault();
		}

		public bool ExternalStateResetDetected => throw Fault();

		public ProcessOperationStatus TryObserveRuntimeInfo(out RuntimeInfo? info)
		{
			throw Fault();
		}

		public LuaOperationStatus ObserveHost(out CheatEngineHostObservation host)
		{
			throw Fault();
		}

		public LuaOperationStatus TryGetCheatEngineFileVersion(out CheatEngineVersion version)
		{
			throw Fault();
		}

		public LuaOperationStatus TryGetSystemArchitecture(out CheatEngineArchitecture architecture)
		{
			throw Fault();
		}

		public LuaOperationStatus TryIsCheatEngine64Bit(out bool is64Bit)
		{
			throw Fault();
		}

		public LuaOperationStatus TryGetOperatingSystem(out CheatEngineOperatingSystem operatingSystem)
		{
			throw Fault();
		}

		public TargetSelectionFacts ObserveSelection()
		{
			throw Fault();
		}

		public TargetIdentityFacts ValidateSelection(TargetProcessIncarnation expected)
		{
			throw Fault();
		}

		public ProcessOperationStatus SelectAndObserve(TargetProcessId processId,
			out CurrentProcessObservation observation)
		{
			throw Fault();
		}

		public bool TryGetLocalProcess(int processId, out LocalProcessInfo process)
		{
			throw Fault();
		}

		public IReadOnlyList<LocalProcessInfo> GetLocalProcesses()
		{
			throw Fault();
		}

		public IReadOnlyList<LocalProcessInfo> FindProcessesByExactName(string processName)
		{
			throw Fault();
		}

		public bool TryReadBytes(Address address, Span<byte> destination, out int written,
			out MemoryAccessFailure failure)
		{
			throw Fault();
		}

		public bool TryWriteBytes(Address address, ReadOnlySpan<byte> source, out MemoryAccessFailure failure)
		{
			throw Fault();
		}

		public bool TryReadPrimitive<T>(Address address, out T value, out MemoryAccessFailure failure)
		{
			throw Fault();
		}

		public bool TryReadPointer(Address address, PointerSize pointerSize, out Address value,
			out MemoryAccessFailure failure)
		{
			throw Fault();
		}

		public bool TryWritePointer(Address address, Address value, PointerSize pointerSize,
			out MemoryAccessFailure failure)
		{
			throw Fault();
		}

		public bool TryWritePrimitive<T>(Address address, T value, out MemoryAccessFailure failure)
		{
			Calls++;
			if (_writes < SucceedingWrites)
			{
				_writes++;
				failure = MemoryAccessFailure.None;
				return true;
			}

			if (RejectAfterSucceedingWrites)
			{
				failure = MemoryAccessFailure.WriteFailed;
				return false;
			}

			throw Fault();
		}

		private Exception Fault()
		{
			Calls++;
			BeforeFault?.Invoke();
			return fault;
		}
	}

	private sealed class ListDouble(string[] entries, Action onRelease) : IAobMatchList
	{
		public bool TryGetCount(out int count)
		{
			count = entries.Length;
			return true;
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			value = entries[index];
			return true;
		}

		public void Dispose()
		{
			onRelease();
		}
	}

	/// <summary>An application-owned exception type, distinct from every Client and SDK exception.</summary>
	private sealed class ConsumerException(string message) : Exception(message);

	private sealed class ThrowingCodec(Exception fault) : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, [MaybeNullWhen(false)] out int value)
		{
			throw fault;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value)
		{
			throw fault;
		}
	}

	private sealed class ThrowingOperation(Exception fault) : ILuaOperation<int>
	{
		public bool TryExecute(ILuaExecutionContext context, [MaybeNullWhen(false)] out int result,
			out CheatEngineFailure failure)
		{
			throw fault;
		}
	}

	private sealed class ConstantOperation : ILuaOperation<int>
	{
		public bool TryExecute(ILuaExecutionContext context, [MaybeNullWhen(false)] out int result,
			out CheatEngineFailure failure)
		{
			result = 1;
			failure = default;
			return true;
		}
	}
}
