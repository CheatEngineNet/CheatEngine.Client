namespace CheatEngine.Client.Core.Domains;

/// <summary>Classifies the SDK AOB result-list boundary without exposing its ownership handle.</summary>
/// <remarks>Classification uses the SDK return value and result presence only, never Cheat Engine or Lua error text.</remarks>
internal enum AobScanHostStatus
{
	/// <summary>The SDK returned an owned result list.</summary>
	Success,

	/// <summary>
	///     CheatEngine.SDK 1.0.0 <c>AobScanner.TryScan</c> returned <see langword="false" />: zero matches (the common
	///     cause on profile ce-7.7.0.10621-x64-managed-hostfxr, where <c>AOBScan</c> returns no value for zero matches),
	///     an unresolved global, a protected Lua failure, or a non-object result. The causes are indistinguishable with
	///     SDK 1.0.0.
	/// </summary>
	NoResultList,

	/// <summary>A result list was returned but is unusable.</summary>
	InvalidResult
}
