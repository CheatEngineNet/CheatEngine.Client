using CheatEngine.Client.Results;

namespace CheatEngine.Client.Runtime;

/// <summary>Reads immutable runtime facts and capability observations for the active Cheat Engine activation.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         After its arguments, every operation checks the activation: an ended activation throws
///         <see cref="CheatEngineActivationExpiredException" /> and a stopping one
///         <see cref="CheatEngineInvalidStateException" />, except that a deactivation callback can still call every
///         operation on Cheat Engine's main thread (see <see cref="ICheatEngineClient" />). A <c>Try</c> member
///         returns every other failure; the throwing member with the same inputs throws it through
///         <see cref="CheatEngineFailure.Throw(CancellationToken)" />.
///     </para>
/// </remarks>
public interface ICheatEngineRuntime
{
	/// <summary>Gets the activation epoch for which this runtime service is valid.</summary>
	public long Epoch
	{
		get;
	}

	/// <summary>Tries to capture the runtime facts available to the active plugin.</summary>
	/// <param name="snapshot">The captured runtime facts on success; otherwise the default value.</param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the capture is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the runtime facts were captured.</returns>
	/// <remarks>
	///     A fact that Cheat Engine could not report stays unknown in the snapshot instead of failing the call.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetSnapshot(
		out CheatEngineRuntimeSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Captures the runtime facts or throws when they are unavailable.</summary>
	/// <param name="cancellationToken">Observed before the capture is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The captured runtime facts.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the capture failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The capture observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The capture failed with any other failure kind.</exception>
	public CheatEngineRuntimeSnapshot GetSnapshot(CancellationToken cancellationToken = default);

	/// <summary>Reads one Client capability observation without exposing internal adapters or handles.</summary>
	/// <param name="capability">The identifier of the capability to read.</param>
	/// <param name="availability">
	///     The observation on success; a capability this Client release does not define is reported with unknown
	///     evidence. Otherwise the default value.
	/// </param>
	/// <param name="failure">The classified failure; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the capture is dispatched to Cheat Engine's main thread.</param>
	/// <returns><see langword="true" /> when the runtime facts behind the observation were captured.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="capability" /> is the <see langword="default" /> identifier, which names no capability.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback.
	/// </exception>
	public bool TryGetClientCapability(
		ClientCapabilityId capability,
		out ClientCapabilityAvailability availability,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Reads one Client capability observation or throws when it cannot be observed.</summary>
	/// <param name="capability">The identifier of the capability to read.</param>
	/// <param name="cancellationToken">Observed before the capture is dispatched to Cheat Engine's main thread.</param>
	/// <returns>The observation; a capability this Client release does not define has unknown evidence.</returns>
	/// <exception cref="ArgumentException">
	///     <paramref name="capability" /> is the <see langword="default" /> identifier, which names no capability.
	/// </exception>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, outside a deactivation callback, or the capture failed with
	///     <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The capture observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The capture failed with any other failure kind.</exception>
	public ClientCapabilityAvailability GetClientCapability(
		ClientCapabilityId capability,
		CancellationToken cancellationToken = default);
}
