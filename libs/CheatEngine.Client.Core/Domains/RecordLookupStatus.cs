namespace CheatEngine.Client.Core.Domains;

/// <summary>Outcome of one protected Address List record lookup.</summary>
internal enum RecordLookupStatus
{
	Success,
	NotFound,
	AddressListUnavailable,
	InvalidRecord
}
