using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

public sealed class UnsafeLuaClientTests
{
	[Fact]
	public void TryExecuteRejectsUnsafeLuaBeforeDispatchWhenTheActivationDidNotOptIn()
	{
		RecordingDispatcher dispatcher = new();
		UnsafeLuaClient client = new(dispatcher, new CoreClientPolicy([], false));

		bool succeeded = client.TryExecute(new LuaScript("return 42"), out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("UnsafeLua.Execute", failure.Operation);
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryExecuteRejectsTheDefaultLuaScriptBeforeCheckingOrDispatchingUnsafeExecution()
	{
		RecordingDispatcher dispatcher = new();
		UnsafeLuaClient client = new(dispatcher, new CoreClientPolicy([], true));

		Assert.Throws<ArgumentNullException>(() =>
			client.TryExecute(default, out _, TestContext.Current.CancellationToken));
		Assert.Equal(0, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryExecuteReportsARefusedLuaAdmissionFromTheSdkStatusWithoutRunningTheScript()
	{
		RecordingDispatcher dispatcher = new();
		UnsafeLuaClient client = new(dispatcher, new CoreClientPolicy([], true));

		// No Lua runtime is attached in unit tests: CheatEngine.SDK reports the admission as Detached.
		bool succeeded = client.TryExecute(new LuaScript("return 42"), out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(1, dispatcher.InvocationCount);
		Assert.Equal(CheatEngineFailureKind.ActivationExpired, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("UnsafeLua.Execute", failure.Operation);
		Assert.Null(failure.Exception);
		Assert.NotEqual(CheatEngineFailureKind.OperationRejected, failure.Kind);
	}

	[Fact]
	public void ExecuteThrowsTheActivationExpiredExceptionForARefusedLuaAdmission()
	{
		UnsafeLuaClient client = new(new RecordingDispatcher(), new CoreClientPolicy([], true));

		CheatEngineActivationExpiredException exception = Assert.Throws<CheatEngineActivationExpiredException>(() =>
			client.Execute(new LuaScript("return 42"), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineHostEffect.NotStarted, exception.Failure.HostEffect);
	}

	private sealed class RecordingDispatcher : ICheatEngineDispatcher
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
			InvocationCount++;
			callback();
			failure = default;
			return true;
		}

		public bool TryInvoke<TResult>(Func<TResult> callback, out TResult result, out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			InvocationCount++;
			result = callback();
			failure = default;
			return true;
		}

		public void Invoke(Action callback, CancellationToken cancellationToken = default)
		{
			callback();
		}

		public TResult Invoke<TResult>(Func<TResult> callback, CancellationToken cancellationToken = default)
		{
			return callback();
		}
	}
}
