using System.Collections.Immutable;

namespace CheatEngine.Client.Lua;

/// <summary>Copied metadata that identifies one explicit Lua module and all global names it exports.</summary>
public readonly record struct LuaModuleDescriptor
{
	/// <summary>Initializes one immutable module descriptor.</summary>
	/// <param name="name">The stable module identity for the Client activation.</param>
	/// <param name="exports">The Lua global names this module will register.</param>
	/// <exception cref="ArgumentException"><paramref name="name" /> is blank or exports contain a blank or duplicate name.</exception>
	public LuaModuleDescriptor(string name, ImmutableArray<LuaExportDescriptor> exports)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(name);
		Name = name;
		Exports = exports.IsDefault ? ImmutableArray<LuaExportDescriptor>.Empty : exports;

		HashSet<string>? names = Exports.IsEmpty ? null : new HashSet<string>(StringComparer.Ordinal);
		foreach (LuaExportDescriptor export in Exports)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(export.Name);
			if (!names!.Add(export.Name))
			{
				throw new ArgumentException(
					$"The Lua module '{name}' contains the export '{export.Name}' more than once.",
					nameof(exports));
			}
		}
	}

	/// <summary>Gets the stable module identity for the Client activation.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets immutable copied descriptors of each Lua global this module exports.</summary>
	public ImmutableArray<LuaExportDescriptor> Exports
	{
		get;
	}
}
