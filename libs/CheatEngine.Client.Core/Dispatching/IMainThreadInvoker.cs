namespace CheatEngine.Client.Core.Dispatching;

internal interface IMainThreadInvoker
{
	public Exception? Invoke(Action callback);

	public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback);

	public Exception? Invoke<TState>(Action<TState> callback, TState state)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return Invoke(() => callback(state));
	}

	public MainThreadInvocationResult<TResult> Invoke<TState, TResult>(Func<TState, TResult> callback, TState state)
	{
		ArgumentNullException.ThrowIfNull(callback);
		return Invoke(() => callback(state));
	}
}
