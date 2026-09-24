namespace CheatEngine.Client.Results;

/// <summary>Classifies a failure observed while executing a Cheat Engine client operation.</summary>
public enum CheatEngineFailureKind
{
	/// <summary>The operation could not be classified more precisely.</summary>
	Unknown = 0,

	/// <summary>
	///     The caller's cancellation was observed. <see cref="CheatEngineFailure.HostEffect" /> tells whether Cheat Engine
	///     work had started; a token never interrupts a Cheat Engine call that has already begun and never removes an
	///     effect that such a call produced.
	/// </summary>
	Cancelled = 1,

	/// <summary>The required Cheat Engine capability or Lua global is unavailable.</summary>
	CapabilityUnavailable = 2,

	/// <summary>Cheat Engine rejected the requested operation.</summary>
	OperationRejected = 3,

	/// <summary>The requested resource was not found.</summary>
	NotFound = 4,

	/// <summary>The operation expected one result but observed several.</summary>
	AmbiguousMatch = 5,

	/// <summary>The host result exceeded the caller's materialization limit.</summary>
	ResultLimitExceeded = 6,

	/// <summary>A protected Lua call failed.</summary>
	LuaError = 7,

	/// <summary>The SDK binding could not uphold its documented contract.</summary>
	BindingError = 8,

	/// <summary>The host returned a value outside its documented result shape.</summary>
	InvalidHostResult = 9,

	/// <summary>The requested feature is intentionally not supported by this client version.</summary>
	Unsupported = 10,

	/// <summary>No target process is currently attached.</summary>
	TargetNotAttached = 11,

	/// <summary>A target-memory read failed.</summary>
	MemoryReadFailed = 12,

	/// <summary>A target-memory write failed.</summary>
	MemoryWriteFailed = 13,

	/// <summary>The client resource belongs to an expired activation epoch.</summary>
	ActivationExpired = 14,

	/// <summary>The operation is not valid in the resource's current lifecycle or session state.</summary>
	InvalidState = 15,

	/// <summary>
	///     Cheat Engine returned a result that the consumed CheatEngine.SDK version cannot attribute to one cause: several
	///     documented causes (for example no match and a host failure) are indistinguishable. Inspect the operation's
	///     documentation; never treat it as absence.
	/// </summary>
	/// <remarks>
	///     This is distinct from <see cref="AmbiguousMatch" /> (several matches were observed), from
	///     <see cref="InvalidHostResult" /> (a result outside the documented shape was observed), and from
	///     <see cref="NotFound" /> (absence was established).
	/// </remarks>
	IndeterminateHostResult = 16,

	/// <summary>
	///     The target process that the operation or resource was bound to is no longer the target Cheat Engine has
	///     selected: the selection moved to another process, or the same process identifier now names another process
	///     incarnation. Nothing is retried against the new target.
	/// </summary>
	/// <remarks>
	///     This is distinct from <see cref="TargetNotAttached" /> (no target is selected at all) and from
	///     <see cref="TargetIdentityUnavailable" /> (the identity of the current target could not be established).
	/// </remarks>
	TargetChanged = 17,

	/// <summary>
	///     The operation had to verify the identity of Cheat Engine's current target and could not establish it, so it
	///     was refused instead of running against a target it cannot vouch for.
	/// </summary>
	/// <remarks>
	///     The target may be unchanged: this kind reports missing evidence, never a proven change (which is
	///     <see cref="TargetChanged" />).
	/// </remarks>
	TargetIdentityUnavailable = 18,

	/// <summary>
	///     The Cheat Engine Lua runtime that the operation or resource depends on is no longer current: Cheat Engine
	///     replaced its Lua state outside the plugin's control, or the resource belongs to an earlier Lua attachment.
	///     Resources created before the change are refused; disabling and re-enabling the plugin recovers.
	/// </summary>
	/// <remarks>
	///     This is distinct from <see cref="ActivationExpired" />, which reports that the Client activation itself ended.
	/// </remarks>
	RuntimeChanged = 19
}
