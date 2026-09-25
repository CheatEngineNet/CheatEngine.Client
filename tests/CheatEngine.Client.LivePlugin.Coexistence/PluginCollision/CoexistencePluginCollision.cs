using System.Globalization;

using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;
using CheatEngine.Client.Results;
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
		builder.Client.AddModule<CoexistencePluginCollisionModule>();
	}
}

/// <summary>
///     Registers the collision Lua module itself, so the expected refusal (Q16) fails the enable with the classification
///     the Client reported, and records the activation facts only if the registration was unexpectedly admitted.
/// </summary>
/// <remarks>
///     The generated module registers through the CheatEngine.SDK registration lease with the <c>RejectExisting</c>
///     policy: the SDK finds Plugin A's global during its preflight and publishes nothing, so the expected failure is
///     <c>OperationRejected</c> with the host effect <c>NotApplied</c>.
/// </remarks>
internal sealed class CoexistencePluginCollisionModule(
	IOptions<CheatEngineClientOptions> options,
	CoexistencePluginIdentity pluginIdentity) : ICheatEngineClientModule
{
	private readonly CheatEngineClientOptions _options = options.Value;
	private ILuaModuleLease? _lease;

	/// <inheritdoc />
	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (!client.Lua.TryRegisterModule(new CoexistencePluginCollisionLuaModule(), out ILuaModuleLease? lease,
				out CheatEngineFailure failure, client.Stopping))
		{
			throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
				$"Collision=Refused; Kind={failure.Kind}; HostEffect={failure.HostEffect}; Operation={failure.Operation}"));
		}

		_lease = lease;
		CoexistenceDiagnostics.RecordEnabled(pluginIdentity.Id, client, _options.AllowedTableRoots.Count);
	}

	/// <inheritdoc />
	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		Interlocked.Exchange(ref _lease, null)?.Dispose();
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
