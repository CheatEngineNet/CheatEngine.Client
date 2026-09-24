using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Maps the release a Lua module reported (<see cref="LuaModuleReleaseOutcome" />) to the outcome of its Client lease.
/// </summary>
/// <remarks>
///     The kind is kept; the host effect says how far the release got, with the rule of <c>SdkReleaseOutcomes</c>:
///     <see cref="CheatEngineHostEffect.Completed" /> for a confirmed release, <see cref="CheatEngineHostEffect.Started" />
///     for a release call that began without a confirmed result, <see cref="CheatEngineHostEffect.NotStarted" /> when no
///     release write was made, and <see cref="CheatEngineHostEffect.Unknown" /> when nothing is known. The mapping is total
///     over <see cref="LeaseReleaseKind" /> and fails closed.
/// </remarks>
internal static class LuaModuleReleaseMapping
{
	/// <summary>Maps a module release kind to the lease outcome.</summary>
	/// <param name="kind">The kind the module reported.</param>
	/// <returns>The lease outcome; <see cref="LeaseReleaseKind.Unknown" /> with an unknown effect for an unknown kind.</returns>
	internal static LeaseReleaseOutcome ToLeaseOutcome(LeaseReleaseKind kind)
	{
		return kind switch
		{
			LeaseReleaseKind.Unknown => Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown),
			LeaseReleaseKind.Released => Outcome(kind, CheatEngineHostEffect.Completed),
			LeaseReleaseKind.PartiallyReleased or LeaseReleaseKind.CleanupUnconfirmed =>
				Outcome(kind, CheatEngineHostEffect.Started),
			LeaseReleaseKind.AlreadyReleased or LeaseReleaseKind.Replaced or LeaseReleaseKind.Superseded
				or LeaseReleaseKind.ExternallyRemoved or LeaseReleaseKind.RefusedNoTarget
				or LeaseReleaseKind.RefusedTargetChanged or LeaseReleaseKind.RefusedTargetIdentityUnavailable
				or LeaseReleaseKind.RefusedRuntimeChanged or LeaseReleaseKind.CleanupUnavailable =>
				Outcome(kind, CheatEngineHostEffect.NotStarted),
			_ => Outcome(LeaseReleaseKind.Unknown, CheatEngineHostEffect.Unknown)
		};
	}

	private static LeaseReleaseOutcome Outcome(LeaseReleaseKind kind, CheatEngineHostEffect hostEffect)
	{
		return new LeaseReleaseOutcome(kind, hostEffect);
	}
}
