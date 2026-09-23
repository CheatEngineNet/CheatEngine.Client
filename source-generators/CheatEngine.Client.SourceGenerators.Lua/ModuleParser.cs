using System.Collections.Immutable;

using CheatEngine.Client.SourceGenerators.Lua.Model;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Turns one <c>[CheatEngineLuaModule]</c> declaration into an equatable <see cref="ModuleModel" />.</summary>
internal static class ModuleParser
{
	public static ModuleModel Parse(GeneratorAttributeSyntaxContext context)
	{
		INamedTypeSymbol? module = context.TargetSymbol as INamedTypeSymbol;
		LocationInfo? location = LocationInfo.From(context.TargetNode);
		string displayName = module is null ? string.Empty : DisplayName(module);
		if (module is null || module.TypeKind != TypeKind.Class || module.IsStatic || module.IsAbstract ||
			module.IsFileLocal || module.ContainingType is not null ||
			module.OriginalDefinition.TypeParameters.Length != 0 || !CheatEngineLuaGenerator.IsPartial(module))
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidModuleShape, location));
		}

		if (!TryDetermineConstructorEmission(module, out bool emitsPublicParameterlessConstructor,
				out IMethodSymbol? nonPublicConstructor))
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.NoPublicConstructor,
					LocationInfo.From(nonPublicConstructor?.Locations.FirstOrDefault()) ?? location,
					module.Name,
					nonPublicConstructor is null
						? "non-public"
						: nonPublicConstructor.DeclaredAccessibility.ToString()));
		}

		AttributeData attribute = context.Attributes[0];
		if (attribute.ConstructorArguments.Length == 0 ||
			attribute.ConstructorArguments[0].Value is not INamedTypeSymbol bindings || !bindings.IsStatic ||
			bindings.TypeKind != TypeKind.Class || bindings.IsFileLocal ||
			bindings.OriginalDefinition.TypeParameters.Length != 0)
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidBindingsType, location));
		}

		ImmutableArray<string> exports = GetExports(bindings, out string? duplicateOrInvalidExport);
		if (duplicateOrInvalidExport is not null)
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidExportSet, location, duplicateOrInvalidExport));
		}

		if (exports.IsEmpty)
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.NoExports, location, bindings.Name));
		}

		string? configuredName = attribute.ConstructorArguments.Length > 1
			? attribute.ConstructorArguments[1].Value as string
			: null;
		string moduleName = configuredName ?? module.Name;
		if (string.IsNullOrWhiteSpace(moduleName))
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidModuleName, location));
		}

		return new ModuleModel(
			EquatableArray<DiagnosticInfo>.Empty,
			location,
			displayName,
			CheatEngineLuaGenerator.HintNameStem(module) + ".CheatEngineLuaModule.g.cs",
			module.ContainingNamespace.IsGlobalNamespace
				? string.Empty
				: module.ContainingNamespace.ToDisplayString(),
			CheatEngineLuaGenerator.TypeDeclaration(module, false),
			CheatEngineLuaGenerator.EscapeIdentifier(module.Name),
			CheatEngineLuaGenerator.TypeName(bindings),
			moduleName,
			EquatableArray.Create(exports),
			emitsPublicParameterlessConstructor);
	}

	private static string DisplayName(INamedTypeSymbol module)
	{
		return module.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
	}

	private static ImmutableArray<string> GetExports(INamedTypeSymbol bindings, out string? invalidExport)
	{
		ImmutableArray<string>.Builder exports = ImmutableArray.CreateBuilder<string>();
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (IMethodSymbol method in bindings.GetMembers().OfType<IMethodSymbol>())
		{
			AttributeData? attribute = CheatEngineLuaGenerator.GetAttribute(method,
				CheatEngineLuaGenerator.LuaFunctionAttributeMetadataName);
			if (attribute is null)
			{
				continue;
			}

			string? name = attribute.ConstructorArguments.Length == 1
				? attribute.ConstructorArguments[0].Value as string
				: null;
			if (name is null)
			{
				invalidExport = "(missing name)";
				return ImmutableArray<string>.Empty;
			}

			if (string.IsNullOrWhiteSpace(name))
			{
				invalidExport = name;
				return ImmutableArray<string>.Empty;
			}

			if (!names.Add(name))
			{
				invalidExport = name;
				return ImmutableArray<string>.Empty;
			}

			exports.Add(name);
		}

		invalidExport = null;
		return exports.ToImmutable();
	}

	private static bool TryDetermineConstructorEmission(INamedTypeSymbol module,
		out bool emitsPublicParameterlessConstructor, out IMethodSymbol? nonPublicConstructor)
	{
		emitsPublicParameterlessConstructor = true;
		nonPublicConstructor = null;
		foreach (IMethodSymbol constructor in module.InstanceConstructors)
		{
			if (constructor.IsImplicitlyDeclared)
			{
				continue;
			}

			if (constructor.DeclaredAccessibility == Accessibility.Public)
			{
				emitsPublicParameterlessConstructor = false;
				return true;
			}

			nonPublicConstructor = constructor;
		}

		return nonPublicConstructor is null;
	}
}
