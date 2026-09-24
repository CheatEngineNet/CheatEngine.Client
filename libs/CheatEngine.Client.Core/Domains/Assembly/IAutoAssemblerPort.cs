using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Assembly;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains.Assembly;

/// <summary>The facts Core copies from one CheatEngine.SDK Auto Assembler activation outcome.</summary>
/// <param name="Kind">The SDK category of the attempt.</param>
/// <param name="Effect">The SDK effect state, derived by the SDK from <paramref name="Kind" /> only.</param>
/// <param name="LuaStatus">The protected Lua status of a <c>ProtectedLuaFailure</c>; otherwise <c>Ok</c>.</param>
/// <param name="HostText">Cheat Engine's bounded rejection detail, when captured.</param>
/// <param name="HostTextTruncated">Whether <paramref name="HostText" /> was cut at the bound.</param>
/// <param name="HostWarnings">Cheat Engine's bounded compilation warnings, when captured.</param>
/// <param name="HostWarningsTruncated">Whether <paramref name="HostWarnings" /> was cut at the bound.</param>
/// <param name="Compensation">The status of the SDK's one compensating disable, for <c>HandoffFailed</c> only.</param>
/// <remarks>
///     The SDK's disable-info snapshot is deliberately not copied: the Client does not project it (plan A8), and the
///     rooted table the patch owns stays the only disable authority.
/// </remarks>
internal readonly record struct AutoAssemblerApplyFacts(
	AutoAssemblerApplyOutcomeKind Kind,
	EngineEffectState Effect,
	LuaStatus LuaStatus,
	string? HostText,
	bool HostTextTruncated,
	string? HostWarnings,
	bool HostWarningsTruncated,
	TargetReleaseStatus? Compensation);

/// <summary>The facts Core copies from one CheatEngine.SDK Auto Assembler syntax check.</summary>
/// <param name="Kind">The SDK category of the check.</param>
/// <param name="LuaStatus">The protected Lua status of a <c>ProtectedLuaFailure</c>; otherwise <c>Ok</c>.</param>
/// <param name="HostText">Cheat Engine's bounded error text for a rejected section, when captured.</param>
/// <param name="HostTextTruncated">Whether <paramref name="HostText" /> was cut at the bound.</param>
internal readonly record struct AutoAssemblerCheckFacts(
	AutoAssemblerCheckOutcomeKind Kind,
	LuaStatus LuaStatus,
	string? HostText,
	bool HostTextTruncated);

/// <summary>The sole owner of one applied patch's disable information, behind the Client lease.</summary>
/// <remarks>
///     The production owner is CheatEngine.SDK's <c>AutoAssemblerPatch</c>. Its first release attempt consumes the
///     disable information before any Lua work, whatever the result; a consumed owner is never released again.
/// </remarks>
internal interface IAutoAssemblerPatchOwner
{
	/// <summary>Gets whether the owner still holds disable information that is current in the attached Lua state.</summary>
	public bool IsEnabled
	{
		get;
	}

	/// <summary>Gets whether a release attempt already consumed the disable information.</summary>
	public bool IsConsumed
	{
		get;
	}

	/// <summary>Gets whether the patch may remain after a release attempt that did not confirm the disable.</summary>
	public bool RequiresManualRecovery
	{
		get;
	}

	/// <summary>Gets the status of the release attempt that consumed the owner, or <c>Unspecified</c> before it.</summary>
	public TargetReleaseStatus LastReleaseStatus
	{
		get;
	}

	/// <summary>
	///     Makes the one target-validated disable attempt on Cheat Engine's main thread; call it only while
	///     <see cref="IsConsumed" /> is <see langword="false" />.
	/// </summary>
	/// <returns>The factual status of the attempt; never retried.</returns>
	public TargetReleaseStatus Release();
}

/// <summary>Internal port for the two Cheat Engine calls of the Auto Assembler domain.</summary>
/// <remarks>
///     Both methods run on Cheat Engine's main thread inside one dispatched callback. Each asks CheatEngine.SDK for Lua
///     admission first and reports a refusal as a classified failure (<c>LuaAdmission</c>) without calling Cheat Engine.
///     Neither passes Cheat Engine's <c>targetself</c> argument, opens or selects a process.
/// </remarks>
internal interface IAutoAssemblerPort
{
	/// <summary>Applies a script once with <c>autoAssemble</c> and publishes the owner of the applied patch.</summary>
	/// <param name="operation">The public Client operation name, for an admission refusal.</param>
	/// <param name="script">The complete script.</param>
	/// <param name="options">The bounded host-text and snapshot options.</param>
	/// <param name="facts">The copied outcome when admitted.</param>
	/// <param name="patch">The owner of the applied patch when the SDK published one; otherwise <see langword="null" />.</param>
	/// <param name="admissionFailure">The classified admission refusal when not admitted.</param>
	/// <returns><see langword="true" /> when the SDK admitted the Lua operation and reported an outcome.</returns>
	public bool TryApply(string operation, string script, AutoAssemblerOptions options,
		out AutoAssemblerApplyFacts facts, out IAutoAssemblerPatchOwner? patch, out CheatEngineFailure admissionFailure);

	/// <summary>Checks the <c>[ENABLE]</c> section of a script with <c>autoAssembleCheck</c>.</summary>
	/// <param name="operation">The public Client operation name, for an admission refusal.</param>
	/// <param name="script">The script to check.</param>
	/// <param name="options">The bounded host-text options.</param>
	/// <param name="facts">The copied outcome when admitted.</param>
	/// <param name="admissionFailure">The classified admission refusal when not admitted.</param>
	/// <returns><see langword="true" /> when the SDK admitted the Lua operation and reported an outcome.</returns>
	public bool TryCheck(string operation, string script, AutoAssemblerOptions options,
		out AutoAssemblerCheckFacts facts, out CheatEngineFailure admissionFailure);
}
