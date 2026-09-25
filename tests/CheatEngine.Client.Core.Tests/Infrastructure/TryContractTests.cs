#pragma warning disable CECLIENT5003 // The Try contract covers the experimental instruction family.
#pragma warning disable CECLIENT5004 // The Try contract covers the experimental Auto Assembler family.

using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Allocations;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Domains.ValueScanning;
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
using CheatEngine.SDK.Engine.Assembly;
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
		"LuaModuleRegistration",
		"UnsafeLua",
		"Processes",
		"Runtime",
		"ValueScans",
		"Allocations",
		"AutoAssembler",
		"Instructions"
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

	/// <summary>Gets each entry point that validates an argument, in each activation state.</summary>
	public static TheoryData<string, string> ArgumentCases
	{
		get
		{
			TheoryData<string, string> data = [];
			foreach (string entryPoint in ArgumentEntryPoints)
			{
				foreach (string state in ActivationStates)
				{
					data.Add(entryPoint, state);
				}
			}

			return data;
		}
	}

	/// <summary>Gets each SDK fault type at each scoped-route SDK call that follows module resolution.</summary>
	public static TheoryData<string, string> ScopedAobFaultCases
	{
		get
		{
			TheoryData<string, string> data = [];
			foreach (string fault in SdkFaultKinds.Keys)
			{
				data.Add(fault, "ObserveSelection");
				data.Add(fault, "TryScanWithinBounds");
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

	private static string[] ActivationStates => ["Active", "Ended", "Stopping"];

	private static string[] ArgumentEntryPoints =>
	[
		"Dispatcher.Invoke",
		"Runtime.GetClientCapability",
		"Processes.Attach",
		"Processes.AttachExactName",
		"Memory.ReadBytes",
		"Memory.Read",
		"Memory.WritePrimitiveBatch",
		"Patterns.Scan",
		"ValueScans.FirstScan",
		"ValueScans.NextScan",
		"ValueScans.Read",
		"Allocations.Allocate",
		"Inspection.GetModules",
		"Inspection.GetSymbol",
		"Inspection.RegisterSymbol",
		"Tables.Find",
		"Tables.Create",
		"Tables.Update",
		"Tables.GetHierarchy",
		"Tables.LoadTrustedTable",
		"Lua.RegisterModule",
		"UnsafeLua.Execute",
		"AutoAssembler.ApplyPatch",
		"Assembly.Assemble"
	];

	private static string[] SdkFaultEntryPoints =>
	[
		"Patterns.Scan",
		"Patterns.Scan.InModule",
		"Inspection.GetSymbol",
		"Inspection.RegisterSymbol",
		"Inspection.ResolveName",
		"Tables.GetRecord",
		"Tables.Delete",
		"Memory.ReadPrimitive",
		"Memory.ReadBytes",
		"Memory.WritePrimitiveBatch",
		"Memory.ResolvePointerChain",
		"Processes.GetCurrentProcess",
		"Processes.Attach",
		"Runtime.GetSnapshot",
		"Lua.RegisterModule",
		"ValueScans.CreateSession",
		"ValueScans.GetResultCount",
		"Allocations.Allocate",
		"AutoAssembler.ApplyPatch",
		"AutoAssembler.Check",
		"Assembly.Assemble",
		"Assembly.Disassemble",
		"Assembly.GetInstructionLength",
		"Assembly.GetPreviousInstructionAddress"
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
		FakeValueScanPort scans = new();
		FakeAllocationPort allocations = new();
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
				.TryExecute<ConstantOperation, int>(new ConstantOperation(), out _, out _, cancelled),
			"LuaModuleRegistration" => () => new LuaClient(dispatcher, lifetime)
				.TryRegisterModule(new PortBackedModule(ports), out _, out _, cancelled),
			"UnsafeLua" => () => new UnsafeLuaClient(dispatcher, policy, lifetime)
				.TryExecute(new LuaScript("return 1"), out _, cancelled),
			"Processes" => () => new ProcessClient(dispatcher, ports, ports, ports, lifetime)
				.TryGetCurrentProcess(out _, out _, cancelled),
			"Runtime" => () => new RuntimeClient(dispatcher, ports, static () => 1).TryGetSnapshot(out _, out _, cancelled),
			"ValueScans" => () => new ValueScanner(dispatcher, Binder(dispatcher), scans)
				.TryCreateSession(out _, out _, cancelled),
			"Allocations" => () => new AllocationClient(dispatcher, Binder(dispatcher), allocations)
				.TryAllocate(new AllocationRequest(4096), out _, out _, cancelled),
			"AutoAssembler" => () => new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(), lifetime,
					Binder(dispatcher), ports)
				.TryApplyPatch(new AutoAssemblerScript("[ENABLE]"), out _, out _, cancelled),
			"Instructions" => () => new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports)
				.TryDisassemble(Target, out _, out _, cancelled),
			_ => throw new ArgumentOutOfRangeException(nameof(family), family, null)
		};

		CheatEngineActivationExpiredException exception =
			Assert.Throws<CheatEngineActivationExpiredException>(entryPoint);

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
		Assert.Equal(0, ports.Calls);
		Assert.Equal(0, scans.Creations);
		Assert.Equal(0, allocations.Allocations);
	}

	/// <summary>
	///     A null, default or malformed argument is a programming error, checked first like the BCL checks its
	///     arguments: every family throws its argument exception whatever the activation state (active, ended or
	///     stopping), before the activation check and before any Cheat Engine call.
	/// </summary>
	[Theory]
	[MemberData(nameof(ArgumentCases))]
	public void AnInvalidArgumentThrowsBeforeTheActivationIsChecked(string entryPoint, string state)
	{
		using ControlledCoreLifetimeContext context = new();
		CoreLifetime lifetime = new(context);
		CountingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		ThrowingPorts ports = new(new InvalidOperationException("never reached"));
		FakeValueScanPort scans = new();
		FakeAllocationPort allocations = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		IValueScanSession session = new ValueScanner(dispatcher, Binder(dispatcher), scans).CreateSession(token);
		MemoryClient memory = new(dispatcher, lifetime, ports);
		InspectionClient inspection = new(dispatcher, lifetime, ports);
		TableClient tables = new(dispatcher, new CoreClientPolicy([], enableUnsafeLuaExecution: false), ports,
			lifetime, ports);
		ProcessClient processes = new(dispatcher, ports, ports, ports, lifetime);
		MemoryRecordId record = new(7);
		Action invalid = entryPoint switch
		{
			"Dispatcher.Invoke" => () => _ = dispatcher.TryInvoke((Action) null!, out _, token),
			"Runtime.GetClientCapability" => () => _ = new RuntimeClient(dispatcher, ports, static () => 1)
				.TryGetClientCapability(default, out _, out _, token),
			"Processes.Attach" => () => _ = processes.TryAttach(default, out _, out _, token),
			"Processes.AttachExactName" => () => _ = processes.TryAttachExactName(" ", out _, out _, token),
			"Memory.ReadBytes" => () => _ = memory.TryReadBytes(default, out _, out _, token),
			"Memory.Read" => () => _ = memory.TryRead(default(MemoryReadRequest<int>), out _, out _, token),
			"Memory.WritePrimitiveBatch" => () =>
				_ = memory.WritePrimitiveBatchDetailed(default(MemoryPrimitiveBatchWriteRequest<int>), token),
			"Patterns.Scan" => () => _ = new PatternScanner(dispatcher, ports).TryScan(default, out _, out _, token),
			"ValueScans.FirstScan" => () => _ = session.TryFirstScan(default, out _, token),
			"ValueScans.NextScan" => () => _ = session.TryNextScan(default, out _, token),
			"ValueScans.Read" => () => _ = session.TryRead(default, out _, out _, token),
			"Allocations.Allocate" => () => _ = new AllocationClient(dispatcher, Binder(dispatcher), allocations)
				.TryAllocate(default, out _, out _, token),
			"Inspection.GetModules" => () => _ = inspection.TryGetModules(default, null, out _, out _, token),
			"Inspection.GetSymbol" => () => _ = inspection.TryGetSymbol(default, out _, out _, token),
			"Inspection.RegisterSymbol" => () => _ = inspection.TryRegisterSymbol(default, out _, out _, token),
			"Tables.Find" => () => _ = tables.TryFind(default, new MemoryRecordCollectionRequest(8), out _, out _,
				token),
			"Tables.Create" => () => _ = tables.TryCreate(default, out _, out _, token),
			"Tables.Update" => () => _ = tables.TryUpdate(record, default, out _, out _, token),
			"Tables.GetHierarchy" => () => _ = tables.TryGetHierarchy(record, default, out _, out _, token),
			"Tables.LoadTrustedTable" => () => _ = tables.TryLoadTrustedTable(default, out _, token),
			"Lua.RegisterModule" => () => _ = new LuaClient(dispatcher, lifetime)
				.TryRegisterModule(null!, out _, out _, token),
			"UnsafeLua.Execute" => () => _ = new UnsafeLuaClient(dispatcher,
					new CoreClientPolicy([], enableUnsafeLuaExecution: true), lifetime)
				.TryExecute(default, out _, token),
			"AutoAssembler.ApplyPatch" => () => _ = new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(),
					lifetime, Binder(dispatcher), ports)
				.TryApplyPatch(default, out _, out _, token),
			"Assembly.Assemble" => () => _ = new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports)
				.TryAssemble(default, out _, out _, token),
			_ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
		};
		int dispatches = invoker.Calls;
		switch (state)
		{
			case "Ended":
				context.IsCurrent = false;
				break;
			case "Stopping":
				context.Stop();
				break;
		}

		_ = Assert.ThrowsAny<ArgumentException>(invalid);

		Assert.Equal(dispatches, invoker.Calls);
		Assert.Equal(0, ports.Calls);
		Assert.Empty(scans.Session.Calls);
		Assert.Equal(0, allocations.Allocations);
	}

	/// <summary>
	///     A refusal of a well-formed request that the Client decides without calling Cheat Engine (a budget, an
	///     unsupported type, a policy, a limit of Cheat Engine's own indexes, a range without room for a match, a
	///     self-parent) never hides an ended or stopping activation: every family checks the activation first, under
	///     the name of its own operation.
	/// </summary>
	[Theory]
	[InlineData("Patterns.Scan", false)]
	[InlineData("Patterns.Scan", true)]
	[InlineData("Memory.ReadPrimitive", false)]
	[InlineData("Memory.ReadBytes", true)]
	[InlineData("Tables.SetParent", false)]
	[InlineData("Tables.LoadTrustedTable", true)]
	[InlineData("ValueScans.Read", false)]
	[InlineData("ValueScans.Read", true)]
	[InlineData("UnsafeLua.Execute", false)]
	[InlineData("AutoAssembler.ApplyPatch", true)]
	public void AnEndedOrStoppingActivationThrowsBeforeARequestIsRefused(string entryPoint, bool stopping)
	{
		using ControlledCoreLifetimeContext context = new();
		CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		ThrowingPorts ports = new(new InvalidOperationException("never reached"));
		FakeValueScanPort scans = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		IValueScanSession session = new ValueScanner(dispatcher, Binder(dispatcher), scans).CreateSession(token);
		MemoryClient memory = new(dispatcher, lifetime, ports);
		CoreClientPolicy withoutOptIns = new([], enableUnsafeLuaExecution: false);
		TableClient tables = new(dispatcher, withoutOptIns, ports, lifetime, ports);
		MemoryRecordId record = new(7);
		if (stopping)
		{
			context.Stop();
		}
		else
		{
			context.IsCurrent = false;
		}

		Action refused = entryPoint switch
		{
			"Patterns.Scan" => () => _ = new PatternScanner(dispatcher, ports).TryScan(NoRoomForAMatch(), out _,
				out _, token),
			"Memory.ReadPrimitive" => () => _ = memory.TryReadPrimitive(Target, out decimal _, out _, token),
			"Memory.ReadBytes" => () => _ = memory.TryReadBytes(
				new MemoryBytesReadRequest(Target, MemoryResourceLimits.DefaultMaximumReadBytes + 1), out _, out _,
				token),
			"Tables.SetParent" => () => _ = tables.TrySetParent(record, record, out _, out _, token),
			"Tables.LoadTrustedTable" => () => _ = tables.TryLoadTrustedTable(new TableLoadRequest(
				new TrustedTableFile(Path.Combine(Path.GetTempPath(), "untrusted.ct"))), out _, token),
			"ValueScans.Read" => () => _ = session.TryRead(new ValueScanReadRequest((long) int.MaxValue + 1, 1),
				out _, out _, token),
			"UnsafeLua.Execute" => () => _ = new UnsafeLuaClient(dispatcher, withoutOptIns, lifetime)
				.TryExecute(new LuaScript("return 1"), out _, token),
			"AutoAssembler.ApplyPatch" => () => _ = new AutoAssemblerClient(dispatcher, withoutOptIns, lifetime,
					Binder(dispatcher), ports)
				.TryApplyPatch(new AutoAssemblerScript("[ENABLE]"), out _, out _, token),
			_ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
		};

		CheatEngineClientException thrown = stopping
			? Assert.Throws<CheatEngineInvalidStateException>(refused)
			: Assert.Throws<CheatEngineActivationExpiredException>(refused);
		Assert.Equal(entryPoint, thrown.Failure.Operation);
		Assert.Equal(CheatEngineHostEffect.NotStarted, thrown.Failure.HostEffect);
		Assert.Equal(0, ports.Calls);
		Assert.Empty(scans.Session.Calls);
	}

	/// <summary>
	///     Creating a lease-owned resource is admitted only while the activation is active, never from the deactivation
	///     cleanup scope that can still release an existing lease: every lease-creating operation throws there before it
	///     dispatches anything.
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q43")]
	[InlineData("Inspection.RegisterSymbol")]
	[InlineData("Lua.RegisterModule")]
	[InlineData("ValueScans.CreateSession")]
	[InlineData("Allocations.Allocate")]
	[InlineData("AutoAssembler.ApplyPatch")]
	public void NoLeaseIsCreatedFromTheDeactivationCleanupScope(string entryPoint)
	{
		using ControlledCoreLifetimeContext context = new();
		CoreLifetime lifetime = new(context);
		CountingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		ThrowingPorts ports = new(new InvalidOperationException("never reached"));
		FakeValueScanPort scans = new();
		FakeAllocationPort allocations = new();
		CancellationToken token = TestContext.Current.CancellationToken;
		Action create = entryPoint switch
		{
			"Inspection.RegisterSymbol" => () => new InspectionClient(dispatcher, lifetime, ports)
				.TryRegisterSymbol(new SymbolRegistration("contractSymbol", Target), out _, out _, token),
			"Lua.RegisterModule" => () => new LuaClient(dispatcher, lifetime)
				.TryRegisterModule(new PortBackedModule(ports), out _, out _, token),
			"ValueScans.CreateSession" => () => new ValueScanner(dispatcher, Binder(dispatcher), scans)
				.TryCreateSession(out _, out _, token),
			"Allocations.Allocate" => () => new AllocationClient(dispatcher, Binder(dispatcher), allocations)
				.TryAllocate(new AllocationRequest(4096), out _, out _, token),
			"AutoAssembler.ApplyPatch" => () => new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(), lifetime,
					Binder(dispatcher), ports)
				.TryApplyPatch(new AutoAssemblerScript("[ENABLE]"), out _, out _, token),
			_ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
		};

		context.Stop();
		using (lifetime.EnterCleanupScope())
		{
			CheatEngineInvalidStateException refused = Assert.Throws<CheatEngineInvalidStateException>(create);
			Assert.Equal(entryPoint, refused.Failure.Operation);
		}

		Assert.Equal(0, invoker.Calls);
		Assert.Equal(0, ports.Calls);
		Assert.Equal(0, scans.Creations);
		Assert.Equal(0, allocations.Allocations);
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
			"Patterns.Scan.InModule" => Run(new PatternScanner(dispatcher, ports), Request(new ModuleName("game.exe")),
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
			"Inspection.ResolveName" => Run(new InspectionClient(dispatcher, lifetime, ports), Target,
				static (client, address, t) => (client.TryResolveName(address, out _, out CheatEngineFailure f, t), f),
				static (client, address, t) => client.ResolveName(address, t), token),
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
			"Processes.GetCurrentProcess" => Run(new ProcessClient(dispatcher, ports, ports, ports, lifetime), 0,
				static (client, _, t) =>
					(client.TryGetCurrentProcess(out ProcessSnapshot _, out CheatEngineFailure f, t), f),
				static (client, _, t) => client.GetCurrentProcess(t), token),
			"Processes.Attach" => Run(new ProcessClient(dispatcher, ports, ports, ports, lifetime), new TargetProcessId(43),
				static (client, processId, t) => (client.TryAttach(processId, out _, out CheatEngineFailure f, t), f),
				static (client, processId, t) => client.Attach(processId, t), token),
			"Runtime.GetSnapshot" => Run(new RuntimeClient(dispatcher, ports, static () => 1), 0,
				static (client, _, t) => (client.TryGetSnapshot(out CheatEngineRuntimeSnapshot _, out CheatEngineFailure f, t), f),
				static (client, _, t) => client.GetSnapshot(t), token),
			// A generated module's Register surfaces the SDK faults of its registration; the Client classifies them.
			"Lua.RegisterModule" => Run(new LuaClient(dispatcher, lifetime), new PortBackedModule(ports),
				static (client, module, t) => (client.TryRegisterModule(module, out _, out CheatEngineFailure f, t), f),
				static (client, module, t) => client.RegisterModule(module, t), token),
			"ValueScans.CreateSession" => Run(
				new ValueScanner(dispatcher, Binder(dispatcher), new FakeValueScanPort { Fault = fault }), 0,
				static (scanner, _, t) => (scanner.TryCreateSession(out IValueScanSession? _, out CheatEngineFailure f, t), f),
				static (scanner, _, t) => scanner.CreateSession(t), token),
			"ValueScans.GetResultCount" => Run(CreateScanSession(dispatcher, fault, token), 0,
				static (session, _, t) => (session.TryGetResultCount(out ulong _, out CheatEngineFailure f, t), f),
				static (session, _, t) => session.GetResultCount(t), token),
			"Allocations.Allocate" => Run(
				new AllocationClient(dispatcher, Binder(dispatcher), new FakeAllocationPort { Fault = fault }),
				new AllocationRequest(4096),
				static (client, request, t) => (client.TryAllocate(request, out ITargetMemoryLease? _,
					out CheatEngineFailure f, t), f),
				static (client, request, t) => client.Allocate(request, t), token),
			"AutoAssembler.ApplyPatch" => Run(
				new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(), lifetime, Binder(dispatcher), ports),
				new AutoAssemblerScript("[ENABLE]"),
				static (client, script, t) => (client.TryApplyPatch(script, out _, out CheatEngineFailure f, t), f),
				static (client, script, t) => client.ApplyPatch(script, t), token),
			"AutoAssembler.Check" => Run(
				new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(), lifetime, Binder(dispatcher), ports),
				new AutoAssemblerScript("[ENABLE]"),
				static (client, script, t) => (client.TryCheck(script, out _, out CheatEngineFailure f, t), f),
				static (client, script, t) => client.Check(script, t), token),
			"Assembly.Assemble" => Run(new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports),
				new AssemblyInstructionRequest(Target, "nop"),
				static (client, request, t) => (client.TryAssemble(request, out _, out CheatEngineFailure f, t), f),
				static (client, request, t) => client.Assemble(request, t), token),
			"Assembly.Disassemble" => Run(new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports),
				Target,
				static (client, address, t) => (client.TryDisassemble(address, out _, out CheatEngineFailure f, t), f),
				static (client, address, t) => client.Disassemble(address, t), token),
			"Assembly.GetInstructionLength" => Run(
				new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports), Target,
				static (client, address, t) =>
					(client.TryGetInstructionLength(address, out _, out CheatEngineFailure f, t), f),
				static (client, address, t) => client.GetInstructionLength(address, t), token),
			"Assembly.GetPreviousInstructionAddress" => Run(
				new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports), Target,
				static (client, address, t) =>
					(client.TryGetPreviousInstructionAddress(address, out _, out CheatEngineFailure f, t), f),
				static (client, address, t) => client.GetPreviousInstructionAddress(address, t), token),
			_ => throw new ArgumentOutOfRangeException(nameof(entryPoint), entryPoint, null)
		};

		Assert.False(succeeded);
		Assert.Equal(expectedKind, failure.Kind);
		// An entry point is named after the operation it reports, with an optional qualifier ("Patterns.Scan.InModule").
		Assert.Equal(string.Join('.', entryPoint.Split('.').Take(2)), failure.Operation);
		Assert.Same(fault, failure.Exception);
		Assert.NotEqual(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		CheatEngineClientException thrown = Assert.ThrowsAny<CheatEngineClientException>(throwingForm);
		Assert.Same(fault, thrown.Failure.Exception);
		Assert.Equal(expectedKind, thrown.Failure.Kind);
	}

	/// <summary>
	///     The scoped AOB route reaches two more SDK calls after module resolution: the target observation that selects the
	///     route, and the bounded scan itself. Neither lets an SDK fault cross a Try method, and each keeps the classified
	///     kind, the scan operation and its own host effect.
	/// </summary>
	[Theory]
	[MemberData(nameof(ScopedAobFaultCases))]
	public void SdkExceptionsFromTheScopedAobRouteNeverCrossATryMethod(string faultType, string stage)
	{
		Exception fault = CreateSdkFault(faultType);
		CheatEngineFailureKind expectedKind = SdkFaultKinds[faultType];
		ThrowingPorts ports = new(fault)
		{
			ModuleSnapshot = [new ModuleInfo("game.exe", new Address(0x4000), new MemorySize(0x100), true, "game.exe")],
			Selection = stage == "TryScanWithinBounds" ? AobHosts.Local() : null
		};
		PatternScanner scanner = new(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), ports);
		AobScanRequest request = Request(new ModuleName("game.exe"));
		CancellationToken token = TestContext.Current.CancellationToken;

		bool succeeded = scanner.TryScan(request, out _, out CheatEngineFailure failure, token);

		Assert.False(succeeded);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Same(fault, failure.Exception);
		Assert.Equal(stage == "TryScanWithinBounds" ? CheatEngineHostEffect.Unknown : CheatEngineHostEffect.NotStarted,
			failure.HostEffect);
		CheatEngineClientException thrown = Assert.ThrowsAny<CheatEngineClientException>(() =>
			scanner.Scan(request, token));
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
			ValueScanner scans = new(dispatcher, Binder(dispatcher));
			AllocationClient allocations = new(dispatcher, Binder(dispatcher));
			AutoAssemblerClient autoAssembler = new(dispatcher, AutoAssemblerPolicy(), lifetime, Binder(dispatcher));
			AssemblyClient instructions = new(dispatcher, lifetime, new MemoryResourceLimits());
			CancellationToken token = TestContext.Current.CancellationToken;

			// No Lua runtime is attached in unit tests: every SDK static below throws InvalidOperationException, and the
			// SDK's own admission reports Detached. CheatTableFiles.TryLoad behind the trusted table load.
			Assert.False(tables.TryLoadTrustedTable(new TableLoadRequest(new TrustedTableFile(tablePath)),
				out CheatEngineFailure loadFailure, token));
			// AddressListMutations.Delete, which acquires its Lua operation itself, behind the record delete.
			Assert.False(tables.TryDelete(new MemoryRecordId(7), out CheatEngineFailure deleteFailure, token));
			Assert.False(inspection.TryRegisterSymbol(new SymbolRegistration("contractSymbol", Target),
				out ISymbolRegistrationLease? lease, out CheatEngineFailure registerFailure, token));
			// SymbolRegistry.TryGetName behind the symbol-name lookup.
			Assert.False(inspection.TryResolveName(Target, out string? name, out CheatEngineFailure nameFailure, token));
			Assert.False(memory.TryReadBytes(new MemoryBytesReadRequest(Target, 8), out ImmutableArray<byte> bytes,
				out CheatEngineFailure readFailure, token));
			// The counted TargetMemory.TryReadBytes overload behind the prefix-reporting read.
			MemoryBytesReadOutcome detailed = memory.ReadBytesDetailed(new MemoryBytesReadRequest(Target, 8), token);
			// The global route: AobScanner.TryScanOutcome with its target context.
			Assert.False(patterns.TryScan(Request(), out _, out CheatEngineFailure scanFailure, token));
			Assert.False(unsafeLua.TryExecute(new LuaScript("return 1"), out CheatEngineFailure luaFailure, token));
			// MemoryScanSessions.TryCreateWithOutcome, behind the value-scan session factory.
			Assert.False(scans.TryCreateSession(out IValueScanSession? session, out CheatEngineFailure createFailure,
				token));
			// TargetMemoryAllocator.TryAllocate, behind the allocation client.
			Assert.False(allocations.TryAllocate(new AllocationRequest(4096), out ITargetMemoryLease? allocation,
				out CheatEngineFailure allocateFailure, token));
			Assert.False(autoAssembler.TryApplyPatch(new AutoAssemblerScript("[ENABLE]"), out IAutoAssemblerPatchLease? patch,
				out CheatEngineFailure applyFailure, token));
			Assert.False(autoAssembler.TryCheck(new AutoAssemblerScript("[ENABLE]"), out _,
				out CheatEngineFailure checkFailure, token));
			Assert.False(instructions.TryAssemble(new AssemblyInstructionRequest(Target, "nop"),
				out ImmutableArray<byte> assembled, out CheatEngineFailure assembleFailure, token));
			Assert.False(instructions.TryDisassemble(Target, out AssemblyInstructionSnapshot disassembled,
				out CheatEngineFailure disassembleFailure, token));

			Assert.Null(lease);
			Assert.Null(name);
			Assert.Null(session);
			Assert.Null(allocation);
			Assert.True(bytes.IsEmpty);
			Assert.Equal(0, detailed.ConfirmedLength);
			CheatEngineFailure[] failures =
				[
					loadFailure, deleteFailure, registerFailure, nameFailure, readFailure, detailed.Failure!.Value,
					scanFailure, createFailure, allocateFailure
				];
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
			Assert.Equal("UnsafeLua.Execute", luaFailure.Operation);
			// The Auto Assembler port asks for the same admission before AutoAssemblerPatcher runs.
			Assert.Null(patch);
			CheatEngineFailure[] autoAssemblerFailures = [applyFailure, checkFailure];
			Assert.All(
				autoAssemblerFailures,
				static failure =>
				{
					Assert.Null(failure.Exception);
					Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
					Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
				});
			Assert.Equal(["AutoAssembler.ApplyPatch", "AutoAssembler.Check"],
				autoAssemblerFailures.Select(static failure => failure.Operation));
			// The instruction port asks for one admission before the profile observation and every instruction call.
			Assert.True(assembled.IsDefault);
			Assert.Equal(default, disassembled);
			CheatEngineFailure[] instructionFailures = [assembleFailure, disassembleFailure];
			Assert.All(
				instructionFailures,
				static failure =>
				{
					Assert.Null(failure.Exception);
					Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
					Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
				});
			Assert.Equal(["Assembly.Assemble", "Assembly.Disassemble"],
				instructionFailures.Select(static failure => failure.Operation));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public void AnAdmissionRefusedInsideAnSdkAddressListCommandIsARejectionWhileTheActivationIsCurrent()
	{
		// LuaAdmission classifies only the admissions Core asks for itself (unsafe Lua, above). AddressListMutations
		// acquires its own admission and raises a plain InvalidOperationException when it is refused. No Lua runtime is
		// attached in unit tests, so the SDK refuses it as Detached while this Client activation is still current.
		CoreLifetime lifetime = InertCoreLifetime.Create();
		TableClient tables = new(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()),
			new CoreClientPolicy([], false), lifetime: lifetime);

		bool succeeded = tables.TrySetParent(new MemoryRecordId(7), new MemoryRecordId(8), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);
		CheatEngineClientException thrown = Assert.ThrowsAny<CheatEngineClientException>(() =>
			tables.SetParent(new MemoryRecordId(7), new MemoryRecordId(8), TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Equal("Tables.SetParent", failure.Operation);
		Assert.IsType<InvalidOperationException>(failure.Exception);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, thrown.Failure.Kind);
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
	public void TheExternalResetFactIsReadWithoutALuaAdmission()
	{
		// The fact Hosting reads when it deactivates the plugin (event 8) is the SDK's lock-free sticky flag: it needs
		// no Lua admission and no Cheat Engine. No Lua state was ever replaced here.
		Assert.False(SdkBoundary.ExternalStateResetDetected);
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
		Exception codecClientFault = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, "Application.Codec",
			"application-owned client exception").ToException(TestContext.Current.CancellationToken);
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
		Assert.Same(operationFault, Assert.Throws<ConsumerException>(() => lua.TryExecute<ThrowingOperation, int>(
			new ThrowingOperation(operationFault), out _, out _, TestContext.Current.CancellationToken)));
		Assert.Same(callbackFault, Assert.Throws<ConsumerException>(() => dispatcher.TryInvoke(
			() => throw callbackFault, out _, TestContext.Current.CancellationToken)));
	}

	[Theory]
	[InlineData("PatternsPreDispatchCancellation")]
	[InlineData("PatternsNoRoomForAMatch")]
	[InlineData("MemoryBudget")]
	[InlineData("MemoryBatchPreDispatchCancellation")]
	[InlineData("MemoryBytesDetailedBudget")]
	[InlineData("TablesPolicy")]
	[InlineData("UnsafeLuaPolicy")]
	[InlineData("LuaPreDispatchCancellation")]
	[InlineData("ProcessesAttachExactNameCancellation")]
	[InlineData("LuaModuleRegistrationPreDispatchCancellation")]
	[InlineData("ValueScansPreDispatchCancellation")]
	[InlineData("ValueScansResultIndexLimit")]
	[InlineData("AllocationsPreDispatchCancellation")]
	[InlineData("AutoAssemblerPolicy")]
	[InlineData("AutoAssemblerPreDispatchCancellation")]
	[InlineData("InstructionsPreDispatchCancellation")]
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
			"PatternsNoRoomForAMatch" => TryFailure(() => (new PatternScanner(dispatcher, ports).TryScan(
				NoRoomForAMatch(), out _, out CheatEngineFailure f, token), f)),
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
			"LuaPreDispatchCancellation" => TryFailure(() =>
				(new LuaClient(dispatcher, lifetime).TryExecute<ConstantOperation, int>(new ConstantOperation(), out _,
					out CheatEngineFailure f, cancelled), f)),
			"ProcessesAttachExactNameCancellation" => TryFailure(() =>
				(new ProcessClient(dispatcher, ports, ports, ports, lifetime).TryAttachExactName("fixture.exe", out _,
					out CheatEngineFailure f, cancelled), f)),
			"LuaModuleRegistrationPreDispatchCancellation" => TryFailure(() =>
				(new LuaClient(dispatcher, lifetime).TryRegisterModule(new PortBackedModule(ports), out _,
					out CheatEngineFailure f, cancelled), f)),
			"ValueScansPreDispatchCancellation" => TryFailure(() =>
				(new ValueScanner(dispatcher, Binder(dispatcher), new FakeValueScanPort()).TryCreateSession(out _,
					out CheatEngineFailure f, cancelled), f)),
			"ValueScansResultIndexLimit" => TryFailure(() =>
				(new ValueScanner(dispatcher, Binder(dispatcher), new FakeValueScanPort()).CreateSession(token)
					.TryRead(new ValueScanReadRequest((long) int.MaxValue + 1, 1), out _, out CheatEngineFailure f,
						token), f)),
			"AllocationsPreDispatchCancellation" => TryFailure(() =>
				(new AllocationClient(dispatcher, Binder(dispatcher), new FakeAllocationPort())
					.TryAllocate(new AllocationRequest(4096), out _, out CheatEngineFailure f, cancelled), f)),
			"AutoAssemblerPolicy" => TryFailure(() =>
				(new AutoAssemblerClient(dispatcher, new CoreClientPolicy([], false), lifetime, Binder(dispatcher),
						ports)
					.TryApplyPatch(new AutoAssemblerScript("[ENABLE]"), out _, out CheatEngineFailure f, token), f)),
			"AutoAssemblerPreDispatchCancellation" => TryFailure(() =>
				(new AutoAssemblerClient(dispatcher, AutoAssemblerPolicy(), lifetime, Binder(dispatcher), ports)
					.TryCheck(new AutoAssemblerScript("[ENABLE]"), out _, out CheatEngineFailure f, cancelled), f)),
			"InstructionsPreDispatchCancellation" => TryFailure(() =>
				(new AssemblyClient(dispatcher, lifetime, new MemoryResourceLimits(), ports)
					.TryAssemble(new AssemblyInstructionRequest(Target, "nop"), out _, out CheatEngineFailure f,
						cancelled), f)),
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
	[InlineData("Inspection")]
	[InlineData("Tables")]
	[InlineData("Lua")]
	[InlineData("Dispatcher")]
	[InlineData("Processes")]
	[InlineData("Runtime")]
	[InlineData("LuaModuleRegistration")]
	[InlineData("ValueScans")]
	[InlineData("Allocations")]
	[InlineData("Instructions")]
	public void ThrowingFormsRaiseTheCancellationExceptionOfTheirTryForm(string family)
	{
		CoreLifetime lifetime = InertCoreLifetime.Create();
		SdkMainThreadDispatcher dispatcher = new(lifetime, new InlineMainThreadInvoker());
		ThrowingPorts ports = new(new InvalidOperationException("must not be reached"));
		PatternScanner patterns = new(dispatcher, ports);
		MemoryClient memory = new(dispatcher, lifetime, ports);
		InspectionClient inspection = new(dispatcher, lifetime, ports);
		SymbolRegistration registration = new("contractSymbol", Target);
		TableClient tables = new(dispatcher, CoreClientPolicy.SafeDefaults, ports, lifetime, ports);
		LuaClient lua = new(dispatcher, lifetime);
		ProcessClient processes = new(dispatcher, ports, ports, ports, lifetime);
		RuntimeClient runtime = new(dispatcher, ports, static () => 1);
		PortBackedModule module = new(ports);
		ValueScanner scans = new(dispatcher, processes, new FakeValueScanPort());
		AllocationClient allocations = new(dispatcher, processes, new FakeAllocationPort());
		AssemblyClient instructions = new(dispatcher, lifetime, new MemoryResourceLimits(), ports);
		CancellationToken cancelled = new(true);

		(CheatEngineFailure Expected, Action ThrowingForm) scenario = family switch
		{
			"Patterns" => (TryFailure(() => (patterns.TryScan(Request(), out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = patterns.Scan(Request(), cancelled)),
			"Memory" => (TryFailure(() => (memory.TryReadPrimitive(Target, out int _, out CheatEngineFailure f,
				cancelled), f)), () => _ = memory.ReadPrimitive<int>(Target, cancelled)),
			"Inspection" => (TryFailure(() => (inspection.TryRegisterSymbol(registration, out _,
				out CheatEngineFailure f, cancelled), f)), () => _ = inspection.RegisterSymbol(registration, cancelled)),
			"Tables" => (TryFailure(() => (tables.TryGetRecordCount(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = tables.GetRecordCount(cancelled)),
			"Lua" => (TryFailure(() => (lua.TryExecute<ConstantOperation, int>(new ConstantOperation(), out _,
				out CheatEngineFailure f, cancelled), f)),
				() => _ = lua.Execute<ConstantOperation, int>(new ConstantOperation(), cancelled)),
			"Dispatcher" => (TryFailure(() => (dispatcher.TryInvoke(static () =>
				{
				}, out CheatEngineFailure f, cancelled), f)), () => dispatcher.Invoke(static () =>
				{
				}, cancelled)),
			"Processes" => (TryFailure(() => (processes.TryRefresh(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = processes.Refresh(cancelled)),
			"Runtime" => (TryFailure(() => (runtime.TryGetSnapshot(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = runtime.GetSnapshot(cancelled)),
			"LuaModuleRegistration" => (TryFailure(() =>
					(lua.TryRegisterModule(module, out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = lua.RegisterModule(module, cancelled)),
			"ValueScans" => (TryFailure(() => (scans.TryCreateSession(out _, out CheatEngineFailure f, cancelled), f)),
				() => _ = scans.CreateSession(cancelled)),
			"Allocations" => (TryFailure(() => (allocations.TryAllocate(new AllocationRequest(4096), out _,
					out CheatEngineFailure f, cancelled), f)),
				() => _ = allocations.Allocate(new AllocationRequest(4096), cancelled)),
			"Instructions" => (TryFailure(() => (instructions.TryGetInstructionLength(Target, out _,
				out CheatEngineFailure f, cancelled), f)), () => _ = instructions.GetInstructionLength(Target, cancelled)),
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
		Assert.Equal(4, outcome.RequestedCount);
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

	private static CoreClientPolicy AutoAssemblerPolicy()
	{
		return new CoreClientPolicy([], false, enableAutoAssemblerPatches: true);
	}

	private static CheatEngineFailure TryFailure(Func<(bool Succeeded, CheatEngineFailure Failure)> attempt)
	{
		(bool succeeded, CheatEngineFailure failure) = attempt();
		Assert.False(succeeded);
		return failure;
	}

	/// <summary>Creates the selection binder of target-bound leases: a process client over the default selected target.</summary>
	private static ProcessClient Binder(SdkMainThreadDispatcher dispatcher)
	{
		return FakeSelectedTarget.CreateProcessClient(dispatcher);
	}

	/// <summary>Creates a value-scan session whose results are ready and whose result count throws <paramref name="fault" />.</summary>
	private static IValueScanSession CreateScanSession(SdkMainThreadDispatcher dispatcher, Exception fault,
		CancellationToken cancellationToken)
	{
		FakeValueScanPort port = new();
		IValueScanSession session =
			new ValueScanner(dispatcher, Binder(dispatcher), port).CreateSession(cancellationToken);
		session.FirstScan(ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(1)), cancellationToken);
		port.Session.CountFault = fault;
		return session;
	}

	private static AobScanRequest Request(ModuleName? module = null)
	{
		return new AobScanRequest(new AobPattern("90"), 2, module);
	}

	/// <summary>
	///     Creates a well-formed range request whose range leaves no room for a whole match below the top of the
	///     address space: the scanner refuses it without calling Cheat Engine.
	/// </summary>
	private static AobScanRequest NoRoomForAMatch()
	{
		Address top = new(ulong.MaxValue);
		return new AobScanRequest(new AobPattern("90 90"), 1, null, new AobScanRange(top, top));
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
			IRuntimeObservationPort, IProcessSelectionPort, IProcessHost, IAutoAssemblerPort, IInstructionPort
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

		/// <summary>Gets the modules the AOB module enumeration returns instead of faulting.</summary>
		internal ModuleInfo[]? ModuleSnapshot
		{
			get;
			init;
		}

		/// <summary>Gets the selection the target observation returns instead of faulting.</summary>
		internal TargetSelectionFacts? Selection
		{
			get;
			init;
		}

		public AobHostOutcome TryScan(string pattern, AobScanOptions options, out IAobMatchList? matches)
		{
			Calls++;
			OnScan?.Invoke();
			if (ScanResult is { } entries)
			{
				matches = new ListDouble(entries, () => ReleasedLists++);
				return AobHosts.Outcome(AobScanOutcomeKind.Matches, entries.Length);
			}

			throw Fault();
		}

		public AobBoundedHostResult TryScanWithinBounds(string pattern, AobScanBounds bounds, AobScanOptions options,
			Span<Address> destination, CancellationToken cancellationToken)
		{
			throw Fault();
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			if (ModuleSnapshot is { } modules)
			{
				Calls++;
				modules.CopyTo(destination, 0);
				written = modules.Length;
				return InspectionStatus.Success;
			}

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

		/// <summary>
		///     The collision pre-check of a symbol registration finds nothing, so the registration fault is raised by
		///     <see cref="TryRegisterOwned" />, the CheatEngine.SDK ownership coordinator.
		/// </summary>
		public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
			out Address address)
		{
			address = default;
			return InspectionStatus.NotFound;
		}

		public LuaOperationStatus TryGetName(Address address, out string? name)
		{
			throw Fault();
		}

		public SymbolRegistrationAttempt TryRegisterOwned(SymbolName name, Address address,
			SymbolRegistrationOptions options)
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

		public TableRecordMutationOutcome TryDelete(MemoryRecordId id)
		{
			throw Fault();
		}

		public TableRecordMutationOutcome TrySetParent(MemoryRecordId childId, MemoryRecordId? parentId,
			out MemoryRecordSnapshot record)
		{
			throw Fault();
		}

		public TableActivationObservation TrySetActive(MemoryRecordId id, bool requested)
		{
			throw Fault();
		}

		public TableRecordMutationOutcome TrySelect(MemoryRecordId id, out MemoryRecordSnapshot record)
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
			if (Selection is { } selection)
			{
				Calls++;
				return selection;
			}

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

		public bool TryApply(string operation, string script, AutoAssemblerOptions options,
			out AutoAssemblerApplyFacts facts, out IAutoAssemblerPatchOwner? patch,
			out CheatEngineFailure admissionFailure)
		{
			throw Fault();
		}

		public bool TryCheck(string operation, string script, AutoAssemblerOptions options,
			out AutoAssemblerCheckFacts facts, out CheatEngineFailure admissionFailure)
		{
			throw Fault();
		}

		public bool TryRunAdmitted(string operation, Action work, out CheatEngineFailure admissionFailure)
		{
			// The fake admits every call; the fault comes from the first instruction call inside it.
			admissionFailure = default;
			work();
			return true;
		}

		public InstructionOperationStatus ObserveProfile(out InstructionProfileObservation profile)
		{
			throw Fault();
		}

		public InstructionOperationStatus Assemble(InstructionProfileObservation profile, string instruction,
			Address address, AssemblePreference preference, bool skipRangeCheck, Span<byte> destination,
			out int written, out int requiredLength)
		{
			throw Fault();
		}

		public InstructionOperationStatus Disassemble(InstructionProfileObservation profile, Address address,
			int maximumUtf8Bytes, out InstructionDisassembly disassembly)
		{
			throw Fault();
		}

		public InstructionOperationStatus GetLength(InstructionProfileObservation profile, Address address,
			out int length)
		{
			throw Fault();
		}

		public InstructionOperationStatus GetPrevious(InstructionProfileObservation profile, Address address,
			out Address previous)
		{
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

		public TargetReleaseStatus Release()
		{
			onRelease();
			return TargetReleaseStatus.Released;
		}
	}

	/// <summary>An application-owned exception type, distinct from every Client and SDK exception.</summary>
	private sealed class ConsumerException(string message) : Exception(message);

	private sealed class ThrowingCodec(Exception fault) : IMemoryCodec<int>
	{
		public bool TryRead(IMemoryReadContext context, Address address, [MaybeNullWhen(false)] out int value, out CheatEngineFailure failure)
		{
			failure = default;
			throw fault;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in int value, out CheatEngineFailure failure)
		{
			failure = default;
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

	/// <summary>A Lua module whose registration makes one port call, standing for the SDK work of a generated module.</summary>
	private sealed class PortBackedModule(ThrowingPorts ports) : ILuaModule
	{
		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new("contract", [new LuaExportDescriptor("contract_global")]);

		public void Register()
		{
			_ = ports.ObserveSelection();
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			return LuaModuleReleaseOutcome.Released("contract", 1, 0, 0);
		}
	}

	/// <summary>Runs every callback inline and counts the dispatches.</summary>
	private sealed class CountingMainThreadInvoker : IMainThreadInvoker
	{
		private readonly InlineMainThreadInvoker _inner = new();

		public int Calls
		{
			get;
			private set;
		}

		public Exception? Invoke(Action callback)
		{
			Calls++;
			return _inner.Invoke(callback);
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			Calls++;
			return _inner.Invoke(callback);
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
