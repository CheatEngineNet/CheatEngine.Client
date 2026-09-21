using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Dispatching;

public sealed class SdkMainThreadDispatcherBehaviorTests
{
	[Fact]
	public void TryInvokeExecutesActionAndGenericCallbacksThroughTheInjectedMainThreadInvoker()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		bool callbackRan = false;

		bool actionSucceeded = dispatcher.TryInvoke(() => callbackRan = true, out CheatEngineFailure actionFailure,
			TestContext.Current.CancellationToken);
		bool functionSucceeded = dispatcher.TryInvoke(static () => 42, out int result,
			out CheatEngineFailure functionFailure, TestContext.Current.CancellationToken);

		Assert.True(actionSucceeded);
		Assert.True(callbackRan);
		Assert.Equal(default, actionFailure);
		Assert.True(functionSucceeded);
		Assert.Equal(42, result);
		Assert.Equal(default, functionFailure);
		Assert.Equal(1, invoker.ActionCalls);
		Assert.Equal(1, invoker.FunctionCalls);
	}

	[Fact]
	public void InternalStatefulFastPathForwardsValueStateWithoutUsingThePublicClosureFallback()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
#pragma warning disable CA1859 // This test intentionally dispatches through the internal interface contract.
		IStatefulCheatEngineDispatcher dispatcher = new SdkMainThreadDispatcher(lifetime, invoker);
#pragma warning restore CA1859

		bool succeeded = dispatcher.TryInvoke(21, static value => value * 2, out int result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(42, result);
		Assert.Equal(default, failure);
		Assert.Equal(1, invoker.StateFunctionCalls);
		Assert.Equal(0, invoker.FunctionCalls);
	}

	[Fact]
	public void CallbackExceptionsAreRethrownWithoutBeingClassifiedAsHostFailures()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime, new RecordingMainThreadInvoker());

		InvalidOperationException actionException = Assert.Throws<InvalidOperationException>(() =>
			dispatcher.TryInvoke(static () => throw new InvalidOperationException("action callback"),
				out CheatEngineFailure _, TestContext.Current.CancellationToken));
		InvalidOperationException functionException = Assert.Throws<InvalidOperationException>(() =>
			dispatcher.TryInvoke<int>(static () => throw new InvalidOperationException("function callback"), out _,
				out CheatEngineFailure _, TestContext.Current.CancellationToken));

		Assert.Equal("action callback", actionException.Message);
		Assert.Equal("function callback", functionException.Message);
	}

	[Fact]
	public void TryInvokeMapsMainThreadInfrastructureFailuresToStableClientFailures()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime,
			new RecordingMainThreadInvoker { HostException = new InvalidOperationException("host queue unavailable") });

		bool succeeded = dispatcher.TryInvoke(static () =>
			{
			}, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Dispatcher.Invoke", failure.Operation);
		Assert.Equal("host queue unavailable", failure.Message);
	}

	[Fact]
	public void ConstructorRejectsMissingLifecycleOrMainThreadInvoker()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();

		ArgumentNullException lifetimeException = Assert.Throws<ArgumentNullException>(() =>
			new SdkMainThreadDispatcher(null!, invoker));
		ArgumentNullException invokerException = Assert.Throws<ArgumentNullException>(() =>
			new SdkMainThreadDispatcher(lifetime, null!));

		Assert.Equal("lifetime", lifetimeException.ParamName);
		Assert.Equal("mainThread", invokerException.ParamName);
	}

	private sealed class RecordingMainThreadInvoker : IMainThreadInvoker
	{
		internal int ActionCalls
		{
			get;
			private set;
		}

		internal int FunctionCalls
		{
			get;
			private set;
		}

		internal int StateFunctionCalls
		{
			get;
			private set;
		}

		internal Exception? HostException
		{
			get;
			init;
		}

		public Exception? Invoke(Action callback)
		{
			ActionCalls++;
			if (HostException is not null)
			{
				throw HostException;
			}

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
			FunctionCalls++;
			if (HostException is not null)
			{
				throw HostException;
			}

			try
			{
				return new MainThreadInvocationResult<T>(callback(), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<T>(default!, exception);
			}
		}

		public MainThreadInvocationResult<TResult> Invoke<TState, TResult>(Func<TState, TResult> callback,
			TState state)
		{
			StateFunctionCalls++;
			if (HostException is not null)
			{
				throw HostException;
			}

			try
			{
				return new MainThreadInvocationResult<TResult>(callback(state), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<TResult>(default!, exception);
			}
		}
	}
}
