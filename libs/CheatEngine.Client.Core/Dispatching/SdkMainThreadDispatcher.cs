using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Hosting.Threading;

namespace CheatEngine.Client.Core.Dispatching;

/// <summary>Adapts the SDK's synchronous main-thread dispatcher without retaining Lua state.</summary>
internal sealed class SdkMainThreadDispatcher : ICheatEngineDispatcher
{
	private const string _invokeOperation = "Dispatcher.Invoke";

	private readonly CoreLifetime _lifetime;
	private readonly IMainThreadInvoker _mainThread;

	internal SdkMainThreadDispatcher(CoreLifetime lifetime)
		: this(lifetime, SdkMainThreadInvoker.Instance)
	{
	}

	internal SdkMainThreadDispatcher(CoreLifetime lifetime, IMainThreadInvoker mainThread)
	{
		_lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
		_mainThread = mainThread ?? throw new ArgumentNullException(nameof(mainThread));
	}

	public bool IsMainThread => _lifetime.CanDispatch && MainThread.IsMainThread;

	public bool TryInvoke(Action callback, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(callback);
		_lifetime.ThrowIfDispatchAllowed(_invokeOperation);
		if (cancellationToken.IsCancellationRequested)
		{
			failure = CoreFailureFactory.Cancelled(_invokeOperation);
			return false;
		}

		Exception? callbackException;
		try
		{
			callbackException = _mainThread.Invoke(callback);
		}
		catch (CheatEngineActivationExpiredException)
		{
			throw;
		}
		catch (Exception exception) when (!_lifetime.IsActivationCurrent)
		{
			throw new CheatEngineActivationExpiredException(_invokeOperation,
				"The Cheat Engine plugin lifecycle changed while dispatching work.", exception);
		}
		catch (Exception exception)
		{
			failure = CoreFailureFactory.FromException(_invokeOperation, exception);
			return false;
		}

		if (callbackException is not null)
		{
			RethrowCallback(callbackException);
		}

		failure = default;
		return true;
	}

	public bool TryInvoke<T>(Func<T> callback, [MaybeNullWhen(false)] out T result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(callback);
		_lifetime.ThrowIfDispatchAllowed(_invokeOperation);
		if (cancellationToken.IsCancellationRequested)
		{
			result = default;
			failure = CoreFailureFactory.Cancelled(_invokeOperation);
			return false;
		}

		MainThreadInvocationResult<T> callbackResult;
		try
		{
			callbackResult = _mainThread.Invoke(callback);
		}
		catch (CheatEngineActivationExpiredException)
		{
			throw;
		}
		catch (Exception exception) when (!_lifetime.IsActivationCurrent)
		{
			result = default;
			throw new CheatEngineActivationExpiredException(_invokeOperation,
				"The Cheat Engine plugin lifecycle changed while dispatching work.", exception);
		}
		catch (Exception exception)
		{
			result = default;
			failure = CoreFailureFactory.FromException(_invokeOperation, exception);
			return false;
		}

		if (callbackResult.Exception is not null)
		{
			RethrowCallback(callbackResult.Exception);
		}

		result = callbackResult.Result;
		failure = default;
		return true;
	}

	public void Invoke(Action callback, CancellationToken cancellationToken = default)
	{
		if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
		{
			_ = ThrowFailure<object?>(failure);
		}
	}

	public T Invoke<T>(Func<T> callback, CancellationToken cancellationToken = default)
	{
		if (TryInvoke(callback, out T? result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		return ThrowFailure<T>(failure);
	}

	private static T ThrowFailure<T>(CheatEngineFailure failure)
	{
		failure.Throw();
		throw new UnreachableException();
	}

	private static void RethrowCallback(Exception callbackException)
	{
		ExceptionDispatchInfo.Capture(callbackException).Throw();
		throw new UnreachableException();
	}

	private static Exception? InvokeCallback(Action callback)
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

	private static MainThreadInvocationResult<T> InvokeCallback<T>(Func<T> callback)
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

	private sealed class SdkMainThreadInvoker : IMainThreadInvoker
	{
		internal static SdkMainThreadInvoker Instance
		{
			get;
		} = new();

		public Exception? Invoke(Action callback)
		{
			return MainThread.Invoke(static action => InvokeCallback(action), callback);
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			return MainThread.Invoke(static function => InvokeCallback(function), callback);
		}
	}
}
