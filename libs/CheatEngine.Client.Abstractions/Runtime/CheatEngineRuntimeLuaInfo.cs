namespace CheatEngine.Client.Runtime;

/// <summary>Immutable observations about the Lua runtime that CheatEngine.SDK hosts for the active plugin.</summary>
public readonly record struct CheatEngineRuntimeLuaInfo
{
	/// <summary>Creates Lua runtime observations.</summary>
	/// <param name="externalStateResetDetected">
	///     Whether CheatEngine.SDK detected that Cheat Engine replaced its Lua state outside the plugin's control.
	/// </param>
	public CheatEngineRuntimeLuaInfo(bool externalStateResetDetected)
	{
		ExternalStateResetDetected = externalStateResetDetected;
	}

	/// <summary>
	///     Gets whether CheatEngine.SDK detected that Cheat Engine replaced its Lua state outside the plugin's control.
	/// </summary>
	/// <remarks>
	///     The fact is sticky until CheatEngine.SDK attaches to a Lua state again. While it is set, CheatEngine.SDK
	///     refuses to admit Lua work, and the Client reports those refusals as
	///     <see cref="CheatEngine.Client.Results.CheatEngineFailureKind.RuntimeChanged" />.
	/// </remarks>
	public bool ExternalStateResetDetected
	{
		get;
	}
}
