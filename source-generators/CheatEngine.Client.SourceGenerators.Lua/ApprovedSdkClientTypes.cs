namespace CheatEngine.Client.SourceGenerators.Lua;

/// <summary>
///     The single allowlist of purely descriptive CheatEngine.SDK value types that may appear in public Client
///     signatures and in generated Client Lua results.
/// </summary>
/// <remarks>
///     <para>
///         Audit ch.11 and Q48: only immutable address values, domain enums, and descriptive snapshots without native
///         behavior may cross the Client boundary; <c>LuaState</c>, <c>CEObject</c>, <c>Owned&lt;T&gt;</c>, function
///         pointers, ABI structures, and registry references never do. A public Client signature that uses one of these
///         types is coupled to the CheatEngine.SDK semantic version, so every entry must resolve in the consumed SDK
///         package (checked by <c>SdkConsumerContractTests</c>).
///     </para>
///     <para>
///         This file is compiled into the Lua generator and linked, unchanged, into <c>CheatEngine.Client.Tests</c> so the
///         generator and the public-signature boundary test can never drift apart.
///     </para>
/// </remarks>
internal static class ApprovedSdkClientTypes
{
	/// <summary>Gets the ordinal-sorted metadata names of the approved SDK value types.</summary>
	internal static readonly string[] Names =
	[
		"CheatEngine.SDK.Engine.AddressList.MemoryRecordId",
		"CheatEngine.SDK.Engine.Enums.VariableType",
		"CheatEngine.SDK.Engine.Inspection.MemoryRegionInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleInfo",
		"CheatEngine.SDK.Engine.Inspection.ModuleName",
		"CheatEngine.SDK.Engine.Inspection.ModuleSectionInfo",
		"CheatEngine.SDK.Engine.Inspection.SymbolExpression",
		"CheatEngine.SDK.Engine.Inspection.SymbolInfo",
		"CheatEngine.SDK.Engine.Inspection.TargetProcessId",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineArchitecture",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineOperatingSystem",
		"CheatEngine.SDK.Engine.Runtime.CheatEngineVersion",
		"CheatEngine.SDK.Engine.Runtime.PointerSize",
		"CheatEngine.SDK.Engine.Runtime.TargetAbi",
		"CheatEngine.SDK.Engine.Runtime.TargetBackend",
		"CheatEngine.SDK.Engine.Values.Address"
	];
}
