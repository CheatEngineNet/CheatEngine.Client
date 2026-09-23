using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class SymbolRegistrationLeaseTests
{
	[Fact]
	public void FailedDispatchKeepsTheLeaseTrackedUntilTheCleanupRetryUnregistersIt()
	{
		List<string> events = [];
		CoreResourceRegistry registry = new();
		RetriableDispatcher dispatcher = new()
		{
			RejectDispatch = true
		};
		SymbolRegistrationLease lease = new(
			new SymbolRegistration("fixture-symbol", Address.Zero),
			dispatcher,
			registeredLease =>
			{
				registry.Untrack(registeredLease);
				events.Add("untrack");
			},
			(_, _) =>
			{
				events.Add("unregister");
				return new SymbolLeaseRelease(SymbolLeaseReleaseKind.Released, null);
			},
			_ => events.Add("release-name"));
		registry.Track(lease);

		Assert.Throws<CheatEngineClientLifecycleException>(lease.Dispose);

		Assert.False(lease.IsReleased);
		Assert.Empty(events);

		dispatcher.RejectDispatch = false;
		registry.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["unregister", "untrack", "release-name"], events);
		Assert.Equal(2, dispatcher.InvocationCount);
	}

	[Theory]
	[Trait("Qualification", "Q16")]
	[InlineData(SymbolLeaseReleaseKind.Released)]
	[InlineData(SymbolLeaseReleaseKind.Replaced)]
	[InlineData(SymbolLeaseReleaseKind.ExternallyRemoved)]
	public void TerminalReleaseOutcomesReleaseTheReservationAndReturnTheOutcome(SymbolLeaseReleaseKind outcome)
	{
		List<string> events = [];
		SymbolRegistrationLease lease = CreateLease(new RetriableDispatcher(), events, outcome);

		SymbolLeaseReleaseKind first = lease.ReleaseDetailed();
		SymbolLeaseReleaseKind second = lease.ReleaseDetailed();

		Assert.Equal(outcome, first);
		Assert.Equal(SymbolLeaseReleaseKind.AlreadyReleased, second);
		Assert.True(lease.IsReleased);
		Assert.Equal(["release:" + outcome, "untrack", "release-name"], events);
	}

	[Fact]
	public void ReleaseDetailedIsIdempotentAndReportsAlreadyReleased()
	{
		List<string> events = [];
		RetriableDispatcher dispatcher = new();
		SymbolRegistrationLease lease = CreateLease(dispatcher, events, SymbolLeaseReleaseKind.Released);

		lease.Dispose();
		SymbolLeaseReleaseKind again = lease.ReleaseDetailed();
		lease.Dispose();

		Assert.Equal(SymbolLeaseReleaseKind.AlreadyReleased, again);
		Assert.Equal(1, dispatcher.InvocationCount);
		Assert.Equal(["release:Released", "untrack", "release-name"], events);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void LeaseLookupFailureKeepsTheLeaseActiveAndDisposeReportsCleanupUnconfirmed()
	{
		List<string> events = [];
		InvalidOperationException fault = new("the lookup faulted");
		SymbolRegistrationLease lease = new(
			new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			new RetriableDispatcher(),
			_ => events.Add("untrack"),
			(_, _) => new SymbolLeaseRelease(SymbolLeaseReleaseKind.CleanupUnavailable, fault),
			_ => events.Add("release-name"));

		SymbolLeaseReleaseKind detailed = lease.ReleaseDetailed();
		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(lease.Dispose);

		Assert.Equal(SymbolLeaseReleaseKind.CleanupUnavailable, detailed);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, exception.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, exception.Failure.HostEffect);
		Assert.Equal("Inspection.ReleaseSymbol", exception.Failure.Operation);
		Assert.Same(fault, exception.Failure.Exception);
		Assert.False(lease.IsReleased);
		Assert.Empty(events);
	}

	private static SymbolRegistrationLease CreateLease(RetriableDispatcher dispatcher, List<string> events,
		SymbolLeaseReleaseKind outcome)
	{
		return new SymbolRegistrationLease(
			new SymbolRegistration("fixture-symbol", new Address(0x401000)),
			dispatcher,
			_ => events.Add("untrack"),
			(_, _) =>
			{
				events.Add("release:" + outcome);
				return new SymbolLeaseRelease(outcome, null);
			},
			_ => events.Add("release-name"));
	}

	private sealed class RetriableDispatcher : ICheatEngineDispatcher
	{
		public int InvocationCount
		{
			get;
			private set;
		}

		public bool RejectDispatch
		{
			get;
			set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			if (RejectDispatch)
			{
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.InvalidState,
					"Test.Dispatch",
					"Dispatch admission is closed.");
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
			InvocationCount++;
			if (RejectDispatch)
			{
				result = default!;
				failure = new CheatEngineFailure(
					CheatEngineFailureKind.InvalidState,
					"Test.Dispatch",
					"Dispatch admission is closed.");
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
				failure.Throw();
			}
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out TResult result, out CheatEngineFailure failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
