using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>
///     Every diagnostic the Client Lua generator reports. Ids come from the CECLUA ranges allocated to this generator and are
///     never renumbered or reused; each one is tracked in <c>AnalyzerReleases.*.md</c> and listed in the generator README.
/// </summary>
internal static class CheatEngineLuaDiagnostics
{
	internal const string Category = "CheatEngine.Client.Lua";

	internal const string HelpLinkUri =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/source-generators/CheatEngine.Client.SourceGenerators.Lua/README.md#diagnostics";

	public static readonly DiagnosticDescriptor InvalidModuleShape = new(
		"CECLUA1001", "Lua module must be a non-static partial class",
		"[CheatEngineLuaModule] requires a top-level, concrete, non-static, non-generic, non-file-local partial class",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InvalidBindingsType = new(
		"CECLUA1002", "Lua module requires a static SDK bindings type",
		"The bindings type for [CheatEngineLuaModule] must be a non-generic, non-file-local static class that owns SDK-generated registration methods",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor NoExports = new(
		"CECLUA1003", "Lua module bindings export nothing",
		"The bindings type '{0}' does not declare a [LuaFunction] export",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InvalidExportSet = new(
		"CECLUA1004", "Lua module exports must have unique names",
		"The Lua module bindings contain an invalid or duplicate export name: '{0}'",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InvalidModuleName = new(
		"CECLUA1005", "Lua module name cannot be blank",
		"The explicit [CheatEngineLuaModule] name cannot be empty or whitespace",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor NoPublicConstructor = new(
		"CECLUA1006", "Lua module needs a public constructor",
		"[CheatEngineLuaModule] cannot provide a safe public constructor because '{0}' declares constructors with no public accessibility; expose a public DI constructor or remove the explicit constructors so the generator can provide one",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InvalidOperationShape = new(
		"CECLUA1101", "Lua operation requires a supported SDK global declaration",
		"[CheatEngineLuaOperation] requires a static partial [LuaGlobal] method in a top-level, non-generic, non-file-local static partial class",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor OverloadedOperation = new(
		"CECLUA1102", "Lua operation method cannot be overloaded",
		"Lua operation '{0}' is overloaded; use distinct method names so generated operation factories remain unambiguous",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor UnsupportedSignature = new(
		"CECLUA1103", "Lua operation has an unsupported result shape",
		"Lua operation '{0}' must have scalar input arguments and exactly one return value or one trailing out result",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor MapperRequired = new(
		"CECLUA1104", "Lua operation result requires a mapper",
		"Lua operation '{0}' returns a non-scalar SDK value; declare [CheatEngineLuaOperation(typeof(TMapper))] with an ILuaResultMapper implementation",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InvalidMapper = new(
		"CECLUA1105", "Lua operation mapper does not match the SDK result",
		"Mapper '{0}' does not implement ILuaResultMapper for the result of Lua operation '{1}'",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor UnsafeMappedType = new(
		"CECLUA1106", "Lua operation mapper must project a safe Client result",
		"Lua operation '{0}' maps an unsafe value across the Client boundary: {1}",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor DuplicateModuleExport = new(
		"CECLUA1201", "Lua export is owned by more than one Lua module",
		"Lua global '{0}' is exported by Lua module '{1}' and again by Lua module '{2}'; a Lua global has a single owning module per plugin assembly, so remove the export from one of the bindings types",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor ReservedModuleMember = new(
		"CECLUA1202", "Lua module declares a member reserved by the generated registration",
		"Lua module '{0}' declares '{1}', which is reserved by the generated ownership-aware registration; rename or remove the member",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor InheritedModuleImplementation = new(
		"CECLUA1203", "Lua module inherits a Lua module implementation",
		"Lua module '{0}' derives from '{1}', which already implements a Lua module; the generated ownership state of one of them would be bypassed, so derive the module from object",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	public static readonly DiagnosticDescriptor LookAlikeAnnotation = new(
		"CECLUA1204", "Lua module annotation is not the contract type",
		"'{0}' is declared in assembly '{1}' instead of the contract assembly '{2}'; the annotation is not a Lua module contract and no module code is generated",
		Category, DiagnosticSeverity.Error, true, helpLinkUri: HelpLinkUri);

	/// <summary>Gets every descriptor, ordered by id.</summary>
	public static ImmutableArray<DiagnosticDescriptor> All
	{
		get;
	} =
	[
		InvalidModuleShape, InvalidBindingsType, NoExports, InvalidExportSet, InvalidModuleName, NoPublicConstructor,
		InvalidOperationShape, OverloadedOperation, UnsupportedSignature, MapperRequired, InvalidMapper,
		UnsafeMappedType, DuplicateModuleExport, ReservedModuleMember, InheritedModuleImplementation,
		LookAlikeAnnotation
	];

	/// <summary>Resolves a descriptor from the id stored in an equatable pipeline model.</summary>
	public static DiagnosticDescriptor Get(string id)
	{
		foreach (DiagnosticDescriptor descriptor in All)
		{
			if (string.Equals(descriptor.Id, id, StringComparison.Ordinal))
			{
				return descriptor;
			}
		}

		throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown CheatEngine.Client.Lua diagnostic id.");
	}
}
