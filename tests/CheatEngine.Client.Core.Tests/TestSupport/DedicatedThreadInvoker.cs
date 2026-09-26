using System.Collections.Concurrent;

using CheatEngine.Client.Core.Dispatching;

namespace CheatEngine.Client.Core.Tests.TestSupport;

/// <summary>
///     Runs every callback on one dedicated thread, as Cheat Engine's main thread, and waits for it; a callback that is
///     already on that thread runs inline.
/// </summary>
internal sealed class DedicatedThreadInvoker : IMainThreadInvoker, IDisposable
{
	private readonly Thread _thread;
	private readonly BlockingCollection<Action> _work = [];

	internal DedicatedThreadInvoker()
	{
		_thread = new Thread(Pump)
		{
			IsBackground = true,
			Name = "Cheat Engine main thread (test)"
		};
		_thread.Start();
	}

	/// <summary>Gets the managed identifier of the dedicated thread.</summary>
	internal int ThreadId => _thread.ManagedThreadId;

	public void Dispose()
	{
		_work.CompleteAdding();
		_thread.Join();
		_work.Dispose();
	}

	public Exception? Invoke(Action callback)
	{
		return Invoke<bool>(() =>
		{
			callback();
			return true;
		}).Exception;
	}

	public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
	{
		if (Environment.CurrentManagedThreadId == _thread.ManagedThreadId)
		{
			return Execute(callback);
		}

		MainThreadInvocationResult<T> result = default;
		using ManualResetEventSlim done = new();
		_work.Add(() =>
		{
			result = Execute(callback);
			done.Set();
		});
		done.Wait();
		return result;
	}

	private static MainThreadInvocationResult<T> Execute<T>(Func<T> callback)
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

	private void Pump()
	{
		foreach (Action work in _work.GetConsumingEnumerable())
		{
			work();
		}
	}
}
