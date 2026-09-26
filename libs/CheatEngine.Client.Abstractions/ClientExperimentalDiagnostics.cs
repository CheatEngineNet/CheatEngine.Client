namespace CheatEngine.Client;

/// <summary>The diagnostic identifiers of the Client's experimental APIs and the address of their documentation.</summary>
/// <remarks>
///     <para>
///         An experimental API is marked <c>[Experimental(id, UrlFormat = UrlFormat)]</c>: using it raises the
///         diagnostic <c>id</c>, which the consumer suppresses to opt in. Its entries in <c>PublicAPI.Unshipped.txt</c>
///         carry the <c>[id]</c> prefix, and the Abstractions README has an anchor named after the id that states the scope
///         and the exit criteria (<c>ClientExperimentalDiagnosticsTests</c> keeps the three in agreement).
///     </para>
///     <para>
///         Every id is declared here once. The dependency-injection package compiles this file as a link for its opt-in,
///         and Core and dependency injection suppress every id in their project file, never file by file.
///     </para>
///     <para>
///         An id is removed, with its attributes and prefixes, only when every live scenario of its capability passes on
///         the exact qualified tuple.
///     </para>
/// </remarks>
internal static class ClientExperimentalDiagnostics
{
	/// <summary>The documentation address of every experimental API; <c>{0}</c> is the diagnostic id.</summary>
	internal const string UrlFormat =
		"https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}";

	/// <summary>Value scans over Cheat Engine scan sessions (<c>IValueScanner</c> and its types).</summary>
	internal const string ValueScans = "CECLIENT5001";

	/// <summary>Target allocations over CheatEngine.SDK's allocator (<c>IAllocationClient</c> and its types).</summary>
	internal const string Allocations = "CECLIENT5002";

	/// <summary>
	///     Single-instruction assembly, disassembly and length operations (<c>ICheatEngineClient.Assembly</c>,
	///     <c>IAssemblyClient</c> and its types).
	/// </summary>
	internal const string Instructions = "CECLIENT5003";

	/// <summary>
	///     Auto Assembler patches (<c>IAutoAssemblerClient</c>, its types and the dependency-injection opt-in
	///     <c>EnableAutoAssemblerPatches</c>).
	/// </summary>
	internal const string AutoAssemblerPatches = "CECLIENT5004";
}
