namespace CheatEngine.Client.Core.Infrastructure;

internal interface ICoreLifetimeContext
{
	public long Epoch
	{
		get;
	}

	public CancellationToken Stopping
	{
		get;
	}

	public bool IsCurrent
	{
		get;
	}

	public bool IsMainThread
	{
		get;
	}
}
