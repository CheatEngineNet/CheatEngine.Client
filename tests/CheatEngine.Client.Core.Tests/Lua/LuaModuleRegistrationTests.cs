using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

public sealed class LuaModuleRegistrationTests
{
	[Fact]
	public void TryRegisterModuleTracksItsLeaseAndDisposesItThroughTheMainThreadDispatcher()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.NotNull(lease);
		Assert.Equal(81, lease.Epoch);
		Assert.False(lease.IsReleased);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
		Assert.Single(tracked, lease);

		lease.Dispose();
		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		Assert.Equal(2, dispatcher.InvocationCount);
		Assert.Empty(tracked);
	}

	[Fact]
	public void TryRegisterModuleRejectsTheSameModuleInstanceUntilItsLeaseIsReleased()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true);

		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? firstLease, out _,
			TestContext.Current.CancellationToken));
		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? duplicateLease,
			out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(duplicateLease);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal(["diagnostics.register"], module.Events);

		firstLease.Dispose();

		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void RegisterModuleReturnsTheLeaseAndThrowsTheMappedFailureForARejectedRegistration()
	{
		ImmediateDispatcher dispatcher = new();
		LuaClient client = CreateClient(dispatcher, static () => true);
		RecordingModule accepted = new("accepted");
		RecordingModule rejected = new("rejected", new InvalidOperationException("generated registration failed"));

		using ILuaModuleLease lease = client.RegisterModule(accepted, TestContext.Current.CancellationToken);
		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			client.RegisterModule(rejected, TestContext.Current.CancellationToken));

		Assert.Equal(81, lease.Epoch);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, exception.Failure.Kind);
		Assert.Equal("Lua.RegisterModule", exception.Failure.Operation);
		Assert.Equal(["accepted.register"], accepted.Events);
		Assert.Equal(["rejected.register"], rejected.Events);
	}

	[Fact]
	public void TryRegisterModuleMapsAnApplicationModuleFailureAndDoesNotCreateALease()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics", new InvalidOperationException("generated registration failed"));
		LuaClient client = CreateClient(dispatcher, static () => true);

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Lua.RegisterModule", failure.Operation);
		Assert.Equal("generated registration failed", failure.Message);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void TryRegisterModuleRollsBackTheGeneratedRegistrationWhenTheActivationCannotTrackItsLease()
	{
		ImmediateDispatcher dispatcher = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			static _ => throw new InvalidOperationException("activation is closed"));

		bool succeeded = client.TryRegisterModule(module, out ILuaModuleLease? lease, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Null(lease);
		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
		Assert.Equal(2, dispatcher.InvocationCount);
	}

	[Fact]
	public void ForgottenModuleLeasesReleaseInReverseRegistrationOrderFromTheActivationResourceRegistry()
	{
		ImmediateDispatcher dispatcher = new();
		CoreResourceRegistry registry = new();
		List<string> events = new();
		RecordingModule first = new("first", events: events);
		RecordingModule second = new("second", events: events);
		LuaClient client = CreateClient(
			dispatcher,
			static () => true,
			lease =>
			{
				registry.Track(lease);
			},
			lease =>
			{
				registry.Untrack(lease);
			});

		Assert.True(client.TryRegisterModule(first, out _, out _, TestContext.Current.CancellationToken));
		Assert.True(client.TryRegisterModule(second, out _, out _, TestContext.Current.CancellationToken));

		registry.Dispose();

		Assert.Equal(["first.register", "second.register", "second.unregister", "first.unregister"], events);
		Assert.Equal(4, dispatcher.InvocationCount);
	}

	[Fact]
	public void StaleModuleLeaseDoesNotAttemptToDispatchIntoAChangedActivation()
	{
		ImmediateDispatcher dispatcher = new();
		bool activationIsCurrent = true;
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, () => activationIsCurrent);

		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		activationIsCurrent = false;

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Equal(["diagnostics.register"], module.Events);
		Assert.Equal(1, dispatcher.InvocationCount);
	}

	[Fact]
	public void DispatcherRejectionLeavesTheLeaseTrackedForTheLaterCleanupDispatch()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = new();
		RecordingModule module = new("diagnostics");
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});

		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));
		dispatcher.InvokeException =
			new CheatEngineClientLifecycleException("Dispatcher.Invoke", "Activation stopping.");

		Assert.Throws<CheatEngineClientLifecycleException>(lease.Dispose);

		Assert.False(lease.IsReleased);
		Assert.Single(tracked, lease);
		Assert.Equal(["diagnostics.register"], module.Events);

		dispatcher.InvokeException = null;
		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Empty(tracked);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);
	}

	[Fact]
	public void FailedLeaseDisposeRemainsTrackedAndCanBeRetriedByTheActivationCleanupPath()
	{
		ImmediateDispatcher dispatcher = new();
		List<ILuaModuleLease> tracked = new();
		RecordingModule module = new("diagnostics", unregisterFailureCount: 1);
		LuaClient client = CreateClient(dispatcher, static () => true, tracked.Add, lease =>
		{
			tracked.Remove(lease);
		});

		Assert.True(client.TryRegisterModule(module, out ILuaModuleLease? lease, out _,
			TestContext.Current.CancellationToken));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(lease.Dispose);

		Assert.Equal("generated unregistration failed", exception.Message);
		Assert.False(lease.IsReleased);
		Assert.Single(tracked, lease);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister"], module.Events);

		lease.Dispose();

		Assert.True(lease.IsReleased);
		Assert.Empty(tracked);
		Assert.Equal(["diagnostics.register", "diagnostics.unregister", "diagnostics.unregister"], module.Events);
		Assert.Equal(3, dispatcher.InvocationCount);
	}

	private static LuaClient CreateClient(
		ICheatEngineDispatcher dispatcher,
		Func<bool> isActivationCurrent,
		Action<ILuaModuleLease>? trackLease = null,
		Action<ILuaModuleLease>? untrackLease = null)
	{
		return new LuaClient(dispatcher, static () => 81, isActivationCurrent, trackLease, untrackLease);
	}

	private sealed class RecordingModule(
		string name,
		Exception? registerException = null,
		List<string>? events = null,
		int unregisterFailureCount = 0) : ILuaModule
	{
		private int _remainingUnregisterFailures = unregisterFailureCount;

		internal List<string> Events
		{
			get;
		} = events ?? [];

		public void Register()
		{
			Events.Add(name + ".register");
			if (registerException is not null)
			{
				throw registerException;
			}
		}

		public void Unregister()
		{
			Events.Add(name + ".unregister");
			if (_remainingUnregisterFailures-- > 0)
			{
				throw new InvalidOperationException("generated unregistration failed");
			}
		}
	}

	private sealed class ImmediateDispatcher : ICheatEngineDispatcher
	{
		public int InvocationCount
		{
			get;
			private set;
		}

		internal Exception? InvokeException
		{
			get;
			set;
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
			if (InvokeException is not null)
			{
				throw InvokeException;
			}

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
