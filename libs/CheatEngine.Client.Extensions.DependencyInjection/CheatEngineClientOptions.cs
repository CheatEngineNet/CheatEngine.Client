using System.ComponentModel.DataAnnotations;

using CheatEngine.Client.Memory;

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

	/// <summary>Gets the absolute roots from which Client table files may be loaded or saved.</summary>
	/// <remarks>
	///     The list is never <see langword="null" /> and is empty by default, which denies table-file access. Configuration
	///     binding adds the entries of <c>CheatEngineClient:AllowedTableRoots</c>; a
	///     <see cref="CheatEngineClientBuilder.Configure" /> delegate can add, remove, or clear entries. Paths are
	///     normalized and validated when an activation creates its client scope; relative paths, blank or
	///     <see langword="null" /> entries, and two entries that normalize to the same path are rejected.
	/// </remarks>
	[Required]
	public IList<string> AllowedTableRoots
	{
		get;
	} = new List<string>();

	/// <summary>Gets the target-memory budgets captured when an activation creates its client services.</summary>
	/// <remarks>
	///     The object is never <see langword="null" />: configuration binding and
	///     <see cref="CheatEngineClientBuilder.Configure" /> delegates set its properties. The registration validates and
	///     copies these values when it constructs <c>MemoryClient</c>; changing this options object afterwards cannot
	///     change the active memory policy.
	/// </remarks>
	public MemoryResourceLimits MemoryResourceLimits
	{
		get;
	} = new();
}
