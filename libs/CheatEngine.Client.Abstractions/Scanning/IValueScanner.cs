using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Results;

namespace CheatEngine.Client.Scanning;

/// <summary>Creates value-scan sessions over Cheat Engine's own scanner, one session per independent scan.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add members
///         to it, so implement it only in a test double.
///     </para>
///     <para>
///         <b>Experimental (<c>CECLIENT5001</c>).</b> The value-scan API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         A session owns one Cheat Engine <c>MemScan</c> and its <c>FoundList</c>, created for the target Cheat Engine
///         has selected. Creation is refused with <see cref="CheatEngineFailureKind.TargetIdentityUnavailable" /> when the
///         identity of that target cannot be established, before any Cheat Engine object exists. The session belongs to
///         the activation and to that target: disabling the plugin releases it, and selecting another process ends it
///         with a release that CheatEngine.SDK refuses on the new target, which leaves both objects in Cheat Engine
///         (see <see cref="IValueScanSession" />). Release a session before selecting another process.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.ValueScans, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public interface IValueScanner
{
	/// <summary>Tries to create a value-scan session for Cheat Engine's selected target.</summary>
	/// <param name="session">The new session when the method returns <see langword="true" />; release it when done.</param>
	/// <param name="failure">The classified failure when the method returns <see langword="false" />.</param>
	/// <param name="cancellationToken">Observed before Cheat Engine creates the session, and after.</param>
	/// <returns><see langword="true" /> when the session was created.</returns>
	/// <remarks>
	///     A cancellation observed after Cheat Engine created the session releases it at once and publishes nothing.
	/// </remarks>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping: no new lease is created while it stops.
	/// </exception>
	public bool TryCreateSession([NotNullWhen(true)] out IValueScanSession? session, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default);

	/// <summary>Creates a value-scan session for Cheat Engine's selected target, or throws the failure.</summary>
	/// <param name="cancellationToken">Observed before Cheat Engine creates the session, and after.</param>
	/// <returns>The new session; release it when done.</returns>
	/// <exception cref="CheatEngineActivationExpiredException">The activation has ended.</exception>
	/// <exception cref="CheatEngineInvalidStateException">
	///     The activation is stopping, or the creation failed with <see cref="CheatEngineFailureKind.InvalidState" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationCanceledException">
	///     The creation observed the cancellation of <paramref name="cancellationToken" />.
	/// </exception>
	/// <exception cref="CheatEngineOperationException">The creation failed with any other failure kind.</exception>
	public IValueScanSession CreateSession(CancellationToken cancellationToken = default);
}
