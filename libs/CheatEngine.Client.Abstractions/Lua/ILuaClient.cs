using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Executes typed Lua operations without exposing a Lua state or CE object handle.</summary>
/// <remarks>
///     <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///     to it, so implement it only in a test double.
/// </remarks>
public interface ILuaClient
{
	/// <summary>Tries to register one explicitly supplied application Lua module on Cheat Engine's main thread.</summary>
	/// <remarks>
	///     The Client neither scans assemblies nor discovers exports by reflection. The module owns its generated SDK
	///     calls; the returned lease gives the Client deterministic, activation-scoped cleanup ownership.
	/// </remarks>
	public bool TryRegisterModule(
		ILuaModule luaModule,
		[NotNullWhen(true)] out ILuaModuleLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers one explicitly supplied application Lua module or throws when registration fails.</summary>
	public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default);

#pragma warning disable RS0026, RS0027 // The constrained overloads preserve the shipped overloads and require a token.

	/// <summary>Tries to execute one typed operation on Cheat Engine's main thread.</summary>
	public bool TryExecute<TResult>(ILuaOperation<TResult> operation, [MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Executes one typed operation or throws when it fails.</summary>
	public TResult Execute<TResult>(ILuaOperation<TResult> operation, CancellationToken cancellationToken = default);

	/// <summary>
	///     Tries to execute a value-type operation without forcing callers to convert it to an interface explicitly.
	/// </summary>
	/// <typeparam name="TOperation">The readonly value operation type.</typeparam>
	/// <typeparam name="TResult">The copied result type.</typeparam>
	/// <param name="operation">The operation to execute.</param>
	/// <param name="result">The copied result on success.</param>
	/// <param name="failure">The mapped Client failure on failure.</param>
	/// <param name="cancellationToken">Cancellation observed before dispatch admission.</param>
	/// <returns><see langword="true" /> when the operation completed successfully.</returns>
	/// <remarks>
	///     This default implementation preserves source and binary compatibility for third-party implementations of
	///     <see cref="ILuaClient" />. Core supplies a constrained implementation that avoids boxing the generated
	///     readonly record struct operation on its measured path.
	/// </remarks>
	public bool TryExecute<TOperation, TResult>(
		TOperation operation,
		[MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken)
		where TOperation : struct, ILuaOperation<TResult>
	{
		return TryExecute<TResult>(operation, out result, out failure, cancellationToken);
	}

	/// <summary>Executes a value-type operation or throws when it fails.</summary>
	/// <typeparam name="TOperation">The readonly value operation type.</typeparam>
	/// <typeparam name="TResult">The copied result type.</typeparam>
	/// <param name="operation">The operation to execute.</param>
	/// <param name="cancellationToken">Cancellation observed before dispatch admission.</param>
	/// <returns>The copied result.</returns>
	/// <remarks>
	///     This default implementation preserves compatibility for existing third-party implementations. Core replaces
	///     it with constrained dispatch for generated value operations.
	/// </remarks>
	public TResult Execute<TOperation, TResult>(TOperation operation, CancellationToken cancellationToken)
		where TOperation : struct, ILuaOperation<TResult>
	{
		return Execute(operation, cancellationToken);
	}

#pragma warning restore RS0026, RS0027
}
