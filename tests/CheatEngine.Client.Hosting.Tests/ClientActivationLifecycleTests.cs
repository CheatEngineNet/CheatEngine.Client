using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Processes;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;

namespace CheatEngine.Client.Hosting.Tests;

public sealed class ClientActivationLifecycleTests
{
	[Fact]
	public void EnableThenCleanupRunsTheApplicationHookAndModulesInTheirSpecifiedOrder()
	{
		List<string> events = [];
		RecordingModule first = new("first", events);
		RecordingModule second = new("second", events);
		ClientActivationLifecycle lifecycle = CreateLifecycle(events, out ICheatEngineClient client, first, second);

		lifecycle.Enable(_ => events.Add("application.enabled"));
		List<Exception> failures = lifecycle.Cleanup(_ => events.Add("application.disabling"));

		Assert.Empty(failures);
		Assert.Equal(
			[
				"first.enabled", "second.enabled", "application.enabled", "application.disabling", "second.disabling",
				"first.disabling"
			],
			events);
		Assert.Same(client, first.LastClient);
		Assert.Same(client, second.LastClient);
	}

	[Fact]
	[Trait("Qualification", "Q06")]
	public void FailedModuleEnableStillCompensatesTheFailingModuleThenEarlierModulesInReverseOrder()
	{
		List<string> events = [];
		ClientActivationLifecycle lifecycle = CreateLifecycle(events, out _,
			new RecordingModule("first", events),
			new RecordingModule("second", events, new InvalidOperationException("module enable")));

		InvalidOperationException exception =
			Assert.Throws<InvalidOperationException>(() => lifecycle.Enable(_ => events.Add("application.enabled")));
		List<Exception> failures = lifecycle.Cleanup(_ => events.Add("application.disabling"));

		Assert.Equal("module enable", exception.Message);
		Assert.Empty(failures);
		Assert.Equal(["first.enabled", "second.enabled", "second.disabling", "first.disabling"], events);
	}

	[Fact]
	public void FailedApplicationEnableStillRunsItsCompensatingHookBeforeModuleCleanup()
	{
		List<string> events = [];
		ClientActivationLifecycle lifecycle = CreateLifecycle(events, out _, new RecordingModule("module", events));

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => lifecycle.Enable(_ =>
		{
			events.Add("application.enabled");
			throw new InvalidOperationException("application enable");
		}));
		List<Exception> failures = lifecycle.Cleanup(_ => events.Add("application.disabling"));

		Assert.Equal("application enable", exception.Message);
		Assert.Empty(failures);
		Assert.Equal(["module.enabled", "application.enabled", "application.disabling", "module.disabling"], events);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void CleanupContinuesAfterFailuresAndIsIdempotent()
	{
		List<string> events = [];
		ClientActivationLifecycle lifecycle = CreateLifecycle(events, out _,
			new RecordingModule("first", events, disableFailure: new InvalidOperationException("first disable")),
			new RecordingModule("second", events, disableFailure: new InvalidOperationException("second disable")));
		lifecycle.Enable(_ => events.Add("application.enabled"));

		List<Exception> failures = lifecycle.Cleanup(_ =>
		{
			events.Add("application.disabling");
			throw new InvalidOperationException("application disable");
		});
		List<Exception> repeatedFailures = lifecycle.Cleanup(_ => events.Add("unexpected"));

		Assert.Collection(
			failures,
			failure => Assert.Equal("application disable", failure.Message),
			failure => Assert.Equal("second disable", failure.Message),
			failure => Assert.Equal("first disable", failure.Message));
		Assert.Empty(repeatedFailures);
		Assert.Equal(
			[
				"first.enabled", "second.enabled", "application.enabled", "application.disabling", "second.disabling",
				"first.disabling"
			],
			events);
	}

	private static ClientActivationLifecycle CreateLifecycle(
		List<string> events,
		out ICheatEngineClient client,
		params ICheatEngineClientModule[] modules)
	{
		ArgumentNullException.ThrowIfNull(events);
		client = new FakeClient();
		return new ClientActivationLifecycle(client, modules);
	}

	private sealed class RecordingModule(
		string name,
		List<string> events,
		Exception? enableFailure = null,
		Exception? disableFailure = null) : ICheatEngineClientModule
	{
		internal ICheatEngineClient? LastClient
		{
			get;
			private set;
		}

		public void OnEnabled(ICheatEngineClient client)
		{
			LastClient = client;
			events.Add($"{name}.enabled");
			if (enableFailure is not null)
			{
				throw enableFailure;
			}
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			LastClient = client;
			events.Add($"{name}.disabling");
			if (disableFailure is not null)
			{
				throw disableFailure;
			}
		}
	}

	private sealed class FakeClient : ICheatEngineClient
	{
		public long Epoch => 42;
		public CancellationToken Stopping => CancellationToken.None;
		public ICheatEngineRuntime Runtime => null!;
		public ICheatEngineDispatcher Dispatcher => null!;
		public IProcessClient Processes => null!;
		public IMemoryClient Memory => null!;
		public IPatternScanner Patterns => null!;
		public IValueScanner Scans => null!;
		public IInspectionClient Inspection => null!;
		public ITableClient Tables => null!;
		public ILuaClient Lua => null!;
		public IAllocationClient Allocations => null!;
		public IAssemblyClient Assembly => null!;
		public IRemoteExecutionClient RemoteExecution => null!;
		public IDebuggerClient Debugger => null!;
		public IHotkeyClient Hotkeys => null!;
		public ITimerClient Timers => null!;
		public ISpeedClient Speed => null!;
		public IHashingClient Hashing => null!;
		public IDbvmClient Dbvm => null!;
	}
}
