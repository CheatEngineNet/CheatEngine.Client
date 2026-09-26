using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Inspection;

/// <summary>Owns one Client-created Cheat Engine symbol registration for the current activation.</summary>
/// <remarks>
///     <para>
///         <b>Call-only.</b> The Client implements this interface and applications call it. A minor release can add
///         members to it, so implement it only in a test double.
///     </para>
///     <para>
///         The name was registered through CheatEngine.SDK's symbol ownership coordinator, and the release delegates to
///         it. The name is unregistered only while it still resolves to <see cref="Address" /> and no newer registration
///         of the same name through that coordinator superseded the lease: a name that a third party replaced or removed
///         is left in place. The check and the unregistration are best effort, not atomic: Cheat Engine has no
///         registration token, so a third party can replace the name in between, and a third-party registration of the
///         same name at the same address is indistinguishable and is removed.
///     </para>
///     <para>
///         <see cref="ICheatEngineLease.Release" /> reports what happened, and <see cref="IDisposable.Dispose" /> never
///         throws: <see cref="CheatEngine.Client.Results.LeaseReleaseKind.Released" />;
///         <see cref="CheatEngine.Client.Results.LeaseReleaseKind.Replaced" /> or
///         <see cref="CheatEngine.Client.Results.LeaseReleaseKind.ExternallyRemoved" /> (the name no longer resolves to
///         <see cref="Address" />; nothing was unregistered);
///         <see cref="CheatEngine.Client.Results.LeaseReleaseKind.Superseded" /> (a newer registration of the name
///         through CheatEngine.SDK owns it now; nothing was unregistered);
///         <see cref="CheatEngine.Client.Results.LeaseReleaseKind.RefusedRuntimeChanged" /> (the Lua runtime of the
///         registration is gone, so no call was made and the name may remain);
///         <see cref="CheatEngine.Client.Results.LeaseReleaseKind.CleanupUnconfirmed" /> (the unregistration began and
///         failed); and the retryable <see cref="CheatEngine.Client.Results.LeaseReleaseKind.CleanupUnavailable" /> (no
///         call began or the ownership could not be verified: the lease stays active and the activation cleanup tries
///         again before the plugin is disabled).
///     </para>
/// </remarks>
public interface ISymbolRegistrationLease : ICheatEngineLease
{
	/// <summary>Gets the exact symbol name registered in Cheat Engine.</summary>
	public string Name
	{
		get;
	}

	/// <summary>Gets the target address bound to <see cref="Name" />.</summary>
	public Address Address
	{
		get;
	}
}
