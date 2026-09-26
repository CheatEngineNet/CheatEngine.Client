using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Dispatching;

public sealed class SdkMainThreadDispatcherTests
{
	[Fact]
	public void TryInvokeRejectsNullCallbacksBeforeEnteringThePluginDispatchGate()
	{
		SdkMainThreadDispatcher dispatcher = new(InertCoreLifetime.Create());

		ArgumentNullException actionException = Assert.Throws<ArgumentNullException>(() =>
			dispatcher.TryInvoke(null!, out CheatEngineFailure _, TestContext.Current.CancellationToken));
		ArgumentNullException functionException = Assert.Throws<ArgumentNullException>(() =>
			dispatcher.TryInvoke<int>(null!, out _, out CheatEngineFailure _, TestContext.Current.CancellationToken));

		Assert.Equal("callback", actionException.ParamName);
		Assert.Equal("callback", functionException.ParamName);
	}

	[Fact]
	public void InvokeConvertsARejectedDispatchToThePublicFailureException()
	{
		using ControlledCoreLifetimeContext context = new();
		using CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime);
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			dispatcher.Invoke(static () =>
			{
			}, cancellation.Token));

		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(CheatEngineHostEffect.NotStarted, exception.Failure.HostEffect);
		Assert.Equal(CheatEngineFailureKind.Cancelled, exception.Failure.Kind);
		Assert.Equal("Dispatcher.Invoke", exception.Failure.Operation);
	}

	[Fact]
	public void MainThreadPropertyRemainsFalseOutsideTheActualSdkMainThread()
	{
		using ControlledCoreLifetimeContext context = new()
		{
			IsMainThread = true
		};
		using CoreLifetime lifetime = new(context);
		SdkMainThreadDispatcher dispatcher = new(lifetime);

		Assert.False(dispatcher.IsMainThread);
	}
}
