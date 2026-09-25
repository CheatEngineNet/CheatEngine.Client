using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Hosting;

/// <summary>Builds the managed configuration and service provider for one Cheat Engine plugin activation.</summary>
/// <remarks>
///     A builder is intentionally single-use. <see cref="CheatEngineClientPlugin" /> creates a new instance for every
///     enable epoch, passes it to <see cref="CheatEngineClientPlugin.Configure" /> and builds the activation provider
///     itself after that method returns, so scoped Client services cannot retain a Lua reference, CE object,
///     cancellation token, or target-specific state from an earlier activation. Application code never creates a
///     builder or a provider.
/// </remarks>
public sealed class CheatEnginePluginBuilder
{
	private readonly ReflectionAssembly _pluginAssembly;
	private bool _built;

	/// <summary>Creates an empty configuration and a service collection for one activation of a plugin.</summary>
	/// <param name="pluginAssembly">The assembly of the concrete plugin, whose folder is <see cref="PluginDirectory" />.</param>
	internal CheatEnginePluginBuilder(ReflectionAssembly pluginAssembly)
	{
		_pluginAssembly = pluginAssembly ?? throw new ArgumentNullException(nameof(pluginAssembly));
		Services = new ServiceCollection();
		Configuration = new ConfigurationManager();
		Services.AddSingleton<IConfiguration>(Configuration);
		Services.AddSingleton<IConfigurationRoot>(Configuration);
		Logging = new PluginLoggingBuilder(Services);
		Client = Services.AddCheatEngineClient(Configuration);
	}

	/// <summary>Gets the mutable configuration manager used before the activation provider is built.</summary>
	/// <remarks>
	///     No source is loaded implicitly. Callers can add an optional <c>appsettings.json</c> or other explicit sources
	///     during <see cref="CheatEngineClientPlugin.Configure" />, resolving file sources against
	///     <see cref="PluginDirectory" /> (for example with <c>SetBasePath(builder.PluginDirectory)</c>). Hosting cannot
	///     set that base itself, and a file source added without it resolves against
	///     <see cref="AppContext.BaseDirectory" />, which under Cheat Engine's .NET host is not the plugin folder: an
	///     optional file then silently loads nothing. Reloading should remain disabled because an activation's ownership,
	///     capabilities, and cancellation boundary cannot safely be reconfigured while attached to CE.
	/// </remarks>
	public ConfigurationManager Configuration
	{
		get;
	}

	/// <summary>Gets the service collection for the new activation provider.</summary>
	public IServiceCollection Services
	{
		get;
	}

	/// <summary>Gets the logging configuration of the new activation provider.</summary>
	/// <remarks>
	///     Providers and filters added here receive the Hosting lifecycle events and the Client's Core diagnostic events
	///     of this activation: both log through the activation provider's <see cref="ILoggerFactory" />. Nothing is
	///     written anywhere until a provider is added.
	/// </remarks>
	public ILoggingBuilder Logging
	{
		get;
	}

	/// <summary>Gets the Client-specific registration builder.</summary>
	public CheatEngineClientBuilder Client
	{
		get;
	}

	/// <summary>Gets the folder that holds the plugin assembly, the base for the plugin's configuration files.</summary>
	/// <remarks>
	///     Cheat Engine starts a managed plugin through its .NET host, so <see cref="AppContext.BaseDirectory" /> describes
	///     the hosting process rather than the plugin's deployment folder. Resolve the files deployed with the plugin,
	///     such as <c>appsettings.json</c>, against this folder instead.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	///     The plugin assembly was not loaded from a file (for example from memory or from a single-file bundle), so the
	///     plugin has no folder.
	/// </exception>
	public string PluginDirectory
	{
		[UnconditionalSuppressMessage("SingleFile", "IL3000:Avoid accessing Assembly file path when publishing as a single file",
			Justification = "ADR-02: Cheat Engine loads a managed plugin through the managed-hostfxr profile, from its " +
							"deployment folder, never from a single-file bundle; an assembly without a file location " +
							"is refused with an InvalidOperationException.")]
		get
		{
			string location = _pluginAssembly.Location;
			return (location.Length == 0 ? null : Path.GetDirectoryName(location))
				   ?? throw new InvalidOperationException(
					   "The plugin assembly was not loaded from a file, so the plugin has no folder.");
		}
	}

	/// <summary>Builds a validating provider after all explicit registrations have been added.</summary>
	/// <exception cref="InvalidOperationException">The builder has already created its provider.</exception>
	internal ServiceProvider BuildServiceProvider()
	{
		if (_built)
		{
			throw new InvalidOperationException("A Cheat Engine plugin builder can create only one provider.");
		}

		_built = true;
		return Services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true,
			ValidateScopes = true
		});
	}

	/// <summary>Releases configuration sources after the activation can no longer resolve services from them.</summary>
	/// <remarks>
	///     The host calls this only after activation construction fails or after the activation scope and provider have
	///     been disposed. This keeps configuration providers, including an explicitly supplied file source, scoped to one
	///     enable epoch.
	/// </remarks>
	internal void ReleaseConfiguration()
	{
		Configuration.Dispose();
	}

	/// <summary>The logging view of <see cref="Services" />; the Client registration adds the logging services.</summary>
	private sealed class PluginLoggingBuilder(IServiceCollection services) : ILoggingBuilder
	{
		public IServiceCollection Services
		{
			get;
		} = services;
	}
}
