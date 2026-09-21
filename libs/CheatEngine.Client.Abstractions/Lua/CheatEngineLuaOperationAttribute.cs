namespace CheatEngine.Client.Lua;

/// <summary>
///     Marks one SDK <c>[LuaGlobal]</c> declaration for generation of a copied, typed Client operation.
/// </summary>
/// <remarks>
///     <para>
///         The Client generator emits a readonly operation value and a strongly typed factory beside the declaring
///         binding type. The operation catches SDK Lua failures inside the Client boundary and returns
///         <see cref="CheatEngine.Client.Results.CheatEngineFailure" /> instead of exposing a Lua state, reference,
///         status or CE object.
///     </para>
///     <para>
///         Supply a mapper type when the SDK result is not itself an approved copied scalar. The named type must
///         implement <see cref="ILuaResultMapper{TSource, TResult}" /> for the SDK result and the
///         desired Client DTO. The generator calls that mapper statically; no reflection is involved.
///     </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CheatEngineLuaOperationAttribute : Attribute
{
	/// <summary>Initializes scalar-result operation generation without a mapper.</summary>
	public CheatEngineLuaOperationAttribute()
	{
	}

	/// <summary>Initializes operation generation with one explicit static Client result mapper.</summary>
	/// <param name="mapperType">The type that implements the required static mapper contract.</param>
	public CheatEngineLuaOperationAttribute(Type mapperType)
	{
		MapperType = mapperType ?? throw new ArgumentNullException(nameof(mapperType));
	}

	/// <summary>Gets the explicit result mapper type, or <see langword="null" /> for a copied scalar result.</summary>
	public Type? MapperType
	{
		get;
	}
}
