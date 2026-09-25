using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Plugin.Modules;
using CheatEngine.SDK.Annotations.Plugin;

using Microsoft.Extensions.Configuration;

namespace CheatEngine.Plugin;

/// <summary>The plugin entry point generated and loaded by Cheat Engine.</summary>
/// <remarks>
///     <c>dotnet new ceplugin</c> writes the project name, in printable ASCII, as the name the plugin reports to Cheat
///     Engine.
/// </remarks>
[CheatEnginePlugin("CheatEngine Client Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
	/// <summary>Adds explicit configuration sources for this activation.</summary>
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		// appsettings.json is deployed next to the plugin assembly, not in Cheat Engine's folder.
		builder.Configuration
			.SetBasePath(builder.PluginDirectory)
			.AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

		builder.Client
			.AddLuaModule<PluginLuaModule>()
			.AddModule<PluginClientModule>();
	}

	/// <summary>Runs after the Client activation scope and its modules have started.</summary>
	protected override void OnClientEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
	}
}
