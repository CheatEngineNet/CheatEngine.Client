using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;
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
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IMemoryCodec<int>));
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
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(UnsafeLuaExecutionRegistration));
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
}
