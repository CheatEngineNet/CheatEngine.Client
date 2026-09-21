using System.Reflection;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Processes;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.Hosting.Tests;

public sealed class CheatEngineClientPluginTests
{
	[Fact]
	public void ProtectedBaseConstructorAllowsAPublicParameterlessConcretePlugin()
	{
		ConstructorInfo? baseConstructor = typeof(CheatEngineClientPlugin).GetConstructor(
			BindingFlags.Instance | BindingFlags.NonPublic,
			null,
			Type.EmptyTypes,
			null);
		ConstructorInfo? concreteConstructor = typeof(TestPlugin).GetConstructor(Type.EmptyTypes);

		Assert.NotNull(baseConstructor);
		Assert.True(baseConstructor.IsFamily);
		Assert.NotNull(concreteConstructor);
		Assert.True(concreteConstructor.IsPublic);
		Assert.NotNull(new TestPlugin());
	}

	[Fact]
	public void GetRequiredClientWithoutAnActiveEnableEpochThrowsLifecycleException()
	{
		TestPlugin plugin = new();

		CheatEngineClientLifecycleException exception =
			Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		Assert.Equal(CheatEngineFailureKind.InvalidState, exception.Failure.Kind);
		Assert.Equal("GetClient", exception.Failure.Operation);
		Assert.Contains("only while the plugin is enabled", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void EnableThenDisableUsesOneScopedClientLifecycleAndReleasesCleanupResourcesInOrder()
	{
		List<string> events = [];
		FakeClient client = new(42);
		RecordingCleanup cleanup = new(events);
		TestPlugin plugin = CreatePlugin(events, client, cleanup,
			static builder => builder.Client.AddModule<RecordingModule>());

		plugin.EnableForTest();

		Assert.Same(client, plugin.GetRequiredClientForTest());
		CheatEngineClientLifecycleException duplicateEnable =
			Assert.Throws<CheatEngineClientLifecycleException>(plugin.EnableForTest);
		Assert.Equal("EnableClient", duplicateEnable.Failure.Operation);

		plugin.DisableForTest();

		Assert.Equal(
			[
				"configure", "module.enabled", "client.enabled", "cleanup.enter", "client.disabling",
				"module.disabling", "cleanup.drain", "cleanup.exit"
			],
			events);
		Assert.Equal(1, cleanup.DrainCount);
		Assert.Equal(1, cleanup.ScopeDisposeCount);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);
	}

	[Fact]
	public void FailedModuleEnableRollsBackAllEnteredModulesAndLeavesThePluginInactive()
	{
		List<string> events = [];
		FakeClient client = new(43);
		RecordingCleanup cleanup = new(events);
		TestPlugin plugin = CreatePlugin(events, client, cleanup, static builder =>
		{
			builder.Client.AddModule<RecordingModule>().AddModule<FailingEnableModule>();
		});

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(plugin.EnableForTest);

		Assert.Equal("module enable", exception.Message);
		Assert.Equal(
			[
				"configure", "module.enabled", "module.enable-failed", "cleanup.enter", "module.disable-failed",
				"module.disabling", "cleanup.drain", "cleanup.exit"
			],
			events);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		plugin.DisableForTest();

		Assert.Equal(1, cleanup.DrainCount);
	}

	[Fact]
	public void DefaultApplicationCallbacksParticipateInTheActivationLifecycle()
	{
		List<string> events = [];
		FakeClient client = new(46);
		RecordingCleanup cleanup = new(events);
		DefaultCallbacksPlugin plugin = new(CreateConfiguration(events, client, cleanup, static _ =>
		{
		}));

		plugin.EnableForTest();

		Assert.Same(client, plugin.GetRequiredClientForTest());

		plugin.DisableForTest();

		Assert.Equal(["configure", "cleanup.enter", "cleanup.drain", "cleanup.exit"], events);
	}

	[Fact]
	public void DisableRethrowsOneCleanupScopeFailureAfterClosingTheActivation()
	{
		List<string> events = [];
		FakeClient client = new(47);
		RecordingCleanup cleanup = new(events, new InvalidOperationException("cleanup scope"));
		TestPlugin plugin = CreatePlugin(events, client, cleanup, static _ =>
		{
		});
		plugin.EnableForTest();

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(plugin.DisableForTest);

		Assert.Equal("cleanup scope", exception.Message);
		Assert.Equal(["configure", "client.enabled", "cleanup.enter"], events);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);
	}

