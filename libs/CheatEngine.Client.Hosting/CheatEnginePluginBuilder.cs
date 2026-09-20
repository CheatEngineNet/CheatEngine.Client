using CheatEngine.Client.Extensions.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CheatEngine.Client.Hosting;

/// <summary>Builds the managed configuration and service provider for one Cheat Engine plugin activation.</summary>
/// <remarks>
///     A builder is intentionally single-use. A new instance is created for every enable epoch so scoped Client services
///     cannot retain a Lua reference, CE object, cancellation token, or target-specific state from an earlier activation.
/// </remarks>
public sealed class CheatEnginePluginBuilder
{
	private bool _built;

	/// <summary>Creates an empty configuration and a service collection for one activation.</summary>
	public CheatEnginePluginBuilder()
	{
		Services = new ServiceCollection();
		Configuration = new ConfigurationManager();
		Services.AddSingleton<IConfiguration>(Configuration);
		Services.AddSingleton<IConfigurationRoot>(Configuration);
		Client = Services.AddCheatEngineClient(Configuration);
	}

	/// <summary>Gets the mutable configuration manager used before the activation provider is built.</summary>
	/// <remarks>
	///     No source is loaded implicitly. Callers can add an optional <c>appsettings.json</c> or other explicit sources
	///     during <see cref="CheatEngineClientPlugin.Configure" />. Reloading should remain disabled because an activation's
	///     ownership, capabilities, and cancellation boundary cannot safely be reconfigured while attached to CE.
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

	/// <summary>Gets the Client-specific registration builder.</summary>
	public CheatEngineClientBuilder Client
	{
		get;
	}

	/// <summary>Builds a validating provider after all explicit registrations have been added.</summary>
	/// <exception cref="InvalidOperationException">The builder has already created its provider.</exception>
	public ServiceProvider BuildServiceProvider()
	{
		if (_built)
		{
			throw new InvalidOperationException("A Cheat Engine plugin builder can create only one provider.");
		}

		_built = true;
		return Services.BuildServiceProvider(new ServiceProviderOptions
		{
			ValidateOnBuild = true, ValidateScopes = true
		});
	}

	internal void ReleaseConfiguration()
	{
		Configuration.Dispose();
	}
}
