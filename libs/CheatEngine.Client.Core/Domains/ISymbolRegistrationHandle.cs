using CheatEngine.SDK.Engine.Inspection;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Internal view of one CheatEngine.SDK <c>SymbolRegistrationLease</c>: the release authority of a symbol that the
///     SDK's ownership coordinator registered.
/// </summary>
/// <remarks>
///     The SDK lease has no public constructor, so Core owns it only through this handle and the Client lease tests use a
///     fake. The handle is called on Cheat Engine's main thread only.
/// </remarks>
internal interface ISymbolRegistrationHandle
{
	/// <summary>Attempts the coordinator-qualified unregistration (<c>SymbolRegistrationLease.Release</c>).</summary>
	/// <returns>
	///     The kind CheatEngine.SDK reports. Only <see cref="SymbolRegistrationReleaseKind.Unknown" /> and
	///     <see cref="SymbolRegistrationReleaseKind.CleanupUnavailable" /> leave the SDK lease retryable.
	/// </returns>
	public SymbolRegistrationReleaseKind Release();
}
