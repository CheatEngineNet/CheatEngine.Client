using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

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
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Tables;
using CheatEngine.Client.Timers;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

public sealed class CheatEngineClientServiceCollectionExtensionsTests
{
	[Fact]
	public void AddCheatEngineClientRegistersDescriptorsThatPassProviderValidationWithoutActivation()
	{
		ServiceCollection services = new();

		CheatEngineClientBuilder builder = services.AddCheatEngineClient();

		Assert.NotNull(builder);
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ICheatEngineClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ILocalProcessDiagnostics));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<int>));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IMemoryBatchClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IPatternScanOutcomeClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IAllocationClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IAssemblyClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IRemoteExecutionClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IDebuggerClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IHotkeyClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ITimerClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(ISpeedClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IHashingClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IDbvmClient));
		Assert.Contains(services,
			static descriptor => descriptor.ServiceType == typeof(IValidateOptions<CheatEngineClientOptions>));
		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});

		Assert.NotNull(provider);
	}

	[Fact]
	public void AddCheatEngineClientResolvesThePatternScanOutcomeClientToThePatternScannerSingleton()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();
		ServiceDescriptor scanner =
			Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IPatternScanner));
		ServiceDescriptor outcomes =
			Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IPatternScanOutcomeClient));
		AliasRecordingServiceProvider provider = new();

		object viaScanner = scanner.ImplementationFactory!(provider);
		object viaOutcomes = outcomes.ImplementationFactory!(provider);

		Assert.Equal(ServiceLifetime.Singleton, outcomes.Lifetime);
		Assert.Same(viaScanner, viaOutcomes);
		Assert.IsAssignableFrom<IPatternScanner>(viaOutcomes);
		Assert.IsAssignableFrom<IPatternScanOutcomeClient>(viaScanner);
		Type requested = Assert.Single(provider.RequestedTypes);
		Assert.Equal("CheatEngine.Client.Core.Domains.PatternScanner", requested.FullName);
	}

	[Fact]
	public void AddMemoryCodecPreservesTheFirstExplicitRegistration()
	{
		ServiceCollection services = new();
		CheatEngineClientBuilder builder = services.AddCheatEngineClient();

		builder.AddMemoryCodec<CustomValue, FirstCustomCodec>();
		builder.AddMemoryCodec<CustomValue, SecondCustomCodec>();

		ServiceDescriptor descriptor = Assert.Single(services,
			static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<CustomValue>));
		Assert.Equal(typeof(FirstCustomCodec), descriptor.ImplementationType);
	}

	[Fact]
	public void EnableUnsafeLuaExecutionAddsOnlyTheExplicitUnsafeLuaDescriptor()
	{
		ServiceCollection services = new();
		CheatEngineClientBuilder builder = services.AddCheatEngineClient();

		builder.EnableUnsafeLuaExecution().EnableUnsafeLuaExecution();

		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));
		Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));
		Assert.Contains(services,
			static descriptor => descriptor.ServiceType == typeof(UnsafeLuaExecutionRegistration));
	}

	[Fact]
	public void SectionOverloadBindsThenProgrammaticConfigurationRunsLast()
	{
		using ConfigurationManager configuration = new();
		string configuredRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "configured"));
		string overriddenRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "overridden"));
		configuration["Configured:AllowedTableRoots:0"] = configuredRoot;
		ServiceCollection services = new();

		services.AddCheatEngineClient(configuration.GetSection("Configured"))
			.Configure(options => options.AllowedTableRoots = [overriddenRoot]);

		using ServiceProvider provider = services.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal([overriddenRoot], Assert.IsType<string[]>(options.AllowedTableRoots));
	}

	[Fact]
	public void RootOverloadUsesTheDefaultClientSection()
	{
		using ConfigurationManager configuration = new();
		string allowedRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "allowed"));
		configuration["CheatEngineClient:AllowedTableRoots:0"] = allowedRoot;
		ServiceCollection services = new();

		services.AddCheatEngineClient(configuration);

		using ServiceProvider provider = services.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal([allowedRoot], Assert.IsType<string[]>(options.AllowedTableRoots));
	}

	[Fact]
	public void RootOverloadBindsMemoryResourceLimitsForTheActivationSnapshot()
	{
		using ConfigurationManager configuration = new();
		configuration["CheatEngineClient:MemoryResourceLimits:MaximumReadBytes"] = "37";
		ServiceCollection services = new();
		services.AddCheatEngineClient(configuration);

		using ServiceProvider provider = services.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.NotNull(options.MemoryResourceLimits);
		Assert.Equal(37, options.MemoryResourceLimits.MaximumReadBytes);
	}

	[Fact]
	public void ConfigurationCannotEnableUnsafeLuaExecution()
	{
		using ConfigurationManager configuration = new();
		configuration["CheatEngineClient:EnableUnsafeLuaExecution"] = "true";
		ServiceCollection services = new();
		services.AddCheatEngineClient(configuration);
		using ServiceProvider provider = services.BuildServiceProvider();

		Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));
		Assert.Empty(provider.GetServices<IUnsafeLuaClient>());
	}

	[Fact]
	public void AddModulePreservesExplicitRegistrationOrder()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient()
			.AddModule<FirstModule>()
			.AddModule<FirstModule>()
			.AddModule<SecondModule>();

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		using IServiceScope scope = provider.CreateScope();
		ICheatEngineClientModule[] modules = scope.ServiceProvider.GetServices<ICheatEngineClientModule>().ToArray();

		Assert.Collection(
			modules,
			module => Assert.IsType<FirstModule>(module),
			module => Assert.IsType<SecondModule>(module));
	}

	[Fact]
	public void AddModuleConstructsAModuleWithScopedDependencyOncePerActivationScope()
	{
		ServiceCollection services = new();
		services.AddScoped<ScopedModuleDependency>();
		services.AddCheatEngineClient().AddModule<ScopedDependencyModule>();

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		using IServiceScope scope = provider.CreateScope();
		ScopedDependencyModule first = Assert.IsType<ScopedDependencyModule>(
			Assert.Single(scope.ServiceProvider.GetServices<ICheatEngineClientModule>()));
		ScopedDependencyModule second = Assert.IsType<ScopedDependencyModule>(
			Assert.Single(scope.ServiceProvider.GetServices<ICheatEngineClientModule>()));

		Assert.Same(first, second);
		Assert.Same(first.Dependency, scope.ServiceProvider.GetRequiredService<ScopedModuleDependency>());
	}

	[Fact]
	public void AddLuaModuleRegistersOneActivationLifecyclePerExplicitDescribedModule()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient()
			.AddLuaModule<FirstLuaModule>()
			.AddLuaModule<FirstLuaModule>()
			.AddLuaModule<SecondLuaModule>();

		using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
		ServiceDescriptor[] lifecycleDescriptors = services
			.Where(static descriptor => descriptor.ServiceType == typeof(ICheatEngineClientModule))
			.ToArray();

		Assert.Collection(
			lifecycleDescriptors,
			descriptor => Assert.Equal(typeof(LuaModuleLifecycle<FirstLuaModule>), descriptor.ImplementationType),
			descriptor => Assert.Equal(typeof(LuaModuleLifecycle<SecondLuaModule>), descriptor.ImplementationType));
		Assert.Contains(services, static descriptor =>
			descriptor.ServiceType == typeof(FirstLuaModule) && descriptor.Lifetime == ServiceLifetime.Scoped);
		Assert.Contains(services, static descriptor =>
			descriptor.ServiceType == typeof(SecondLuaModule) && descriptor.Lifetime == ServiceLifetime.Scoped);
	}

	[Fact]
	public void LuaModuleLifecycleRegistersAndReleasesItsModuleExactlyOnce()
	{
		RecordingLuaClient lua = new();
		FirstLuaModule module = new();
		LuaModuleLifecycle<FirstLuaModule> lifecycle = new(lua, module);
		TestClient client = new();

		lifecycle.OnEnabled(client);
		lifecycle.OnDisabling(client);
		lifecycle.OnDisabling(client);

		Assert.Same(module, lua.RegisteredModule);
		RecordingLease lease = Assert.IsType<RecordingLease>(lua.Lease);
		Assert.Equal(1, lease.DisposeCount);
	}

	[Fact]
	public void LuaModuleLifecycleForwardsTheClientStoppingTokenToRegistration()
	{
		RecordingLuaClient lua = new();
		LuaModuleLifecycle<FirstLuaModule> lifecycle = new(lua, new FirstLuaModule());
		using CancellationTokenSource stopping = new();
		TestClient client = new()
		{
			Stopping = stopping.Token
		};

		lifecycle.OnEnabled(client);

		Assert.Equal(client.Stopping, lua.RegistrationCancellationToken);
	}

	private readonly record struct CustomValue(int Value);

	private sealed class FirstCustomCodec : IMemoryCodec<CustomValue>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out CustomValue value)
		{
			value = default;
			return false;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in CustomValue value)
		{
			return false;
		}
	}

	private sealed class SecondCustomCodec : IMemoryCodec<CustomValue>
	{
		public bool TryRead(IMemoryReadContext context, Address address, out CustomValue value)
		{
			value = new CustomValue(2);
			return true;
		}

		public bool TryWrite(IMemoryWriteContext context, Address address, in CustomValue value)
		{
			return value.Value == 2;
		}
	}

	public sealed class FirstModule : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
		}

		public void OnDisabling(ICheatEngineClient client)
		{
		}
	}

	public sealed class SecondModule : ICheatEngineClientModule
	{
		public void OnEnabled(ICheatEngineClient client)
		{
		}

		public void OnDisabling(ICheatEngineClient client)
		{
		}
	}

	public sealed class ScopedModuleDependency
	{
		public Guid Identifier
		{
			get;
		} = Guid.NewGuid();
	}

	public sealed class ScopedDependencyModule(ScopedModuleDependency dependency) : ICheatEngineClientModule
	{
		public ScopedModuleDependency Dependency
		{
			get;
		} = dependency;

		public void OnEnabled(ICheatEngineClient client)
		{
		}

		public void OnDisabling(ICheatEngineClient client)
		{
		}
	}

	public sealed class FirstLuaModule : IDescribedLuaModule
	{
		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new("first", ImmutableArray.Create(new LuaExportDescriptor("first")));

		public void Register()
		{
		}

		public void Unregister()
		{
		}
	}

	public sealed class SecondLuaModule : IDescribedLuaModule
	{
		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new("second", ImmutableArray.Create(new LuaExportDescriptor("second")));

		public void Register()
		{
		}

		public void Unregister()
		{
		}
	}

	private sealed class RecordingLuaClient : ILuaClient
	{
		internal RecordingLease? Lease
		{
			get;
			private set;
		}

		internal ILuaModule? RegisteredModule
		{
			get;
			private set;
		}

		internal CancellationToken RegistrationCancellationToken
		{
			get;
			private set;
		}

		public bool TryRegisterModule(
			ILuaModule luaModule,
			[NotNullWhen(true)] out ILuaModuleLease? lease,
			out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(luaModule);
			RegistrationCancellationToken = cancellationToken;
			RegisteredModule = luaModule;
			Lease = new RecordingLease();
			lease = Lease;
			failure = default;
			return true;
		}

		public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default)
		{
			if (TryRegisterModule(luaModule, out ILuaModuleLease? lease, out CheatEngineFailure failure,
					cancellationToken))
			{
				return lease;
			}

			failure.Throw();
			throw new InvalidOperationException("A failed Lua registration must throw its mapped exception.");
		}

		public bool TryExecute<TResult>(
			ILuaOperation<TResult> operation,
			[MaybeNullWhen(false)] out TResult result,
			out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
		{
			ArgumentNullException.ThrowIfNull(operation);
			result = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Test.Lua", "Not used by this test.");
			return false;
		}

		public TResult Execute<TResult>(ILuaOperation<TResult> operation, CancellationToken cancellationToken = default)
		{
			_ = TryExecute(operation, out TResult? result, out CheatEngineFailure failure, cancellationToken);
			failure.Throw();
			return result!;
		}
	}

	private sealed class RecordingLease : ILuaModuleLease
	{
		internal int DisposeCount
		{
			get;
			private set;
		}

		public long Epoch => 1;

		public bool IsReleased => DisposeCount != 0;

		public void Dispose()
		{
			if (DisposeCount == 0)
			{
				DisposeCount++;
			}
		}
	}

	private sealed class TestClient : ICheatEngineClient
	{
		public long Epoch => 1;

		public CancellationToken Stopping
		{
			get;
			init;
		} = CancellationToken.None;

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

	/// <summary>
	///     Resolves each requested implementation type to one uninitialized singleton so descriptor aliases can be compared
	///     without activating Core (which requires an enabled Cheat Engine plugin context).
	/// </summary>
	private sealed class AliasRecordingServiceProvider : IServiceProvider
	{
		private readonly Dictionary<Type, object> _instances = [];

		internal IReadOnlyCollection<Type> RequestedTypes => _instances.Keys;

		public object? GetService(Type serviceType)
		{
			if (!_instances.TryGetValue(serviceType, out object? instance))
			{
				instance = RuntimeHelpers.GetUninitializedObject(serviceType);
				_instances.Add(serviceType, instance);
			}

			return instance;
		}
	}
}
