using CheatEngine.Client.Lua;
using CheatEngine.Client.Modules;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Bridges one descriptor-backed Lua module into the activation lifecycle.</summary>
/// <typeparam name="TModule">The explicitly registered Lua module type.</typeparam>
/// <remarks>
///     Hosting calls modules in registration order and compensates them in reverse order. The lease is retained here,
///     rather than in a user module, so generated modules never need to expose a raw Lua state, state lease, or SDK
///     operation to application code.
/// </remarks>
internal sealed class LuaModuleLifecycle<TModule>(ILuaClient lua, TModule module) : ICheatEngineClientModule
	where TModule : class, IDescribedLuaModule
{
	private readonly ILuaClient _lua = lua ?? throw new ArgumentNullException(nameof(lua));
	private readonly TModule _module = module ?? throw new ArgumentNullException(nameof(module));
	private ILuaModuleLease? _lease;

	public void OnEnabled(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		if (_lease is not null)
		{
			throw new InvalidOperationException(
				"The Lua module lifecycle has already been enabled for this activation.");
		}

		_lease = _lua.RegisterModule(_module, client.Stopping);
	}

	public void OnDisabling(ICheatEngineClient client)
	{
		ArgumentNullException.ThrowIfNull(client);
		ILuaModuleLease? lease = Interlocked.Exchange(ref _lease, null);
		lease?.Dispose();
	}
}
