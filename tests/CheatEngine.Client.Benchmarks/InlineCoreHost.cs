using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Benchmarks;

/// <summary>
///     A Core activation that is always current and a dispatcher that runs callbacks inline, so a benchmark measures the
///     Client's own work over a fake port and never a Cheat Engine thread hop.
/// </summary>
internal static class InlineCoreHost
{
	/// <summary>Creates an always-current Core lifetime.</summary>
	internal static CoreLifetime CreateLifetime()
	{
		return new CoreLifetime(new AlwaysCurrentLifetimeContext());
	}

	/// <summary>Creates the production dispatcher over an inline main-thread invoker.</summary>
	internal static SdkMainThreadDispatcher CreateDispatcher(CoreLifetime lifetime)
	{
		return new SdkMainThreadDispatcher(lifetime, new InlineMainThreadInvoker());
	}

	private sealed class AlwaysCurrentLifetimeContext : ICoreLifetimeContext
	{
		public long Epoch => 1;

		public bool IsCurrent => true;

		public bool IsMainThread => true;

		public CancellationToken Stopping => CancellationToken.None;
	}

	private sealed class InlineMainThreadInvoker : IMainThreadInvoker
	{
		public Exception? Invoke(Action callback)
		{
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
