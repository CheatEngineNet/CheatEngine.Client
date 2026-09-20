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
		List<string> events = new();
		CoreResourceRegistry registry = new();
		RetriableDispatcher dispatcher = new() { RejectDispatch = true };
		SymbolRegistrationLease lease = new(
			new SymbolRegistration("fixture-symbol", Address.Zero),
			dispatcher,
			registeredLease =>
			{
				registry.Untrack(registeredLease);
				events.Add("untrack");
			},
			_ => events.Add("unregister"),
			_ => events.Add("release-name"));
		registry.Track(lease);

		Assert.Throws<CheatEngineOperationException>(lease.Dispose);

		Assert.False(lease.IsReleased);
		Assert.Empty(events);

		dispatcher.RejectDispatch = false;
		registry.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["unregister", "untrack", "release-name"], events);
		Assert.Equal(2, dispatcher.InvocationCount);
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
			if (TryInvoke(callback, out var result, out var failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
