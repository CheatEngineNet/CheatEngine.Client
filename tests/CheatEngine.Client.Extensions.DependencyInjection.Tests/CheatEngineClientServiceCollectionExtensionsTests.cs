using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;
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
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IProcessClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<int>));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IMemoryClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IPatternScanner));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IAllocationClient));
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IAssemblyClient));
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

	/// <summary>
	///     The detailed scan is a member of <see cref="IPatternScanner" />: DI registers the scanner once, as the Core
	///     singleton, and no companion service for its outcomes.
	/// </summary>
	[Fact]
	public void AddCheatEngineClientRegistersThePatternScannerOnceAndNoOutcomeCompanion()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();
		ServiceDescriptor scanner =
			Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IPatternScanner));
		AliasRecordingServiceProvider provider = new();

		object viaScanner = scanner.ImplementationFactory!(provider);

		Assert.Equal(ServiceLifetime.Singleton, scanner.Lifetime);
		Assert.IsAssignableFrom<IPatternScanner>(viaScanner);
		Type requested = Assert.Single(provider.RequestedTypes);
		Assert.Equal("CheatEngine.Client.Core.Domains.PatternScanner", requested.FullName);
		Assert.DoesNotContain(services, static descriptor =>
			descriptor.ServiceType.Namespace == "CheatEngine.Client.Scanning" &&
			descriptor.ServiceType.Name.Contains("Outcome", StringComparison.Ordinal));
	}

	[Fact]
	[Trait("Qualification", "Q25")]
	public void AddCheatEngineClientComposesTheOperationalValueScannerAsASingleton()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();
		ServiceDescriptor descriptor =
			Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IValueScanner));
		AliasRecordingServiceProvider provider = new();

		object scanner = descriptor.ImplementationFactory!(provider);

		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
		Assert.IsAssignableFrom<IValueScanner>(scanner);
		Type requested = Assert.Single(provider.RequestedTypes);
		Assert.Equal("CheatEngine.Client.Core.Domains.ValueScanning.ValueScanner", requested.FullName);
	}

	[Fact]
	[Trait("Qualification", "Q30.a")]
	public void AddCheatEngineClientComposesTheOperationalAllocationClientAsASingleton()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient();
		ServiceDescriptor descriptor =
			Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IAllocationClient));
		AliasRecordingServiceProvider provider = new();

		object allocations = descriptor.ImplementationFactory!(provider);

		Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
		Assert.IsAssignableFrom<IAllocationClient>(allocations);
		Type requested = Assert.Single(provider.RequestedTypes);
		Assert.Equal("CheatEngine.Client.Core.Domains.Allocations.AllocationClient", requested.FullName);
	}

	[Fact]
	[Trait("Qualification", "Q44")]
	public void AddCheatEngineClientComposesOnlyUnavailableAdaptersForContractOnlyDomains()
	{
		// ADR-09 / A17-20: a contract-only interface is composed with its unavailable adapter, never an operational one.
		ServiceCollection services = new();
		services.AddCheatEngineClient();
		AliasRecordingServiceProvider provider = new();
		Type[] contractOnly = [typeof(IAssemblyClient)];

		foreach (Type serviceType in contractOnly)
		{
			ServiceDescriptor descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == serviceType);
			object implementation = descriptor.ImplementationFactory!(provider);

			Assert.Matches(@"^CheatEngine\.Client\.Core\.Domains(\.[A-Za-z]+)?\.Unavailable[A-Za-z]+$",
				implementation.GetType().FullName!);
			Assert.IsAssignableFrom(serviceType, implementation);
		}
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

	public sealed class FirstLuaModule : ILuaModule
	{
		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new("first", ImmutableArray.Create(new LuaExportDescriptor("first")));

		public void Register()
		{
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			return LuaModuleReleaseOutcome.Released(Descriptor.Name, 1, 0, 0);
		}
	}

	public sealed class SecondLuaModule : ILuaModule
	{
		public LuaModuleDescriptor Descriptor
		{
			get;
		} = new("second", ImmutableArray.Create(new LuaExportDescriptor("second")));

		public void Register()
		{
		}

		public LuaModuleReleaseOutcome Unregister()
		{
			return LuaModuleReleaseOutcome.Released(Descriptor.Name, 1, 0, 0);
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

			failure.Throw(cancellationToken);
			throw new InvalidOperationException("A failed Lua registration must throw its mapped exception.");
		}

		public bool TryExecute<TOperation, TResult>(
			in TOperation operation,
			[MaybeNullWhen(false)] out TResult result,
			out CheatEngineFailure failure,
			CancellationToken cancellationToken = default)
			where TOperation : ILuaOperation<TResult>
		{
			result = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidState, "Test.Lua", "Not used by this test.");
			return false;
		}

		public TResult Execute<TOperation, TResult>(in TOperation operation,
			CancellationToken cancellationToken = default)
			where TOperation : ILuaOperation<TResult>
		{
			_ = TryExecute<TOperation, TResult>(in operation, out TResult? result, out CheatEngineFailure failure,
				cancellationToken);
			failure.Throw(cancellationToken);
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

		public LeaseReleaseOutcome? LastReleaseOutcome => IsReleased
			? new LeaseReleaseOutcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed)
			: null;

		public LuaModuleReleaseOutcome? ModuleReleaseOutcome => null;

		public LeaseReleaseOutcome Release()
		{
			bool alreadyReleased = IsReleased;
			Dispose();
			return alreadyReleased
				? new LeaseReleaseOutcome(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted)
				: LastReleaseOutcome!.Value;
		}

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
