namespace CheatEngine.Client.Core.Dispatching;

internal interface IMainThreadInvoker
{
	public Exception? Invoke(Action callback);

	public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback);
}
