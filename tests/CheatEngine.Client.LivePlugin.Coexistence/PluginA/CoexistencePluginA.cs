using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;
using CheatEngine.SDK.Annotations.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LivePlugin.Coexistence.PluginA;

/// <summary>First half of the manual Client coexistence fixture. Its Lua export names are distinct from Plugin B's.</summary>
[CheatEnginePlugin("CheatEngine.Client Coexistence Plugin A")]
public sealed class CoexistencePluginA : CheatEngineClientPlugin
{
	/// <inheritdoc />
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddSingleton(new CoexistencePluginIdentity(Context.PluginId));
		builder.Client
			.AddLuaModule<CoexistencePluginALuaModule>()
			.AddModule<CoexistencePluginAModule>();
	}
}

/// <summary>Records activation-local options and epoch facts without selecting a target or issuing a Client operation.</summary>
internal sealed class CoexistencePluginAModule(
	IOptions<CheatEngineClientOptions> options,
	CoexistencePluginIdentity pluginIdentity) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;

	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		CoexistenceDiagnostics.RecordEnabled(pluginIdentity.Id, client, _options.AllowedTableRoots.Count);
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		CoexistenceDiagnostics.RecordDisabling();
	}
}

/// <summary>Declares Plugin A's generated activation-scoped Lua module.</summary>
[CheatEngineLuaModule(typeof(CoexistencePluginAFunctions), "coexistence_a")]
internal sealed partial class CoexistencePluginALuaModule : ILuaModule;

internal sealed class CoexistencePluginIdentity(uint id)
{
	internal uint Id
	{
		get;
	} = id;
}
