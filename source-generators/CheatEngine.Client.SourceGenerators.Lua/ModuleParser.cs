using System.Collections.Immutable;

using CheatEngine.Client.SourceGenerators.Lua.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Turns one <c>[CheatEngineLuaModule]</c> declaration into an equatable <see cref="ModuleModel" />.</summary>
internal static class ModuleParser
{
	internal const string ContractAssemblyName = "CheatEngine.Client.Abstractions";
	internal const string AnnotationsAssemblyName = "CheatEngine.SDK.Annotations";

	private const string LuaModuleInterfaceMetadataName = "CheatEngine.Client.Lua.ILuaModule";

	/// <summary>The names of the members the generated adapter declares; CECLUA1202 reserves them in the module.</summary>
	internal static readonly ImmutableArray<string> ReservedMemberNames =
	[
		"Register", "Unregister", "Descriptor", "LastReleaseOutcome", "s_descriptor", "get_Descriptor",
		"get_LastReleaseOutcome"
	];

	private static readonly ImmutableArray<string> ModuleContractInterfaces =
	[
		LuaModuleInterfaceMetadataName, "CheatEngine.Client.Lua.IDescribedLuaModule",
		"CheatEngine.Client.Lua.IOwnershipAwareLuaModule"
	];

	public static ModuleModel Parse(GeneratorAttributeSyntaxContext context)
	{
		INamedTypeSymbol? module = context.TargetSymbol as INamedTypeSymbol;
		LocationInfo? location = LocationInfo.From(context.TargetNode);
		string displayName = module is null ? string.Empty : DisplayName(module);
		AttributeData attribute = context.Attributes[0];
		if (!IsDeclaredIn(attribute.AttributeClass, ContractAssemblyName))
		{
			return ModuleModel.Invalid(location, displayName,
				LookAlike(attribute, ContractAssemblyName, location));
		}

		if (module is null || module.TypeKind != TypeKind.Class || module.IsStatic || module.IsAbstract ||
			module.IsFileLocal || module.ContainingType is not null ||
			module.OriginalDefinition.TypeParameters.Length != 0 || !CheatEngineLuaGenerator.IsPartial(module))
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidModuleShape, location));
		}

		INamedTypeSymbol? inheritedModule = FindInheritedModuleImplementation(module);
		if (inheritedModule is not null)
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InheritedModuleImplementation,
					BaseListLocation(context, module) ?? location, displayName, DisplayName(inheritedModule)));
		}

		DiagnosticInfo[] reserved = FindReservedMembers(module, displayName);
		if (reserved.Length != 0)
		{
			return ModuleModel.Invalid(location, displayName, reserved);
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

		if (attribute.ConstructorArguments.Length == 0 ||
			attribute.ConstructorArguments[0].Value is not INamedTypeSymbol bindings || !bindings.IsStatic ||
			bindings.TypeKind != TypeKind.Class || bindings.IsFileLocal ||
			bindings.OriginalDefinition.TypeParameters.Length != 0)
		{
			return ModuleModel.Invalid(location, displayName,
				DiagnosticInfo.Create(CheatEngineLuaDiagnostics.InvalidBindingsType, location));
		}

		ImmutableArray<string> exports = GetExports(bindings, location, out string? duplicateOrInvalidExport,
			out DiagnosticInfo[] lookAlikes);
		if (lookAlikes.Length != 0)
		{
			return ModuleModel.Invalid(location, displayName, lookAlikes);
		}

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

	private static string DisplayName(INamedTypeSymbol type)
	{
		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty);
	}

	/// <summary>
	///     Assembly identity check that rejects look-alike annotation types: the same metadata name declared in user code
	///     (or in any other assembly) is not the contract (DoD D.7).
	/// </summary>
	private static bool IsDeclaredIn(INamedTypeSymbol? type, string assemblyName)
	{
		return type?.ContainingAssembly is { } assembly &&
			   string.Equals(assembly.Name, assemblyName, StringComparison.Ordinal);
	}

	private static DiagnosticInfo LookAlike(AttributeData attribute, string expectedAssembly, LocationInfo? fallback)
	{
		return DiagnosticInfo.Create(CheatEngineLuaDiagnostics.LookAlikeAnnotation,
			LocationInfo.From(attribute.ApplicationSyntaxReference?.GetSyntax()) ?? fallback,
			attribute.AttributeClass?.ToDisplayString() ?? "(unresolved attribute)",
			attribute.AttributeClass?.ContainingAssembly?.Name ?? "(unknown)",
			expectedAssembly);
	}

	private static INamedTypeSymbol? FindInheritedModuleImplementation(INamedTypeSymbol module)
	{
		for (INamedTypeSymbol? baseType = module.BaseType;
			 baseType is not null && baseType.SpecialType != SpecialType.System_Object;
			 baseType = baseType.BaseType)
		{
			// The base's own generated adapter is invisible to this run, so a generated base module is recognized by its
			// contract attribute as well as by an implemented ILuaModule.
			if (ImplementsLuaModuleContract(baseType) || HasContractModuleAttribute(baseType))
			{
				return baseType;
			}
		}

		return null;
	}

	private static bool ImplementsLuaModuleContract(INamedTypeSymbol type)
	{
		foreach (INamedTypeSymbol candidate in type.AllInterfaces)
		{
			if (string.Equals(candidate.ToDisplayString(), LuaModuleInterfaceMetadataName, StringComparison.Ordinal) &&
				IsDeclaredIn(candidate, ContractAssemblyName))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasContractModuleAttribute(INamedTypeSymbol type)
	{
		foreach (AttributeData attribute in type.GetAttributes())
		{
			if (string.Equals(attribute.AttributeClass?.ToDisplayString(),
					CheatEngineLuaGenerator.LuaModuleAttributeMetadataName, StringComparison.Ordinal) &&
				IsDeclaredIn(attribute.AttributeClass, ContractAssemblyName))
			{
				return true;
			}
		}

		return false;
	}

	private static LocationInfo? BaseListLocation(GeneratorAttributeSyntaxContext context, INamedTypeSymbol module)
	{
		if (context.TargetNode is ClassDeclarationSyntax { BaseList: { } targetBaseList })
		{
			return LocationInfo.From(targetBaseList);
		}

		foreach (SyntaxReference reference in module.DeclaringSyntaxReferences)
		{
			if (reference.GetSyntax() is ClassDeclarationSyntax { BaseList: { } baseList })
			{
				return LocationInfo.From(baseList);
			}
		}

		return null;
	}

	private static DiagnosticInfo[] FindReservedMembers(INamedTypeSymbol module, string displayName)
	{
		List<DiagnosticInfo> diagnostics = [];
		foreach (ISymbol member in module.GetMembers())
		{
			if (member.IsImplicitlyDeclared || member is IMethodSymbol
				{
					MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor or MethodKind.PropertyGet or
					MethodKind.PropertySet or MethodKind.EventAdd or MethodKind.EventRemove or MethodKind.EventRaise
				})
			{
				continue;
			}

			string? reservedName = ReservedName(member);
			if (reservedName is not null)
			{
				diagnostics.Add(DiagnosticInfo.Create(CheatEngineLuaDiagnostics.ReservedModuleMember,
					LocationInfo.From(member.Locations.FirstOrDefault()), displayName, reservedName));
			}
		}

		return [.. diagnostics];
	}

	private static string? ReservedName(ISymbol member)
	{
		ImmutableArray<ISymbol> implemented = member switch
		{
			IMethodSymbol method => [.. method.ExplicitInterfaceImplementations],
			IPropertySymbol property => [.. property.ExplicitInterfaceImplementations],
			_ => ImmutableArray<ISymbol>.Empty
		};
		foreach (ISymbol target in implemented)
		{
			// An explicit implementation of the module contract would replace the generated one and bypass it.
			if (target.ContainingType is { } contract &&
				ModuleContractInterfaces.Contains(contract.ToDisplayString()) &&
				IsDeclaredIn(contract, ContractAssemblyName))
			{
				return contract.Name + "." + target.Name;
			}
		}

		string name = member.Name;
		return ReservedMemberNames.Contains(name) ||
			   name.StartsWith(ModuleEmitter.ReservedPrefix, StringComparison.Ordinal)
			? name
			: null;
	}

	private static ImmutableArray<string> GetExports(INamedTypeSymbol bindings, LocationInfo? fallback,
		out string? invalidExport, out DiagnosticInfo[] lookAlikes)
	{
		ImmutableArray<string>.Builder exports = ImmutableArray.CreateBuilder<string>();
		List<DiagnosticInfo> lookAlikeDiagnostics = [];
		HashSet<string> names = new(StringComparer.Ordinal);
		invalidExport = null;
		foreach (IMethodSymbol method in bindings.GetMembers().OfType<IMethodSymbol>())
		{
			AttributeData? attribute = CheatEngineLuaGenerator.GetAttribute(method,
				CheatEngineLuaGenerator.LuaFunctionAttributeMetadataName);
			if (attribute is null)
			{
				continue;
			}

			if (!IsDeclaredIn(attribute.AttributeClass, AnnotationsAssemblyName))
			{
				// A look-alike [LuaFunction] is never counted as an export.
				lookAlikeDiagnostics.Add(LookAlike(attribute, AnnotationsAssemblyName, fallback));
				continue;
			}

			if (invalidExport is not null)
			{
				continue;
			}

			string? name = attribute.ConstructorArguments.Length == 1
				? attribute.ConstructorArguments[0].Value as string
				: null;
			if (name is null)
			{
				invalidExport = "(missing name)";
				continue;
			}

			if (string.IsNullOrWhiteSpace(name) || !names.Add(name))
			{
				invalidExport = name;
				continue;
			}

			exports.Add(name);
		}

		lookAlikes = [.. lookAlikeDiagnostics];
		return invalidExport is null ? exports.ToImmutable() : ImmutableArray<string>.Empty;
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
