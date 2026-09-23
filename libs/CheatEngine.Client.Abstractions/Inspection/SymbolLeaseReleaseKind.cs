namespace CheatEngine.Client.Inspection;

/// <summary>Describes what the release of a Client symbol registration did in Cheat Engine.</summary>
/// <remarks>
///     A lease unregisters its name only when the name still resolves to the leased address. The check and the
///     unregistration are not atomic: a third party can replace the name in between, and a third-party registration of
///     the same name at the same address is indistinguishable from the lease's own and is removed.
/// </remarks>
public enum SymbolLeaseReleaseKind : byte
{
	/// <summary>No release outcome was recorded.</summary>
	Unknown = 0,

	/// <summary>The name still resolved to the leased address and was unregistered.</summary>
	Released = 1,

	/// <summary>The lease had already been released; nothing was done.</summary>
	AlreadyReleased = 2,

	/// <summary>
	///     The name resolves to another address: a third party replaced the registration, so the Client left it in place.
	/// </summary>
	Replaced = 3,

	/// <summary>The name no longer resolves: it was removed outside this lease, so nothing was unregistered.</summary>
	ExternallyRemoved = 4,

	/// <summary>
	///     The ownership check or the unregistration failed: nothing is confirmed, and the lease stays active so a later
	///     cleanup can try again.
	/// </summary>
	CleanupUnavailable = 5
}
