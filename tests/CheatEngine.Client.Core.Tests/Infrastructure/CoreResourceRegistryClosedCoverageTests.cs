using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

public sealed class CoreResourceRegistryClosedCoverageTests
{
	[Fact]
	public void DisposeTargetSelectionDoesNotRepeatCleanupAfterTheRegistryHasClosed()
	{
		RecordingDisposable resource = new();
		CoreResourceRegistry registry = new();
		registry.Track(resource, 9);

		registry.Dispose();
		registry.DisposeTargetSelection(9);

		Assert.Equal(1, resource.DisposeCount);
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
