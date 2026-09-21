using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.TestSupport;

internal static class InertCoreLifetime
{
	internal static CoreLifetime Create()
	{
		return new CoreLifetime(new AlwaysCurrentLifetimeContext());
	}

	private sealed class AlwaysCurrentLifetimeContext : ICoreLifetimeContext
	{
		public long Epoch => 1;

		public bool IsCurrent => true;

		public bool IsMainThread => true;

		public CancellationToken Stopping => CancellationToken.None;
	}
}
