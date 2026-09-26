using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Tests.Lua;

/// <summary>The Lua module lease maps every release kind a module can report, and an unknown kind fails closed.</summary>
public sealed class LuaModuleReleaseMappingTests
{
	private static readonly Dictionary<LeaseReleaseKind, LeaseReleaseOutcome> Table = new()
	{
		[LeaseReleaseKind.Unknown] = Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown),
		[LeaseReleaseKind.Released] = Outcome(LeaseReleaseKind.Released, CheatEngineHostEffect.Completed),
		[LeaseReleaseKind.AlreadyReleased] = Outcome(LeaseReleaseKind.AlreadyReleased, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.PartiallyReleased] =
			Outcome(LeaseReleaseKind.PartiallyReleased, CheatEngineHostEffect.Started),
		[LeaseReleaseKind.Replaced] = Outcome(LeaseReleaseKind.Replaced, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.Superseded] = Outcome(LeaseReleaseKind.Superseded, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.ExternallyRemoved] =
			Outcome(LeaseReleaseKind.ExternallyRemoved, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.RefusedTargetNotAttached] =
			Outcome(LeaseReleaseKind.RefusedTargetNotAttached, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.RefusedTargetChanged] =
			Outcome(LeaseReleaseKind.RefusedTargetChanged, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.RefusedTargetIdentityUnavailable] =
			Outcome(LeaseReleaseKind.RefusedTargetIdentityUnavailable, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.RefusedRuntimeChanged] =
			Outcome(LeaseReleaseKind.RefusedRuntimeChanged, CheatEngineHostEffect.NotStarted),
		[LeaseReleaseKind.CleanupUnconfirmed] =
			Outcome(LeaseReleaseKind.CleanupUnconfirmed, CheatEngineHostEffect.Started),
		[LeaseReleaseKind.CleanupUnavailable] =
			Outcome(LeaseReleaseKind.CleanupUnavailable, CheatEngineHostEffect.NotStarted)
	};

	[Fact]
	public void EveryModuleReleaseKindIsMappedAndAnUnknownKindFailsClosed()
	{
		MappingTotality.AssertTotal<LeaseReleaseKind>(
			static kind => Table.TryGetValue(kind, out LeaseReleaseOutcome expected) &&
						   LuaModuleReleaseMapping.ToLeaseOutcome(kind) == expected,
			static kind => LuaModuleReleaseMapping.ToLeaseOutcome(kind) ==
						   Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown));
	}

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		return new LeaseReleaseOutcome(kind, hostEffect);
	}
}
