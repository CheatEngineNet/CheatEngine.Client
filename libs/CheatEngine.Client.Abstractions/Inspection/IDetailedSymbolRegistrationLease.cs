namespace CheatEngine.Client.Inspection;

/// <summary>
///     Companion of <see cref="ISymbolRegistrationLease" /> that reports what the release did instead of hiding a skipped
///     or unconfirmed cleanup.
/// </summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         The Client's symbol leases implement this interface; test for it with a type check
///         (<c>lease is IDetailedSymbolRegistrationLease detailed</c>).
///     </para>
/// </remarks>
public interface IDetailedSymbolRegistrationLease : ISymbolRegistrationLease
{
	/// <summary>Releases the registration on Cheat Engine's main thread and reports the outcome.</summary>
	/// <returns>
	///     <see cref="SymbolLeaseReleaseKind.Released" />, <see cref="SymbolLeaseReleaseKind.Replaced" /> and
	///     <see cref="SymbolLeaseReleaseKind.ExternallyRemoved" /> are terminal;
	///     <see cref="SymbolLeaseReleaseKind.CleanupUnavailable" /> keeps the lease active;
	///     <see cref="SymbolLeaseReleaseKind.AlreadyReleased" /> reports a lease that was already terminal.
	/// </returns>
	/// <exception cref="CheatEngine.Client.Results.CheatEngineClientException">
	///     The work could not be dispatched to Cheat Engine (for example admission closed during disable); the lease stays
	///     active.
	/// </exception>
	public SymbolLeaseReleaseKind ReleaseDetailed();
}
