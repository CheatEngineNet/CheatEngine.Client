using System.Collections.Immutable;

using CheatEngine.Client.Lua;

namespace CheatEngine.Client.Abstractions.Tests.Lua;

/// <summary>
///     C1 contract of the handle-free Lua module release outcome that generated modules publish (F12, Q16): the kind is
///     computed from the export statuses and the shape of a release that cannot happen is refused.
/// </summary>
[Trait("Qualification", "Q16")]
public sealed class LuaModuleReleaseOutcomeTests
{
	[Fact]
	public void KindIsReleasedWhenEveryExportWasRemovedReplacedOrAbsent()
	{
		LuaModuleReleaseOutcome outcome = Create(
			("status", LuaExportReleaseStatus.Removed),
			("ping", LuaExportReleaseStatus.Replaced),
			("marker", LuaExportReleaseStatus.Absent));

		Assert.Equal("plugin", outcome.ModuleName);
		Assert.Equal(LuaModuleReleaseKind.Released, outcome.Kind);
		Assert.True(outcome.IsComplete);
	}

	[Fact]
	public void AnyFailedExportMakesTheReleasePartial()
	{
		LuaModuleReleaseOutcome outcome = Create(
			("status", LuaExportReleaseStatus.Removed),
			("ping", LuaExportReleaseStatus.Failed),
			("marker", LuaExportReleaseStatus.Replaced));

		Assert.Equal(LuaModuleReleaseKind.PartiallyReleased, outcome.Kind);
		Assert.False(outcome.IsComplete);
	}

	[Fact]
	public void AllNotAttemptedExportsMakeTheReleaseStale()
	{
		LuaModuleReleaseOutcome outcome = Create(
			("status", LuaExportReleaseStatus.NotAttempted),
			("ping", LuaExportReleaseStatus.NotAttempted));

		Assert.Equal(LuaModuleReleaseKind.Stale, outcome.Kind);
		Assert.True(outcome.IsComplete);
		Assert.Equal(0, outcome.RemovedCount + outcome.ReplacedCount + outcome.AbsentCount + outcome.FailedCount);
	}

	[Fact]
	public void NotAttemptedMixedWithAttemptedExportsIsRejected()
	{
		Assert.Throws<ArgumentException>(() => Create(
			("status", LuaExportReleaseStatus.NotAttempted),
			("ping", LuaExportReleaseStatus.Removed)));
		Assert.Throws<ArgumentException>(() => Create(
			("status", LuaExportReleaseStatus.NotAttempted),
			("ping", LuaExportReleaseStatus.Failed)));
	}

	[Fact]
	public void CountsMatchTheExportStatuses()
	{
		LuaModuleReleaseOutcome outcome = Create(
			("a", LuaExportReleaseStatus.Removed),
			("b", LuaExportReleaseStatus.Removed),
			("c", LuaExportReleaseStatus.Replaced),
			("d", LuaExportReleaseStatus.Absent),
			("e", LuaExportReleaseStatus.Absent),
			("f", LuaExportReleaseStatus.Absent),
			("g", LuaExportReleaseStatus.Failed));

		Assert.Equal(2, outcome.RemovedCount);
		Assert.Equal(1, outcome.ReplacedCount);
		Assert.Equal(3, outcome.AbsentCount);
		Assert.Equal(1, outcome.FailedCount);
		Assert.Equal(7, outcome.Exports.Length);
	}

	[Fact]
	public void UnknownStatusIsRejected()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new LuaExportReleaseOutcome("status", LuaExportReleaseStatus.Unknown));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new LuaExportReleaseOutcome("status", (LuaExportReleaseStatus) 6));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			new LuaExportReleaseOutcome("status", (LuaExportReleaseStatus) (-1)));
		Assert.Throws<ArgumentException>(() => new LuaModuleReleaseOutcome("plugin",
			[new LuaExportReleaseOutcome("status", LuaExportReleaseStatus.Removed), default]));
	}

	[Fact]
	public void BlankOrDuplicateExportNamesAreRejected()
	{
		Assert.Throws<ArgumentException>(() => new LuaExportReleaseOutcome(" ", LuaExportReleaseStatus.Removed));
		Assert.Throws<ArgumentNullException>(() => new LuaExportReleaseOutcome(null!, LuaExportReleaseStatus.Removed));
		Assert.Throws<ArgumentException>(() => Create(
			("status", LuaExportReleaseStatus.Removed),
			("status", LuaExportReleaseStatus.Replaced)));
		Assert.Throws<ArgumentException>(() => new LuaModuleReleaseOutcome(" ",
			[new LuaExportReleaseOutcome("status", LuaExportReleaseStatus.Removed)]));
	}

	[Fact]
	public void DefaultOrEmptyExportsAreRejected()
	{
		Assert.Throws<ArgumentException>(() => new LuaModuleReleaseOutcome("plugin", default));
		Assert.Throws<ArgumentException>(() =>
			new LuaModuleReleaseOutcome("plugin", ImmutableArray<LuaExportReleaseOutcome>.Empty));
	}

	[Fact]
	public void ExportsAreCopiedAndImmutable()
	{
		ImmutableArray<LuaExportReleaseOutcome>.Builder builder =
			ImmutableArray.CreateBuilder<LuaExportReleaseOutcome>();
		builder.Add(new LuaExportReleaseOutcome("status", LuaExportReleaseStatus.Removed));
		builder.Add(new LuaExportReleaseOutcome("ping", LuaExportReleaseStatus.Replaced));
		LuaModuleReleaseOutcome outcome = new("plugin", builder.ToImmutable());

		builder[1] = new LuaExportReleaseOutcome("ping", LuaExportReleaseStatus.Failed);
		builder.Add(new LuaExportReleaseOutcome("marker", LuaExportReleaseStatus.Absent));

		Assert.Equal(
			[
				new LuaExportReleaseOutcome("status", LuaExportReleaseStatus.Removed),
				new LuaExportReleaseOutcome("ping", LuaExportReleaseStatus.Replaced)
			],
			outcome.Exports);
		Assert.Equal(LuaModuleReleaseKind.Released, outcome.Kind);
		Assert.Equal(0, outcome.FailedCount);
	}

	[Fact]
	public void KindNumbersMatchTheSdkRegistrationReleaseKinds()
	{
		// CheatEngine.SDK 2.0 LuaRegistrationReleaseKind: Released = 1, PartiallyReleased = 2, Stale = 3. The Client stays
		// on SDK 1.0.0, so the numbers are frozen here for the one-to-one migration mapping.
		Assert.Equal(1, (int) LuaModuleReleaseKind.Released);
		Assert.Equal(2, (int) LuaModuleReleaseKind.PartiallyReleased);
		Assert.Equal(3, (int) LuaModuleReleaseKind.Stale);
		Assert.Equal(0, (int) LuaModuleReleaseKind.Unknown);
		Assert.Equal(0, (int) LuaExportReleaseStatus.Unknown);
	}

	[Fact]
	public void ToStringListsTheModuleKindAndEveryExportStatusInOrder()
	{
		LuaModuleReleaseOutcome outcome = Create(
			("status", LuaExportReleaseStatus.Removed),
			("ping", LuaExportReleaseStatus.Replaced));

		Assert.Equal("Module=plugin; Kind=Released; status=Removed; ping=Replaced", outcome.ToString());
	}

	private static LuaModuleReleaseOutcome Create(params (string Name, LuaExportReleaseStatus Status)[] exports)
	{
		return new LuaModuleReleaseOutcome("plugin",
			[.. exports.Select(static export => new LuaExportReleaseOutcome(export.Name, export.Status))]);
	}
}
