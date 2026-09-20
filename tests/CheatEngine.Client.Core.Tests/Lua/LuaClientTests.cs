using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

public sealed class LuaClientTests
{
	[Fact]
	public void TryExecuteProvidesTheCapturedEpochOnlyDuringItsSynchronousOperation()
	{
		ImmediateDispatcher dispatcher = new();
		RetainingOperation operation = new();
		LuaClient client = new(dispatcher, static () => 42, static () => true);

		bool succeeded = client.TryExecute<int>(operation, out int result,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(42, result);
		Assert.Equal(default, failure);
		Assert.Equal(1, dispatcher.InvocationCount);
		ILuaExecutionContext? context = operation.Context;
		Assert.NotNull(context);
		Assert.Equal(42, context.Epoch);
		Assert.False(context.IsActive);
		Assert.Throws<CheatEngineActivationExpiredException>(context.ThrowIfExpired);
	}

	[Fact]
	public void TryExecutePreservesAnExpectedOperationFailure()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.LuaError, "Lua.Custom", "The Lua global failed.");
		LuaClient client = new(new ImmediateDispatcher(), static () => 1, static () => true);

		bool succeeded = client.TryExecute<int>(new FailingOperation(expected), out int result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(0, result);
		Assert.Equal(expected, failure);
	}

	[Fact]
	public void TryExecuteNormalizesAnOperationFailureWithoutDetails()
	{
		LuaClient client = new(new ImmediateDispatcher(), static () => 1, static () => true);

		bool succeeded = client.TryExecute<int>(new FailingOperation(default), out _,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.Unknown, failure.Kind);
		Assert.Equal("Lua.Execute", failure.Operation);
		Assert.Contains("without an associated", failure.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ExecuteTurnsAnExpectedFailureIntoTheClientException()
	{
		CheatEngineFailure expected = new(CheatEngineFailureKind.LuaError, "Lua.Custom", "The Lua global failed.");
		LuaClient client = new(new ImmediateDispatcher(), static () => 1, static () => true);

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.Execute<int>(new FailingOperation(expected),
				TestContext.Current.CancellationToken));

		Assert.Equal(expected, exception.Failure);
	}

	[Fact]
	public void TryExecuteThrowsActivationExpiredWhenTheContextCannotEnterTheCurrentActivation()
	{
		RetainingOperation operation = new();
		LuaClient client = new(new ImmediateDispatcher(), static () => 7, static () => false);

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			client.TryExecute<int>(operation, out _, out _, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.ActivationExpired, exception.Failure.Kind);
		Assert.Equal("Lua.OperationContext", exception.Failure.Operation);
		Assert.Null(operation.Context);
	}

	[Fact]
	public void TryExecuteHonorsCancellationBeforeTheTypedOperationRuns()
	{
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		ImmediateDispatcher dispatcher = new();
		RetainingOperation operation = new();
		LuaClient client = new(dispatcher, static () => 7, static () => true);

		bool succeeded = client.TryExecute<int>(
			operation,
			out _,
			out CheatEngineFailure failure,
			cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(0, dispatcher.InvocationCount);
		Assert.Null(operation.Context);
	}

	private sealed class RetainingOperation : ILuaOperation<int>
	{
		internal ILuaExecutionContext? Context
		{
			get;
			private set;
		}

		public bool TryExecute(ILuaExecutionContext context, out int result, out CheatEngineFailure failure)
		{
			context.ThrowIfExpired();
			Context = context;
			result = checked((int) context.Epoch);
			failure = default;
			return true;
		}
	}

	private sealed class FailingOperation(CheatEngineFailure expectedFailure) : ILuaOperation<int>
	{
		public bool TryExecute(ILuaExecutionContext context, out int result, out CheatEngineFailure failure)
		{
			context.ThrowIfExpired();
			result = default;
			failure = expectedFailure;
			return false;
		}
	}

	private sealed class ImmediateDispatcher : ICheatEngineDispatcher
	{
		public int InvocationCount
		{
			get;
			private set;
		}

		public bool IsMainThread => true;

		public bool TryInvoke(Action callback, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatch", "Cancelled.");
				return false;
			}

			InvocationCount++;
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<TResult>(Func<TResult> callback, out TResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (cancellationToken.IsCancellationRequested)
			{
				result = default!;
				failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, "Test.Dispatch", "Cancelled.");
				return false;
			}

			InvocationCount++;
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			if (!TryInvoke(callback, out CheatEngineFailure failure, cancellationToken))
			{
				failure.Throw();
			}
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			if (TryInvoke(callback, out var result, out var failure, cancellationToken))
			{
				return result;
			}

			failure.Throw();
			return default!;
		}
	}
}
