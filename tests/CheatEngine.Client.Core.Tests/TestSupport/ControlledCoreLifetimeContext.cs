using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Tests.TestSupport;

internal sealed class ControlledCoreLifetimeContext : ICoreLifetimeContext, IDisposable
{
	private readonly CancellationTokenSource _stopping = new();

	internal long Epoch
	{
		get;
		set;
	} = 17;

	internal bool IsCurrent
	{
		get;
		set;
	} = true;

	internal bool IsMainThread
	{
		get;
		set;
	} = true;

	CancellationToken ICoreLifetimeContext.Stopping => _stopping.Token;

	long ICoreLifetimeContext.Epoch => Epoch;

	bool ICoreLifetimeContext.IsCurrent => IsCurrent;

	bool ICoreLifetimeContext.IsMainThread => IsMainThread;

	public void Dispose()
	{
		_stopping.Dispose();
	}

	internal void Stop()
	{
		_stopping.Cancel();
	}
}
