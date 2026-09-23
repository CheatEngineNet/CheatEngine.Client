namespace CheatEngine.Client.SourceGenerators.Lua.Model;

/// <summary>
///     The equatable, Roslyn-free description of one <c>[CheatEngineLuaOperation]</c> declaration: either the data the
///     operation emitter needs or the diagnostics that stop generation.
/// </summary>
internal sealed class OperationModel : IEquatable<OperationModel>
{
	public OperationModel(EquatableArray<DiagnosticInfo> diagnostics, string hintName, string @namespace,
		string containingTypeDeclaration, string operationVisibility, string bindingsType, string methodName,
		string operationTypeName, EquatableArray<OperationParameter> parameters, string sourceResultType,
		string resultType, string? mapperType, bool hasOutResult, string failureOperation)
	{
		Diagnostics = diagnostics;
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

	/// <summary>Gets the diagnostics that stop generation; empty for a valid operation.</summary>
	public EquatableArray<DiagnosticInfo> Diagnostics
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

	public EquatableArray<OperationParameter> Parameters
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

	public bool IsValid => Diagnostics.IsEmpty;

	public static OperationModel Invalid(DiagnosticInfo diagnostic)
	{
		return new OperationModel(EquatableArray.Create(diagnostic), string.Empty, string.Empty, string.Empty,
			string.Empty, string.Empty, string.Empty, string.Empty, EquatableArray<OperationParameter>.Empty,
			string.Empty, string.Empty, null, false, string.Empty);
	}

	public bool Equals(OperationModel? other)
	{
		return other is not null && Diagnostics.Equals(other.Diagnostics) &&
			   string.Equals(HintName, other.HintName, StringComparison.Ordinal) &&
			   string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
			   string.Equals(ContainingTypeDeclaration, other.ContainingTypeDeclaration, StringComparison.Ordinal) &&
			   string.Equals(OperationVisibility, other.OperationVisibility, StringComparison.Ordinal) &&
			   string.Equals(BindingsType, other.BindingsType, StringComparison.Ordinal) &&
			   string.Equals(MethodName, other.MethodName, StringComparison.Ordinal) &&
			   string.Equals(OperationTypeName, other.OperationTypeName, StringComparison.Ordinal) &&
			   Parameters.Equals(other.Parameters) &&
			   string.Equals(SourceResultType, other.SourceResultType, StringComparison.Ordinal) &&
			   string.Equals(ResultType, other.ResultType, StringComparison.Ordinal) &&
			   string.Equals(MapperType, other.MapperType, StringComparison.Ordinal) &&
			   HasOutResult == other.HasOutResult &&
			   string.Equals(FailureOperation, other.FailureOperation, StringComparison.Ordinal);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as OperationModel);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			int hash = StringComparer.Ordinal.GetHashCode(HintName);
			hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(ResultType);
			return (hash * 31) + Parameters.GetHashCode();
		}
	}
}

/// <summary>One scalar input parameter of a generated Lua operation.</summary>
internal readonly struct OperationParameter : IEquatable<OperationParameter>
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

	public bool Equals(OperationParameter other)
	{
		return string.Equals(Type, other.Type, StringComparison.Ordinal) &&
			   string.Equals(Name, other.Name, StringComparison.Ordinal);
	}

	public override bool Equals(object? obj)
	{
		return obj is OperationParameter other && Equals(other);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			return (StringComparer.Ordinal.GetHashCode(Type ?? string.Empty) * 31) +
				   StringComparer.Ordinal.GetHashCode(Name ?? string.Empty);
		}
	}
}
