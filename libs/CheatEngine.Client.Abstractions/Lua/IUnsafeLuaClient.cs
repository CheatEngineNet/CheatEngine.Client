using CheatEngine.Client.Results;

namespace CheatEngine.Client.Lua;

/// <summary>Opt-in execution of trusted arbitrary Lua source.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         Dependency injection registers it only when the activation calls <c>EnableUnsafeLuaExecution()</c>; an
///         execution that the activation policy did not enable fails with
///         <see cref="CheatEngineFailureKind.CapabilityUnavailable" /> and
///         <see cref="CheatEngineHostEffect.NotStarted" />. After its arguments, every member checks the activation: an
///         ended activation throws <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />, from a deactivation callback too (see
///         <see cref="ICheatEngineClient" />).
///     </para>
/// </remarks>
public interface IUnsafeLuaClient
{
	/// <summary>Tries to execute trusted Lua source through the SDK protected-call boundary.</summary>
	/// <param name="script">The trusted Lua source and its optional chunk name.</param>
	/// <param name="failure">
	///     The classified failure; the default value on success. A script that failed may have run partially.
	/// </param>
	/// <param name="cancellationToken">Observed before the script is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the script ran without a Lua error.</returns>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="script" /> is the <see langword="default" /> script, which has no source.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="script" /> is a tampered script whose chunk name is empty.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">The activation is stopping.</exception>
	public bool TryExecute(LuaScript script, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Executes trusted Lua source or throws when execution fails.</summary>
	/// <param name="script">The trusted Lua source and its optional chunk name.</param>
	/// <param name="cancellationToken">Observed before the script is dispatched to Cheat Engine's main thread.</param>
	/// <exception cref="ArgumentNullException">
	///     <paramref name="script" /> is the <see langword="default" /> script, which has no source.
	/// </exception>
	/// <exception cref="ArgumentException">
	///     <paramref name="script" /> is a tampered script whose chunk name is empty.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the execution failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The execution observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">
	///     The execution failed with any other failure kind; a script that failed may have run partially.
	/// </exception>
	public void Execute(LuaScript script, CancellationToken cancellationToken = default);
}
