using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Opt-in execution of trusted arbitrary Lua source.</summary>
public interface IUnsafeLuaClient
{
	/// <summary>Tries to execute trusted Lua source through the SDK protected-call boundary.</summary>
	public bool TryExecute(LuaScript script, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Executes trusted Lua source or throws when execution fails.</summary>
	public void Execute(LuaScript script, CancellationToken cancellationToken = default);
}
