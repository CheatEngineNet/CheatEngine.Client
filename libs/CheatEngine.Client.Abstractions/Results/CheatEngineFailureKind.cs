namespace CheatEngine.Client.Results;

/// <summary>Classifies a failure observed while executing a Cheat Engine client operation.</summary>
public enum CheatEngineFailureKind
{
	/// <summary>The operation could not be classified more precisely.</summary>
	Unknown = 0,

	/// <summary>The operation was cancelled before a Cheat Engine call began.</summary>
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
	InvalidState = 15
}