	[Fact]
	public void ApplicationEnableFailureAndCleanupFailureAreReportedTogetherAfterRollback()
	{
		List<string> events = [];
		FakeClient client = new(44);
		RecordingCleanup cleanup = new(events, drainFailure: new InvalidOperationException("drain"));
		TestPlugin plugin = CreatePlugin(events, client, cleanup,
			static builder => builder.Client.AddModule<RecordingModule>(),
			static _ => throw new InvalidOperationException("application enable"));

		AggregateException exception = Assert.Throws<AggregateException>(plugin.EnableForTest);

		Assert.Collection(
			exception.InnerExceptions,
			failure => Assert.Equal("application enable", failure.Message),
			failure => Assert.Equal("drain", failure.Message));
		Assert.Equal(
			[
				"configure", "module.enabled", "cleanup.enter", "client.disabling", "module.disabling", "cleanup.drain",
				"cleanup.exit"
			],
			events);
		Assert.Equal(1, cleanup.ScopeDisposeCount);
	}

	[Fact]
	public void DisableAggregatesApplicationModuleAndResourceCleanupFailures()
	{
		List<string> events = [];
		FakeClient client = new(45);
		RecordingCleanup cleanup = new(events, drainFailure: new InvalidOperationException("drain"));
		TestPlugin plugin = CreatePlugin(events, client, cleanup,
			static builder => builder.Client.AddModule<FailingDisableModule>(),
			onClientDisabling: _ =>
			{
				events.Add("client.disabling");
				throw new InvalidOperationException("application disable");
			});
		plugin.EnableForTest();

		AggregateException exception = Assert.Throws<AggregateException>(plugin.DisableForTest);

		Assert.Collection(
			exception.InnerExceptions,
			failure => Assert.Equal("application disable", failure.Message),
			failure => Assert.Equal("module disable", failure.Message),
			failure => Assert.Equal("drain", failure.Message));
		Assert.Equal(
			[
				"configure", "module.enabled", "client.enabled", "cleanup.enter", "client.disabling",
				"module.disabling",
				"cleanup.drain", "cleanup.exit"
			],
			events);
		Assert.Equal(1, cleanup.DrainCount);
	}

	[Fact]
	public void ConfigureFailureDoesNotPublishAnActivation()
	{
		int configureCalls = 0;
		TestPlugin plugin = new(_ =>
		{
			configureCalls++;
			throw new InvalidOperationException("configuration");
		});

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(plugin.EnableForTest);

		Assert.Equal("configuration", exception.Message);
		Assert.Equal(1, configureCalls);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		plugin.DisableForTest();
	}

	[Fact]
	public void FailedConstructionCleansEveryAcquiredStageAndAllowsANewEnableEpoch()
	{
		List<string> events = [];
		FakeClient client = new(48);
		RecordingCleanup cleanup = new(events);
		int configureAttempt = 0;
		TestPlugin plugin = new(builder =>
		{
			events.Add("configure");
			if (configureAttempt++ == 0)
			{
				AddFailingConstructionRegistrations(builder, events);
				return;
			}

			builder.Services.AddSingleton<ICheatEngineClient>(client);
			builder.Services.AddSingleton<ICheatEngineClientActivationCleanup>(cleanup);
		});

		AggregateException exception = Assert.Throws<AggregateException>(plugin.EnableForTest);

		Assert.Collection(
			exception.InnerExceptions,
			failure => Assert.Equal("client resolution", failure.Message),
			failure => Assert.Equal("scope dispose", failure.Message),
			failure => Assert.Equal("provider dispose", failure.Message),
			failure => Assert.Equal("configuration dispose", failure.Message));
		Assert.Equal(
			["configure", "client.resolve", "scope.dispose", "provider.dispose", "configuration.dispose"],
			events);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		plugin.DisableForTest();
		plugin.EnableForTest();

		Assert.Same(client, plugin.GetRequiredClientForTest());
		plugin.DisableForTest();
		Assert.Equal(1, cleanup.DrainCount);
		Assert.Equal(2, configureAttempt);
	}

