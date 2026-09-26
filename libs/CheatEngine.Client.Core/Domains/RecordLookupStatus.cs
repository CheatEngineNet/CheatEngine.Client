namespace CheatEngine.Client.Core.Domains;

/// <summary>Outcome of one protected Address List record lookup.</summary>
internal enum RecordLookupStatus
{
	Success,
	NotFound,
	AddressListUnavailable,
	InvalidRecord,

	/// <summary>The table holds more records than the caller's materialization limit; nothing was copied.</summary>
	LimitExceeded
}
