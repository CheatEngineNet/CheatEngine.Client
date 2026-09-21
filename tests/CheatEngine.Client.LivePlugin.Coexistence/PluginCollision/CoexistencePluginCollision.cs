using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;
using CheatEngine.SDK.Annotations.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace LivePlugin.Coexistence.PluginCollision;

/// <summary>
///     Deliberately declares one export already owned by Plugin A. It must be loaded only after A in the controlled
///     collision protocol; a failure to enable must leave A's existing globals callable and unchanged.
/// </summary>
[CheatEnginePlugin("CheatEngine.Client Coexistence Collision Plugin")]
public sealed class CoexistencePluginCollision : CheatEngineClientPlugin
{
	/// <inheritdoc />
	protected override void Configure(CheatEnginePluginBuilder builder)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddSingleton(new CoexistencePluginIdentity(Context.PluginId));
		builder.Client
			.AddLuaModule<CoexistencePluginCollisionLuaModule>()
			.AddModule<CoexistencePluginCollisionModule>();
	}
}

/// <summary>Records the activation facts only if collision registration was unexpectedly admitted.</summary>
internal sealed class CoexistencePluginCollisionModule(
	IOptions<CheatEngineClientOptions> options,
	CoexistencePluginIdentity pluginIdentity) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;

	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		CoexistenceDiagnostics.RecordEnabled(pluginIdentity.Id, client, _options.AllowedTableRoots?.Length ?? 0);
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		CoexistenceDiagnostics.RecordDisabling();
	}
}

/// <summary>Declares the collision module in a distinct managed assembly.</summary>
[CheatEngineLuaModule(typeof(CoexistencePluginCollisionFunctions), "coexistence_collision")]
internal sealed partial class CoexistencePluginCollisionLuaModule : ILuaModule;

internal sealed class CoexistencePluginIdentity(uint id)
{
	internal uint Id
	{
		get;
	} = id;
}
