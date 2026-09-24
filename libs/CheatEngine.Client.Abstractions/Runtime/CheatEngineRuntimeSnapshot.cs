namespace CheatEngine.Client.Runtime;

/// <summary>Immutable runtime observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     The observations are grouped: <see cref="Version" /> (Cheat Engine, Client and CheatEngine.SDK versions),
///     <see cref="Platform" /> (host and target facts), <see cref="Capabilities" /> (the Client capability evidence) and
///     <see cref="Lua" /> (the Lua runtime facts). Each fact is what CheatEngine.SDK reported, and a fact that was not
///     observed stays unknown.
/// </remarks>
public readonly record struct CheatEngineRuntimeSnapshot
{
	/// <summary>Creates a runtime snapshot from grouped observations.</summary>
	/// <param name="epoch">The plugin activation epoch.</param>
	/// <param name="version">The version observations.</param>
	/// <param name="platform">The host and target platform observations.</param>
	/// <param name="capabilities">The Client capability observations.</param>
	/// <param name="lua">The Lua runtime observations.</param>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="epoch" /> is negative.</exception>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="version" /> is uninitialized, or <paramref name="capabilities" /> is <see langword="null" />.
	/// </exception>
	public CheatEngineRuntimeSnapshot(
		long epoch,
		CheatEngineRuntimeVersionInfo version,
		CheatEngineRuntimePlatformInfo platform,
		ClientCapabilities capabilities,
		CheatEngineRuntimeLuaInfo lua)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(epoch);
		ArgumentNullException.ThrowIfNull(version.ClientAssemblyVersion);
		ArgumentNullException.ThrowIfNull(version.SdkAssemblyVersion);

		Epoch = epoch;
		Version = version;
		Platform = platform;
		Capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
		Lua = lua;
	}

	/// <summary>Gets the plugin activation epoch.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Gets the Cheat Engine, Client and CheatEngine.SDK version observations.</summary>
	public CheatEngineRuntimeVersionInfo Version
	{
		get;
	}

	/// <summary>Gets the host and target platform observations.</summary>
	public CheatEngineRuntimePlatformInfo Platform
	{
		get;
	}

	/// <summary>Gets the explicit availability observation and evidence of each Client capability.</summary>
	public ClientCapabilities Capabilities
	{
		get;
	}

	/// <summary>Gets the Lua runtime observations.</summary>
	public CheatEngineRuntimeLuaInfo Lua
	{
		get;
	}
}
