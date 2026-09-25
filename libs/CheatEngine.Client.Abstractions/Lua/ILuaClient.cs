using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Executes typed Lua operations without exposing a Lua state or CE object handle.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         One generic pair executes every operation: the operation type is a type parameter, so a readonly value
///         operation is passed by reference and called without boxing. For each <c>[CheatEngineLuaOperation]</c> the
///         generator also emits <c>Execute</c> and <c>TryExecute</c> extension methods on <see cref="ILuaClient" />
///         whose types are inferred from the operation, so a call reads <c>client.Execute(operation)</c>.
///     </para>
///     <para>
///         After its arguments, every member checks the activation: an ended activation throws
///         <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />, from a deactivation callback too (see
///         <see cref="ICheatEngineClient" />). A <c>Try</c> member returns every other failure; the
///         throwing member with the same inputs throws it through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />. An exception thrown by an operation propagates
///         unchanged, as the same instance.
///     </para>
/// </remarks>
public interface ILuaClient
{
	/// <summary>Tries to register one explicitly supplied application Lua module on Cheat Engine's main thread.</summary>
	/// <param name="luaModule">The module to register, usually one the Lua generator emitted.</param>
	/// <param name="lease">The lease that owns the module's registration on success; release it when done.</param>
	/// <param name="failure">
	///     The classified failure, including the failure of a registration the module itself reported; the default
	///     value on success.
	/// </param>
	/// <param name="cancellationToken">
	///     Observed before the registration is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns><see langword="true" /> when the module registered its exports and its lease was published.</returns>
	/// <remarks>
	///     The Client neither scans assemblies nor discovers exports by reflection. The module owns its generated SDK
	///     calls; the returned lease gives the Client deterministic, activation-scoped cleanup ownership. A module
	///     identity or export name that another module of this activation reserved is refused with
	///     <see cref="CheatEngineFailureKind.OperationRejected" />, like a symbol name the activation already owns,
	///     and a module instance that is already registered with <see cref="CheatEngineFailureKind.InvalidState" />;
	///     both report <see cref="CheatEngineHostEffect.NotStarted" /> before any Lua call.
	/// </remarks>
	/// <exception cref="ArgumentNullException"><paramref name="luaModule" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     The <see cref="ILuaModule.Descriptor" /> of <paramref name="luaModule" /> is the <see langword="default" />
	///     descriptor, which names no module.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryRegisterModule(
		ILuaModule luaModule,
		[NotNullWhen(true)] out ILuaModuleLease? lease,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Registers one explicitly supplied application Lua module or throws when registration fails.</summary>
	/// <param name="luaModule">The module to register, usually one the Lua generator emitted.</param>
	/// <param name="cancellationToken">
	///     Observed before the registration is dispatched to Cheat Engine's main thread.
	/// </param>
	/// <returns>The lease that owns the module's registration; release it when done.</returns>
	/// <exception cref="ArgumentNullException"><paramref name="luaModule" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException">
	///     The <see cref="ILuaModule.Descriptor" /> of <paramref name="luaModule" /> is the <see langword="default" />
	///     descriptor, which names no module.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the registration failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The registration observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The registration failed with any other failure kind.</exception>
	public ILuaModuleLease RegisterModule(ILuaModule luaModule, CancellationToken cancellationToken = default);

	/// <summary>Tries to execute one typed operation on Cheat Engine's main thread.</summary>
	/// <typeparam name="TOperation">The operation type; a readonly value operation is never boxed.</typeparam>
	/// <typeparam name="TResult">The copied result type.</typeparam>
	/// <param name="operation">The operation to execute.</param>
	/// <param name="result">The copied result on success.</param>
	/// <param name="failure">The mapped Client failure on failure.</param>
	/// <param name="cancellationToken">Cancellation observed before dispatch admission.</param>
	/// <returns><see langword="true" /> when the operation completed successfully.</returns>
	/// <remarks>An exception the operation throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentNullException"><paramref name="operation" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryExecute<TOperation, TResult>(
		in TOperation operation,
		[MaybeNullWhen(false)] out TResult result,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>;

	/// <summary>Executes one typed operation on Cheat Engine's main thread or throws when it fails.</summary>
	/// <typeparam name="TOperation">The operation type; a readonly value operation is never boxed.</typeparam>
	/// <typeparam name="TResult">The copied result type.</typeparam>
	/// <param name="operation">The operation to execute.</param>
	/// <param name="cancellationToken">Cancellation observed before dispatch admission.</param>
	/// <returns>The copied result.</returns>
	/// <remarks>An exception the operation throws propagates unchanged, as the same instance.</remarks>
	/// <exception cref="ArgumentNullException"><paramref name="operation" /> is <see langword="null" />.</exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the operation failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The operation observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The operation failed with any other failure kind.</exception>
	public TResult Execute<TOperation, TResult>(in TOperation operation, CancellationToken cancellationToken = default)
		where TOperation : ILuaOperation<TResult>;
}
