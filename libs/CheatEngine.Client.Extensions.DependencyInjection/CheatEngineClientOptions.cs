using System.ComponentModel.DataAnnotations;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Configuration values used by the high-level Cheat Engine client during one plugin activation.</summary>
/// <remarks>
///     Host capabilities, target architecture, thread affinity, and unsafe Lua execution are established by the active
///     Cheat Engine client registration and are never configured from an application settings file.
/// </remarks>
public sealed class CheatEngineClientOptions
{
	/// <summary>Gets the default configuration section used by plugin hosting.</summary>
	public const string ConfigurationSectionName = "CheatEngineClient";

	/// <summary>Initializes the default table-file policy, which denies table-file access until roots are configured.</summary>
	public CheatEngineClientOptions()
	{
	}

	/// <summary>Gets or sets absolute roots from which Client table files may be loaded or saved.</summary>
	/// <remarks>
	///     An empty list denies table-file access by default. Paths are normalized and validated when an activation creates
	///     its client scope; relative paths, blank entries, and <see langword="null" /> are rejected.
	/// </remarks>
	[Required]
	public string[]? AllowedTableRoots
	{
		get;
		set;
	} = Array.Empty<string>();
}
