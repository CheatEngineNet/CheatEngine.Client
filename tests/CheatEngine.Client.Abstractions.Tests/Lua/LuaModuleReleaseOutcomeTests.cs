using System.Collections.Immutable;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Abstractions.Tests.Lua;

/// <summary>
///     C1 contract of the handle-free Lua module release outcome (F12, Q16): the facts CheatEngine.SDK observed, in the
///     Client lease vocabulary, and the shapes of a release that cannot happen are refused.
/// </summary>
[Trait("Qualification", "Q16")]
public sealed class LuaModuleReleaseOutcomeTests
{
	[Fact]
	public void ReleasedCarriesTheCountsTheSdkObserved()
	{
		LuaModuleReleaseOutcome outcome = LuaModuleReleaseOutcome.Released("plugin", 2, 0, 1);

		Assert.Equal("plugin", outcome.ModuleName);
		Assert.Equal(LeaseReleaseKind.Released, outcome.Kind);
		Assert.Equal(2, outcome.RemovedCount);
		Assert.Equal(0, outcome.RestoredCount);
		Assert.Equal(1, outcome.ReplacementCount);
		Assert.Equal(0, outcome.RemainingCount);
		Assert.Empty(outcome.FailedExports);
		Assert.True(outcome.IsComplete);
	}

	[Fact]
	public void PartiallyReleasedNamesItsFailedExportsAsRemaining()
	{
		LuaModuleReleaseOutcome outcome =
			LuaModuleReleaseOutcome.PartiallyReleased("plugin", 1, 0, 0, ["status", "ping"]);

		Assert.Equal(LeaseReleaseKind.PartiallyReleased, outcome.Kind);
		Assert.Equal(["status", "ping"], outcome.FailedExports);
		Assert.Equal(2, outcome.RemainingCount);
		Assert.False(outcome.IsComplete);
	}

	[Fact]
	public void TheRemainingFactoriesReportWhatTheirKindMeans()
	{
		LuaModuleReleaseOutcome alreadyReleased = LuaModuleReleaseOutcome.AlreadyReleased("plugin");
		LuaModuleReleaseOutcome stale = LuaModuleReleaseOutcome.RefusedRuntimeChanged("plugin", 3);
		LuaModuleReleaseOutcome unavailable = LuaModuleReleaseOutcome.CleanupUnavailable("plugin", 3);

		Assert.Equal(LeaseReleaseKind.AlreadyReleased, alreadyReleased.Kind);
		Assert.True(alreadyReleased.IsComplete);
		Assert.Equal(LeaseReleaseKind.RefusedRuntimeChanged, stale.Kind);
		Assert.Equal(3, stale.RemainingCount);
		Assert.False(stale.IsComplete);
		Assert.Equal(LeaseReleaseKind.CleanupUnavailable, unavailable.Kind);
		Assert.Equal(3, unavailable.RemainingCount);
		Assert.False(unavailable.IsComplete);
	}

	[Theory]
	[InlineData(LeaseReleaseKind.Unknown, false)]
	[InlineData(LeaseReleaseKind.Released, true)]
	[InlineData(LeaseReleaseKind.AlreadyReleased, true)]
	[InlineData(LeaseReleaseKind.Replaced, true)]
	[InlineData(LeaseReleaseKind.Superseded, true)]
	[InlineData(LeaseReleaseKind.ExternallyRemoved, true)]
	[InlineData(LeaseReleaseKind.RefusedNoTarget, false)]
	[InlineData(LeaseReleaseKind.RefusedTargetChanged, false)]
	[InlineData(LeaseReleaseKind.RefusedTargetIdentityUnavailable, false)]
	[InlineData(LeaseReleaseKind.RefusedRuntimeChanged, false)]
	[InlineData(LeaseReleaseKind.CleanupUnconfirmed, false)]
	[InlineData(LeaseReleaseKind.CleanupUnavailable, false)]
	public void IsCompleteOnlyForAKindThatLeavesNothingBehind(LeaseReleaseKind kind, bool expected)
	{
		LuaModuleReleaseOutcome outcome = LuaModuleReleaseOutcome.Create("plugin", kind, 0, 0, 0, 1, default);

		Assert.Equal(expected, outcome.IsComplete);
		Assert.Empty(outcome.FailedExports);
	}

	[Fact]
	public void FailedExportsAreRequiredExactlyForAPartialRelease()
	{
		Assert.Throws<ArgumentException>(() =>
			LuaModuleReleaseOutcome.Create("plugin", LeaseReleaseKind.PartiallyReleased, 0, 0, 0, 0, []));
		Assert.Throws<ArgumentException>(() =>
			LuaModuleReleaseOutcome.Create("plugin", LeaseReleaseKind.Released, 0, 0, 0, 1, ["status"]));
	}

	[Fact]
	public void BlankOrDuplicateFailedExportsAreRejected()
	{
		Assert.Throws<ArgumentException>(() =>
			LuaModuleReleaseOutcome.PartiallyReleased("plugin", 0, 0, 0, [" "]));
		Assert.Throws<ArgumentException>(() =>
			LuaModuleReleaseOutcome.PartiallyReleased("plugin", 0, 0, 0, ["status", "status"]));
	}

	[Fact]
	public void BlankModuleNamesUndefinedKindsNegativeCountsAndTooFewRemainingAreRejected()
	{
		Assert.Throws<ArgumentException>(() => LuaModuleReleaseOutcome.AlreadyReleased(" "));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			LuaModuleReleaseOutcome.Create("plugin", (LeaseReleaseKind) 99, 0, 0, 0, 0, []));
		Assert.Throws<ArgumentOutOfRangeException>(() => LuaModuleReleaseOutcome.Released("plugin", -1, 0, 0));
		Assert.Throws<ArgumentOutOfRangeException>(() => LuaModuleReleaseOutcome.RefusedRuntimeChanged("plugin", -1));
		Assert.Throws<ArgumentException>(() =>
			LuaModuleReleaseOutcome.Create("plugin", LeaseReleaseKind.PartiallyReleased, 0, 0, 0, 1, ["status", "ping"]));
	}

	[Fact]
	public void FailedExportsAreCopiedAndImmutable()
	{
		ImmutableArray<string>.Builder builder = ImmutableArray.CreateBuilder<string>();
		builder.Add("status");
		LuaModuleReleaseOutcome outcome = LuaModuleReleaseOutcome.PartiallyReleased("plugin", 0, 0, 0,
			builder.ToImmutable());

		builder.Add("ping");

		Assert.Equal(["status"], outcome.FailedExports);
	}

	[Fact]
	public void ToStringListsTheModuleKindCountsAndFailedExports()
	{
		LuaModuleReleaseOutcome outcome =
			LuaModuleReleaseOutcome.PartiallyReleased("plugin", 1, 0, 2, ["status", "ping"]);

		Assert.Equal(
			"Module=plugin; Kind=PartiallyReleased; Removed=1; Restored=0; Replacement=2; Remaining=2; Failed=status,ping",
			outcome.ToString());
	}
}
