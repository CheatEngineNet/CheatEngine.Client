namespace CheatEngine.Client.Modules;

/// <summary>A scoped feature that participates in the Cheat Engine client lifecycle.</summary>
public interface ICheatEngineClientModule
{
	/// <summary>Runs after the client scope and Lua runtime are active.</summary>
	public void OnEnabled(ICheatEngineClient client);

	/// <summary>Runs before the client scope is disposed and the Lua runtime is detached.</summary>
	public void OnDisabling(ICheatEngineClient client);
}
