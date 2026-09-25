using System.Collections.Immutable;

using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Where the policy gate of a capability comes from.</summary>
internal enum CapabilityPolicySource
{
	/// <summary>The capability needs no activation opt-in: the policy gate is satisfied.</summary>
	NotRequired,

	/// <summary>The policy gate follows the activation's <c>EnableUnsafeLuaExecution</c> opt-in.</summary>
	UnsafeLuaExecutionOptIn,

	/// <summary>The policy gate follows the activation's <c>EnableAutoAssemblerPatches</c> opt-in.</summary>
	AutoAssemblerPatchesOptIn
}

/// <summary>Where the host gate of a capability comes from.</summary>
internal enum CapabilityHostSource
{
	/// <summary>
	///     The runtime snapshot does not probe every host primitive the capability needs: the gate stays unknown.
	/// </summary>
	NotProbed,

	/// <summary>
	///     The host gate is CheatEngine.SDK's observation of the selected-process primitive (<c>Process.Current</c>):
	///     its entry in the SDK runtime snapshot, or the status of the target observation when the SDK produced none.
	/// </summary>
	SdkSelectedProcess
}

/// <summary>One row of <see cref="ClientCapabilityCatalog" />.</summary>
/// <param name="Id">The Client capability.</param>
/// <param name="Policy">Where the policy gate comes from.</param>
/// <param name="Host">Where the host gate comes from.</param>
/// <param name="RequiredScenarios">
///     The live qualification scenarios whose receipts the qualification gate requires; empty when the capability can
///     never be qualified, so its qualification gate stays unknown.
/// </param>
internal sealed record ClientCapabilityDescriptor(
	ClientCapabilityId Id,
	CapabilityPolicySource Policy,
	CapabilityHostSource Host,
	ImmutableArray<string> RequiredScenarios)
{
	/// <summary>
	///     Gets the diagnostic id of the <c>[Experimental]</c> attribute on the capability's public API (for example
	///     <c>CECLIENT5001</c>), or <see langword="null" /> when the API is stable. The READMEs label the capability
	///     "Operational adapter, experimental (id)" (<c>CapabilityDocumentationTests</c>).
	/// </summary>
	internal string? ExperimentalDiagnosticId
	{
		get;
		init;
	}
}

/// <summary>The one description of every Client capability, in the order the runtime snapshot reports them.</summary>
/// <remarks>
///     <para>
///         <see cref="RuntimeClient" /> composes the evidence of each capability from its row: the source of the policy
///         gate and of the host gate, and the experimental id that the implementation gate names. Every capability
///         composes an operational adapter, so its implementation gate is satisfied. The package, lifetime and
///         live-qualification gates are the same for every capability. The required scenarios are the live qualification scenarios that the
///         qualification gate will require; that gate stays unknown until committed Client receipts exist.
///     </para>
///     <para>
///         Each capability has one row, and a lot changes only its own row. The capability tables of the READMEs follow
///         the experimental ids and the required scenarios (<c>CapabilityDocumentationTests</c>), and <c>ClientCapabilityCatalogTests</c>
///         proves that every <see cref="ClientCapabilityId" /> appears exactly once.
///     </para>
/// </remarks>
internal static class ClientCapabilityCatalog
{
	/// <summary>Gets every capability row, in snapshot order.</summary>
	internal static ImmutableArray<ClientCapabilityDescriptor> Entries
	{
		get;
	} =
	[
		Entry(ClientCapabilityId.ProcessSelection,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.SdkSelectedProcess, "Q30.a", "Q31", "Q32"),
		Entry(ClientCapabilityId.TypedMemory,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q20", "Q21", "Q33"),
		Entry(ClientCapabilityId.PatternScanning,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q27", "Q28", "Q29"),
		Entry(ClientCapabilityId.ValueScanning,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q25", "Q26") with
		{
			ExperimentalDiagnosticId = "CECLIENT5001"
		},
		Entry(ClientCapabilityId.Inspection,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q16.b", "Q28"),
		Entry(ClientCapabilityId.Tables,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q34"),
		Entry(ClientCapabilityId.ProtectedLua,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q05", "Q16", "Q19"),
		// Arbitrary Lua is never qualified: no scenario, so its qualification gate stays unknown.
		Entry(ClientCapabilityId.UnsafeLuaExecution,
			CapabilityPolicySource.UnsafeLuaExecutionOptIn, CapabilityHostSource.NotProbed),
		Entry(ClientCapabilityId.Allocations,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q30.a") with
		{
			ExperimentalDiagnosticId = "CECLIENT5002"
		},
		// Experimental (CECLIENT5003); Q32 covers the instruction profile of the x64 and the x86 target.
		Entry(ClientCapabilityId.Assembly,
			CapabilityPolicySource.NotRequired, CapabilityHostSource.NotProbed, "Q32") with
		{
			ExperimentalDiagnosticId = "CECLIENT5003"
		},
		// Experimental (CECLIENT5004) and registered only by the EnableAutoAssemblerPatches opt-in; Q44 is the policy
		// refusal without it.
		Entry(ClientCapabilityId.AutoAssemblerPatches,
			CapabilityPolicySource.AutoAssemblerPatchesOptIn, CapabilityHostSource.NotProbed, "Q35", "Q44") with
		{
			ExperimentalDiagnosticId = "CECLIENT5004"
		}
	];

	private static ClientCapabilityDescriptor Entry(ClientCapabilityId id, CapabilityPolicySource policy,
		CapabilityHostSource host, params string[] requiredScenarios)
	{
		return new ClientCapabilityDescriptor(id, policy, host, [.. requiredScenarios]);
	}
}
