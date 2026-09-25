using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Assembly;

/// <summary>Checks Auto Assembler scripts and applies them as Client-owned patch leases.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5004</c>).</b> The Auto Assembler API can change in a minor release until its
///         live scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         <b>Explicit opt-in.</b> The dependency-injection integration registers this client only when the activation
///         calls <c>CheatEngineClientBuilder.EnableAutoAssemblerPatches()</c>; it is never a property of
///         <see cref="ICheatEngineClient" />. Without the opt-in no implementation is registered, and the
///         <c>Client.AutoAssemblerPatches</c> capability reports a <c>Missing</c> policy gate. An Auto Assembler script
///         can allocate memory, inject code and run Lua in Cheat Engine: pass only scripts your plugin owns.
///     </para>
///     <para>
///         <see cref="TryApplyPatch" /> applies the script once through Cheat Engine's <c>autoAssemble</c> and returns
///         the only owner of the disable information Cheat Engine returned. Releasing the lease runs the script's
///         <c>[DISABLE]</c> section once with that information, on the target the patch was applied to; the Client never
///         rebuilds a <c>[DISABLE]</c> section and never retries a disable that began.
///     </para>
///     <para>
///         <b>Release every patch lease before selecting another process.</b> The lease is bound to the target
///         selection of the process CheatEngine.SDK applied the patch in, and the Client observes a selection change
///         only after Cheat Engine already targets the new process. It then ends the lease with
///         <see cref="LeaseReleaseKind.RefusedTargetChanged" />: CheatEngine.SDK refuses the disable on the new target
///         but consumes the disable information, so the patch stays in the previous process,
///         <see cref="ICheatEngineLease.RequiresManualRecovery" /> is <see langword="true" />, and selecting the
///         previous process again cannot disable it.
///     </para>
///     <para>
///         A cancellation token is observed only before the work is dispatched to Cheat Engine's main thread
///         (<see cref="CheatEngineFailureKind.Cancelled" /> with <see cref="CheatEngineHostEffect.NotStarted" />): it never
///         interrupts or undoes a check or an activation that began. The host messages and warnings Cheat Engine returns
///         are bounded copies, never parsed; like <see cref="CheatEngineFailure.Message" />, they can contain script text
///         and are user data.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.AutoAssemblerPatches, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IAutoAssemblerClient
{
	/// <summary>Tries to check the <c>[ENABLE]</c> section of a script without applying it.</summary>
	/// <param name="script">The script to check.</param>
	/// <param name="result">
	///     Whether Cheat Engine accepted the section, with its bounded messages when it did not; the default value when the
	///     check could not run.
	/// </param>
	/// <param name="failure">The reason the check could not run; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the check is dispatched.</param>
	/// <returns>
	///     <see langword="true" /> when Cheat Engine reported a verdict, including a rejection
	///     (<see cref="AutoAssemblerCheckResult.IsAccepted" /> is then <see langword="false" />).
	/// </returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is the default value.</exception>
	/// <remarks>
	///     An accepted check does not prove that an activation will succeed: the target or its symbols can change before
	///     it, and allocations or injections are not attempted. A check never creates a lease.
	/// </remarks>
	public bool TryCheck(AutoAssemblerScript script, out AutoAssemblerCheckResult result,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Checks the <c>[ENABLE]</c> section of a script, or throws when the check could not run.</summary>
	/// <param name="script">The script to check.</param>
	/// <param name="cancellationToken">Observed before the check is dispatched.</param>
	/// <returns>Whether Cheat Engine accepted the section, with its bounded messages when it did not.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is the default value.</exception>
	/// <exception cref="CheatEngineOperationCanceledException">The check was cancelled before it was dispatched.</exception>
	/// <exception cref="CheatEngineClientException">The check could not run.</exception>
	public AutoAssemblerCheckResult Check(AutoAssemblerScript script, CancellationToken cancellationToken = default);

	/// <summary>Tries to apply a script and to take ownership of the patch Cheat Engine applied.</summary>
	/// <param name="script">The complete script, with its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <param name="lease">The owner of the applied patch on success; otherwise <see langword="null" />.</param>
	/// <param name="failure">The reason no lease was returned; the default value on success.</param>
	/// <param name="cancellationToken">Observed before the activation is dispatched.</param>
	/// <returns><see langword="true" /> when Cheat Engine applied the script and the Client owns the patch.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is the default value.</exception>
	/// <remarks>
	///     A rejection (<see cref="CheatEngineFailureKind.OperationRejected" />) does not prove that nothing changed: a
	///     script can apply part of its effects before it fails, so its host effect is
	///     <see cref="CheatEngineHostEffect.Unknown" /> and Cheat Engine's bounded error text is in
	///     <see cref="CheatEngineFailure.Message" />. When Cheat Engine applied the script but the selected target no
	///     longer matched right after, the lease is still returned with
	///     <see cref="IAutoAssemblerPatchLease.AppliedAfterTargetChange" /> set, and the Client logs a warning. A script
	///     applied without an owner (<see cref="CheatEngineFailureKind.IndeterminateHostResult" />), and a failed
	///     activation whose owner was released incompletely, report
	///     <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />: the patch may remain in the target.
	/// </remarks>
	public bool TryApplyPatch(AutoAssemblerScript script, [NotNullWhen(true)] out IAutoAssemblerPatchLease? lease,
		out CheatEngineFailure failure, CancellationToken cancellationToken = default);

	/// <summary>Applies a script and returns the owner of the patch, or throws when no lease can be returned.</summary>
	/// <param name="script">The complete script, with its <c>[ENABLE]</c> and <c>[DISABLE]</c> sections.</param>
	/// <param name="cancellationToken">Observed before the activation is dispatched.</param>
	/// <returns>The owner of the applied patch.</returns>
	/// <exception cref="ArgumentException"><paramref name="script" /> is the default value.</exception>
	/// <exception cref="CheatEngineOperationCanceledException">The activation was cancelled before it was dispatched.</exception>
	/// <exception cref="CheatEngineClientException">No lease was returned.</exception>
	public IAutoAssemblerPatchLease ApplyPatch(AutoAssemblerScript script,
		CancellationToken cancellationToken = default);
}
