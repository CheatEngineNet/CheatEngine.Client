using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Configuration values used by the high-level Cheat Engine client during one plugin activation.</summary>
/// <remarks>
///     The options intentionally contain only managed limits. Host capabilities, target architecture, and thread affinity
///     are established by Cheat Engine for each activation and are never configured from an application settings file.
/// </remarks>
public sealed class CheatEngineClientOptions
{
	/// <summary>Gets the default configuration section used by plugin hosting.</summary>
	public const string ConfigurationSectionName = "CheatEngineClient";

	/// <summary>Gets or sets the default maximum number of AOB matches copied into managed memory.</summary>
	[Range(1, 1_000_000)]
	public int DefaultMaximumAobResults
	{
		get;
		set;
	} = 4_096;

	/// <summary>Gets or sets the default maximum number of value-scan matches copied in one page.</summary>
	[Range(1, 1_000_000)]
	public int DefaultMaximumValueScanPageSize
	{
		get;
		set;
	} = 1_024;

	/// <summary>Gets or sets absolute roots from which Client table files may be loaded or saved.</summary>
	/// <remarks>
	///     An empty list denies table-file access by default. Paths are normalized and validated when an activation creates
	///     its client scope; relative paths, blank entries, and <see langword="null" /> are rejected.
	/// </remarks>
	public string[]? AllowedTableRoots
	{
		get;
		set;
	} = Array.Empty<string>();

	/// <summary>Gets or sets whether the explicit unsafe Lua execution module may be registered.</summary>
	/// <remarks>
	///     This remains <see langword="false" /> by default. Enabling it is a policy opt-in only; applications must still
	///     register the optional module that exposes any unsafe operation.
	/// </remarks>
	public bool EnableUnsafeLuaExecution
	{
		get;
		set;
	}
}
