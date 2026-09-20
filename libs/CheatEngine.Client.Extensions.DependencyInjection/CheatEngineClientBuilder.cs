using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Modules;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Configures explicit registrations for one Cheat Engine client service provider.</summary>
/// <remarks>
///     The builder never constructs a service provider. Plugin hosting creates and validates one provider for each Cheat
///     Engine activation epoch, after all registrations are complete.
/// </remarks>
public sealed class CheatEngineClientBuilder
{
	internal CheatEngineClientBuilder(IServiceCollection services)
	{
		Services = services;
	}

	/// <summary>Gets the service collection that will form a future activation provider.</summary>
	public IServiceCollection Services
	{
		get;
	}

	/// <summary>Adds a programmatic options configuration that runs after configuration binding.</summary>
	public CheatEngineClientBuilder Configure(Action<CheatEngineClientOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		Services.Configure(configure);
		return this;
	}

	/// <summary>Binds client options from the default client section of a configuration root.</summary>
	public CheatEngineClientBuilder BindConfiguration(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return BindConfiguration(configuration.GetSection(CheatEngineClientOptions.ConfigurationSectionName));
	}

	/// <summary>Binds client options from an explicitly selected configuration section.</summary>
	public CheatEngineClientBuilder BindConfiguration(IConfigurationSection section)
	{
		ArgumentNullException.ThrowIfNull(section);
		Services.AddOptions<CheatEngineClientOptions>().Bind(section);
		return this;
	}

	/// <summary>Adds one activation module in registration order.</summary>
	/// <typeparam name="TModule">The concrete module type.</typeparam>
	/// <remarks>
	///     Module construction is explicit through the generic service descriptor; no assembly scanning or runtime type
	///     discovery is performed. Hosting enables modules in this order and disables successfully enabled modules in the
	///     reverse order.
	/// </remarks>
	public CheatEngineClientBuilder AddModule<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
		TModule>()
		where TModule : class, ICheatEngineClientModule
	{
		Services.TryAddEnumerable(ServiceDescriptor.Singleton<ICheatEngineClientModule, TModule>());
		return this;
	}

	/// <summary>Adds a singleton, deterministic codec for a managed memory value type.</summary>
	/// <typeparam name="T">The managed memory value type.</typeparam>
	/// <typeparam name="TCodec">The concrete codec type.</typeparam>
	/// <remarks>
	///     Codecs must not capture a Lua state, CE object, activation scope, or target-specific state. The default codecs
	///     cover only fixed-width scalar and pointer representations; variable-length memory is deliberately opt-in.
	/// </remarks>
	public CheatEngineClientBuilder AddMemoryCodec<T,
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
		TCodec>()
		where TCodec : class, IMemoryCodec<T>
	{
		Services.TryAdd(ServiceDescriptor.Singleton<IMemoryCodec<T>, TCodec>());
		return this;
	}

	/// <summary>Opts this activation into trusted arbitrary Lua execution.</summary>
	public CheatEngineClientBuilder EnableUnsafeLuaExecution()
	{
		Services.Configure<CheatEngineClientOptions>(static options => options.EnableUnsafeLuaExecution = true);
		Services.TryAddSingleton<IUnsafeLuaClient>(static serviceProvider => new UnsafeLuaClient(
			serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
			serviceProvider.GetRequiredService<CoreClientPolicy>()));
		return this;
	}
}
