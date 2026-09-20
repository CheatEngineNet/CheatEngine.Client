using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class SymbolRegistrationLeaseCleanupCoverageTests
{
	[Fact]
	public void DisposeReleasesTheNameAndBecomesTerminalWhenUntrackingFails()
	{
		List<string> events = [];
		InlineDispatcher dispatcher = new();
		SymbolRegistrationLease lease = new(
			new SymbolRegistration("fixture-symbol", Address.Zero),
			dispatcher,
			_ =>
			{
				events.Add("untrack");
				throw new InvalidOperationException("untracking failed");
			},
			name => events.Add("unregister:" + name),
			name => events.Add("release-name:" + name));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(lease.Dispose);

		Assert.Equal("untracking failed", exception.Message);
		Assert.True(lease.IsReleased);
		Assert.Equal(["unregister:fixture-symbol", "untrack", "release-name:fixture-symbol"], events);
		Assert.Equal(1, dispatcher.InvocationCount);

		lease.Dispose();

		Assert.Equal(1, dispatcher.InvocationCount);
		Assert.Equal(["unregister:fixture-symbol", "untrack", "release-name:fixture-symbol"], events);
	}

	private sealed class InlineDispatcher : ICheatEngineDispatcher
	{
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
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<TResult>(Func<TResult> callback, out TResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			InvocationCount++;
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out _, cancellationToken);
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			_ = TryInvoke(callback, out TResult result, out _, cancellationToken);
			return result;
		}
	}
}