	[Fact]
	public void ConfigureFailureRemainsPrimaryWhenConfigurationReleaseAlsoFails()
	{
		List<string> events = [];
		TestPlugin plugin = new(builder =>
		{
			builder.Configuration.Sources.Add(new ThrowingDisposeConfigurationSource(events));
			throw new InvalidOperationException("configuration");
		});

		AggregateException exception = Assert.Throws<AggregateException>(plugin.EnableForTest);

		Assert.Collection(
			exception.InnerExceptions,
			failure => Assert.Equal("configuration", failure.Message),
			failure => Assert.Equal("configuration dispose", failure.Message));
		Assert.Equal(["configuration.dispose"], events);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);
	}

	[Fact]
	public void FailedModuleEnableRollsBackAndTheSamePluginCanEnableAgain()
	{
		List<string> events = [];
		FakeClient client = new(49);
		RecordingCleanup cleanup = new(events);
		FailOnceEnableState state = new();
		TestPlugin plugin = CreatePlugin(events, client, cleanup, builder =>
		{
			builder.Services.AddSingleton(state);
			builder.Client.AddModule<FailOnceEnableModule>();
		});

		InvalidOperationException failure = Assert.Throws<InvalidOperationException>(plugin.EnableForTest);

		Assert.Equal("module enable", failure.Message);
		Assert.Equal(
			[
				"configure", "module.enabled", "cleanup.enter", "module.disabling", "cleanup.drain", "cleanup.exit"
			],
			events);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);

		plugin.EnableForTest();
		Assert.Same(client, plugin.GetRequiredClientForTest());
		plugin.DisableForTest();

		Assert.Equal(2, cleanup.DrainCount);
		Assert.Equal(
			[
				"configure", "module.enabled", "cleanup.enter", "module.disabling", "cleanup.drain", "cleanup.exit",
				"configure", "module.enabled", "client.enabled", "cleanup.enter", "client.disabling", "module.disabling",
				"cleanup.drain", "cleanup.exit"
			],
			events);
	}

	private static void AddFailingConstructionRegistrations(CheatEnginePluginBuilder builder, List<string> events)
	{
		builder.Configuration.Sources.Add(new ThrowingDisposeConfigurationSource(events));
		builder.Services.AddScoped<ThrowingScopedDisposable>(_ => new ThrowingScopedDisposable(events));
		builder.Services.AddSingleton<ThrowingSingletonDisposable>(_ => new ThrowingSingletonDisposable(events));
		builder.Services.AddScoped<ICheatEngineClient>(services =>
		{
			_ = services.GetRequiredService<ThrowingScopedDisposable>();
			_ = services.GetRequiredService<ThrowingSingletonDisposable>();
			events.Add("client.resolve");
			throw new InvalidOperationException("client resolution");
		});
	}

	private static TestPlugin CreatePlugin(
		List<string> events,
		FakeClient client,
		RecordingCleanup cleanup,
		Action<CheatEnginePluginBuilder> configure,
		Action<ICheatEngineClient>? onClientEnabled = null,
		Action<ICheatEngineClient>? onClientDisabling = null)
	{
		return new TestPlugin(CreateConfiguration(events, client, cleanup, configure),
			onClientEnabled ?? (currentClient => events.Add("client.enabled")),
			onClientDisabling ?? (currentClient => events.Add("client.disabling")));
	}

	private static Action<CheatEnginePluginBuilder> CreateConfiguration(
		List<string> events,
		FakeClient client,
		RecordingCleanup cleanup,
		Action<CheatEnginePluginBuilder> configure)
	{
		return builder =>
		{
			events.Add("configure");
			builder.Services.AddSingleton(events);
			builder.Services.AddSingleton<ICheatEngineClient>(client);
			builder.Services.AddSingleton<ICheatEngineClientActivationCleanup>(cleanup);
			configure(builder);
		};
	}

	private sealed class TestPlugin : CheatEngineClientPlugin
	{
		private readonly Action<CheatEnginePluginBuilder> _configure;
		private readonly Action<ICheatEngineClient> _onClientDisabling;
		private readonly Action<ICheatEngineClient> _onClientEnabled;

		public TestPlugin()
			: this(static _ =>
			{
			})
		{
		}

		internal TestPlugin(
			Action<CheatEnginePluginBuilder> configure,
			Action<ICheatEngineClient>? onClientEnabled = null,
			Action<ICheatEngineClient>? onClientDisabling = null)
		{
			_configure = configure;
			_onClientEnabled = onClientEnabled ?? (static _ =>
			{
			});
			_onClientDisabling = onClientDisabling ?? (static _ =>
			{
			});
		}

		protected override void Configure(CheatEnginePluginBuilder builder)
		{
			_configure(builder);
		}

		protected override void OnClientEnabled(ICheatEngineClient client)
		{
			_onClientEnabled(client);
		}

		protected override void OnClientDisabling(ICheatEngineClient client)
		{
			_onClientDisabling(client);
		}

		internal void EnableForTest()
		{
			OnEnable();
		}

		internal void DisableForTest()
		{
			OnDisable();
		}

		internal ICheatEngineClient GetRequiredClientForTest()
		{
			return GetRequiredClient();
		}
	}

	private sealed class DefaultCallbacksPlugin(Action<CheatEnginePluginBuilder> configure) : CheatEngineClientPlugin
	{
		protected override void Configure(CheatEnginePluginBuilder builder)
		{
			configure(builder);
		}

		internal void EnableForTest()
		{
			OnEnable();
		}

		internal void DisableForTest()
		{
			OnDisable();
		}

		internal ICheatEngineClient GetRequiredClientForTest()
		{
			return GetRequiredClient();
		}
	}

	public sealed class RecordingModule(List<string> events) : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
			events.Add("module.enabled");
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			events.Add("module.disabling");
		}
	}

	public sealed class FailingEnableModule(List<string> events) : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
			events.Add("module.enable-failed");
			throw new InvalidOperationException("module enable");
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			events.Add("module.disable-failed");
		}
	}

	public sealed class FailingDisableModule(List<string> events) : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
			events.Add("module.enabled");
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			events.Add("module.disabling");
			throw new InvalidOperationException("module disable");
		}
	}

	public sealed class FailOnceEnableModule(List<string> events, FailOnceEnableState state) : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
			events.Add("module.enabled");
			if (!state.HasFailed)
			{
				state.HasFailed = true;
				throw new InvalidOperationException("module enable");
			}
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			events.Add("module.disabling");
		}
	}

	public sealed class FailOnceEnableState
	{
		public bool HasFailed
		{
			get;
			set;
		}
	}

	private sealed class RecordingCleanup(
		List<string> events,
		Exception? enterFailure = null,
		Exception? drainFailure = null)
		: ICheatEngineClientActivationCleanup
	{
		internal int DrainCount
		{
			get;
			private set;
		}

		internal int ScopeDisposeCount
		{
			get;
			private set;
		}

		public IDisposable EnterCleanupScope()
		{
			events.Add("cleanup.enter");
			if (enterFailure is not null)
			{
				throw enterFailure;
			}

			return new CallbackDisposable(() =>
			{
				ScopeDisposeCount++;
				events.Add("cleanup.exit");
			});
		}

		public void DrainOwnedResourcesForDisable()
		{
			DrainCount++;
			events.Add("cleanup.drain");
			if (drainFailure is not null)
			{
				throw drainFailure;
			}
		}
	}

	private sealed class CallbackDisposable(Action dispose) : IDisposable
	{
		private Action? _dispose = dispose;

		public void Dispose()
		{
			Interlocked.Exchange(ref _dispose, null)?.Invoke();
		}
	}

	private sealed class ThrowingScopedDisposable(List<string> events) : IDisposable
	{
		public void Dispose()
		{
			events.Add("scope.dispose");
			throw new InvalidOperationException("scope dispose");
		}
	}

	private sealed class ThrowingSingletonDisposable(List<string> events) : IDisposable
	{
		public void Dispose()
		{
			events.Add("provider.dispose");
			throw new InvalidOperationException("provider dispose");
		}
	}

	private sealed class ThrowingDisposeConfigurationSource(List<string> events) : IConfigurationSource
	{
		public IConfigurationProvider Build(IConfigurationBuilder builder)
		{
			return new ThrowingDisposeConfigurationProvider(events);
		}
	}

	private sealed class ThrowingDisposeConfigurationProvider(List<string> events) : ConfigurationProvider, IDisposable
	{
		public void Dispose()
		{
			events.Add("configuration.dispose");
			throw new InvalidOperationException("configuration dispose");
		}
	}

	private sealed class FakeClient(long epoch) : ICheatEngineClient
	{
		public long Epoch => epoch;
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
