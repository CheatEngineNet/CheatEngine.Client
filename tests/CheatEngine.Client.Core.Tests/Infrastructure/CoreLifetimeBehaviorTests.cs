using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreLifetimeBehaviorTests
{
	[Fact]
	public void ActiveContextExposesItsEpochAndAdmitsOrdinaryWork()
	{
		using ControlledCoreLifetimeContext context = new() { Epoch = 42 };
		using CoreLifetime lifetime = new(context);

		lifetime.ThrowIfInactive("Test.Work");

		Assert.Equal(42, lifetime.Epoch);
		Assert.True(lifetime.IsActivationCurrent);
		Assert.True(lifetime.IsCurrent);
		Assert.True(lifetime.CanDispatch);
	}

	[Fact]
	public void StoppingContextRejectsOrdinaryWorkButAllowsMainThreadCleanupScope()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		context.Stop();

		CheatEngineClientLifecycleException rejected = Assert.Throws<CheatEngineClientLifecycleException>(() =>
			lifetime.ThrowIfInactive("Test.Work"));
		Assert.Equal("Test.Work", rejected.Failure.Operation);
		Assert.False(lifetime.CanDispatch);

		using (lifetime.EnterCleanupScope())
		{
			Assert.True(lifetime.CanDispatch);
			lifetime.ThrowIfDispatchAllowed("Test.Cleanup");
		}

		Assert.False(lifetime.CanDispatch);
	}

	[Fact]
	public void CleanupScopeRequiresTheCapturedMainThread()
	{
		using ControlledCoreLifetimeContext context = new() { IsMainThread = false };
		using CoreLifetime lifetime = new(context);

		CheatEngineClientLifecycleException exception = Assert.Throws<CheatEngineClientLifecycleException>(
			lifetime.EnterCleanupScope);

		Assert.Equal("Client.EnterCleanupScope", exception.Failure.Operation);
		Assert.Contains("main thread", exception.Failure.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void StaleContextRejectsAllLifecycleAdmissionsWithActivationExpired()
	{
		using ControlledCoreLifetimeContext context = new() { IsCurrent = false };
		using CoreLifetime lifetime = new(context);

		CheatEngineActivationExpiredException inactive = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			lifetime.ThrowIfInactive("Test.Inactive"));
		CheatEngineActivationExpiredException dispatch = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			lifetime.ThrowIfDispatchAllowed("Test.Dispatch"));

		Assert.Equal("Test.Inactive", inactive.Failure.Operation);
		Assert.Equal("Test.Dispatch", dispatch.Failure.Operation);
		Assert.False(lifetime.IsActivationCurrent);
		Assert.False(lifetime.CanDispatch);
	}

	[Fact]
	public void DisposeDrainsTrackedResourcesOnlyOnceAndClosesTheLifetime()
	{
		using ControlledCoreLifetimeContext context = new();
		CoreLifetime lifetime = new(context);
		RecordingDisposable resource = new();
		lifetime.Track(resource);

		lifetime.Dispose();
		lifetime.Dispose();

		Assert.Equal(1, resource.DisposeCount);
		Assert.False(lifetime.IsActivationCurrent);
		Assert.Throws<CheatEngineActivationExpiredException>(() => lifetime.ThrowIfInactive("Test.Disposed"));
	}

	private sealed class RecordingDisposable : IDisposable
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public void Dispose()
		{
			DisposeCount++;
		}
	}
}
