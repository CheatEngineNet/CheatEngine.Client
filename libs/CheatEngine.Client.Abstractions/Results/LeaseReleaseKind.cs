namespace CheatEngine.Client.Results;

/// <summary>Classifies one attempt to release a Client lease (<see cref="ICheatEngineLease" />).</summary>
/// <remarks>
///     <para>
///         The kind is the one vocabulary of every Client lease, whatever CheatEngine.SDK primitive backs it.
///         <see cref="LeaseReleaseOutcome" /> derives from it whether the release is complete, whether the lease can
///         still be released later, and whether something may remain that only manual recovery can remove.
///     </para>
///     <para>
///         <see cref="Unknown" /> is the zero value, so an outcome that was never assigned never reads as a release.
///         Values never change meaning; new values can be added in a minor release, so handle an unrecognized value like
///         <see cref="Unknown" />.
///     </para>
/// </remarks>
public enum LeaseReleaseKind
{
	/// <summary>
	///     No release outcome could be established: the value of <see langword="default" />. The lease stays active and
	///     a later release can try again.
	/// </summary>
	Unknown = 0,

	/// <summary>The resource was released and Cheat Engine confirmed it.</summary>
	Released = 1,

	/// <summary>An earlier attempt had already ended the lease; this attempt did nothing.</summary>
	AlreadyReleased = 2,

	/// <summary>
	///     Part of the resource was released and at least one part failed; the failed parts may remain and are not
	///     retried.
	/// </summary>
	PartiallyReleased = 3,

	/// <summary>
	///     A third party replaced the resource (for example a symbol name now bound to another address), so the lease left
	///     it in place and released nothing.
	/// </summary>
	Replaced = 4,

	/// <summary>A newer registration made through the Client replaced the lease, so nothing was left to release.</summary>
	Superseded = 5,

	/// <summary>The resource was already gone: something outside the lease removed it, so nothing was released.</summary>
	ExternallyRemoved = 6,

	/// <summary>
	///     Cleanup was refused before any Cheat Engine call because Cheat Engine has no selected target. The resource may
	///     remain in the target it was created in.
	/// </summary>
	RefusedNoTarget = 7,

	/// <summary>
	///     Cleanup was refused before any Cheat Engine call because the selected target is no longer the process, or the
	///     process incarnation, that the resource belongs to. The resource may remain in that process.
	/// </summary>
	RefusedTargetChanged = 8,

	/// <summary>
	///     Cleanup was refused before any Cheat Engine call because the identity of the selected target could not be
	///     established. The resource may remain.
	/// </summary>
	RefusedTargetIdentityUnavailable = 9,

	/// <summary>
	///     Cleanup was refused before any Cheat Engine call because the Lua runtime that created the resource is no longer
	///     current (the plugin was re-enabled or Cheat Engine replaced its Lua state). The resource may remain.
	/// </summary>
	RefusedRuntimeChanged = 10,

	/// <summary>
	///     A Cheat Engine release call began but its result was not confirmed. The resource may remain, and the call is
	///     not retried because it may have had an effect.
	/// </summary>
	CleanupUnconfirmed = 11,

	/// <summary>
	///     No release call could begin (for example Cheat Engine could not admit the work now, or dispatch was closed).
	///     Nothing happened, the lease stays active, and a later release, or the activation cleanup, tries again.
	/// </summary>
	CleanupUnavailable = 12
}
