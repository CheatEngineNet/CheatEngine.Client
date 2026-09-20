namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Records the builder-only opt-in required to enable unsafe Lua for an activation.</summary>
internal sealed class UnsafeLuaExecutionRegistration
{
	internal bool IsEnabled
	{
		get;
	} = true;
}
