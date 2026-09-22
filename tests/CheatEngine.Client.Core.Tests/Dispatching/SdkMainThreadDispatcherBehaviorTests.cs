using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Dispatching;

public sealed class SdkMainThreadDispatcherBehaviorTests
{
	public enum DispatchForm
	{
		Action,
		Function,
		StatefulFunction
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractExecutesCallbacksAndReturnsTheirSuccessfulResults(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		bool callbackRan = false;

		DispatchInvocation invocation = TryInvoke(form, dispatcher, () => callbackRan = true,
			TestContext.Current.CancellationToken);

		Assert.True(invocation.Succeeded);
		Assert.True(callbackRan);
		Assert.Equal(default, invocation.Failure);
		Assert.Equal(form == DispatchForm.Action ? null : 42, invocation.Result);
		AssertInvokedOnlyThrough(form, invoker);
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractRethrowsTheSameCallbackExceptionInstance(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		InvalidOperationException expected = new("callback failure");

		InvalidOperationException actual = Assert.Throws<InvalidOperationException>(() =>
			TryInvoke(form, dispatcher, () => throw expected, TestContext.Current.CancellationToken));

		Assert.Same(expected, actual);
		AssertInvokedOnlyThrough(form, invoker);
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractMapsInfrastructureFailuresToStructuredClientFailures(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		InvalidOperationException expected = new("host queue unavailable");
		RecordingMainThreadInvoker invoker = new()
		{
			HostException = expected
		};
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		bool callbackRan = false;

		DispatchInvocation invocation = TryInvoke(form, dispatcher, () => callbackRan = true,
			TestContext.Current.CancellationToken);

		Assert.False(invocation.Succeeded);
		Assert.Equal(DefaultResultForFailedInvocation(form), invocation.Result);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, invocation.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", invocation.Failure.Operation);
		Assert.Equal("host queue unavailable", invocation.Failure.Message);
		Assert.Same(expected, invocation.Failure.Exception);
		Assert.False(callbackRan);
		AssertInvokedOnlyThrough(form, invoker);
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractRejectsCancelledWorkBeforeDispatchAdmission(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		bool callbackRan = false;

		DispatchInvocation invocation = TryInvoke(form, dispatcher, () => callbackRan = true, cancellation.Token);

		Assert.False(invocation.Succeeded);
		Assert.Equal(DefaultResultForFailedInvocation(form), invocation.Result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, invocation.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", invocation.Failure.Operation);
		Assert.False(callbackRan);
		Assert.Equal(0, invoker.InvocationCount);
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractRejectsAnExpiredActivationBeforeDispatchAdmission(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		bool callbackRan = false;

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			TryInvoke(form, dispatcher, () => callbackRan = true, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", exception.Failure.Operation);
		Assert.False(callbackRan);
		Assert.Equal(0, invoker.InvocationCount);
	}

	[Theory]
	[InlineData(DispatchForm.Action)]
	[InlineData(DispatchForm.Function)]
	[InlineData(DispatchForm.StatefulFunction)]
	public void TryInvokeContractPrioritizesAnExpiredActivationOverPreAdmissionCancellation(DispatchForm form)
	{
		using ControlledCoreLifetimeContext context = new()
		{
			IsCurrent = false
		};
		using CoreLifetime lifetime = new(context);
		RecordingMainThreadInvoker invoker = new();
		SdkMainThreadDispatcher dispatcher = new(lifetime, invoker);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			TryInvoke(form, dispatcher, static () =>
			{
			}, cancellation.Token));

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", exception.Failure.Operation);
		Assert.Equal(0, invoker.InvocationCount);
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

	private static DispatchInvocation TryInvoke(
		DispatchForm form,
		SdkMainThreadDispatcher dispatcher,
		Action callback,
		CancellationToken cancellationToken)
	{
		switch (form)
		{
			case DispatchForm.Action:
				{
					bool succeeded = dispatcher.TryInvoke(callback, out CheatEngineFailure failure, cancellationToken);
					return new DispatchInvocation(succeeded, null, failure);
				}
			case DispatchForm.Function:
				{
					bool succeeded = dispatcher.TryInvoke(
						() =>
						{
							callback();
							return 42;
						},
						out int result,
						out CheatEngineFailure failure,
						cancellationToken);
					return new DispatchInvocation(succeeded, result, failure);
				}
			case DispatchForm.StatefulFunction:
				{
#pragma warning disable CA1859 // This helper intentionally exercises the internal stateful dispatch contract.
					IStatefulCheatEngineDispatcher statefulDispatcher = dispatcher;
#pragma warning restore CA1859
					CallbackState state = new(callback, 42);
					bool succeeded = statefulDispatcher.TryInvoke(
						state,
						static current =>
						{
							current.Callback();
							return current.Result;
						},
						out int result,
						out CheatEngineFailure failure,
						cancellationToken);
					return new DispatchInvocation(succeeded, result, failure);
				}
			default:
				throw new ArgumentOutOfRangeException(nameof(form), form, null);
		}
	}

	private static void AssertInvokedOnlyThrough(DispatchForm form, RecordingMainThreadInvoker invoker)
	{
		Assert.Equal(form == DispatchForm.Action ? 1 : 0, invoker.ActionCalls);
		Assert.Equal(form == DispatchForm.Function ? 1 : 0, invoker.FunctionCalls);
		Assert.Equal(form == DispatchForm.StatefulFunction ? 1 : 0, invoker.StateFunctionCalls);
	}

	private static int? DefaultResultForFailedInvocation(DispatchForm form)
	{
		return form == DispatchForm.Action ? null : 0;
	}

	private readonly record struct CallbackState(Action Callback, int Result);

	private readonly record struct DispatchInvocation(bool Succeeded, int? Result, CheatEngineFailure Failure);

	private sealed class RecordingMainThreadInvoker : IMainThreadInvoker
	{
		internal int InvocationCount => ActionCalls + FunctionCalls + StateFunctionCalls;

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
