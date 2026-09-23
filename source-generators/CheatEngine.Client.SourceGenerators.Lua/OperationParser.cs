using System.Collections.Immutable;

using CheatEngine.Client.SourceGenerators.Lua.Model;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>Turns one <c>[CheatEngineLuaOperation]</c> method into an equatable <see cref="OperationModel" />.</summary>
internal static class OperationParser
{
	public static OperationModel Parse(GeneratorAttributeSyntaxContext context)
	{
		IMethodSymbol? method = context.TargetSymbol as IMethodSymbol;
		LocationInfo? location = LocationInfo.From(context.TargetNode);
		if (method is null || !CheatEngineLuaGenerator.HasAttribute(method,
				CheatEngineLuaGenerator.LuaGlobalAttributeMetadataName) ||
			method.MethodKind != MethodKind.Ordinary ||
			!method.IsStatic || method.IsGenericMethod || !method.IsPartialDefinition ||
			method.PartialImplementationPart is not null || method.ReturnsByRef || method.ReturnsByRefReadonly ||
			method.ContainingType.ContainingType is not null || method.ContainingType.IsFileLocal ||
			method.ContainingType.OriginalDefinition.TypeParameters.Length != 0 ||
			!method.ContainingType.IsStatic || !CheatEngineLuaGenerator.IsPartial(method.ContainingType))
		{
			return Invalid(CheatEngineLuaDiagnostics.InvalidOperationShape, location);
		}

		if (method.ContainingType.GetMembers(method.Name).OfType<IMethodSymbol>()
				.Count(static candidate => CheatEngineLuaGenerator.HasAttribute(candidate,
					CheatEngineLuaGenerator.LuaOperationAttributeMetadataName)) != 1)
		{
			return Invalid(CheatEngineLuaDiagnostics.OverloadedOperation, location, method.Name);
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
					return Invalid(CheatEngineLuaDiagnostics.UnsupportedSignature, location, method.Name);
				}

				outResult = parameter;
				continue;
			}

			if (parameter.RefKind != RefKind.None || parameter.IsParams || parameter.HasExplicitDefaultValue ||
				!CheatEngineLuaGenerator.IsScalar(parameter.Type))
			{
				return Invalid(CheatEngineLuaDiagnostics.UnsupportedSignature, location, method.Name);
			}

			parameters.Add(new OperationParameter(CheatEngineLuaGenerator.TypeName(parameter.Type),
				CheatEngineLuaGenerator.EscapeIdentifier(parameter.Name)));
		}

		if (outResult is not null && method.ReturnType.SpecialType != SpecialType.System_Boolean)
		{
			return Invalid(CheatEngineLuaDiagnostics.UnsupportedSignature, location, method.Name);
		}

		if (outResult is null && method.ReturnsVoid)
		{
			return Invalid(CheatEngineLuaDiagnostics.UnsupportedSignature, location, method.Name);
		}

		ITypeSymbol sourceResult = outResult?.Type ?? method.ReturnType;
		AttributeData operationAttribute = context.Attributes[0];
		INamedTypeSymbol? mapper = operationAttribute.ConstructorArguments.Length == 1
			? operationAttribute.ConstructorArguments[0].Value as INamedTypeSymbol
			: null;
		ITypeSymbol result = sourceResult;
		if (mapper is null)
		{
			if (!CheatEngineLuaGenerator.IsScalar(sourceResult))
			{
				return Invalid(CheatEngineLuaDiagnostics.MapperRequired, location, method.Name);
			}
		}
		else if (!CheatEngineLuaGenerator.TryGetMapperResult(mapper, sourceResult, out result))
		{
			return Invalid(CheatEngineLuaDiagnostics.InvalidMapper, location, mapper.Name, method.Name);
		}
		else if (CheatEngineLuaGenerator.TryFindClientBoundaryViolation(sourceResult,
					 CheatEngineLuaGenerator.ClientBoundaryRole.MapperSource, out string sourceViolation))
		{
			return Invalid(CheatEngineLuaDiagnostics.UnsafeMappedType, location, method.Name, sourceViolation);
		}
		else if (CheatEngineLuaGenerator.TryFindClientBoundaryViolation(result,
					 CheatEngineLuaGenerator.ClientBoundaryRole.ClientResult, out string resultViolation))
		{
			return Invalid(CheatEngineLuaDiagnostics.UnsafeMappedType, location, method.Name, resultViolation);
		}

		string operationTypeName = method.Name + "LuaOperation";
		return new OperationModel(
			EquatableArray<DiagnosticInfo>.Empty,
			CheatEngineLuaGenerator.HintNameStem(method.ContainingType) + "_" + method.Name +
			".CheatEngineLuaOperation.g.cs",
			method.ContainingNamespace.IsGlobalNamespace
				? string.Empty
				: method.ContainingNamespace.ToDisplayString(),
			CheatEngineLuaGenerator.TypeDeclaration(method.ContainingType, true),
			method.ContainingType.DeclaredAccessibility == Accessibility.Public ? "public" : "internal",
			CheatEngineLuaGenerator.TypeName(method.ContainingType),
			method.Name,
			operationTypeName,
			EquatableArray.Create(parameters.ToImmutable()),
			CheatEngineLuaGenerator.TypeName(sourceResult),
			CheatEngineLuaGenerator.TypeName(result),
			mapper is null ? null : CheatEngineLuaGenerator.TypeName(mapper),
			outResult is not null,
			"Lua.Operation." + method.ContainingType.Name + "." + method.Name);
	}

	private static OperationModel Invalid(DiagnosticDescriptor descriptor, LocationInfo? location,
		params string[] arguments)
	{
		return OperationModel.Invalid(DiagnosticInfo.Create(descriptor, location, arguments));
	}
}
