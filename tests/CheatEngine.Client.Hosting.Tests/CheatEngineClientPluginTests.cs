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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
				"configure", "module.enabled", "client.enabled", "cleanup.enter", "client.disabling",
				"module.disabling",
				"cleanup.drain", "cleanup.exit"
			],
			events);
	}

	[Fact]
	public void FreshActivationProvidersDisposeOwnedModuleDependenciesOnceWhileAliasesAndOptionsRemainUsable()
	{
		List<string> events = [];
		FakeClient client = new(50);
		RecordingCleanup cleanup = new(events);
		DisposableModuleState state = new();
		TestPlugin plugin = CreatePlugin(events, client, cleanup, builder =>
		{
			builder.Configuration["CheatEngineClient:AllowedTableRoots:0"] = Path.GetTempPath();
			builder.Services.AddSingleton(state);
			builder.Services.AddSingleton<ProviderOwnedActivationSingleton>();
			builder.Services.AddScoped<ActivationOwnedDisposable>();
			builder.Services.AddScoped<IActivationOwnedAlias>(static services => new ActivationOwnedAlias(
				services.GetRequiredService<ActivationOwnedDisposable>()));
			builder.Client.AddModule<DisposableOptionsModule>();
		});

		plugin.EnableForTest();
		plugin.DisableForTest();
		plugin.EnableForTest();
		plugin.DisableForTest();

		Assert.True(state.AliasReferencedOwnedDisposable);
		Assert.Equal(1, state.AllowedTableRootCount);
		Assert.Equal(2, state.EnabledModuleIds.Count);
		Assert.NotEqual(state.EnabledModuleIds[0], state.EnabledModuleIds[1]);
		Assert.Equal(2, state.ProviderSingletonIds.Count);
		Assert.NotEqual(state.ProviderSingletonIds[0], state.ProviderSingletonIds[1]);
		Assert.Equal(2, state.ModuleDisableCount);
		Assert.Equal(2, state.ModuleDisposeCount);
		Assert.Equal(2, state.DependencyDisposeCount);
		Assert.Equal(2, state.ProviderSingletonDisposeCount);
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	[Trait("Qualification", "Q46")]
	public void DisableAttemptsEveryStageAndLogsEachFailedStageWithoutExceptionMessages()
	{
		const string SensitiveModuleText = "module failed at 0x7FFC7A0A0000 reading C:\\Users\\player\\secret.ct";
		const string SensitiveDrainText = "drain failed for symbol game.exe+1234 and Lua 'return readInteger(x)'";
		List<string> events = [];
		CapturingLoggerProvider logs = new();
		FakeClient client = new(51);
		RecordingCleanup cleanup = new(events, drainFailure: new InvalidOperationException(SensitiveDrainText));
		TestPlugin plugin = CreatePlugin(events, client, cleanup, builder =>
		{
			builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs));
			builder.Client.AddModule<SensitiveDisableModule>();
			builder.Services.AddSingleton(new SensitiveFailure(SensitiveModuleText));
		});
		plugin.EnableForTest();

		AggregateException exception = Assert.Throws<AggregateException>(plugin.DisableForTest);

		Assert.Equal(2, exception.InnerExceptions.Count);
		Assert.Equal(1, cleanup.DrainCount);
		Assert.Equal(1, cleanup.ScopeDisposeCount);
		Assert.Contains("cleanup.exit", events);
		Assert.Collection(
			logs.Entries.Where(static entry => entry.EventId == 6),
			module => Assert.Equal(
				"Cheat Engine Client activation 51 cleanup stage ModuleCallbacks failed with System.InvalidOperationException.",
				module.Message),
			drain => Assert.Equal(
				"Cheat Engine Client activation 51 cleanup stage ClientResources failed with System.InvalidOperationException.",
				drain.Message));
		LogEntry completed = Assert.Single(logs.Entries, static entry => entry.EventId == 7);
		Assert.Equal("Cheat Engine Client activation 51 attempted 6 cleanup stage(s); 2 failed.", completed.Message);
		Assert.Contains(logs.Entries, static entry => entry.EventId == 5);
		Assert.All(logs.Entries, static entry =>
		{
			Assert.Null(entry.Exception);
			Assert.DoesNotContain("0x7FFC", entry.Message, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("secret", entry.Message, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("game.exe", entry.Message, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("readInteger", entry.Message, StringComparison.Ordinal);
		});
	}

	[Fact]
	[Trait("Qualification", "Q43")]
	public void ThrowingLoggerProviderCannotAbortCleanup()
	{
		List<string> events = [];
		FakeClient client = new(52);
		RecordingCleanup cleanup = new(events);
		TestPlugin plugin = CreatePlugin(events, client, cleanup, static builder =>
		{
			builder.Services.AddLogging(logging =>
				logging.SetMinimumLevel(LogLevel.Trace).AddProvider(new ThrowingLoggerProvider()));
			builder.Client.AddModule<RecordingModule>();
		});

		plugin.EnableForTest();
		Assert.Same(client, plugin.GetRequiredClientForTest());
		plugin.DisableForTest();

		Assert.Equal(
			[
				"configure", "module.enabled", "client.enabled", "cleanup.enter", "client.disabling", "module.disabling",
				"cleanup.drain", "cleanup.exit"
			],
			events);
		Assert.Equal(1, cleanup.DrainCount);
		Assert.Throws<CheatEngineClientLifecycleException>(plugin.GetRequiredClientForTest);
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void EnableLogsOneIdentificationEventWithoutPaths()
	{
		List<string> events = [];
		CapturingLoggerProvider logs = new();
		FakeClient client = new(53);
		RecordingCleanup cleanup = new(events);
		TestPlugin plugin = CreatePlugin(events, client, cleanup, builder =>
			builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace).AddProvider(logs)));

		plugin.EnableForTest();
		plugin.DisableForTest();

		LogEntry identification = Assert.Single(logs.Entries, static entry => entry.EventId == 20);
		string message = identification.Message;
		Assert.Equal(LogLevel.Information, identification.Level);
		Assert.Null(identification.Exception);
		Assert.Contains("activation 53 enables " + typeof(TestPlugin).FullName, message, StringComparison.Ordinal);
		Assert.Contains("CheatEngine.SDK " + GetConsumedSdkMetadata("Version"), message, StringComparison.Ordinal);
		Assert.Contains("NuGet content hash " + GetConsumedSdkMetadata("ContentHashSha512"), message,
			StringComparison.Ordinal);
		Assert.Contains("supported host profile ce-7.7.0.10621-x64-managed-hostfxr.", message, StringComparison.Ordinal);
		Assert.DoesNotMatch(@"[A-Za-z]:\\", message);
		Assert.DoesNotContain("\\", message, StringComparison.Ordinal);
		Assert.DoesNotContain(".dll", message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(".exe", message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), message,
			StringComparison.OrdinalIgnoreCase);
		Assert.True(logs.Entries.ToList().FindIndex(static entry => entry.EventId == 20) <
					logs.Entries.ToList().FindIndex(static entry => entry.EventId == 1),
			"The identification event must precede the enabled event.");
	}

	[Fact]
	public void ThrowingLoggerProviderCannotFailEnable()
	{
		List<string> events = [];
		FakeClient client = new(54);
		RecordingCleanup cleanup = new(events);
		TestPlugin plugin = CreatePlugin(events, client, cleanup, static builder =>
		{
			builder.Services.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Trace)
				.AddProvider(new ThrowingLoggerProvider(throwFromIsEnabled: true)));
			builder.Client.AddModule<RecordingModule>();
		});

		plugin.EnableForTest();

		Assert.Same(client, plugin.GetRequiredClientForTest());
		Assert.Equal(["configure", "module.enabled", "client.enabled"], events);
		plugin.DisableForTest();
		Assert.Equal(1, cleanup.DrainCount);
	}

	private static string GetConsumedSdkMetadata(string name)
	{
		string key = "CheatEngine.Client.ConsumedSdk." + name;
		return System.Reflection.Assembly.Load("CheatEngine.Client.Core")
				   .GetCustomAttributes<AssemblyMetadataAttribute>()
				   .Single(attribute => attribute.Key == key).Value
			   ?? throw new InvalidOperationException($"The Core assembly embeds no {key} value.");
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

	public sealed class DisposableModuleState
	{
		internal bool AliasReferencedOwnedDisposable
		{
			get;
			set;
		}

		internal int AllowedTableRootCount
		{
			get;
			set;
		}

		internal List<Guid> EnabledModuleIds
		{
			get;
		} = [];

		internal List<Guid> ProviderSingletonIds
		{
			get;
		} = [];

		internal int ModuleDisableCount
		{
			get;
			set;
		}

		internal int ModuleDisposeCount
		{
			get;
			set;
		}

		internal int DependencyDisposeCount
		{
			get;
			set;
		}

		internal int ProviderSingletonDisposeCount
		{
			get;
			set;
		}
	}

	public sealed class ProviderOwnedActivationSingleton(DisposableModuleState state) : IDisposable
	{
		internal Guid Id
		{
			get;
		} = Guid.NewGuid();

		public void Dispose()
		{
			state.ProviderSingletonDisposeCount++;
		}
	}

	public sealed class ActivationOwnedDisposable(DisposableModuleState state) : IDisposable
	{
		public void Dispose()
		{
			state.DependencyDisposeCount++;
		}
	}

	public interface IActivationOwnedAlias
	{
		public ActivationOwnedDisposable OwnedDisposable
		{
			get;
		}
	}

	public sealed class ActivationOwnedAlias(ActivationOwnedDisposable ownedDisposable) : IActivationOwnedAlias
	{
		public ActivationOwnedDisposable OwnedDisposable
		{
			get;
		} = ownedDisposable;
	}

	public sealed class DisposableOptionsModule(
		ProviderOwnedActivationSingleton providerOwnedSingleton,
		ActivationOwnedDisposable ownedDisposable,
		IActivationOwnedAlias alias,
		IOptions<CheatEngineClientOptions> options,
		DisposableModuleState state) : ICheatEngineClientModule, IDisposable
	{
		private readonly Guid _id = Guid.NewGuid();

		public void OnEnabled(ICheatEngineClient client)
		{
			ArgumentNullException.ThrowIfNull(client);
			state.AliasReferencedOwnedDisposable = ReferenceEquals(ownedDisposable, alias.OwnedDisposable);
			state.AllowedTableRootCount = options.Value.AllowedTableRoots?.Length ?? 0;
			state.EnabledModuleIds.Add(_id);
			state.ProviderSingletonIds.Add(providerOwnedSingleton.Id);
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			ArgumentNullException.ThrowIfNull(client);
			state.ModuleDisableCount++;
		}

		public void Dispose()
		{
			state.ModuleDisposeCount++;
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

	public sealed record SensitiveFailure(string Message);

	public sealed class SensitiveDisableModule(List<string> events, SensitiveFailure failure) : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
			events.Add("module.enabled");
		}

		public void OnDisabling(ICheatEngineClient client)
		{
			events.Add("module.disabling");
			throw new InvalidOperationException(failure.Message);
		}
	}

	private sealed record LogEntry(int EventId, LogLevel Level, string Message, Exception? Exception);

	private sealed class CapturingLoggerProvider : ILoggerProvider
	{
		private readonly Lock _gate = new();
		private readonly List<LogEntry> _entries = [];

		internal IReadOnlyList<LogEntry> Entries
		{
			get
			{
				lock (_gate)
				{
					return [.. _entries];
				}
			}
		}

		public ILogger CreateLogger(string categoryName)
		{
			return new CapturingLogger(this);
		}

		public void Dispose()
		{
		}

		private void Add(LogEntry entry)
		{
			lock (_gate)
			{
				_entries.Add(entry);
			}
		}

		private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				owner.Add(new LogEntry(eventId.Id, logLevel, formatter(state, exception), exception));
			}
		}
	}

	private sealed class ThrowingLoggerProvider(bool throwFromIsEnabled = false) : ILoggerProvider
	{
		public ILogger CreateLogger(string categoryName)
		{
			return new ThrowingLogger(throwFromIsEnabled);
		}

		public void Dispose()
		{
		}

		private sealed class ThrowingLogger(bool throwFromIsEnabled) : ILogger
		{
			public IDisposable? BeginScope<TState>(TState state)
				where TState : notnull
			{
				return null;
			}

			public bool IsEnabled(LogLevel logLevel)
			{
				return throwFromIsEnabled ? throw new InvalidOperationException("The logging filter failed.") : true;
			}

			public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
				Func<TState, Exception?, string> formatter)
			{
				throw new InvalidOperationException("The logging provider failed.");
			}
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
