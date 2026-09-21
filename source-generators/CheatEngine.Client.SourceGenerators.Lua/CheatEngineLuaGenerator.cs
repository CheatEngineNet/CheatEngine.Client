using System.Collections.Immutable;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>
///     Emits the Client-facing, handle-free adapters for explicitly declared SDK Lua binding types.
/// </summary>
/// <remarks>
///     The generator is deliberately attribute-driven. It neither scans assemblies nor persists compilation state:
///     every pipeline transform is static and converts Roslyn symbols into immutable value data before emission.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class CheatEngineLuaGenerator : IIncrementalGenerator
{
	internal const string LuaModuleAttributeMetadataName =
		"CheatEngine.Client.Lua.CheatEngineLuaModuleAttribute";

	internal const string LuaOperationAttributeMetadataName =
		"CheatEngine.Client.Lua.CheatEngineLuaOperationAttribute";

	private const string LuaFunctionAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaFunctionAttribute";
	private const string LuaGlobalAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaGlobalAttribute";
	private const string LuaResultMapperMetadataName = "CheatEngine.Client.Lua.ILuaResultMapper<TSource, TResult>";
	private const string LuaClassAttributeMetadataName = "CheatEngine.SDK.Annotations.Lua.LuaClassAttribute";
	private const string CheatEngineSdkAssemblyPrefix = "CheatEngine.SDK";
	private const string CheatEngineSdkObjectContractMetadataName =
		"CheatEngine.SDK.Engine.Objects.ICEObject<TSelf>";
	private const int ClientBoundaryMaximumDepth = 32;
	private const int ClientBoundaryMaximumNodes = 256;

	private static readonly HashSet<string> ApprovedSdkClientResultTypes = new(StringComparer.Ordinal)
	{
		"CheatEngine.SDK.Engine.AddressList.MemoryRecordId",
		"CheatEngine.SDK.Engine.Enums.FastScanMethod",
		"CheatEngine.SDK.Engine.Enums.VariableType",
		"CheatEngine.SDK.Engine.Inspection.AddressResolutionOptions",
		"CheatEngine.SDK.Engine.Inspection.MemoryRegionInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleName",
		"CheatEngine.SDK.Engine.Inspection.ModuleSectionInfo",
		"CheatEngine.SDK.Engine.Inspection.SymbolExpression",
		"CheatEngine.SDK.Engine.Inspection.SymbolInfo",
		"CheatEngine.SDK.Engine.Inspection.TargetProcessId",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineArchitecture",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineVersion",
		"CheatEngine.SDK.Engine.Runtime.PointerSize",
		"CheatEngine.SDK.Engine.Runtime.RuntimeCapabilityAvailability",
		"CheatEngine.SDK.Engine.Runtime.RuntimeCapabilityId",
		"CheatEngine.SDK.Engine.Runtime.TargetAbi",
		"CheatEngine.SDK.Engine.Scanning.Aob.AobPattern",
		"CheatEngine.SDK.Engine.Scanning.Aob.AobScanOptions",
		"CheatEngine.SDK.Engine.Scanning.Values.FirstScanRequest",
		"CheatEngine.SDK.Engine.Scanning.Values.NextScanRequest",
		"CheatEngine.SDK.Engine.Values.Address"
	};

	/// <inheritdoc />
	public void Initialize(IncrementalGeneratorInitializationContext context)
	{
		IncrementalValuesProvider<ModuleCandidate> modules = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				LuaModuleAttributeMetadataName,
				static (node, _) => node is ClassDeclarationSyntax,
				static (attributeContext, _) => ModuleCandidate.Create(attributeContext))
			.WithTrackingName("CheatEngineLuaModule");

		context.RegisterSourceOutput(modules, static (productionContext, candidate) =>
		{
			if (candidate.Diagnostic is not null)
			{
				productionContext.ReportDiagnostic(candidate.Diagnostic);
				return;
			}

			productionContext.AddSource(candidate.HintName, EmitModule(candidate));
		});

		IncrementalValuesProvider<OperationCandidate> operations = context.SyntaxProvider
			.ForAttributeWithMetadataName(
				LuaOperationAttributeMetadataName,
				static (node, _) => node is MethodDeclarationSyntax,
				static (attributeContext, _) => OperationCandidate.Create(attributeContext))
			.WithTrackingName("CheatEngineLuaOperation");

		context.RegisterSourceOutput(operations, static (productionContext, candidate) =>
		{
			if (candidate.Diagnostic is not null)
			{
				productionContext.ReportDiagnostic(candidate.Diagnostic);
				return;
			}

			productionContext.AddSource(candidate.HintName, EmitOperation(candidate));
		});
	}

	private static string EmitModule(ModuleCandidate candidate)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(candidate.Namespace);
		source.WriteLine(candidate.TypeDeclaration + " : global::CheatEngine.Client.Lua.IDescribedLuaModule");
		source.OpenBlock();
		if (candidate.EmitsPublicParameterlessConstructor)
		{
			source.WriteLine(
				"/// <summary>Initializes a Lua module instance for activation-scoped dependency injection.</summary>");
			source.WriteLine("public " + candidate.ModuleTypeName + "()");
			source.OpenBlock();
			source.CloseBlock();
			source.WriteLine();
		}

		source.WriteLine("private static readonly global::CheatEngine.Client.Lua.LuaModuleDescriptor s_descriptor =");
		source.Indent();
		source.WriteLine("new global::CheatEngine.Client.Lua.LuaModuleDescriptor(");
		source.Indent();
		source.WriteLine(CSharpLiteral(candidate.ModuleName) + ",");
		source.WriteLine(EmitExports(candidate.Exports) + ");");
		source.Unindent();
		source.Unindent();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public global::CheatEngine.Client.Lua.LuaModuleDescriptor Descriptor => s_descriptor;");
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Register()");
		source.OpenBlock();
		source.WriteLine("using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation operation =");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();");
		source.Unindent();
		source.WriteLine("EnsureExportsAreVacant(operation.State);");
		source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = " + candidate.BindingsType +
		                 ".RegisterLuaFunctions(operation.State);");
		source.WriteLine("if (status.IsOk)");
		source.OpenBlock();
		source.WriteLine("return;");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("try");
		source.OpenBlock();
		source.WriteLine("status.ThrowIfFailed(operation.State);");
		source.CloseBlock();
		source.WriteLine("finally");
		source.OpenBlock();
		source.WriteLine("_ = " + candidate.BindingsType + ".UnregisterLuaFunctions(operation.State);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public void Unregister()");
		source.OpenBlock();
		source.WriteLine("using global::CheatEngine.SDK.Lua.Runtime.LuaRuntimeOperation operation =");
		source.Indent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Runtime.LuaRuntime.AcquireOperation();");
		source.Unindent();
		source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = " + candidate.BindingsType +
		                 ".UnregisterLuaFunctions(operation.State);");
		source.WriteLine("if (!status.IsOk)");
		source.OpenBlock();
		source.WriteLine("status.ThrowIfFailed(operation.State);");
		source.CloseBlock();
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine(
			"private static void EnsureExportsAreVacant(global::CheatEngine.SDK.Lua.State.LuaState state)");
		source.OpenBlock();
		source.WriteLine("int top = state.Top;");
		source.WriteLine("try");
		source.OpenBlock();
		foreach (string export in candidate.Exports)
		{
			source.OpenBlock();
			source.WriteLine("global::CheatEngine.SDK.Lua.Calls.LuaStatus status = state.TryGetGlobal(" +
			                 CSharpLiteral(export) + "u8);");
			source.WriteLine("if (!status.IsOk)");
			source.OpenBlock();
			source.WriteLine("status.ThrowIfFailed(state);");
			source.CloseBlock();
			source.WriteLine("if (!state.IsNil(-1))");
			source.OpenBlock();
			source.WriteLine("throw new global::System.InvalidOperationException(" +
			                 CSharpLiteral("Lua global '" + export +
			                               "' is already defined and cannot be replaced by Client module '" +
			                               candidate.ModuleName + "'.") + ");");
			source.CloseBlock();
			source.WriteLine("state.Pop(1);");
			source.CloseBlock();
		}

		source.CloseBlock();
		source.WriteLine("finally");
		source.OpenBlock();
		source.WriteLine("state.SetTop(top);");
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();

		return source.ToString();
	}

	private static string EmitOperation(OperationCandidate candidate)
	{
		SourceBuilder source = new();
		source.WriteGeneratedHeader();
		source.OpenNamespace(candidate.Namespace);
		source.WriteLine(candidate.ContainingTypeDeclaration);
		source.OpenBlock();
		source.WriteLine("/// <summary>Creates a handle-free Client operation for <c>" + candidate.MethodName +
		                 "</c>.</summary>");
		source.WriteLine("public static " + candidate.OperationTypeName + " Create" + candidate.OperationTypeName +
		                 "(" +
		                 EmitParameterList(candidate.Parameters) + ")");
		source.OpenBlock();
		source.WriteLine("return new " + candidate.OperationTypeName + "(" +
		                 EmitArgumentList(candidate.Parameters, false) + ");");
		source.CloseBlock();
		source.WriteLine();
		source.WriteLine("/// <summary>Generated readonly value operation for <c>" + candidate.MethodName +
		                 "</c>.</summary>");
		source.WriteLine(candidate.OperationVisibility + " readonly record struct " + candidate.OperationTypeName +
		                 "(" +
		                 EmitRecordParameterList(candidate.Parameters) +
		                 ") : global::CheatEngine.Client.Lua.ILuaOperation<" +
		                 candidate.ResultType + ">");
		source.OpenBlock();
		source.WriteLine("/// <inheritdoc />");
		source.WriteLine("public bool TryExecute(global::CheatEngine.Client.Lua.ILuaExecutionContext context, out " +
		                 candidate.ResultType +
		                 " result, out global::CheatEngine.Client.Results.CheatEngineFailure failure)");
		source.OpenBlock();
		source.WriteLine("global::System.ArgumentNullException.ThrowIfNull(context);");
		source.WriteLine("context.ThrowIfExpired();");
		source.WriteLine("try");
		source.OpenBlock();
		if (candidate.HasOutResult)
		{
			source.WriteLine(candidate.SourceResultType + " source;");
			source.WriteLine("if (!" + candidate.BindingsType + "." + candidate.MethodName + "(" +
			                 EmitOutArgumentList(candidate.Parameters) + "))");
			source.OpenBlock();
			source.WriteLine("result = default;");
			source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
			source.Indent();
			source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.LuaError,");
			source.WriteLine(CSharpLiteral(candidate.FailureOperation) + ",");
			source.WriteLine(
				CSharpLiteral("The SDK Lua Try binding returned false without exposing an unsafe Lua handle.") + ");");
			source.Unindent();
			source.WriteLine("return false;");
			source.CloseBlock();
		}
		else
		{
			source.WriteLine(candidate.SourceResultType + " source = " + candidate.BindingsType + "." +
			                 candidate.MethodName + "(" + EmitArgumentList(candidate.Parameters, true) + ");");
		}

		source.WriteLine(candidate.MapperType is null
			? "result = source;"
			: "result = " + candidate.MapperType + ".Map(source);");
		source.WriteLine("failure = default;");
		source.WriteLine("return true;");
		source.CloseBlock();
		source.WriteLine("catch (global::CheatEngine.SDK.Lua.Calls.LuaException exception)");
		source.OpenBlock();
		source.WriteLine("result = default;");
		source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
		source.Indent();
		source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.LuaError,");
		source.WriteLine(CSharpLiteral(candidate.FailureOperation) + ",");
		source.WriteLine("exception.Message,");
		source.WriteLine("exception);");
		source.Unindent();
		source.WriteLine("return false;");
		source.CloseBlock();
		source.WriteLine("catch (global::System.Exception exception)");
		source.OpenBlock();
		source.WriteLine("result = default;");
		source.WriteLine("failure = new global::CheatEngine.Client.Results.CheatEngineFailure(");
		source.Indent();
		source.WriteLine("global::CheatEngine.Client.Results.CheatEngineFailureKind.BindingError,");
		source.WriteLine(CSharpLiteral(candidate.FailureOperation) + ",");
		source.WriteLine("exception.Message,");
		source.WriteLine("exception);");
		source.Unindent();
		source.WriteLine("return false;");
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();
		source.CloseBlock();

		return source.ToString();
	}

	private static string EmitExports(ImmutableArray<string> exports)
	{
		if (exports.IsEmpty)
		{
			return
				"global::System.Collections.Immutable.ImmutableArray<global::CheatEngine.Client.Lua.LuaExportDescriptor>.Empty";
		}

		StringBuilder source = new("global::System.Collections.Immutable.ImmutableArray.Create(");
		for (int index = 0; index < exports.Length; index++)
		{
			if (index != 0)
			{
				source.Append(", ");
			}

			source.Append("new global::CheatEngine.Client.Lua.LuaExportDescriptor(");
			source.Append(CSharpLiteral(exports[index]));
			source.Append(')');
		}

		source.Append(')');
		return source.ToString();
	}

	private static string EmitParameterList(ImmutableArray<OperationParameter> parameters)
	{
		return string.Join(", ", parameters.Select(static parameter => parameter.Type + " " + parameter.Name));
	}

	private static string EmitRecordParameterList(ImmutableArray<OperationParameter> parameters)
	{
		return string.Join(", ", parameters.Select(static parameter => parameter.Type + " " + parameter.FieldName));
	}

	private static string EmitArgumentList(ImmutableArray<OperationParameter> parameters, bool fields)
	{
		return string.Join(", ", parameters.Select(parameter => fields ? parameter.FieldName : parameter.Name));
	}

	private static string EmitOutArgumentList(ImmutableArray<OperationParameter> parameters)
	{
		string arguments = EmitArgumentList(parameters, true);
		return arguments.Length == 0 ? "out source" : arguments + ", out source";
	}

	private static string CSharpLiteral(string value)
	{
		return SymbolDisplay.FormatLiteral(value, true);
	}

	private static ImmutableArray<string> GetExports(INamedTypeSymbol bindings, out string? invalidExport)
	{
		ImmutableArray<string>.Builder exports = ImmutableArray.CreateBuilder<string>();
		HashSet<string> names = new(StringComparer.Ordinal);
		foreach (IMethodSymbol method in bindings.GetMembers().OfType<IMethodSymbol>())
		{
			AttributeData? attribute = GetAttribute(method, LuaFunctionAttributeMetadataName);
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

			string exportName = name;
			if (!names.Add(exportName))
			{
				invalidExport = exportName;
				return ImmutableArray<string>.Empty;
			}

			exports.Add(exportName);
		}

		invalidExport = null;
		return exports.ToImmutable();
	}

	private static bool TryGetMapperResult(INamedTypeSymbol mapper, ITypeSymbol source, out ITypeSymbol result)
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

	private static bool HasAttribute(ISymbol symbol, string metadataName)
	{
		return GetAttribute(symbol, metadataName) is not null;
	}

	private static AttributeData? GetAttribute(ISymbol symbol, string metadataName)
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

	private static bool IsPartial(INamedTypeSymbol type)
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

	private static bool IsScalar(ITypeSymbol type)
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
				SpecialType.System_String or SpecialType.System_IntPtr or SpecialType.System_UIntPtr => true,
			_ => false
		};
	}

	private static bool TryFindClientBoundaryViolation(ITypeSymbol type, ClientBoundaryRole role,
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
		if (depth > ClientBoundaryMaximumDepth || ++visitedCount > ClientBoundaryMaximumNodes)
		{
			violation = path + " exceeds the supported Client result type graph budget.";
			return true;
		}

		if (!visited.Add(type))
		{
			violation = string.Empty;
			return false;
		}

		if (type.TypeKind == TypeKind.Error || type.TypeKind == TypeKind.Dynamic)
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
			return TryFindConstraintViolation(typeParameter, role, path, visited, ref visitedCount, depth, out violation);
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
		if (type.BaseType is { SpecialType: not SpecialType.System_Object } baseType &&
		    TryFindClientBoundaryViolation(baseType, role, path + ".base", visited, ref visitedCount, depth + 1,
			out violation))
		{
			return true;
		}

		foreach (INamedTypeSymbol implementedInterface in type.Interfaces.OrderBy(TypeName, StringComparer.Ordinal))
		{
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
				                                                method.DeclaredAccessibility != Accessibility.Private:
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

		string? assemblyName = type.ContainingAssembly?.Name;
		return assemblyName is not null &&
		       !assemblyName.StartsWith(CheatEngineSdkAssemblyPrefix, StringComparison.Ordinal) &&
		       (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
		        assemblyName.StartsWith("Microsoft", StringComparison.Ordinal));
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

	private enum ClientBoundaryRole
	{
		MapperSource,
		ClientResult
	}

	private static string TypeDeclaration(INamedTypeSymbol type, bool isStatic)
	{
		string accessibility = type.DeclaredAccessibility == Accessibility.Public ? "public" : "internal";
		return accessibility + (isStatic ? " static" : string.Empty) + " partial class " + EscapeIdentifier(type.Name);
	}

	private static string TypeName(ITypeSymbol type)
	{
		return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
			SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
	}

	private static string EscapeIdentifier(string name)
	{
		return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ||
		       SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
			? "@" + name
			: name;
	}

	private sealed class ModuleCandidate
	{
		private ModuleCandidate(Diagnostic? diagnostic, string hintName, string @namespace, string typeDeclaration,
			string moduleTypeName, string bindingsType, string moduleName, ImmutableArray<string> exports,
			bool emitsPublicParameterlessConstructor)
		{
			Diagnostic = diagnostic;
			HintName = hintName;
			Namespace = @namespace;
			TypeDeclaration = typeDeclaration;
			ModuleTypeName = moduleTypeName;
			BindingsType = bindingsType;
			ModuleName = moduleName;
			Exports = exports;
			EmitsPublicParameterlessConstructor = emitsPublicParameterlessConstructor;
		}

		public Diagnostic? Diagnostic
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

		public ImmutableArray<string> Exports
		{
			get;
		}

		public bool EmitsPublicParameterlessConstructor
		{
			get;
		}

		public static ModuleCandidate Create(GeneratorAttributeSyntaxContext context)
		{
			INamedTypeSymbol? module = context.TargetSymbol as INamedTypeSymbol;
			Location location = context.TargetNode.GetLocation();
			if (module is null || module.TypeKind != TypeKind.Class || module.IsStatic ||
			    module.ContainingType is not null ||
			    module.TypeParameters.Length != 0 || !IsPartial(module))
			{
				return Invalid(ModuleDiagnosticDescriptors.InvalidModuleShape, location);
			}

			if (!TryDetermineConstructorEmission(module, out bool emitsPublicParameterlessConstructor,
				    out IMethodSymbol? nonPublicConstructor))
			{
				return Invalid(ModuleDiagnosticDescriptors.NoPublicConstructor,
					nonPublicConstructor?.Locations.FirstOrDefault() ?? location,
					module.Name,
					nonPublicConstructor is null
						? "non-public"
						: nonPublicConstructor.DeclaredAccessibility.ToString());
			}

			AttributeData attribute = context.Attributes[0];
			if (attribute.ConstructorArguments.Length == 0 ||
			    attribute.ConstructorArguments[0].Value is not INamedTypeSymbol bindings || !bindings.IsStatic ||
			    bindings.TypeKind != TypeKind.Class)
			{
				return Invalid(ModuleDiagnosticDescriptors.InvalidBindingsType, location);
			}

			ImmutableArray<string> exports = GetExports(bindings, out string? duplicateOrInvalidExport);
			if (duplicateOrInvalidExport is not null)
			{
				return Invalid(ModuleDiagnosticDescriptors.InvalidExportSet, location, duplicateOrInvalidExport);
			}

			if (exports.IsEmpty)
			{
				return Invalid(ModuleDiagnosticDescriptors.NoExports, location, bindings.Name);
			}

			string? configuredName = attribute.ConstructorArguments.Length > 1
				? attribute.ConstructorArguments[1].Value as string
				: null;
			string moduleName = configuredName ?? module.Name;
			if (string.IsNullOrWhiteSpace(moduleName))
			{
				return Invalid(ModuleDiagnosticDescriptors.InvalidModuleName, location);
			}

			return new ModuleCandidate(
				null,
				module.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
					.Replace("global::", string.Empty)
					.Replace('.', '_') + ".CheatEngineLuaModule.g.cs",
				module.ContainingNamespace.IsGlobalNamespace
					? string.Empty
					: module.ContainingNamespace.ToDisplayString(),
				TypeDeclaration(module, false),
				EscapeIdentifier(module.Name),
				TypeName(bindings),
				moduleName,
				exports,
				emitsPublicParameterlessConstructor);
		}

		private static ModuleCandidate Invalid(DiagnosticDescriptor descriptor, Location location,
			params object[] arguments)
		{
			return new ModuleCandidate(Diagnostic.Create(descriptor, location, arguments), string.Empty, string.Empty,
				string.Empty, string.Empty, string.Empty, string.Empty, ImmutableArray<string>.Empty, false);
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

	private sealed class OperationCandidate
	{
		private OperationCandidate(Diagnostic? diagnostic, string hintName, string @namespace,
			string containingTypeDeclaration, string operationVisibility, string bindingsType, string methodName,
			string operationTypeName, ImmutableArray<OperationParameter> parameters, string sourceResultType,
			string resultType, string? mapperType, bool hasOutResult, string failureOperation)
		{
			Diagnostic = diagnostic;
			HintName = hintName;
			Namespace = @namespace;
			ContainingTypeDeclaration = containingTypeDeclaration;
			OperationVisibility = operationVisibility;
			BindingsType = bindingsType;
			MethodName = methodName;
			OperationTypeName = operationTypeName;
			Parameters = parameters;
			SourceResultType = sourceResultType;
			ResultType = resultType;
			MapperType = mapperType;
			HasOutResult = hasOutResult;
			FailureOperation = failureOperation;
		}

		public Diagnostic? Diagnostic
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

		public string ContainingTypeDeclaration
		{
			get;
		}

		public string OperationVisibility
		{
			get;
		}

		public string BindingsType
		{
			get;
		}

		public string MethodName
		{
			get;
		}

		public string OperationTypeName
		{
			get;
		}

		public ImmutableArray<OperationParameter> Parameters
		{
			get;
		}

		public string SourceResultType
		{
			get;
		}

		public string ResultType
		{
			get;
		}

		public string? MapperType
		{
			get;
		}

		public bool HasOutResult
		{
			get;
		}

		public string FailureOperation
		{
			get;
		}

		public static OperationCandidate Create(GeneratorAttributeSyntaxContext context)
		{
			IMethodSymbol? method = context.TargetSymbol as IMethodSymbol;
			Location location = context.TargetNode.GetLocation();
			if (method is null || !HasAttribute(method, LuaGlobalAttributeMetadataName) ||
			    method.MethodKind != MethodKind.Ordinary ||
			    !method.IsStatic || method.IsGenericMethod || !method.IsPartialDefinition ||
			    method.PartialImplementationPart is not null || method.ReturnsByRef || method.ReturnsByRefReadonly ||
			    method.ContainingType.ContainingType is not null || method.ContainingType.TypeParameters.Length != 0 ||
			    !method.ContainingType.IsStatic || !IsPartial(method.ContainingType))
			{
				return Invalid(OperationDiagnosticDescriptors.InvalidOperationShape, location);
			}

			if (method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>()
				    .Count(static candidate => HasAttribute(candidate, LuaOperationAttributeMetadataName)) != 1)
			{
				return Invalid(OperationDiagnosticDescriptors.OverloadedOperation, location, method.Name);
			}

			ImmutableArray<OperationParameter>.Builder parameters = ImmutableArray.CreateBuilder<OperationParameter>();
			IParameterSymbol? outResult = null;
			foreach (IParameterSymbol parameter in method.Parameters)
			{
				if (parameter.RefKind == RefKind.Out)
				{
					if (outResult is not null || parameter.Ordinal != method.Parameters.Length - 1 ||
					    parameter.IsParams ||
					    parameter.HasExplicitDefaultValue)
					{
						return Invalid(OperationDiagnosticDescriptors.UnsupportedSignature, location, method.Name);
					}

					outResult = parameter;
					continue;
				}

				if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.HasExplicitDefaultValue ||
				    !IsScalar(parameter.Type))
				{
					return Invalid(OperationDiagnosticDescriptors.UnsupportedSignature, location, method.Name);
				}

				parameters.Add(new OperationParameter(TypeName(parameter.Type), EscapeIdentifier(parameter.Name)));
			}

			if (outResult is not null && method.ReturnType.SpecialType != SpecialType.System_Boolean)
			{
				return Invalid(OperationDiagnosticDescriptors.UnsupportedSignature, location, method.Name);
			}

			if (outResult is null && method.ReturnsVoid)
			{
				return Invalid(OperationDiagnosticDescriptors.UnsupportedSignature, location, method.Name);
			}

			ITypeSymbol sourceResult = outResult?.Type ?? method.ReturnType;
			AttributeData operationAttribute = context.Attributes[0];
			INamedTypeSymbol? mapper = operationAttribute.ConstructorArguments.Length == 1
				? operationAttribute.ConstructorArguments[0].Value as INamedTypeSymbol
				: null;
			ITypeSymbol result = sourceResult;
			if (mapper is null)
			{
				if (!IsScalar(sourceResult))
				{
					return Invalid(OperationDiagnosticDescriptors.MapperRequired, location, method.Name);
				}
			}
			else if (!TryGetMapperResult(mapper, sourceResult, out result))
			{
				return Invalid(OperationDiagnosticDescriptors.InvalidMapper, location, mapper.Name, method.Name);
			}
			else if (TryFindClientBoundaryViolation(sourceResult, ClientBoundaryRole.MapperSource,
			             out string sourceViolation))
			{
				return Invalid(OperationDiagnosticDescriptors.UnsafeMappedType, location, method.Name,
					sourceViolation);
			}
			else if (TryFindClientBoundaryViolation(result, ClientBoundaryRole.ClientResult,
			             out string resultViolation))
			{
				return Invalid(OperationDiagnosticDescriptors.UnsafeMappedType, location, method.Name,
					resultViolation);
			}

			string operationTypeName = method.Name + "LuaOperation";
			return new OperationCandidate(
				null,
				method.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
					.Replace("global::", string.Empty)
					.Replace('.', '_') + "_" + method.Name + ".CheatEngineLuaOperation.g.cs",
				method.ContainingNamespace.IsGlobalNamespace
					? string.Empty
					: method.ContainingNamespace.ToDisplayString(),
				TypeDeclaration(method.ContainingType, true),
				method.ContainingType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
				TypeName(method.ContainingType),
				method.Name,
				operationTypeName,
				parameters.ToImmutable(),
				TypeName(sourceResult),
				TypeName(result),
				mapper is null ? null : TypeName(mapper),
				outResult is not null,
				"Lua.Operation." + method.ContainingType.Name + "." + method.Name);
		}

		private static OperationCandidate Invalid(DiagnosticDescriptor descriptor, Location location,
			params object[] arguments)
		{
			return new OperationCandidate(Diagnostic.Create(descriptor, location, arguments), string.Empty,
				string.Empty,
				string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
				ImmutableArray<OperationParameter>.Empty,
				string.Empty, string.Empty, null, false, string.Empty);
		}
	}

	private readonly struct OperationParameter
	{
		public OperationParameter(string type, string name)
		{
			Type = type;
			Name = name;
			FieldName = "_" + name.TrimStart('@');
		}

		public string Type
		{
			get;
		}

		public string Name
		{
			get;
		}

		public string FieldName
		{
			get;
		}
	}

	private static class ModuleDiagnosticDescriptors
	{
		public static readonly DiagnosticDescriptor InvalidModuleShape = new(
			"CECLUA1001", "Lua module must be a non-static partial class",
			"[CheatEngineLuaModule] requires a top-level, non-static, non-generic partial class",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor InvalidBindingsType = new(
			"CECLUA1002", "Lua module requires a static SDK bindings type",
			"The bindings type for [CheatEngineLuaModule] must be a static class that owns SDK-generated registration methods",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor NoExports = new(
			"CECLUA1003", "Lua module bindings export nothing",
			"The bindings type '{0}' does not declare a [LuaFunction] export",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor InvalidExportSet = new(
			"CECLUA1004", "Lua module exports must have unique names",
			"The Lua module bindings contain an invalid or duplicate export name: '{0}'",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor InvalidModuleName = new(
			"CECLUA1005", "Lua module name cannot be blank",
			"The explicit [CheatEngineLuaModule] name cannot be empty or whitespace",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor NoPublicConstructor = new(
			"CECLUA1006", "Lua module needs a public constructor",
			"[CheatEngineLuaModule] cannot provide a safe public constructor because '{0}' declares constructors with no public accessibility; expose a public DI constructor or remove the explicit constructors so the generator can provide one",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);
	}

	private static class OperationDiagnosticDescriptors
	{
		public static readonly DiagnosticDescriptor InvalidOperationShape = new(
			"CECLUA1101", "Lua operation requires a supported SDK global declaration",
			"[CheatEngineLuaOperation] requires a static partial [LuaGlobal] method in a top-level static partial class",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor OverloadedOperation = new(
			"CECLUA1102", "Lua operation method cannot be overloaded",
			"Lua operation '{0}' is overloaded; use distinct method names so generated operation factories remain unambiguous",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor UnsupportedSignature = new(
			"CECLUA1103", "Lua operation has an unsupported result shape",
			"Lua operation '{0}' must have scalar input arguments and exactly one return value or one trailing out result",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor MapperRequired = new(
			"CECLUA1104", "Lua operation result requires a mapper",
			"Lua operation '{0}' returns a non-scalar SDK value; declare [CheatEngineLuaOperation(typeof(TMapper))] with an ILuaResultMapper implementation",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor InvalidMapper = new(
			"CECLUA1105", "Lua operation mapper does not match the SDK result",
			"Mapper '{0}' does not implement ILuaResultMapper for the result of Lua operation '{1}'",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);

		public static readonly DiagnosticDescriptor UnsafeMappedType = new(
			"CECLUA1106", "Lua operation mapper must project a safe Client result",
			"Lua operation '{0}' maps an unsafe value across the Client boundary: {1}",
			"CheatEngine.Client.Lua", DiagnosticSeverity.Error, true);
	}

	private sealed class SourceBuilder
	{
		private readonly StringBuilder _source = new();
		private int _indent;

		public void WriteGeneratedHeader()
		{
			WriteLine("// <auto-generated />");
			WriteLine("#nullable enable");
			WriteLine();
		}

		public void OpenNamespace(string @namespace)
		{
			if (string.IsNullOrEmpty(@namespace))
			{
				return;
			}

			WriteLine("namespace " + @namespace + ";");
			WriteLine();
		}

		public void OpenBlock()
		{
			WriteLine("{");
			Indent();
		}

		public void CloseBlock()
		{
			Unindent();
			WriteLine("}");
		}

		public void WriteLine(string value = "")
		{
			if (value.Length != 0)
			{
				_source.Append('\t', _indent);
			}

			_source.AppendLine(value);
		}

		public void Indent()
		{
			_indent++;
		}

		public void Unindent()
		{
			_indent--;
		}

		public override string ToString()
		{
			return _source.ToString();
		}
	}
}
