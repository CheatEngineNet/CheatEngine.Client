using CheatEngine.Client;
using CheatEngine.Client.Hosting;
using CheatEngine.Plugin.Modules;
using CheatEngine.SDK.Annotations.Plugin;
using Microsoft.Extensions.Configuration;

namespace CheatEngine.Plugin;

/// <summary>The plugin entry point generated and loaded by Cheat Engine.</summary>
[CheatEnginePlugin("CheatEngine Client Plugin")]
public sealed class Plugin : CheatEngineClientPlugin
{
    /// <summary>Adds explicit configuration sources for this activation.</summary>
    protected override void Configure(CheatEnginePluginBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

        builder.Client.AddModule<PluginClientModule>();
    }

    /// <summary>Runs after the Client activation scope and its modules have started.</summary>
    protected override void OnClientEnabled(ICheatEngineClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
    }
}
