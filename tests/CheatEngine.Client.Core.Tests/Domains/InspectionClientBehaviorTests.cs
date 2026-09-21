using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class InspectionClientBehaviorTests
{
	[Fact]
	public void GetModulesCopiesOnlyTheWrittenEntriesAndUsesTheRequestedProcessOverload()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new() { ModulesWritten = 1 };
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryGetModules(new InspectionCollectionRequest(2),
			out ImmutableArray<ModuleInfo> modules,
			out CheatEngineFailure failure, new TargetProcessId(42), TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Single(modules);
		Assert.Equal(2, port.LastModuleBufferLength);
		Assert.Equal(new TargetProcessId(42), port.LastModuleProcessId);
		Assert.Equal(0, port.CurrentProcessModuleCalls);
		Assert.Equal(1, port.ExplicitProcessModuleCalls);
	}

	[Theory]
	[InlineData(InspectionStatus.NotFound, CheatEngineFailureKind.NotFound)]
	[InlineData(InspectionStatus.DestinationTooSmall, CheatEngineFailureKind.ResultLimitExceeded)]
	[InlineData(InspectionStatus.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(InspectionStatus.LuaFailure, CheatEngineFailureKind.LuaError)]
	[InlineData(InspectionStatus.InvalidResult, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData((InspectionStatus) 999, CheatEngineFailureKind.Unknown)]
	public void InspectionStatusesMapToStableClientFailures(InspectionStatus status,
		CheatEngineFailureKind expectedKind)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new() { MemoryRegionStatus = status };
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryGetMemoryRegion(new Address(0x1234), out MemoryRegionInfo region,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, region);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal("Inspection.GetMemoryRegion", failure.Operation);
	}

	[Fact]
	public void CollectionAndScalarQueriesForwardInputsAndCopyOnlyTheWrittenEntries()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			SectionsWritten = 1, RegionsWritten = 1, ResolvedAddress = new Address(0xC0FFEE)
		};
		InspectionClient client = CreateClient(lifetime, port);
		ModuleName module = new("fixture.exe");
		SymbolExpression symbol = new("fixture+10");
		AddressResolutionOptions options = new(true, true);

		Assert.True(client.TryGetModuleSections(module, new InspectionCollectionRequest(2),
			out ImmutableArray<ModuleSectionInfo> sections, out CheatEngineFailure sectionsFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryGetMemoryRegions(new InspectionCollectionRequest(2),
			out ImmutableArray<MemoryRegionInfo> regions, out CheatEngineFailure regionsFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryGetSymbol(symbol, out SymbolInfo returnedSymbol, out CheatEngineFailure symbolFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryResolveAddress(symbol, options, out Address resolved,
			out CheatEngineFailure addressFailure,
			TestContext.Current.CancellationToken));

		Assert.Single(sections);
		Assert.Single(regions);
		Assert.Equal(default, sectionsFailure);
		Assert.Equal(default, regionsFailure);
		Assert.Equal(default, returnedSymbol);
		Assert.Equal(default, symbolFailure);
		Assert.Equal(new Address(0xC0FFEE), resolved);
		Assert.Equal(default, addressFailure);
		Assert.Equal(module, port.LastSectionModule);
		Assert.Equal(2, port.LastSectionBufferLength);
		Assert.Equal(2, port.LastRegionBufferLength);
		Assert.Equal(symbol, port.LastSymbolExpression);
		Assert.Equal(symbol, port.LastAddressExpression);
		Assert.Equal(options, port.LastAddressOptions);
	}

	[Theory]
	[InlineData(true, "playerHealth", true, CheatEngineFailureKind.Unknown)]
	[InlineData(false, null, false, CheatEngineFailureKind.NotFound)]
	[InlineData(true, null, false, CheatEngineFailureKind.InvalidHostResult)]
	public void ResolveNameDistinguishesANameFromNotFoundAndInvalidHostResults(bool resolveName, string? name,
		bool expectedSuccess, CheatEngineFailureKind expectedKind)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new() { ResolveNameResult = resolveName, ResolvedName = name };
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryResolveName(new Address(0x1234), out string? result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.Equal(expectedSuccess, succeeded);
		Assert.Equal(name, result);
		Assert.Equal(new nuint(0x1234), port.LastNameAddress);
		if (expectedSuccess)
		{
			Assert.Equal(default, failure);
		}
		else
		{
			Assert.Equal(expectedKind, failure.Kind);
			Assert.Equal("Inspection.ResolveName", failure.Operation);
		}
	}

	[Fact]
	public void RegisterSymbolReservesOneNameAndReleasesItWithTheLease()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		SymbolRegistration registration = new("fixture-symbol", new Address(0x401000));

		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? lease,
			out CheatEngineFailure firstFailure, TestContext.Current.CancellationToken));
		Assert.False(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? duplicate,
			out CheatEngineFailure duplicateFailure, TestContext.Current.CancellationToken));
		Assert.NotNull(lease);
		Assert.Null(duplicate);
		Assert.Equal(default, firstFailure);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, duplicateFailure.Kind);
		Assert.Equal(1, port.RegisterCalls);
		Assert.Equal("fixture-symbol", port.LastRegisteredName);
		Assert.Equal(new nuint(0x401000), port.LastRegisteredAddress);
		Assert.True(port.LastRegisteredDoNotSave);

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? retry,
			out CheatEngineFailure retryFailure, TestContext.Current.CancellationToken));
		Assert.NotNull(retry);
		Assert.Equal(default, retryFailure);
		retry.Dispose();
	}

	[Fact]
	public void CancelledInspectionDoesNotContactTheSdkPort()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		bool succeeded = client.TryGetModules(new InspectionCollectionRequest(1),
			out ImmutableArray<ModuleInfo> modules,
			out CheatEngineFailure failure, cancellationToken: cancellation.Token);

		Assert.False(succeeded);
		Assert.Empty(modules);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, port.CurrentProcessModuleCalls);
		Assert.Equal(0, port.ExplicitProcessModuleCalls);
	}

	private static InspectionClient CreateClient(CoreLifetime lifetime, IInspectionPort port)
	{
		return new InspectionClient(new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker()), lifetime,
			port);
	}

	private sealed class FakeInspectionPort : IInspectionPort
	{
		internal int CurrentProcessModuleCalls
		{
			get;
			private set;
		}

		internal int ExplicitProcessModuleCalls
		{
			get;
			private set;
		}

		internal int LastModuleBufferLength
		{
			get;
			private set;
		}

		internal TargetProcessId? LastModuleProcessId
		{
			get;
			private set;
		}

		internal int SectionsWritten
		{
			get;
			init;
		}

		internal int RegionsWritten
		{
			get;
			init;
		}

		internal int LastSectionBufferLength
		{
			get;
			private set;
		}

		internal int LastRegionBufferLength
		{
			get;
			private set;
		}

		internal ModuleName LastSectionModule
		{
			get;
			private set;
		}

		internal SymbolExpression LastSymbolExpression
		{
			get;
			private set;
		}

		internal SymbolExpression LastAddressExpression
		{
			get;
			private set;
		}

		internal AddressResolutionOptions LastAddressOptions
		{
			get;
			private set;
		}

		internal InspectionStatus ModulesStatus
		{
			get;
		} = InspectionStatus.Success;

		internal InspectionStatus MemoryRegionStatus
		{
			get;
			init;
		} = InspectionStatus.Success;

		internal Address ResolvedAddress
		{
			get;
			init;
		}

		internal bool ResolveNameResult
		{
			get;
			init;
		}

		internal string? ResolvedName
		{
			get;
			init;
		}

		internal nuint LastNameAddress
		{
			get;
			private set;
		}

		internal int RegisterCalls
		{
			get;
			private set;
		}

		internal string? LastRegisteredName
		{
			get;
			private set;
		}

		internal nuint LastRegisteredAddress
		{
			get;
			private set;
		}

		internal bool LastRegisteredDoNotSave
		{
			get;
			private set;
		}

		internal List<string> UnregisteredNames
		{
			get;
		} = [];

		public int ModulesWritten
		{
			get;
			init;
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			CurrentProcessModuleCalls++;
			LastModuleBufferLength = destination.Length;
			written = ModulesWritten;
			return ModulesStatus;
		}

		public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written)
		{
			ExplicitProcessModuleCalls++;
			LastModuleProcessId = processId;
			LastModuleBufferLength = destination.Length;
			written = ModulesWritten;
			return ModulesStatus;
		}

		public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination,
			out int written)
		{
			LastSectionModule = moduleName;
			LastSectionBufferLength = destination.Length;
			written = SectionsWritten;
			return InspectionStatus.Success;
		}

		public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written)
		{
			LastRegionBufferLength = destination.Length;
			written = RegionsWritten;
			return InspectionStatus.Success;
		}

		public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region)
		{
			region = default;
			return MemoryRegionStatus;
		}

		public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol)
		{
			LastSymbolExpression = expression;
			symbol = default;
			return InspectionStatus.Success;
		}

		public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionOptions options,
			out Address address)
		{
			LastAddressExpression = expression;
			LastAddressOptions = options;
			address = ResolvedAddress;
			return InspectionStatus.Success;
		}

		public bool TryResolveName(nuint address, out string? name)
		{
			LastNameAddress = address;
			name = ResolvedName;
			return ResolveNameResult;
		}

		public void RegisterSymbol(string name, nuint address, bool doNotSave)
		{
			RegisterCalls++;
			LastRegisteredName = name;
			LastRegisteredAddress = address;
			LastRegisteredDoNotSave = doNotSave;
		}

		public void UnregisterSymbol(string name)
		{
			UnregisteredNames.Add(name);
		}
	}
}
