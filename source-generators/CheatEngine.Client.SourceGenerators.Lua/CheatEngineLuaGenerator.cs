using System.Collections.Immutable;

using CheatEngine.Client.SourceGenerators.Lua.Model;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>
///     Emits the Client-facing, handle-free adapters for explicitly declared SDK Lua binding types.
/// </summary>
/// <remarks>
///     The generator is deliberately attribute-driven. It neither scans assemblies nor persists compilation state:
///     every pipeline transform is static and converts Roslyn symbols into equatable value models (strings, copied
///     locations, <see cref="EquatableArray{T}" />) before emission, so unrelated edits leave the pipeline cached.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CheatEngineLuaGenerator : IIncrementalGenerator
{
	internal const string LuaModuleAttributeMetadataName =
		"CheatEngine.Client.Lua.CheatEngineLuaModuleAttribute";

	internal const string LuaOperationAttributeMetadataName =
		"CheatEngine.Client.Lua.CheatEngineLuaOperationAttribute";

	internal const string LuaFunctionAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute";
	internal const string LuaGlobalAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute";
	private const string LuaResultMapperMetadataName = "CheatEngine.Client.Lua.ILuaResultMapper<TSource, TResult>";
	private const string LuaClassAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaClassAttribute";
	private const string CheatEngineSdkAssemblyPrefix = "CheatEngine.SDK";

	private const string CheatEngineSdkObjectContractMetadataName =
		"CheatEngine.SDK.Engine.Objects.ICEObject<TSelf>";

	private const int ClientBoundaryMaximumDepth = 32;
	private const int ClientBoundaryMaximumNodes = 256;

	// Hint names use the namespace-qualified metadata name: keyword identifiers are not escaped ('@' is not allowed).
	private static readonly SymbolDisplayFormat HintNameFormat = new(
		SymbolDisplayGlobalNamespaceStyle.Omitted,
		SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces);

	// The allowlist has a single source, ApprovedSdkClientTypes.cs, which CheatEngine.Client.Tests links unchanged.
	private static readonly HashSet<string> ApprovedSdkClientResultTypes =
		new(ApprovedSdkClientTypes.Names, StringComparer.Ordinal);

	// The generated Client contract is an ownership boundary, so framework provenance is not sufficient proof
	// that a value is safe to expose. Keep this deliberately small and require all contained type arguments to
	// pass the same boundary walk.
	private static readonly HashSet<string> ApprovedFrameworkValueTypes = new(StringComparer.Ordinal)
	{
		"System.DateOnly",
		"System.DateTime",
		"System.DateTimeOffset",
		"System.Decimal",
		"System.Guid",
		"System.Half",
		"System.Index",
		"System.Int128",
		"System.Range",
		"System.Text.Rune",
		"System.TimeOnly",
		"System.TimeSpan",
		"System.UInt128"
	};

	private static readonly HashSet<string> ApprovedFrameworkGenericCollectionTypes = new(StringComparer.Ordinal)
	{
		"System.Collections.Frozen.FrozenDictionary<TKey, TValue>",
		"System.Collections.Frozen.FrozenSet<T>",
		"System.Collections.Generic.IEnumerable<T>",
		"System.Collections.Generic.IReadOnlyCollection<T>",
		"System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>",
		"System.Collections.Generic.IReadOnlyList<T>",
		"System.Collections.Generic.IReadOnlySet<T>",
		"System.Collections.Immutable.IImmutableDictionary<TKey, TValue>",
		"System.Collections.Immutable.IImmutableList<T>",
		"System.Collections.Immutable.IImmutableQueue<T>",
		"System.Collections.Immutable.IImmutableSet<T>",
		"System.Collections.Immutable.IImmutableStack<T>",
		"System.Collections.Immutable.ImmutableArray<T>",
		"System.Collections.Immutable.ImmutableDictionary<TKey, TValue>",
		"System.Collections.Immutable.ImmutableHashSet<T>",
		"System.Collections.Immutable.ImmutableList<T>",
		"System.Collections.Immutable.ImmutableQueue<T>",
		"System.Collections.Immutable.ImmutableSortedDictionary<TKey, TValue>",
		"System.Collections.Immutable.ImmutableSortedSet<T>",
		"System.Collections.Immutable.ImmutableStack<T>",
		"System.Collections.ObjectModel.ReadOnlyCollection<T>",
		"System.Collections.ObjectModel.ReadOnlyDictionary<TKey, TValue>",
		"System.Nullable<T>"
	};

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<ModuleModel> modules = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				LuaModuleAttributeMetadataName,
				static (node, _) => node is ClassDeclarationSyntax,
				static (attributeContext, _) => ModuleParser.Parse(attributeContext))
			.WithTrackingName("CheatEngineLuaModule");

		context.RegisterSourceOutput(modules, static (productionContext, model) =>
		{
			foreach (DiagnosticInfo diagnostic in model.Diagnostics)
			{
				productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
			}

			if (model.IsValid)
			{
				productionContext.AddSource(model.HintName, ModuleEmitter.Emit(model));
			}
		});

		// CECLUA1201: one owning module per Lua global per plugin assembly (Q16). The models are equatable, so the
		// collected batch stays cached while no module changes.
		context.RegisterSourceOutput(modules.Collect(), static (productionContext, models) =>
		{
			foreach (DiagnosticInfo diagnostic in FindDuplicateExports(models))
			{
				productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
			}
		});

		IncrementalValuesProvider<OperationModel> operations = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				LuaOperationAttributeMetadataName,
				static (node, _) => node is MethodDeclarationSyntax,
				static (attributeContext, _) => OperationParser.Parse(attributeContext))
			.WithTrackingName("CheatEngineLuaOperation");

		context.RegisterSourceOutput(operations, static (productionContext, model) =>
		{
			foreach (DiagnosticInfo diagnostic in model.Diagnostics)
			{
				productionContext.ReportDiagnostic(diagnostic.ToDiagnostic());
			}

			if (model.IsValid)
			{
				productionContext.AddSource(model.HintName, OperationEmitter.Emit(model));
			}
		});
	}

	/// <summary>
	///     Reports every export that a later module (by file path, then position) publishes again. The order is
	///     deterministic and independent of the order in which the models were collected.
	/// </summary>
	internal static ImmutableArray<DiagnosticInfo> FindDuplicateExports(ImmutableArray<ModuleModel> models)
	{
		ModuleModel[] valid = [.. models.Where(static model => model.IsValid)];
		Array.Sort(valid, CompareDeclarationOrder);
		Dictionary<string, ModuleModel> owners = new(StringComparer.Ordinal);
		ImmutableArray<DiagnosticInfo>.Builder diagnostics = ImmutableArray.CreateBuilder<DiagnosticInfo>();
		foreach (ModuleModel model in valid)
		{
			foreach (string export in model.Exports)
			{
				if (owners.TryGetValue(export, out ModuleModel? owner))
				{
					diagnostics.Add(DiagnosticInfo.Create(CheatEngineLuaDiagnostics.DuplicateModuleExport, model.Location,
						export, owner.ModuleDisplayName, model.ModuleDisplayName));
					continue;
				}

				owners.Add(export, model);
			}
		}

		return diagnostics.ToImmutable();
	}

	private static int CompareDeclarationOrder(ModuleModel left, ModuleModel right)
	{
		int order = string.CompareOrdinal(left.Location?.FilePath, right.Location?.FilePath);
		if (order == 0)
		{
			order = (left.Location?.TextSpan.Start ?? -1).CompareTo(right.Location?.TextSpan.Start ?? -1);
		}

		return order != 0 ? order : string.CompareOrdinal(left.ModuleDisplayName, right.ModuleDisplayName);
	}

	internal static string CSharpLiteral(string value)
	{
		return SymbolDisplay.FormatLiteral(value, true);
	}

	internal static bool TryGetMapperResult(INamedTypeSymbol mapper, ITypeSymbol source, out ITypeSymbol result)
	{
		foreach (INamedTypeSymbol contract in mapper.AllInterfaces)
		{
			if (!string.Equals(contract.OriginalDefinition.ToDisplayString(), LuaResultMapperMetadataName,
					StringComparison.Ordinal) ||
				!SymbolEqualityComparer.Default.Equals(contract.TypeArguments[0], source))
			{
				continue;
			}

			result = contract.TypeArguments[1];
			return true;
		}

		result = source;
		return false;
	}

	internal static bool HasAttribute(ISymbol symbol, string metadataName)
	{
		return GetAttribute(symbol, metadataName) is not null;
	}

	internal static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
	{
		foreach (AttributeData attribute in symbol.GetAttributes())
		{
			if (string.Equals(attribute.AttributeClass?.ToDisplayString(), metadataName, StringComparison.Ordinal))
			{
				return attribute;
			}
		}

		return null;
	}

	internal static bool IsPartial(INamedTypeSymbol type)
	{
		foreach (SyntaxReference declaration in type.DeclaringSyntaxReferences)
		{
			if (declaration.GetSyntax() is TypeDeclarationSyntax syntax &&
				syntax.Modifiers.Any(static modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
			{
				return true;
			}
		}

		return false;
	}

	internal static bool IsScalar(ITypeSymbol type)
	{
		if (type.TypeKind == TypeKind.Pointer || type.IsRefLikeType)
		{
			return false;
		}

		return type.SpecialType switch
		{
			SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte or
				SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 or
				SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Int64 or
				SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or
				SpecialType.System_String or SpecialType.System_IntPtr or SpecialType.System_UIntPtr or
				SpecialType.System_Void => true,
			_ => false
		};
	}

	internal static bool TryFindClientBoundaryViolation(ITypeSymbol type, ClientBoundaryRole role,
		out string violation)
	{
		HashSet<ISymbol> visited = new(SymbolEqualityComparer.Default);
		int visitedCount = 0;
		string rootPath = role == ClientBoundaryRole.MapperSource ? "mapper source" : "mapper result";
		return TryFindClientBoundaryViolation(type, role, rootPath, visited, ref visitedCount, 0, out violation);
	}

	private static bool TryFindClientBoundaryViolation(ITypeSymbol type, ClientBoundaryRole role, string path,
		HashSet<ISymbol> visited, ref int visitedCount, int depth, out string violation)
	{
		if (depth > ClientBoundaryMaximumDepth)
		{
			violation = path + " exceeds the supported Client result type graph budget.";
			return true;
		}

		if (!visited.Add(type))
		{
			violation = string.Empty;
			return false;
		}

		if (++visitedCount > ClientBoundaryMaximumNodes)
		{
			violation = path + " exceeds the supported Client result type graph budget.";
			return true;
		}

		if (type.TypeKind is TypeKind.Error or TypeKind.Dynamic)
		{
			violation = path + " uses an unresolved or dynamic type.";
			return true;
		}

		if (type is IFunctionPointerTypeSymbol)
		{
			violation = path + " exposes a function pointer.";
			return true;
		}

		if (type is IPointerTypeSymbol)
		{
			violation = path + " exposes a pointer.";
			return true;
		}

		if (type.IsRefLikeType)
		{
			violation = path + " exposes a ref-like type.";
			return true;
		}

		if (type is IArrayTypeSymbol array)
		{
			return TryFindClientBoundaryViolation(array.ElementType, role, path + "[]", visited, ref visitedCount,
				depth + 1, out violation);
		}

		if (type is ITypeParameterSymbol typeParameter)
		{
			return TryFindConstraintViolation(typeParameter, role, path, visited, ref visitedCount, depth,
				out violation);
		}

		if (type is not INamedTypeSymbol named)
		{
			violation = path + " uses an unsupported type shape '" + TypeName(type) + "'.";
			return true;
		}

		if (TryFindNamedTypeViolation(named, role, path, out violation))
		{
			return true;
		}

		foreach (ITypeParameterSymbol parameter in named.OriginalDefinition.TypeParameters
					 .OrderBy(static candidate => candidate.Ordinal))
		{
			if (TryFindConstraintViolation(parameter, role, path + "." + parameter.Name, visited, ref visitedCount,
					depth + 1, out violation))
			{
				return true;
			}
		}

		if (named.IsTupleType)
		{
			foreach (IFieldSymbol element in named.TupleElements)
			{
				if (TryFindClientBoundaryViolation(element.Type, role, path + "." + element.Name, visited,
						ref visitedCount, depth + 1, out violation))
				{
					return true;
				}
			}
		}

		foreach (ITypeSymbol argument in named.TypeArguments)
		{
			if (TryFindClientBoundaryViolation(argument, role, path + "<" + TypeName(argument) + ">", visited,
					ref visitedCount, depth + 1, out violation))
			{
				return true;
			}
		}

		if (IsFrameworkOrApprovedSdkValue(named) ||
			(role == ClientBoundaryRole.MapperSource && IsCheatEngineSdkType(named)))
		{
			violation = string.Empty;
			return false;
		}

		return TryFindUserDefinedDtoViolation(named, role, path, visited, ref visitedCount, depth, out violation);
	}

	private static bool TryFindNamedTypeViolation(INamedTypeSymbol type, ClientBoundaryRole role, string path,
		out string violation)
	{
		string metadataName = type.OriginalDefinition.ToDisplayString();
		if (metadataName is "CheatEngine.SDK.Lua.State.LuaState" or "CheatEngine.SDK.Lua.References.LuaRef" or
				"CheatEngine.SDK.Engine.Objects.CEObject" or "CheatEngine.SDK.Engine.Objects.Owned<T>" ||
			type.ContainingNamespace.ToDisplayString().Contains(".Interop", StringComparison.Ordinal) ||
			HasAttribute(type, LuaClassAttributeMetadataName) || ImplementsSdkObjectContract(type))
		{
			violation = path + " exposes forbidden SDK lifetime or interop type '" + metadataName + "'.";
			return true;
		}

		if (type.TypeKind == TypeKind.Delegate)
		{
			violation = path + " exposes a delegate or callback.";
			return true;
		}

		if (role == ClientBoundaryRole.ClientResult &&
			type.ContainingAssembly?.Name.StartsWith(CheatEngineSdkAssemblyPrefix, StringComparison.Ordinal) == true &&
			!ApprovedSdkClientResultTypes.Contains(metadataName))
		{
			violation = path + " exposes non-approved SDK type '" + metadataName + "'.";
			return true;
		}

		if (type.SpecialType == SpecialType.System_Object || metadataName == "System.Type")
		{
			violation = path + " exposes an unqualified object or runtime type.";
			return true;
		}

		if (IsFrameworkType(type) && !IsApprovedFrameworkClientBoundaryType(type))
		{
			violation = path + " exposes unsupported framework type '" + metadataName + "'.";
			return true;
		}

		violation = string.Empty;
		return false;
	}

	private static bool TryFindConstraintViolation(ITypeParameterSymbol typeParameter, ClientBoundaryRole role,
		string path, HashSet<ISymbol> visited, ref int visitedCount, int depth, out string violation)
	{
		foreach (ITypeSymbol constraint in typeParameter.ConstraintTypes.OrderBy(TypeName, StringComparer.Ordinal))
		{
			if (TryFindClientBoundaryViolation(constraint, role, path + " constraint", visited, ref visitedCount,
					depth + 1, out violation))
			{
				return true;
			}
		}

		violation = string.Empty;
		return false;
	}

	private static bool TryFindUserDefinedDtoViolation(INamedTypeSymbol type, ClientBoundaryRole role, string path,
		HashSet<ISymbol> visited, ref int visitedCount, int depth, out string violation)
	{
		if (type.BaseType is
			{
				SpecialType: not SpecialType.System_Object and not SpecialType.System_ValueType and
				not SpecialType.System_Enum
			} baseType &&
			TryFindClientBoundaryViolation(baseType, role, path + ".base", visited, ref visitedCount, depth + 1,
				out violation))
		{
			return true;
		}

		foreach (INamedTypeSymbol implementedInterface in type.Interfaces.OrderBy(TypeName, StringComparer.Ordinal))
		{
			if (IsSafeFrameworkDtoImplementationContract(implementedInterface))
			{
				continue;
			}

			if (TryFindClientBoundaryViolation(implementedInterface, role, path + ".interface", visited,
					ref visitedCount, depth + 1, out violation))
			{
				return true;
			}
		}

		foreach (ISymbol member in type.GetMembers().OrderBy(static candidate => candidate.MetadataName,
					 StringComparer.Ordinal))
		{
			switch (member)
			{
				case IFieldSymbol { IsStatic: false } field:
					if (TryFindClientBoundaryViolation(field.Type, role, path + "." + field.Name, visited,
							ref visitedCount, depth + 1, out violation))
					{
						return true;
					}

					break;
				case IPropertySymbol { IsStatic: false } property:
					if (TryFindClientBoundaryViolation(property.Type, role, path + "." + property.Name, visited,
							ref visitedCount, depth + 1, out violation) ||
						TryFindParameterViolation(property.Parameters, role, path + "." + property.Name, visited,
							ref visitedCount, depth + 1, out violation))
					{
						return true;
					}

					break;
				case IEventSymbol { IsStatic: false } @event:
					if (TryFindClientBoundaryViolation(@event.Type, role, path + "." + @event.Name, visited,
							ref visitedCount, depth + 1, out violation))
					{
						return true;
					}

					break;
				case IMethodSymbol { IsStatic: false } method when !method.IsImplicitlyDeclared &&
																   method.DeclaredAccessibility !=
																   Accessibility.Private:
					violation = string.Empty;
					if (method.ReturnsByRef || method.ReturnsByRefReadonly ||
						TryFindClientBoundaryViolation(method.ReturnType, role, path + "." + method.Name, visited,
							ref visitedCount, depth + 1, out violation) ||
						TryFindParameterViolation(method.Parameters, role, path + "." + method.Name, visited,
							ref visitedCount, depth + 1, out violation))
					{
						violation = string.IsNullOrEmpty(violation)
							? path + "." + method.Name + " exposes a by-reference return."
							: violation;
						return true;
					}

					break;
			}
		}

		violation = string.Empty;
		return false;
	}

	private static bool TryFindParameterViolation(ImmutableArray<IParameterSymbol> parameters, ClientBoundaryRole role,
		string path, HashSet<ISymbol> visited, ref int visitedCount, int depth, out string violation)
	{
		foreach (IParameterSymbol parameter in parameters.OrderBy(static candidate => candidate.Ordinal))
		{
			if (parameter.RefKind != RefKind.None)
			{
				violation = path + " parameter '" + parameter.Name + "' is passed by reference.";
				return true;
			}

			if (TryFindClientBoundaryViolation(parameter.Type, role, path + " parameter '" + parameter.Name + "'",
					visited, ref visitedCount, depth + 1, out violation))
			{
				return true;
			}
		}

		violation = string.Empty;
		return false;
	}

	private static bool IsFrameworkOrApprovedSdkValue(INamedTypeSymbol type)
	{
		string metadataName = type.OriginalDefinition.ToDisplayString();
		if (ApprovedSdkClientResultTypes.Contains(metadataName))
		{
			return true;
		}

		return IsApprovedFrameworkClientBoundaryType(type);
	}

	private static bool IsApprovedFrameworkClientBoundaryType(INamedTypeSymbol type)
	{
		string metadataName = type.OriginalDefinition.ToDisplayString();
		return IsScalar(type) || type.IsTupleType || ApprovedFrameworkValueTypes.Contains(metadataName) ||
			   ApprovedFrameworkGenericCollectionTypes.Contains(metadataName);
	}

	private static bool IsFrameworkType(INamedTypeSymbol type)
	{
		string? assemblyName = type.ContainingAssembly?.Name;
		return assemblyName is not null &&
			   !assemblyName.StartsWith(CheatEngineSdkAssemblyPrefix, StringComparison.Ordinal) &&
			   (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
				assemblyName.StartsWith("Microsoft", StringComparison.Ordinal));
	}

	private static bool IsSafeFrameworkDtoImplementationContract(INamedTypeSymbol type)
	{
		return type.OriginalDefinition.ToDisplayString() is "System.IAsyncDisposable" or "System.IComparable" or
			"System.IComparable<T>" or "System.IConvertible" or "System.IDisposable" or "System.IEquatable<T>" or
			"System.IFormattable" or "System.ISpanFormattable" or "System.IUtf8SpanFormattable";
	}

	private static bool IsCheatEngineSdkType(INamedTypeSymbol type)
	{
		return type.ContainingAssembly?.Name.StartsWith(CheatEngineSdkAssemblyPrefix, StringComparison.Ordinal) == true;
	}

	private static bool ImplementsSdkObjectContract(INamedTypeSymbol type)
	{
		return type.AllInterfaces.Any(@interface =>
			string.Equals(@interface.OriginalDefinition.ToDisplayString(), CheatEngineSdkObjectContractMetadataName,
				StringComparison.Ordinal));
	}

	internal static string TypeDeclaration(INamedTypeSymbol type, bool isStatic)
	{
		string accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
		return accessibility + (isStatic ? " static" : string.Empty) + " partial class " + EscapeIdentifier(type.Name);
	}

	/// <summary>
	///     The hint-name stem of a type: its namespace-qualified metadata name with dots replaced by underscores. Keyword
	///     identifiers are not escaped, because a hint name cannot contain '@'.
	/// </summary>
	internal static string HintNameStem(INamedTypeSymbol type)
	{
		return type.ToDisplayString(HintNameFormat).Replace('.', '_');
	}

	internal static string TypeName(ITypeSymbol type)
	{
		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
			SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
	}

	internal static string EscapeIdentifier(string name)
	{
		return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
			   SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
			? "@" + name
			: name;
	}

	internal enum ClientBoundaryRole
	{
		MapperSource,
		ClientResult
	}
}
