namespace CheatEngine.Client.Scanning;

/// <summary>What a scan requires of one memory protection flag (executable, copy-on-write or writable).</summary>
/// <remarks>
///     The values follow Cheat Engine's protection grammar: <c>+</c> requires the flag, <c>-</c> excludes it and
///     <c>*</c> accepts either. <see cref="Unspecified" /> leaves the flag out of the request, which Cheat Engine treats
///     as "either". Cheat Engine's grammar has no readable flag: every scanned page is readable.
/// </remarks>
public enum ScanProtectionRequirement
{
	/// <summary>The flag is left out of the request; Cheat Engine accepts memory with or without it.</summary>
	Unspecified = 0,

	/// <summary>The flag must be set (<c>+</c>).</summary>
	Required = 1,

	/// <summary>The flag must not be set (<c>-</c>).</summary>
	Excluded = 2,

	/// <summary>The flag is explicitly accepted either way (<c>*</c>).</summary>
	Any = 3
}
