using CheatEngine.Client.Core.Dispatching;

namespace CheatEngine.Client.Core.Tests.TestSupport;

internal sealed class InlineMainThreadInvoker : IMainThreadInvoker
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
