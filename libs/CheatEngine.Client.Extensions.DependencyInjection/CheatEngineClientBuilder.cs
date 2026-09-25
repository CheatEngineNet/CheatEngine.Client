using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Domains.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Configures explicit registrations for one Cheat Engine client service provider.</summary>
/// <remarks>
///     <para>
///         The builder never constructs a service provider. CheatEngine.Client.Hosting creates and validates one provider
///         for each Cheat Engine activation epoch, after all registrations are complete, and hands this builder to the
///         plugin as <c>CheatEnginePluginBuilder.Client</c>. This package is the composition layer of Hosting: composing
///         the Client in a provider that Hosting does not own is not supported in 1.0.
///     </para>
///     <para>
///         No memory codec is registered or resolved implicitly. A plugin registers its own codec as an ordinary
///         service, for example <c>Services.AddSingleton&lt;IMemoryCodec&lt;T&gt;, TCodec&gt;()</c>, and passes it to
///         <c>IMemoryClient</c> through <c>MemoryReadRequest&lt;T&gt;</c> or <c>MemoryWriteRequest&lt;T&gt;</c>.
///     </para>
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
	/// <param name="configure">Configures the options of every activation.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="configure" /> is <see langword="null" />.</exception>
	public CheatEngineClientBuilder Configure(Action<CheatEngineClientOptions> configure)
	{
		ArgumentNullException.ThrowIfNull(configure);
		Services.Configure(configure);
		return this;
	}

	/// <summary>Binds client options from the default client section of a configuration root.</summary>
	/// <param name="configuration">
	///     The configuration whose <see cref="CheatEngineClientOptions.ConfigurationSectionName" /> section is bound.
	/// </param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="configuration" /> is <see langword="null" />.
	/// </exception>
	public CheatEngineClientBuilder BindConfiguration(IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(configuration);
		return BindConfiguration(configuration.GetSection(CheatEngineClientOptions.ConfigurationSectionName));
	}

	/// <summary>Binds client options from an explicitly selected configuration section.</summary>
	/// <param name="section">The configuration section bound to the options.</param>
	/// <returns>This builder.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="section" /> is <see langword="null" />.</exception>
	public CheatEngineClientBuilder BindConfiguration(IConfigurationSection section)
	{
		ArgumentNullException.ThrowIfNull(section);
		Services.AddOptions<CheatEngineClientOptions>().Bind(section);
		return this;
	}

	/// <summary>Adds one activation module in registration order.</summary>
	/// <typeparam name="TModule">The concrete module type.</typeparam>
	/// <returns>This builder.</returns>
	/// <remarks>
	///     Module construction is explicit through the generic service descriptor; no assembly scanning or runtime type
	///     discovery is performed. Modules are scoped to the activation so they can depend on other scoped application
	///     services. Hosting enables modules in this order and disables successfully enabled modules in the reverse order.
	/// </remarks>
	public CheatEngineClientBuilder AddModule<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
	TModule>()
		where TModule : class, ICheatEngineClientModule
	{
		Services.TryAddEnumerable(ServiceDescriptor.Scoped<ICheatEngineClientModule, TModule>());
		return this;
	}

	/// <summary>Adds one Lua module to every Client activation.</summary>
	/// <typeparam name="TModule">The generated (<see cref="CheatEngineLuaModuleAttribute" />) or manual Lua module type.</typeparam>
	/// <returns>This builder.</returns>
	/// <remarks>
	///     The module is created from its public constructor by the activation-scoped provider and is registered only
	///     after the Client and Lua runtime are live, after the Client reserved its descriptor's module name and exports
	///     for the activation. Its lease is released in reverse module order during disable.
	/// </remarks>
	public CheatEngineClientBuilder AddLuaModule<
		[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
	TModule>()
		where TModule : class, ILuaModule
	{
		Services.TryAdd(ServiceDescriptor.Describe(typeof(TModule), typeof(TModule), ServiceLifetime.Scoped));
		Services.TryAddEnumerable(ServiceDescriptor.Describe(
			typeof(ICheatEngineClientModule),
			typeof(LuaModuleLifecycle<TModule>),
			ServiceLifetime.Scoped));
		return this;
	}

	/// <summary>Opts this activation into trusted arbitrary Lua execution.</summary>
	/// <returns>This builder.</returns>
	/// <exception cref="InvalidOperationException">
	///     <see cref="IUnsafeLuaClient" /> was registered by another path than this method.
	/// </exception>
	/// <remarks>
	///     This is the only supported opt-in path. Configuration binding cannot enable the capability or register the
	///     unsafe facade, so the activation policy and service registration are established together.
	/// </remarks>
	public CheatEngineClientBuilder EnableUnsafeLuaExecution()
	{
		if (Services.Any(static descriptor => descriptor.ServiceType == typeof(IUnsafeLuaClient)))
		{
			if (Services.Any(static descriptor => descriptor.ServiceType == typeof(UnsafeLuaExecutionRegistration)))
			{
				return this;
			}

			throw new InvalidOperationException(
				"IUnsafeLuaClient can only be registered through EnableUnsafeLuaExecution().");
		}

		Services.AddSingleton<UnsafeLuaExecutionRegistration>();
		Services.AddSingleton<IUnsafeLuaClient>(static serviceProvider => new UnsafeLuaClient(
			serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
			serviceProvider.GetRequiredService<CoreClientPolicy>(),
			serviceProvider.GetRequiredService<CoreLifetime>()));
		return this;
	}

	/// <summary>Opts this activation into experimental Auto Assembler patches.</summary>
	/// <returns>This builder.</returns>
	/// <exception cref="InvalidOperationException">
	///     <see cref="IAutoAssemblerClient" /> was registered by another path than this method.
	/// </exception>
	/// <remarks>
	///     <para>
	///         This is the only supported opt-in path: configuration binding cannot enable the capability or register the
	///         client, so the activation policy and the registration of <see cref="IAutoAssemblerClient" /> are established
	///         together. Calling it again keeps the single registration. Without it, nothing is registered and the
	///         <c>Client.AutoAssemblerPatches</c> capability reports a <c>Missing</c> policy gate.
	///     </para>
	///     <para>
	///         An Auto Assembler script can allocate target memory, inject code and run Lua in Cheat Engine. Resolve
	///         <see cref="IAutoAssemblerClient" /> from the activation provider and apply only scripts your plugin owns.
	///     </para>
	/// </remarks>
	[Experimental(ClientExperimentalDiagnostics.AutoAssemblerPatches, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
	public CheatEngineClientBuilder EnableAutoAssemblerPatches()
	{
		if (Services.Any(static descriptor => descriptor.ServiceType == typeof(IAutoAssemblerClient)))
		{
			if (Services.Any(static descriptor => descriptor.ServiceType == typeof(AutoAssemblerPatchesRegistration)))
			{
				return this;
			}

			throw new InvalidOperationException(
				"IAutoAssemblerClient can only be registered through EnableAutoAssemblerPatches().");
		}

		Services.AddSingleton<AutoAssemblerPatchesRegistration>();
		Services.AddSingleton<IAutoAssemblerClient>(static serviceProvider => new AutoAssemblerClient(
			serviceProvider.GetRequiredService<SdkMainThreadDispatcher>(),
			serviceProvider.GetRequiredService<CoreClientPolicy>(),
			serviceProvider.GetRequiredService<CoreLifetime>(),
			serviceProvider.GetRequiredService<ProcessClient>()));
		return this;
	}
}
