using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

public sealed class LuaModuleRegistrationTests
{
	[Fact]
	public void TryRegisterModuleTracksItsLeaseAndDisposesItThroughTheMainThreadDispatcher()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.NotNull(lease);
		Assert.Equal(81, lease.Epoch);
		Assert.False(lease.IsReleased);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
		Assert.Single(tracked, lease);

		lease.Dispose();
		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		Assert.Equal(2, dispatcher.InvocationCount);
		Assert.Empty(tracked);
	}

	[Fact]
	public void TryRegisterModuleRejectsTheSameModuleInstanceUntilItsLeaseIsReleased()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true);

		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? firstLease, out _,
			TestContext.Current.CancellationToken));
		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? duplicateLease,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(duplicateLease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal(["diagnostics.register"], module.Events);

		firstLease.Dispose();

		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void TryRegisterModuleAllowsModulesWhoseDescriptorsDoNotOverlap()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule first = new("first");
		RecordingModule second = new("second");
		LuaClient client = CreateClient(dispatcher, static () => true);

		using ILuaModuleLease firstLease = client.RegisterModule(first, TestContext.Current.CancellationToken);
		using ILuaModuleLease secondLease = client.RegisterModule(second, TestContext.Current.CancellationToken);

		Assert.Equal(["first.register"], first.Events);
		Assert.Equal(["second.register"], second.Events);
	}

	[Fact]
	public void TryRegisterModuleRejectsAnAlreadyReservedDescribedModuleIdentityBeforeLuaMutation()
	{
		ImmediateDispatcher dispatcher = new();
		DescribedRecordingModule first = new("first", "diagnostics", ["first_export"]);
		DescribedRecordingModule second = new("second", "diagnostics", ["second_export"]);
		LuaClient client = CreateClient(dispatcher, static () => true);

		using ILuaModuleLease firstLease = client.RegisterModule(first, TestContext.Current.CancellationToken);
		bool succeeded = client.TryRegisterModule(second, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Contains("identity 'diagnostics'", failure.Message, StringComparison.Ordinal);
		Assert.Equal(["first.register"], first.Events);
		Assert.Empty(second.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryRegisterModuleRejectsAnAlreadyReservedDescribedExportBeforeLuaMutation()
	{
		ImmediateDispatcher dispatcher = new();
		DescribedRecordingModule first = new("first", "first", ["diagnostics"]);
		DescribedRecordingModule second = new("second", "second", ["diagnostics"]);
		LuaClient client = CreateClient(dispatcher, static () => true);

		using ILuaModuleLease firstLease = client.RegisterModule(first, TestContext.Current.CancellationToken);
		bool succeeded = client.TryRegisterModule(second, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Contains("export 'diagnostics'", failure.Message, StringComparison.Ordinal);
		Assert.Equal(["first.register"], first.Events);
		Assert.Empty(second.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void FailedDescribedRegistrationReleasesItsNameReservationForTheNextModule()
	{
		ImmediateDispatcher dispatcher = new();
		DescribedRecordingModule failed = new("failed", "diagnostics", ["diagnostics"],
			new InvalidOperationException("generated registration failed"));
		DescribedRecordingModule replacement = new("replacement", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(dispatcher, static () => true);

		Assert.False(client.TryRegisterModule(failed, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		using ILuaModuleLease lease = client.RegisterModule(replacement, TestContext.Current.CancellationToken);

		Assert.Equal(["failed.register"], failed.Events);
		Assert.Equal(["replacement.register"], replacement.Events);
	}

	[Fact]
	public void TryRegisterModuleReleasesTrackedReservationWhenDispatcherRejectsRegistration()
	{
		CheatEngineFailure dispatchFailure = new(CheatEngineFailureKind.InvalidState, "Test.Dispatcher", "Rejected.");
		ImmediateDispatcher dispatcher = new()
		{
			TryInvokeFailure = dispatchFailure
		};
		List<ILuaModuleLease> tracked = [];
		int trackCount = 0;
		int untrackCount = 0;
		DescribedRecordingModule rejected = new("rejected", "diagnostics", ["diagnostics"]);
		DescribedRecordingModule replacement = new("replacement", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			lease =>
			{
				trackCount++;
				tracked.Add(lease);
			},
			lease =>
			{
				untrackCount++;
				tracked.Remove(lease);
			});

		bool succeeded = client.TryRegisterModule(rejected, out ILuaModuleLease? rejectedLease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(rejectedLease);
		Assert.Equal(dispatchFailure, failure);
		Assert.Empty(rejected.Events);
		Assert.Equal(1, trackCount);
		Assert.Equal(1, untrackCount);
		Assert.Empty(tracked);

		dispatcher.TryInvokeFailure = null;
		using ILuaModuleLease replacementLease =
			client.RegisterModule(replacement, TestContext.Current.CancellationToken);

		Assert.Equal(["replacement.register"], replacement.Events);
		Assert.Single(tracked, replacementLease);
	}

	[Fact]
	public async Task ConcurrentDescribedRegistrationRejectsTheSecondModuleBeforeEitherOfItsLuaExportsMutateAsync()
	{
		using BlockingDispatcher dispatcher = new();
		DescribedRecordingModule first = new("first", "one", ["diagnostics"]);
		DescribedRecordingModule second = new("second", "two", ["diagnostics"]);
		LuaClient client = CreateClient(dispatcher, static () => true);
		ILuaModuleLease? firstLease = null;

		Task<bool> firstRegistration = Task.Run(() =>
			client.TryRegisterModule(first, out firstLease, out _, CancellationToken.None));
		Assert.True(dispatcher.WaitUntilEntered(TimeSpan.FromSeconds(5)));

		bool secondSucceeded = client.TryRegisterModule(second, out ILuaModuleLease? secondLease,
			out CheatEngineFailure secondFailure, TestContext.Current.CancellationToken);
		dispatcher.Release();

		Assert.True(await firstRegistration);
		Assert.False(secondSucceeded);
		Assert.Null(secondLease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, secondFailure.Kind);
		Assert.Empty(second.Events);
		Assert.Equal(["first.register"], first.Events);
		firstLease!.Dispose();
	}

	[Fact]
	public void TryRegisterModuleHonorsCancellationBeforeReservingOrDispatching()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		ImmediateDispatcher dispatcher = new();
		DescribedRecordingModule module = new("diagnostics", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(dispatcher, static () => true);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Empty(module.Events);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void RegisterModuleReturnsTheLeaseAndThrowsTheMappedFailureForARejectedRegistration()
	{
		ImmediateDispatcher dispatcher = new();
		LuaClient client = CreateClient(dispatcher, static () => true);
		RecordingModule accepted = new("accepted");
		RecordingModule rejected = new("rejected", new InvalidOperationException("generated registration failed"));

		using ILuaModuleLease lease = client.RegisterModule(accepted, TestContext.Current.CancellationToken);
		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.RegisterModule(rejected, TestContext.Current.CancellationToken));

		Assert.Equal(81, lease.Epoch);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, exception.Failure.Kind);
		Assert.Equal("Lua.RegisterModule", exception.Failure.Operation);
		Assert.Equal(["accepted.register"], accepted.Events);
		Assert.Equal(["rejected.register"], rejected.Events);
	}

	[Fact]
	public void TryRegisterModuleMapsAnApplicationModuleFailureAndDoesNotCreateALease()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics", new InvalidOperationException("generated registration failed"));
		LuaClient client = CreateClient(dispatcher, static () => true);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal("generated registration failed", failure.Message);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryRegisterModuleDoesNotMutateLuaAndReleasesReservationsWhenLeaseTrackingFails()
	{
		ImmediateDispatcher dispatcher = new();
		bool trackingAvailable = false;
		List<ILuaModuleLease> tracked = [];
		DescribedRecordingModule rejected = new("rejected", "diagnostics", ["diagnostics"]);
		DescribedRecordingModule replacement = new("replacement", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			lease =>
			{
				if (!trackingAvailable)
				{
					throw new InvalidOperationException("activation is closed");
				}

				tracked.Add(lease);
			},
			lease =>
			{
				tracked.Remove(lease);
			});

		bool succeeded = client.TryRegisterModule(rejected, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Empty(rejected.Events);
		Assert.Equal(0, dispatcher.InvocationCount);
		Assert.Empty(tracked);

		trackingAvailable = true;
		using ILuaModuleLease replacementLease =
			client.RegisterModule(replacement, TestContext.Current.CancellationToken);

		Assert.Equal(["replacement.register"], replacement.Events);
		Assert.Single(tracked, replacementLease);
	}

	[Fact]
	public void TryRegisterModuleRetainsAnAbandonmentFailureAlongsideTheLeaseTrackingFailure()
	{
		ImmediateDispatcher dispatcher = new();
		bool failTracking = true;
		bool failUntracking = true;
		DescribedRecordingModule rejected = new("rejected", "diagnostics", ["diagnostics"]);
		DescribedRecordingModule replacement = new("replacement", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			_ =>
			{
				if (failTracking)
				{
					throw new InvalidOperationException("tracking failed");
				}
			},
			_ =>
			{
				if (failUntracking)
				{
					throw new InvalidOperationException("untracking failed");
				}
			});

		Assert.False(client.TryRegisterModule(rejected, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Contains("tracking failed", failure.Message, StringComparison.Ordinal);
		Assert.Contains("untracking failed", failure.Message, StringComparison.Ordinal);
		AggregateException aggregate = Assert.IsType<AggregateException>(failure.Exception);
		Assert.Collection(
			aggregate.InnerExceptions,
			exception => Assert.Equal("tracking failed", exception.Message),
			exception => Assert.Equal("untracking failed", exception.Message));
		Assert.Empty(rejected.Events);
		Assert.Equal(0, dispatcher.InvocationCount);

		failTracking = false;
		failUntracking = false;
		using ILuaModuleLease replacementLease =
			client.RegisterModule(replacement, TestContext.Current.CancellationToken);

		Assert.Equal(["replacement.register"], replacement.Events);
	}

	[Fact]
	public void TryRegisterModuleDefersARegistrationCompletedDuringShutdownToMainThreadCleanupWithoutPublishingALease()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		DescribedRecordingModule module = new("diagnostics", "diagnostics", ["diagnostics"], onRegister: context.Stop);
		LuaClient client = new(dispatcher, lifetime);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.IsType<CheatEngineClientLifecycleException>(failure.Exception);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, invoker.ActionCalls);

		using (lifetime.EnterCleanupScope())
		{
			lifetime.DrainOwnedResourcesForDisable();
		}

		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		// Registration is an action; the lease release is a function whose outcome the lease records.
		Assert.Equal(1, invoker.ActionCalls);
		Assert.Equal(1, invoker.FunctionCalls);
	}

	[Fact]
	public void TryRegisterModuleSecondAdmissionFailureOutsideStoppingUnregistersUntracksAndAllowsReplacement()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		int trackCount = 0;
		int untrackCount = 0;
		int admissionCount = 0;
		DescribedRecordingModule rejected = new("rejected", "diagnostics", ["diagnostics"]);
		DescribedRecordingModule replacement = new("replacement", "diagnostics", ["diagnostics"]);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			lease =>
			{
				trackCount++;
				tracked.Add(lease);
			},
			lease =>
			{
				untrackCount++;
				tracked.Remove(lease);
			},
			_ =>
			{
				admissionCount++;
				if (admissionCount == 2)
				{
					throw new InvalidOperationException("Second admission failed.");
				}
			},
			static () => false);

		bool succeeded = client.TryRegisterModule(rejected, out ILuaModuleLease? rejectedLease,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(rejectedLease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal(["rejected.register", "rejected.unregister"], rejected.Events);
		Assert.Equal(2, dispatcher.InvocationCount);
		Assert.Equal(1, trackCount);
		Assert.Equal(1, untrackCount);
		Assert.Empty(tracked);

		using ILuaModuleLease replacementLease =
			client.RegisterModule(replacement, TestContext.Current.CancellationToken);

		Assert.Equal(["replacement.register"], replacement.Events);
		Assert.Equal(4, admissionCount);
		Assert.Equal(2, trackCount);
		Assert.Equal(1, untrackCount);
		Assert.Single(tracked, replacementLease);
	}

	[Fact]
	public void TryRegisterModulePreservesADeferredUnregistrationFailureFromShutdownCleanup()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		RecordingModule module = new("diagnostics", onRegister: context.Stop, unregisterFailureCount: 1);
		LuaClient client = new(dispatcher, lifetime);

		Assert.False(client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);

		using (lifetime.EnterCleanupScope())
		{
			// The module threw from Unregister: the lease records an unconfirmed cleanup and the drain reports it with
			// safe fields only.
			CheatEngineOperationException report = Assert.Throws<CheatEngineOperationException>(
				lifetime.DrainOwnedResourcesForDisable);
			Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, report.Failure.Kind);
			Assert.Equal("Lua.UnregisterModule", report.Failure.Operation);
			Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, report.Failure.HostEffect);
		}

		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		// Registration is an action; the lease release is a function whose outcome the lease records.
		Assert.Equal(1, invoker.ActionCalls);
		Assert.Equal(1, invoker.FunctionCalls);
	}

	[Fact]
	public void ForgottenModuleLeasesReleaseInReverseRegistrationOrderFromTheActivationResourceRegistry()
	{
		ImmediateDispatcher dispatcher = new();
		CoreResourceRegistry registry = new();
		List<string> events = [];
		RecordingModule first = new("first", events: events);
		RecordingModule second = new("second", events: events);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			lease =>
			{
				registry.Track(lease);
			},
			lease =>
			{
				registry.Untrack(lease);
			});

		Assert.True(client.TryRegisterModule(first, out _, out _, TestContext.Current.CancellationToken));
		Assert.True(client.TryRegisterModule(second, out _, out _, TestContext.Current.CancellationToken));

		registry.Dispose();

		Assert.Equal(["first.register", "second.register", "second.unregister", "first.unregister"], events);
		Assert.Equal(4, dispatcher.InvocationCount);
	}

	[Fact]
	public void ALeaseWhoseReleaseCannotBeDispatchedStaysActiveAndTrackedForTheCleanupRetry()
	{
		CheatEngineFailure refused = new(CheatEngineFailureKind.ActivationExpired, "Test.Dispatcher", "Refused.");
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		dispatcher.TryInvokeFailure = refused;

		LeaseReleaseOutcome unavailable = lease.Release();
		lease.Dispose();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			unavailable);
		Assert.False(lease.IsReleased);
		Assert.Single(tracked, lease);
		Assert.Null(lease.ModuleReleaseOutcome);
		Assert.Equal(["diagnostics.register"], module.Events);

		dispatcher.TryInvokeFailure = null;
		LeaseReleaseOutcome released = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), released);
		Assert.True(lease.IsReleased);
		Assert.Empty(tracked);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void AnUnregistrationExceptionIsAnUnconfirmedCleanupThatDisposeNeverThrowsAndNeverRetries()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		RecordingModule module = new("diagnostics", unregisterFailureCount: 1);
		RecordingModule replacement = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));

		lease.Dispose();
		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Unknown),
			lease.LastReleaseOutcome);
		Assert.Null(lease.ModuleReleaseOutcome);
		// Incomplete: the activation keeps tracking it so the deactivation report carries it.
		Assert.Single(tracked, lease);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		// The lease ended, so its names are free again.
		using ILuaModuleLease replacementLease =
			client.RegisterModule(replacement, TestContext.Current.CancellationToken);
	}

	[Theory]
	[InlineData(LeaseReleaseKind.PartiallyReleased, CheatEngineHostEffect.Started)]
	[InlineData(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted)]
	public void AReleaseThatRequiresManualRecoveryEndsTheLeaseAndStaysTrackedForTheReport(LeaseReleaseKind kind,
		CheatEngineHostEffect hostEffect)
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		LuaModuleReleaseOutcome reported = kind == LeaseReleaseKind.PartiallyReleased
			? LuaModuleReleaseOutcome.PartiallyReleased("diagnostics", 0, 0, 0, ["diagnostics_global"])
			: LuaModuleReleaseOutcome.RefusedRuntimeChanged("diagnostics", 1);
		module.NextOutcome = reported;

		LeaseReleaseOutcome outcome = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(kind, hostEffect), outcome);
		Assert.True(outcome.RequiresManualRecovery);
		Assert.True(lease.IsReleased);
		Assert.Same(reported, lease.ModuleReleaseOutcome);
		Assert.Single(tracked, lease);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, lease.Release().Kind);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void AnUnconfirmedModuleReleaseEndsTheLeaseAndTheActivationCleanupReportsIt()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime, new RecordingMainThreadInvoker());
		RecordingModule module = new("diagnostics");
		LuaClient client = new(dispatcher, lifetime);
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		// What a generated module reports when CheatEngine.SDK consumed its registration lease but returned a release
		// outside the documented shape.
		LuaModuleReleaseOutcome unconfirmed =
			LuaModuleReleaseOutcome.Create("diagnostics", LeaseReleaseKind.CleanupUnconfirmed, 0, 0, 0, 1, []);
		module.NextOutcome = unconfirmed;

		LeaseReleaseOutcome outcome = lease.Release();
		LeaseReleaseOutcome retry = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started), outcome);
		Assert.False(outcome.IsRetryable);
		Assert.True(lease.IsReleased);
		Assert.Same(unconfirmed, lease.ModuleReleaseOutcome);
		Assert.Equal(LeaseReleaseKind.AlreadyReleased, retry.Kind);
		context.Stop();
		using (lifetime.EnterCleanupScope())
		{
			// The lease stayed with the activation, so the drain reports the incomplete release instead of retrying it.
			CheatEngineOperationException report = Assert.Throws<CheatEngineOperationException>(
				lifetime.DrainOwnedResourcesForDisable);
			Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, report.Failure.Kind);
			Assert.Equal("Lua.UnregisterModule", report.Failure.Operation);
			Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, report.Failure.HostEffect);
			Assert.Contains(nameof(LeaseReleaseKind.CleanupUnconfirmed), report.Failure.Message, StringComparison.Ordinal);
		}

		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void TheLeaseIsAClientLeaseThatReportsWhatTheModuleObserved()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true);
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		LuaModuleReleaseOutcome reported = LuaModuleReleaseOutcome.Released("diagnostics", 0, 0, 1);
		module.NextOutcome = reported;

		ICheatEngineLease clientLease = lease;
		Assert.Null(clientLease.LastReleaseOutcome);
		LeaseReleaseOutcome outcome = clientLease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed), outcome);
		Assert.Equal(outcome, clientLease.LastReleaseOutcome);
		Assert.Same(reported, lease.ModuleReleaseOutcome);
		Assert.Equal(1, lease.ModuleReleaseOutcome!.ReplacementCount);
	}

	[Fact]
	public void TryRegisterModuleReportsTheFailureAClassifiedRegistrationRefusalCarries()
	{
		ImmediateDispatcher dispatcher = new();
		CheatEngineFailure refusal = new(CheatEngineFailureKind.OperationRejected, "Lua.RegisterModule",
			"Lua global 'diagnostics_global' is already defined.", null, CheatEngineHostEffect.NotApplied);
		RecordingModule module = new("diagnostics", new CheatEngineOperationException(refusal));
		LuaClient client = CreateClient(dispatcher, static () => true);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(refusal, failure);
	}

	[Fact]
	public void TryRegisterModuleNeverReturnsADefaultFailureForARefusalThatDescribesNone()
	{
		ImmediateDispatcher dispatcher = new();
		CheatEngineOperationException undescribed = new(default);
		RecordingModule module = new("diagnostics", undescribed);
		LuaClient client = CreateClient(dispatcher, static () => true);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);
		CheatEngineOperationException thrown = Assert.Throws<CheatEngineOperationException>(() =>
			client.RegisterModule(module, TestContext.Current.CancellationToken));

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.False(failure.IsDefault);
		Assert.Equal(CheatEngineFailureKind.Unknown, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
		Assert.Same(undescribed, failure.Exception);
		Assert.Equal(failure, thrown.Failure);
	}

	[Fact]
	public void TryRegisterModuleRefusesAModuleWithoutADescriptorBeforeAnyDispatch()
	{
		ImmediateDispatcher dispatcher = new();
		LuaClient client = CreateClient(dispatcher, static () => true);

		ArgumentException exception = Assert.Throws<ArgumentException>(() =>
			client.TryRegisterModule(new UndescribedModule(), out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal("luaModule", exception.ParamName);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void AReleaseThatCouldNotBeginKeepsTheLeaseForTheCleanupRetry()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = [];
		RecordingModule module = new("diagnostics");
		RecordingModule contender = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});
		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		LuaModuleReleaseOutcome unavailable = LuaModuleReleaseOutcome.CleanupUnavailable("diagnostics", 1);
		module.NextOutcome = unavailable;

		LeaseReleaseOutcome refused = lease.Release();

		Assert.Equal(new LeaseReleaseOutcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted),
			refused);
		Assert.True(refused.IsRetryable);
		Assert.False(lease.IsReleased);
		Assert.Same(unavailable, lease.ModuleReleaseOutcome);
		Assert.Single(tracked, lease);
		// The module still owns its registration, so its names stay reserved.
		Assert.False(client.TryRegisterModule(contender, out _, out _, TestContext.Current.CancellationToken));

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Empty(tracked);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister", "diagnostics.unregister"], module.Events);
	}

	private sealed class UndescribedModule : ILuaModule
	{
		public LuaModuleDescriptor Descriptor => default;

		public void Register()
		{
			throw new InvalidOperationException("A module without a descriptor must never be registered.");
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			throw new InvalidOperationException("A module without a descriptor must never be released.");
		}
	}

	private static LuaClient CreateClient(
		ICheatEngineDispatcher dispatcher,
		Func<bool> isActivationCurrent,
		Action<ILuaModuleLease>? trackLease = null,
		Action<ILuaModuleLease>? untrackLease = null,
		Action<string>? admitStatefulOperation = null,
		Func<bool>? isStopping = null)
	{
		return new LuaClient(dispatcher, static () => 81, isActivationCurrent, trackLease, untrackLease,
			admitStatefulOperation, isStopping);
	}

	private sealed class RecordingModule(
		string name,
		Exception? registerException = null,
		List<string>? events = null,
		int unregisterFailureCount = 0,
		Action? onRegister = null) : ILuaModule
	{
		private int _remainingUnregisterFailures = unregisterFailureCount;

		internal List<string> Events
		{
			get;
		} = events ?? [];

		internal LuaModuleReleaseOutcome? NextOutcome
		{
			get;
			set;
		}

		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new(name, [new LuaExportDescriptor(name + "_global")]);

		public void Register()
		{
			Events.Add(name + ".register");
			onRegister?.Invoke();
			if (registerException is not null)
			{
				throw registerException;
			}
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			Events.Add(name + ".unregister");
			if (_remainingUnregisterFailures-- > 0)
			{
				throw new InvalidOperationException("generated unregistration failed");
			}

			LuaModuleReleaseOutcome outcome = NextOutcome ?? LuaModuleReleaseOutcome.Released(name, 1, 0, 0);
			NextOutcome = null;
			return outcome;
		}
	}

	private sealed class DescribedRecordingModule : ILuaModule
	{
		private readonly RecordingModule _inner;

		internal DescribedRecordingModule(
			string name,
			string moduleName,
			string[] exports,
			Exception? registerException = null,
			Action? onRegister = null)
		{
			_inner = new RecordingModule(name, registerException, onRegister: onRegister);
			Descriptor = new LuaModuleDescriptor(moduleName,
				exports.Select(static export => new LuaExportDescriptor(export)).ToImmutableArray());
		}

		internal List<string> Events => _inner.Events;

		public LuaModuleDescriptor Descriptor
		{
			get;
		}

		public void Register()
		{
			_inner.Register();
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			return _inner.Unregister();
		}
	}

	private sealed class ImmediateDispatcher : ICheatEngineDispatcher
	{
		public int InvocationCount
		{
			get;
			private set;
		}

		internal CheatEngineFailure? TryInvokeFailure
		{
			get;
			set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatch", "Cancelled.");
				return false;
			}

			InvocationCount++;
			if (TryInvokeFailure.HasValue)
			{
				failure = TryInvokeFailure.Value;
				return false;
			}

			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<TResult>(Func<TResult> callback, out TResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default!;
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatch", "Cancelled.");
				return false;
			}

			InvocationCount++;
			if (TryInvokeFailure.HasValue)
			{
				result = default!;
				failure = TryInvokeFailure.Value;
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
				failure.Throw(cancellationToken);
			}
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out TResult? result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}
	}

	private sealed class BlockingDispatcher : ICheatEngineDispatcher, IDisposable
	{
		private readonly ManualResetEventSlim _entered = new(false);
		private readonly ManualResetEventSlim _release = new(false);

		public int InvocationCount
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
			_entered.Set();
			_release.Wait(cancellationToken);
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<TResult>(Func<TResult> callback, out TResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			_entered.Set();
			_release.Wait(cancellationToken);
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw(cancellationToken);
			}
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out TResult result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw(cancellationToken);
			return default!;
		}

		public void Dispose()
		{
			_entered.Dispose();
			_release.Dispose();
		}

		public void Release()
		{
			_release.Set();
		}

		internal bool WaitUntilEntered(TimeSpan timeout)
		{
			return _entered.Wait(timeout);
		}
	}

	private sealed class RecordingMainThreadInvoker : IMainThreadInvoker
	{
		internal int ActionCalls
		{
			get;
			private set;
		}

		internal int FunctionCalls
		{
			get;
			private set;
		}

		public Exception? Invoke(Action callback)
		{
			ArgumentNullException.ThrowIfNull(callback);
			ActionCalls++;
			try
			{
				callback();
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			ArgumentNullException.ThrowIfNull(callback);
			FunctionCalls++;
			try
			{
				return new MainThreadInvocationResult<T>(callback(), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<T>(default!, exception);
			}
		}
	}
}
