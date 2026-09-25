using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class InspectionClientBehaviorTests
{
	[Fact]
	public void GetModulesCopiesOnlyTheWrittenEntriesAndUsesTheRequestedProcessOverload()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			ModulesWritten = 1
		};
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryGetModules(new InspectionCollectionRequest(2), new TargetProcessId(42),
			out ImmutableArray<ModuleInfo> modules, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

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
	[InlineData((InspectionStatus) 999, CheatEngineFailureKind.IndeterminateHostResult)]
	public void InspectionStatusesMapToStableClientFailures(InspectionStatus status,
		CheatEngineFailureKind expectedKind)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			MemoryRegionStatus = status
		};
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
			SectionsWritten = 1,
			RegionsWritten = 1,
			ResolvedAddress = new Address(0xC0FFEE)
		};
		InspectionClient client = CreateClient(lifetime, port);
		ModuleName module = new("fixture.exe");
		SymbolExpression symbol = new("fixture+10");

		Assert.True(client.TryGetModuleSections(module, new InspectionCollectionRequest(2),
			out ImmutableArray<ModuleSectionInfo> sections, out CheatEngineFailure sectionsFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryGetMemoryRegions(new InspectionCollectionRequest(2),
			out ImmutableArray<MemoryRegionInfo> regions, out CheatEngineFailure regionsFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryGetSymbol(symbol, out SymbolInfo returnedSymbol, out CheatEngineFailure symbolFailure,
			TestContext.Current.CancellationToken));
		Assert.True(client.TryResolveAddress(symbol, AddressResolutionMode.Shallow, out Address resolved,
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
		Assert.Equal(AddressResolutionMode.Shallow, port.LastAddressMode);
	}

	[Fact]
	public void AnUndefinedResolutionModeIsAProgrammingErrorThatNeverReachesCheatEngine()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			ResolvedAddress = new Address(0xC0FFEE)
		};
		InspectionClient client = CreateClient(lifetime, port);
		SymbolExpression symbol = new("fixture+10");

		ArgumentOutOfRangeException tryForm = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.TryResolveAddress(symbol, (AddressResolutionMode) 2, out _, out _,
				TestContext.Current.CancellationToken));
		ArgumentOutOfRangeException throwingForm = Assert.Throws<ArgumentOutOfRangeException>(() =>
			client.ResolveAddress(symbol, (AddressResolutionMode) (-1), TestContext.Current.CancellationToken));

		Assert.Equal("mode", tryForm.ParamName);
		Assert.Equal("mode", throwingForm.ParamName);
		Assert.Equal(default, port.LastAddressExpression);
	}

	[Theory]
	[InlineData(LuaOperationStatusKind.Success, "playerHealth", true, CheatEngineFailureKind.Unknown)]
	[InlineData(LuaOperationStatusKind.NilResult, null, false, CheatEngineFailureKind.NotFound)]
	[InlineData(LuaOperationStatusKind.Success, null, false, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(LuaOperationStatusKind.GlobalUnavailable, null, false, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(LuaOperationStatusKind.LuaFailure, null, false, CheatEngineFailureKind.LuaError)]
	[InlineData(LuaOperationStatusKind.Unknown, null, false, CheatEngineFailureKind.IndeterminateHostResult)]
	public void ResolveNameMapsEverySdkLookupStatus(LuaOperationStatusKind status, string? name,
		bool expectedSuccess, CheatEngineFailureKind expectedKind)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			NameStatus = StatusOf(status),
			ResolvedName = name
		};
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryResolveName(new Address(0x1234), out string? result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.Equal(expectedSuccess, succeeded);
		Assert.Equal(expectedSuccess ? name : null, result);
		Assert.Equal(new Address(0x1234), port.LastNameAddress);
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
	[Trait("Qualification", "Q16.b")]
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
		Assert.Equal(CheatEngineHostEffect.NotStarted, duplicateFailure.HostEffect);
		Assert.Contains("already owns", duplicateFailure.Message, StringComparison.Ordinal);
		Assert.Equal(1, port.RegisterCalls);
		Assert.Equal("fixture-symbol", port.LastRegisteredName);
		Assert.Equal(new Address(0x401000), port.LastRegisteredAddress);
		Assert.True(port.LastRegisteredDoNotSave);
		Assert.Equal("fixture-symbol", lease.Name);
		Assert.Equal(new Address(0x401000), lease.Address);

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			lease.LastReleaseOutcome);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? retry,
			out CheatEngineFailure retryFailure, TestContext.Current.CancellationToken));
		Assert.NotNull(retry);
		Assert.Equal(default, retryFailure);
		retry.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q16")]
	public void RegisterSymbolRejectsANameThatAlreadyResolvesBeforeAnyRegistration()
	{
		// A14-39: registerSymbol would shadow or replace a definition that already resolves (a third-party symbol, a
		// module, or an expression that parses as an address); CheatEngine.SDK keeps that behavior, so the Client checks.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		port.Symbols["thirdPartySymbol"] = new Address(0x500000);
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryRegisterSymbol(new SymbolRegistration("thirdPartySymbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Contains("already resolves", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, port.RegisterCalls);
		Assert.Equal(new SymbolExpression("thirdPartySymbol"), port.LastAddressExpression);
		Assert.Equal(AddressResolutionMode.Default, port.LastAddressMode);
		Assert.Equal(new Address(0x500000), port.Symbols["thirdPartySymbol"]);
	}

	[Theory]
	[InlineData(InspectionStatus.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable)]
	[InlineData(InspectionStatus.LuaFailure, CheatEngineFailureKind.LuaError)]
	[InlineData(InspectionStatus.InvalidResult, CheatEngineFailureKind.InvalidHostResult)]
	public void RegisterSymbolRejectsWhenTheCollisionLookupFails(InspectionStatus status,
		CheatEngineFailureKind expectedKind)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			ResolveStatusOverride = status
		};
		InspectionClient client = CreateClient(lifetime, port);
		SymbolRegistration registration = new("fixture-symbol", new Address(0x401000));

		bool succeeded = client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Contains("ownership of the name cannot be established", failure.Message, StringComparison.Ordinal);
		Assert.Equal(0, port.RegisterCalls);

		port.ResolveStatusOverride = null;
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? retry, out _,
			TestContext.Current.CancellationToken), "A failed check releases the activation-local reservation.");
		retry!.Dispose();
	}

	[Fact]
	public void RegisterSymbolReservationIsCaseInsensitive()
	{
		// Cheat Engine's case rules for user symbols are not established, so the reservation is conservative.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);

		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("PlayerHealth", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));
		bool duplicate = client.TryRegisterSymbol(new SymbolRegistration("playerhealth", new Address(0x402000)),
			out ISymbolRegistrationLease? second, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(duplicate);
		Assert.Null(second);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(1, port.RegisterCalls);
		lease!.Dispose();
	}

	[Theory]
	[Trait("Qualification", "Q16.b")]
	[InlineData(LuaOperationStatusKind.GlobalUnavailable, CheatEngineFailureKind.CapabilityUnavailable,
		CheatEngineHostEffect.Unknown)]
	[InlineData(LuaOperationStatusKind.LuaFailure, CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Started)]
	[InlineData(LuaOperationStatusKind.StackUnavailable, CheatEngineFailureKind.LuaError,
		CheatEngineHostEffect.NotStarted)]
	[InlineData(LuaOperationStatusKind.InvalidResult, CheatEngineFailureKind.InvalidHostResult,
		CheatEngineHostEffect.Started)]
	[InlineData(LuaOperationStatusKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult,
		CheatEngineHostEffect.Unknown)]
	[InlineData(LuaOperationStatusKind.Success, CheatEngineFailureKind.IndeterminateHostResult,
		CheatEngineHostEffect.CleanupUnconfirmed)]
	public void ARegistrationWithoutAnSdkLeaseOwnsNothingAndFreesTheReservation(LuaOperationStatusKind status,
		CheatEngineFailureKind expectedKind, CheatEngineHostEffect expectedEffect)
	{
		// Success without a lease breaks the SDK acquisition contract: Cheat Engine accepted a registration that nothing
		// owns.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			RegistrationStatus = StatusOf(status),
			OmitHandle = true
		};
		InspectionClient client = CreateClient(lifetime, port);
		SymbolRegistration registration = new("fixture-symbol", new Address(0x401000));

		bool succeeded = client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
		Assert.Equal("Inspection.RegisterSymbol", failure.Operation);
		Assert.DoesNotContain("fixture-symbol", failure.Message, StringComparison.Ordinal);
		Assert.Equal(1, port.RegisterCalls);

		port.RegistrationStatus = LuaOperationStatus.Success;
		port.OmitHandle = false;
		port.Symbols.Remove("fixture-symbol");
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? retry, out _,
			TestContext.Current.CancellationToken), "A registration without a lease releases the reservation.");
		retry!.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void AHandoffFailureIsReportedAsAnUnconfirmedCleanupAndOwnsNothing()
	{
		// CheatEngine.SDK registered the name, could not publish its lease and compensated once: the Client never claims
		// the registration and never retries an unregistration by name.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		SymbolRegistrationHandoffException handoff = new(default, new InvalidOperationException("publication failed"));
		FakeInspectionPort port = new()
		{
			RegistrationFault = handoff
		};
		InspectionClient client = CreateClient(lifetime, port);
		SymbolRegistration registration = new("fixture-symbol", new Address(0x401000));

		bool succeeded = client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? lease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.BindingError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal("Inspection.RegisterSymbol", failure.Operation);
		Assert.Same(handoff, failure.Exception);
		Assert.Empty(port.UnregisteredNames);
		CheatEngineOperationException thrown = Assert.Throws<CheatEngineOperationException>(() =>
			client.RegisterSymbol(registration, TestContext.Current.CancellationToken));
		Assert.Same(handoff, thrown.Failure.Exception);

		port.RegistrationFault = null;
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? retry, out _,
			TestContext.Current.CancellationToken), "A handoff failure releases the activation-local reservation.");
		retry!.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void LeaseUnregistersWhenTheNameStillMapsToTheLeasedAddress()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome repeated = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), outcome);
		Assert.Equal(outcome, repeated);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
		Assert.Equal(1, port.ReleaseCalls);
		Assert.True(lease.IsReleased);
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void LeaseSkipsUnregisterWhenTheNameWasReplacedByAThirdParty()
	{
		// A14-25: a name that a third party re-registered at another address is never removed by the Client.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));
		port.Symbols["fixture-symbol"] = new Address(0x777000);

		LeaseReleaseOutcome outcome = lease.Release();
		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Replaced, CheatEngineHostEffect.NotStarted), outcome);
		Assert.Empty(port.UnregisteredNames);
		Assert.Equal(new Address(0x777000), port.Symbols["fixture-symbol"]);
		Assert.True(lease.IsReleased);
	}

	[Fact]
	public void LeaseReportsExternallyRemovedWhenTheNameNoLongerResolves()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));
		port.Symbols.Remove("fixture-symbol");

		LeaseReleaseOutcome outcome = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.ExternallyRemoved, CheatEngineHostEffect.NotStarted),
			outcome);
		Assert.Empty(port.UnregisteredNames);
		Assert.True(lease.IsReleased);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? again, out _, TestContext.Current.CancellationToken));
		again!.Dispose();
	}

	[Fact]
	[Trait("Qualification", "Q16.b")]
	public void ACoordinatorSupersededLeaseLeavesTheNewerRegistrationAndFreesTheActivationReservation()
	{
		// The activation-local reservation refuses a second registration of the name before CheatEngine.SDK is reached,
		// so only another owner that registers the name through the same SDK coordinator can supersede the lease. The
		// superseded lease sends no unregistration, and once it ended the collision pre-check, not the reservation,
		// protects the newer registration.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		SymbolRegistration registration = new("fixture-symbol", new Address(0x401000));
		Assert.True(client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? lease, out _,
			TestContext.Current.CancellationToken));
		Assert.False(client.TryRegisterSymbol(registration, out _, out CheatEngineFailure reserved,
			TestContext.Current.CancellationToken));
		port.RegisterThroughCoordinator("fixture-symbol", new Address(0x777000));

		LeaseReleaseOutcome outcome = lease.Release();
		bool registeredAgain = client.TryRegisterSymbol(registration, out ISymbolRegistrationLease? again,
			out CheatEngineFailure collision, TestContext.Current.CancellationToken);

		Assert.Contains("already owns", reserved.Message, StringComparison.Ordinal);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Superseded, CheatEngineHostEffect.NotStarted), outcome);
		Assert.True(outcome.IsComplete);
		Assert.True(lease.IsReleased);
		Assert.Empty(port.UnregisteredNames);
		Assert.Equal(new Address(0x777000), port.Symbols["fixture-symbol"]);
		Assert.False(registeredAgain);
		Assert.Null(again);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, collision.Kind);
		Assert.Contains("already resolves", collision.Message, StringComparison.Ordinal);
		Assert.Equal(1, port.RegisterCalls);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void LeaseWhoseOwnershipCheckFailsStaysActiveUntilACleanupRetrySucceeds()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));
		port.ResolveStatusOverride = InspectionStatus.LuaFailure;

		lease.Dispose();
		LeaseReleaseOutcome? unavailable = lease.LastReleaseOutcome;
		bool releasedWhileUnavailable = lease.IsReleased;
		port.ResolveStatusOverride = null;
		LeaseReleaseOutcome retried = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			unavailable);
		Assert.False(releasedWhileUnavailable);
		Assert.Equal(LeaseReleaseKind.Released, retried.Kind);
		Assert.True(lease.IsReleased);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void TheDeactivationCleanupReleasesALeaseTheApplicationKept()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new();
		InspectionClient client = CreateClient(lifetime, port);
		Assert.True(client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out _, TestContext.Current.CancellationToken));

		context.Stop();
		using (lifetime.EnterCleanupScope())
		{
			lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.True(lease.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
			lease.LastReleaseOutcome);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void ARegistrationWhoseActivationStopsBeforeItIsOwnedIsReleasedOnceOnTheMainThread()
	{
		// The lease joins the activation inside the dispatched registration; when the activation began stopping in
		// between, nothing would own the registration, so it is released once in the same main-thread call.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			OnRegister = context.Stop
		};
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Inspection.RegisterSymbol", failure.Operation);
		Assert.IsType<CheatEngineInvalidStateException>(failure.Exception);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
		Assert.Equal(1, port.ReleaseCalls);
		Assert.False(port.Symbols.ContainsKey("fixture-symbol"));
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void ARegistrationWhoseActivationResourcesWereDrainedIsReleasedOnceOnTheMainThread()
	{
		// The activation is still current, but its resources were drained before the lease could join them: the
		// closed registry refuses the lease with an ObjectDisposedException, so nothing would own the registration.
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		FakeInspectionPort port = new()
		{
			OnRegister = () =>
			{
				using (lifetime.EnterCleanupScope())
				{
					lifetime.DrainOwnedResourcesForDisable();
				}
			}
		};
		InspectionClient client = CreateClient(lifetime, port);

		bool succeeded = client.TryRegisterSymbol(new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			out ISymbolRegistrationLease? lease, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal("Inspection.RegisterSymbol", failure.Operation);
		Assert.IsType<ObjectDisposedException>(failure.Exception);
		Assert.Equal(["fixture-symbol"], port.UnregisteredNames);
		Assert.Equal(1, port.ReleaseCalls);
		Assert.False(port.Symbols.ContainsKey("fixture-symbol"));
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

		bool succeeded = client.TryGetModules(new InspectionCollectionRequest(1), null,
			out ImmutableArray<ModuleInfo> modules, out CheatEngineFailure failure, cancellation.Token);

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

	private static LuaOperationStatus StatusOf(LuaOperationStatusKind kind)
	{
		return kind switch
		{
			LuaOperationStatusKind.Success => LuaOperationStatus.Success,
			LuaOperationStatusKind.GlobalUnavailable => LuaOperationStatus.GlobalUnavailable,
			LuaOperationStatusKind.LuaFailure => LuaOperationStatus.LuaFailure(LuaStatus.RuntimeError),
			LuaOperationStatusKind.NilResult => LuaOperationStatus.NilResult,
			LuaOperationStatusKind.InvalidResult => LuaOperationStatus.InvalidResult,
			LuaOperationStatusKind.StackUnavailable => LuaOperationStatus.StackUnavailable,
			LuaOperationStatusKind.MissingResult => LuaOperationStatus.MissingResult,
			LuaOperationStatusKind.ResultCapacityExceeded => LuaOperationStatus.ResultCapacityExceeded,
			_ => default
		};
	}

	private sealed class FakeInspectionPort : IInspectionPort
	{
		private readonly Dictionary<string, Registration> _current = new(StringComparer.Ordinal);

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

		internal AddressResolutionMode LastAddressMode
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

		internal LuaOperationStatus NameStatus
		{
			get;
			init;
		}

		internal string? ResolvedName
		{
			get;
			init;
		}

		internal Address LastNameAddress
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

		internal Address LastRegisteredAddress
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

		/// <summary>Gets the number of SDK lease releases the Client requested.</summary>
		internal int ReleaseCalls
		{
			get;
			private set;
		}

		/// <summary>Gets or sets the registration status the fake SDK coordinator reports.</summary>
		internal LuaOperationStatus RegistrationStatus
		{
			get;
			set;
		} = LuaOperationStatus.Success;

		/// <summary>Gets or sets whether a registration returns no lease, whatever its status.</summary>
		internal bool OmitHandle
		{
			get;
			set;
		}

		/// <summary>Gets or sets the exception the fake SDK coordinator raises from the registration.</summary>
		internal Exception? RegistrationFault
		{
			get;
			set;
		}

		/// <summary>Gets the callback that runs inside the registration, on the main thread.</summary>
		internal Action? OnRegister
		{
			get;
			init;
		}

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

		/// <summary>Gets Cheat Engine's registered symbols as the fake models them (ordinal names).</summary>
		internal Dictionary<string, Address> Symbols
		{
			get;
		} = new(StringComparer.Ordinal);

		/// <summary>Forces the status of every address resolution, for example a failed collision check.</summary>
		internal InspectionStatus? ResolveStatusOverride
		{
			get;
			set;
		}

		public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
			out Address address)
		{
			LastAddressExpression = expression;
			LastAddressMode = mode;
			if (ResolveStatusOverride is { } forced)
			{
				address = default;
				return forced;
			}

			if (Symbols.TryGetValue(expression.Value, out address))
			{
				return InspectionStatus.Success;
			}

			// An unregistered name does not resolve unless the test configured an address for any expression.
			address = ResolvedAddress;
			return ResolvedAddress == Address.Zero ? InspectionStatus.NotFound : InspectionStatus.Success;
		}

		public LuaOperationStatus TryGetName(Address address, out string? name)
		{
			LastNameAddress = address;
			name = ResolvedName;
			return NameStatus;
		}

		public SymbolRegistrationAttempt TryRegisterOwned(SymbolName name, Address address,
			SymbolRegistrationOptions options)
		{
			RegisterCalls++;
			LastRegisteredName = name.Value;
			LastRegisteredAddress = address;
			LastRegisteredDoNotSave = options.DoNotSave;
			OnRegister?.Invoke();
			if (RegistrationFault is { } fault)
			{
				throw fault;
			}

			if (!RegistrationStatus.IsSuccess || OmitHandle)
			{
				return new SymbolRegistrationAttempt(RegistrationStatus, null);
			}

			return new SymbolRegistrationAttempt(LuaOperationStatus.Success, Register(name.Value, address));
		}

		/// <summary>Registers a name through the same coordinator as another owner would, superseding the current lease.</summary>
		internal void RegisterThroughCoordinator(string name, Address address)
		{
			_ = Register(name, address);
		}

		private Registration Register(string name, Address address)
		{
			Symbols[name] = address;
			if (_current.Remove(name, out Registration? existing))
			{
				existing.Supersede();
			}

			Registration registration = new(this, name, address);
			_current[name] = registration;
			return registration;
		}

		/// <summary>Models CheatEngine.SDK's <c>SymbolRegistrationLease.Release</c> over the fake symbol table.</summary>
		private sealed class Registration(FakeInspectionPort owner, string name, Address address)
			: ISymbolRegistrationHandle
		{
			private SymbolRegistrationReleaseKind? _terminal;

			internal void Supersede()
			{
				_terminal = SymbolRegistrationReleaseKind.Superseded;
			}

			public SymbolRegistrationReleaseKind Release()
			{
				owner.ReleaseCalls++;
				if (_terminal is { } terminal)
				{
					_terminal = SymbolRegistrationReleaseKind.AlreadyReleased;
					return terminal;
				}

				InspectionStatus lookup = owner.ResolveAddress(new SymbolExpression(name), default, out Address current);
				SymbolRegistrationReleaseKind kind = lookup switch
				{
					InspectionStatus.Success when current == address => SymbolRegistrationReleaseKind.Released,
					InspectionStatus.Success => SymbolRegistrationReleaseKind.Replaced,
					InspectionStatus.NotFound => SymbolRegistrationReleaseKind.ExternallyRemoved,
					_ => SymbolRegistrationReleaseKind.CleanupUnavailable
				};
				if (kind == SymbolRegistrationReleaseKind.CleanupUnavailable)
				{
					return kind;
				}

				if (kind == SymbolRegistrationReleaseKind.Released)
				{
					owner.UnregisteredNames.Add(name);
					owner.Symbols.Remove(name);
				}

				owner._current.Remove(name);
				_terminal = SymbolRegistrationReleaseKind.AlreadyReleased;
				return kind;
			}
		}
	}
}
