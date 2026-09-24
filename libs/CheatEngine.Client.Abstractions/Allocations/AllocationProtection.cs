using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Allocations;

/// <summary>The page protection of a target allocation, fixed when Cheat Engine allocates it.</summary>
/// <remarks>
///     <para>
///         <b>Experimental (<c>CECLIENT5002</c>).</b> The allocation API can change in a minor release until its live
///         scenarios pass; see the Abstractions README.
///     </para>
///     <para>
///         The Client always passes the protection to Cheat Engine's <c>allocateMemory</c>, so an allocation never
///         depends on Cheat Engine's default protection. The protection cannot be changed after the allocation.
///     </para>
/// </remarks>
[Experimental(ClientExperimentalDiagnostics.Allocations, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public enum AllocationProtection
{
	/// <summary>
	///     Readable and writable, not executable (<c>PAGE_READWRITE</c>): memory for data exchanged with the target. The
	///     default of <see cref="AllocationRequest" />.
	/// </summary>
	ReadWrite = 0,

	/// <summary>
	///     Readable, writable and executable (<c>PAGE_EXECUTE_READWRITE</c>): memory for code. It needs no opt-in other than
	///     the experimental diagnostic of the allocation API.
	/// </summary>
	ExecuteReadWrite = 1
}
