namespace CheatEngine.Client.SourceGenerators.Lua.Model;

/// <summary>
///     The equatable, Roslyn-free description of one <c>[CheatEngineLuaModule]</c> declaration: either the data the module
///     emitter needs or the diagnostics that stop generation.
/// </summary>
internal sealed class ModuleModel : IEquatable<ModuleModel>
{
	public ModuleModel(EquatableArray<DiagnosticInfo> diagnostics, LocationInfo? location, string moduleDisplayName,
		string hintName, string @namespace, string typeDeclaration, string moduleTypeName, string bindingsType,
		string moduleName, EquatableArray<string> exports, bool emitsPublicParameterlessConstructor)
	{
		Diagnostics = diagnostics;
		Location = location;
		ModuleDisplayName = moduleDisplayName;
		HintName = hintName;
		Namespace = @namespace;
		TypeDeclaration = typeDeclaration;
		ModuleTypeName = moduleTypeName;
		BindingsType = bindingsType;
		ModuleName = moduleName;
		Exports = exports;
		EmitsPublicParameterlessConstructor = emitsPublicParameterlessConstructor;
	}

	/// <summary>Gets the diagnostics that stop generation; empty for a valid module.</summary>
	public EquatableArray<DiagnosticInfo> Diagnostics
	{
		get;
	}

	/// <summary>Gets the location of the attributed declaration (cross-module diagnostics and their ordering).</summary>
	public LocationInfo? Location
	{
		get;
	}

	/// <summary>Gets the module type's fully qualified display name without the <c>global::</c> alias.</summary>
	public string ModuleDisplayName
	{
		get;
	}

	public string HintName
	{
		get;
	}

	public string Namespace
	{
		get;
	}

	public string TypeDeclaration
	{
		get;
	}

	public string ModuleTypeName
	{
		get;
	}

	public string BindingsType
	{
		get;
	}

	public string ModuleName
	{
		get;
	}

	/// <summary>Gets the exported Lua global names in declaration order (the descriptor and release order).</summary>
	public EquatableArray<string> Exports
	{
		get;
	}

	public bool EmitsPublicParameterlessConstructor
	{
		get;
	}

	public bool IsValid => Diagnostics.IsEmpty;

	public static ModuleModel Invalid(LocationInfo? location, string moduleDisplayName,
		params DiagnosticInfo[] diagnostics)
	{
		return new ModuleModel(EquatableArray.Create(diagnostics), location, moduleDisplayName, string.Empty,
			string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, EquatableArray<string>.Empty, false);
	}

	public bool Equals(ModuleModel? other)
	{
		return other is not null && Diagnostics.Equals(other.Diagnostics) && Equals(Location, other.Location) &&
			   string.Equals(ModuleDisplayName, other.ModuleDisplayName, StringComparison.Ordinal) &&
			   string.Equals(HintName, other.HintName, StringComparison.Ordinal) &&
			   string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
			   string.Equals(TypeDeclaration, other.TypeDeclaration, StringComparison.Ordinal) &&
			   string.Equals(ModuleTypeName, other.ModuleTypeName, StringComparison.Ordinal) &&
			   string.Equals(BindingsType, other.BindingsType, StringComparison.Ordinal) &&
			   string.Equals(ModuleName, other.ModuleName, StringComparison.Ordinal) &&
			   Exports.Equals(other.Exports) &&
			   EmitsPublicParameterlessConstructor == other.EmitsPublicParameterlessConstructor;
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as ModuleModel);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = StringComparer.Ordinal.GetHashCode(ModuleDisplayName);
			hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ModuleName);
			return (hash * 31) + Exports.GetHashCode();
		}
	}
}
