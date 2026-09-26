using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     A copied CheatEngine.SDK <c>AobScanOutcome</c> and the <c>AobScanTargetContext</c> of the same global
///     <c>AOBScan</c> call.
/// </summary>
/// <remarks>
///     The SDK context type has an internal constructor, so the port copies its two target observations into
///     <see cref="TargetSelectionFacts" /> and <see cref="PatternScanner" /> is testable without a host. The observations
///     are facts: they never change <see cref="Kind" />.
/// </remarks>
/// <param name="Kind">The factual SDK outcome category.</param>
/// <param name="LuaStatus">The protected Lua status of a <see cref="AobScanOutcomeKind.ProtectedLuaFailure" />.</param>
/// <param name="ResultCount">The verified host-list count of <see cref="AobScanOutcomeKind.Matches" />.</param>
/// <param name="TargetBefore">The target selection observed immediately before the <c>AOBScan</c> call.</param>
/// <param name="TargetAfter">The target selection observed immediately after the <c>AOBScan</c> call returned.</param>
internal readonly record struct AobHostOutcome(
	AobScanOutcomeKind Kind,
	LuaStatus LuaStatus,
	int ResultCount,
	TargetSelectionFacts TargetBefore,
	TargetSelectionFacts TargetAfter)
{
	/// <summary>
	///     Gets whether both observations are qualified and denote the same process incarnation, the SDK's
	///     <c>AobScanTargetContext.IsSameQualifiedIncarnation</c>.
	/// </summary>
	internal bool IsSameQualifiedIncarnation =>
		TargetBefore.IsQualified && TargetAfter.IsQualified && TargetBefore.Incarnation == TargetAfter.Incarnation;
}
