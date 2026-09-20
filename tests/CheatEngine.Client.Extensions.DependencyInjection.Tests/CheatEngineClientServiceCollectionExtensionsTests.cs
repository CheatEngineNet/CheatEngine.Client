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
			ValidateOnBuild = true, ValidateScopes = true
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

		builder.EnableUnsafeLuaExecution();

		using ServiceProvider provider = services.BuildServiceProvider();
		Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));
		Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient));
		Assert.True(provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value.EnableUnsafeLuaExecution);
	}

	[Fact]
	public void SectionOverloadBindsThenProgrammaticConfigurationRunsLast()
	{
		using ConfigurationManager configuration = new();
		configuration["Configured:DefaultMaximumAobResults"] = "11";
		ServiceCollection services = new();

		services.AddCheatEngineClient(configuration.GetSection("Configured"))
			.Configure(static options => options.DefaultMaximumAobResults = 29);

		using ServiceProvider provider = services.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal(29, options.DefaultMaximumAobResults);
	}

	[Fact]
	public void RootOverloadUsesTheDefaultClientSection()
	{
		using ConfigurationManager configuration = new();
		configuration["CheatEngineClient:DefaultMaximumValueScanPageSize"] = "87";
		ServiceCollection services = new();

		services.AddCheatEngineClient(configuration);

		using ServiceProvider provider = services.BuildServiceProvider();
		CheatEngineClientOptions options = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value;

		Assert.Equal(87, options.DefaultMaximumValueScanPageSize);
	}

	[Fact]
	public void GeneratedOptionsValidatorRejectsOutOfRangeBoundConfiguration()
	{
		using ConfigurationManager configuration = new();
		configuration["CheatEngineClient:DefaultMaximumAobResults"] = "0";
		ServiceCollection services = new();
		services.AddCheatEngineClient(configuration);
		using ServiceProvider provider = services.BuildServiceProvider();

		OptionsValidationException exception = Assert.Throws<OptionsValidationException>(() =>
			_ = provider.GetRequiredService<IOptions<CheatEngineClientOptions>>().Value);

		Assert.Contains("DefaultMaximumAobResults", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddModulePreservesExplicitRegistrationOrder()
	{
		ServiceCollection services = new();
		services.AddCheatEngineClient()
			.AddModule<FirstModule>()
			.AddModule<SecondModule>();

		using ServiceProvider provider = services.BuildServiceProvider();
		ICheatEngineClientModule[] modules = provider.GetServices<ICheatEngineClientModule>().ToArray();

		Assert.Collection(
			modules,
			module => Assert.IsType<FirstModule>(module),
			module => Assert.IsType<SecondModule>(module));
	}

	private readonly record struct CustomValue(int Value);

	private class FirstCustomCodec : IMemoryCodec<CustomValue>
	{
		public virtual bool TryRead(IMemoryReadContext context, Address address, out CustomValue value)
		{
			value = default;
			return false;
		}

		public virtual bool TryWrite(IMemoryWriteContext context, Address address, in CustomValue value)
		{
			return false;
		}
	}

	private sealed class SecondCustomCodec : FirstCustomCodec
	{
		public override bool TryRead(IMemoryReadContext context, Address address, out CustomValue value)
		{
			return base.TryRead(context, address, out value);
		}

		public override bool TryWrite(IMemoryWriteContext context, Address address, in CustomValue value)
		{
			return base.TryWrite(context, address, in value);
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
}
