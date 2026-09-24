using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>A typed, handle-free Lua operation implemented with SDK-generated bindings.</summary>
/// <typeparam name="TResult">The copied managed result type.</typeparam>
/// <remarks>
///     <b>Implementable.</b> Applications implement this interface and the Client calls it. Its members are frozen for
///     the 1.x line.
/// </remarks>
public interface ILuaOperation<TResult>
{
	/// <summary>Executes while the Client holds a valid activation and main-thread boundary.</summary>
	/// <param name="context">The ephemeral, handle-free context for this one synchronous operation.</param>
	/// <param name="result">The copied managed result on success.</param>
	/// <param name="failure">The expected operation failure on failure.</param>
	/// <returns><see langword="true" /> when the operation completed successfully.</returns>
	/// <remarks>
	///     Do not retain <paramref name="context" />. Generated SDK bindings own their own protected Lua calls; this
	///     context only proves that the Client dispatched the operation in its active activation scope.
	/// </remarks>
	public bool TryExecute(ILuaExecutionContext context, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure);
}
