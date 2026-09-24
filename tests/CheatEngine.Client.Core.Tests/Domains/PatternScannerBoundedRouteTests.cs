using System.Globalization;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     A module or range request on a qualified target runs the SDK's bounded, exhaustive route (audit F07, SDK2-05):
///     its bounds, its destination, its factual zero, its truncation, its fallback and its cancellation points.
/// </summary>
public sealed class PatternScannerBoundedRouteTests
{
	private const ulong ModuleBase = 0x4000;
	private const ulong ModuleSize = 0x100;

	public static TheoryData<string> Fallbacks => new()
	{
		"UnqualifiedTarget",
		"FileAsProcess",
		"SessionCreationFailed",
		"TargetIdentityUnavailable"
	};

	[Fact]
	[Trait("Qualification", "Q28")]
	public void AModuleRequestOnAQualifiedTargetScansOnlyTheModule()
	{
		FakeAobScanPort port = QualifiedPort([0x4010, 0x4020]);
		PatternScanner scanner = CreateScanner(port);

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), null, 10),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.Equal([0x4010, 0x4020], outcome.Result!.Value.Matches);
		Assert.False(outcome.Result.Value.IsTruncated);
		Assert.Equal(new Address(ModuleBase), port.LastBounds!.Value.Start);
		Assert.Equal(new Address(ModuleBase + ModuleSize), port.LastBounds.Value.Stop);
		Assert.Equal(1, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(PatternScanScope.HostBoundedRange, metrics.Scope);
		Assert.Equal(2UL, metrics.HostResultCount);
		Assert.Equal(2, metrics.MaterializedCount);
	}

	/// <summary>The bounded route reports a factual zero: the empty success the global route can never give.</summary>
	[Fact]
	[Trait("Qualification", "Q28")]
	public void ABoundedNoMatchWithReadableErrorTextIsAFactualEmptySuccess()
	{
		PatternScanner scanner = CreateScanner(QualifiedPort([]));

		bool succeeded = scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded, failure.Message);
		Assert.Empty(result.Matches);
		Assert.False(result.IsTruncated);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void ABoundedNoMatchWithUnreadableErrorTextIsIndeterminate()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.NoMatches) with
		{
			IsHostErrorTextUnreadable = true
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([], bounded));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), null, 1),
			TestContext.Current.CancellationToken);

		Assert.False(outcome.IsSuccess);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, outcome.Failure!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, outcome.Failure.Value.HostEffect);
		Assert.Equal(PatternScanScope.HostBoundedRange, Assert.NotNull(outcome.Metrics).Scope);
	}

	[Theory]
	[InlineData(false, "Cheat Engine reported an error for the bounded AOB scan: scan failed")]
	[InlineData(true, "Cheat Engine reported an error for the bounded AOB scan: scan failed (truncated)")]
	public void AHostReportedErrorIsARejectionCarryingTheBoundedText(bool truncated, string expectedMessage)
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.HostReportedError) with
		{
			HostErrorText = "scan failed",
			IsHostErrorTextTruncated = truncated
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(expectedMessage, failure.Message);
	}

	/// <summary>The inclusive range end becomes the exclusive stop: the last allowed match must fit below it.</summary>
	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(0x1000UL, 0x1FFFUL, "90 90 90", 0x1000UL, 0x2002UL)]
	[InlineData(0xFFFF_FFFF_FFFF_FFF0UL, 0xFFFF_FFFF_FFFF_FFFEUL, "90 90 90 90", 0xFFFF_FFFF_FFFF_FFF0UL,
		ulong.MaxValue)]
	[InlineData(0xFFFF_FFFF_FFFF_FFF0UL, ulong.MaxValue, "90", 0xFFFF_FFFF_FFFF_FFF0UL, ulong.MaxValue)]
	public void TheRangeEndBecomesAStopThatSaturatesAtTheTopOfTheAddressSpace(ulong start, ulong end, string pattern,
		ulong expectedStart, ulong expectedStop)
	{
		FakeAobScanPort port = QualifiedPort([]);
		PatternScanner scanner = CreateScanner(port);
		AobScanRequest request = new(new AobPattern(pattern), 1, null,
			new AobScanRange(start, end));

		Assert.True(scanner.TryScan(request, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken), failure.Message);

		Assert.Equal(new Address(expectedStart), port.LastBounds!.Value.Start);
		Assert.Equal(new Address(expectedStop), port.LastBounds.Value.Stop);
		Assert.Equal(0, port.EnumerationCalls);
	}

	[Fact]
	public void ARangeAtTheLastAddressLeavesNoBoundsAndIsRefusedBeforeAnyScan()
	{
		FakeAobScanPort port = QualifiedPort([]);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(null, new AobScanRange(ulong.MaxValue, ulong.MaxValue), 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void TheBoundsAreTheModuleIntersectedWithTheRange()
	{
		FakeAobScanPort port = QualifiedPort([0x4040]);
		PatternScanner scanner = CreateScanner(port);

		Assert.True(scanner.TryScan(Request(new ModuleName("game.exe"), new AobScanRange(0x3000, 0x4050), 2),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken),
			failure.Message);

		Assert.Equal([0x4040], result.Matches);
		Assert.Equal(new Address(ModuleBase), port.LastBounds!.Value.Start);
		Assert.Equal(new Address(0x4052), port.LastBounds.Value.Stop);
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void ARangeOutsideTheModuleIsRefusedBeforeAnyScanOnBothRoutes(bool qualified)
	{
		FakeAobScanPort port = new(new RecordingAobMatchList(["4010"]))
		{
			Modules = [Module()],
			Selection = qualified ? AobHosts.Local() : default
		};
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), new AobScanRange(0x9000, 0x9FFF), 1),
			out _, out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal("The AOB range leaves no room for a whole match inside the requested module.", failure.Message);
		Assert.Equal(0, port.SelectionCalls);
		Assert.Equal(0, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	/// <summary>
	///     A range that ends fewer than pattern-length bytes before the module overlaps the module's bytes but allows no
	///     match start whose whole match fits inside it, so it is refused before any scan, like a module smaller than the
	///     pattern.
	/// </summary>
	[Theory]
	[InlineData(true, "Range", "The AOB range leaves no room for a whole match inside the requested module.")]
	[InlineData(false, "Range", "The AOB range leaves no room for a whole match inside the requested module.")]
	[InlineData(true, "SmallModule", "The requested module is smaller than the AOB pattern.")]
	[InlineData(false, "SmallModule", "The requested module is smaller than the AOB pattern.")]
	public void BoundsThatCannotHoldAWholeMatchAreRefusedBeforeAnyScanOnBothRoutes(bool qualified, string shape,
		string expectedMessage)
	{
		ModuleInfo module = shape == "SmallModule"
			? new ModuleInfo("game.exe", new Address(ModuleBase), new MemorySize(3), true, "game.exe")
			: Module();
		FakeAobScanPort port = new(new RecordingAobMatchList(["3FFF"]))
		{
			Modules = [module],
			Selection = qualified ? AobHosts.Local() : default
		};
		PatternScanner scanner = CreateScanner(port);
		AobScanRequest request = shape == "SmallModule"
			? new AobScanRequest(new AobPattern("90 90 90 90"), 1, new ModuleName("game.exe"))
			: Request(new ModuleName("game.exe"), new AobScanRange(0x3000, ModuleBase - 1), 1);

		Assert.False(scanner.TryScan(request, out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(expectedMessage, failure.Message);
		Assert.Equal(0, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	/// <summary>
	///     Deviation 9 settled: the same module request gives the same addresses on both routes. A match must lie entirely
	///     inside the module, so a match that straddles the module end is dropped on the fallback route by the managed
	///     filter, and on the bounded route by the same Client check even if Cheat Engine reported it (the SDK drops rows by
	///     their start only).
	/// </summary>
	[Theory]
	[Trait("Qualification", "Q28")]
	[InlineData(false)]
	[InlineData(true)]
	public void BothRoutesKeepOnlyMatchesThatLieEntirelyInsideTheModule(bool withRange)
	{
		// A 4-byte pattern in the module [0x4000, 0x4100): 0x40FC is its last whole match, 0x40FD and 0x40FF straddle
		// the module end, and 0x4100 starts after it.
		string[] hostRows = ["40FC", "40FD", "40FF", "4100"];
		AobScanRequest request = new(new AobPattern("90 90 90 90"), 10, new ModuleName("game.exe"),
			withRange ? new AobScanRange(0x40F0, 0x40FE) : null);

		PatternScanOutcome bounded = Scan(AobHosts.Local());
		PatternScanOutcome global = Scan(AobHosts.Remote);

		Assert.True(bounded.IsSuccess, bounded.Failure?.Message);
		Assert.True(global.IsSuccess, global.Failure?.Message);
		Assert.Equal(PatternScanScope.HostBoundedRange, Assert.NotNull(bounded.Metrics).Scope);
		Assert.Equal(PatternScanScope.GlobalHostScanWithManagedFilter, Assert.NotNull(global.Metrics).Scope);
		Assert.Equal([0x40FC], bounded.Result!.Value.Matches);
		Assert.Equal(bounded.Result.Value.Matches, global.Result!.Value.Matches);
		Assert.Equal(bounded.Result.Value.IsTruncated, global.Result.Value.IsTruncated);
		Assert.Equal(3UL, bounded.Metrics.Value.FilteredOutCount);
		Assert.Equal(3UL, global.Metrics.Value.FilteredOutCount);

		PatternScanOutcome Scan(TargetSelectionFacts selection)
		{
			FakeAobScanPort port = new(new RecordingAobMatchList(hostRows))
			{
				Modules = [Module()],
				Selection = selection,
				BoundedRows = [.. hostRows.Select(static row => Address.Parse(row))]
			};
			return CreateScanner(port).ScanDetailed(request, TestContext.Current.CancellationToken);
		}
	}

	/// <summary>Both routes copy at most the Client's cap, so a request above it is truncated the same way on each.</summary>
	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(true)]
	[InlineData(false)]
	public void BothRoutesCopyAtMostTheClientCapAndReportTheCutAsTruncation(bool qualified)
	{
		const int rows = ScanResourceLimits.MaximumPatternMatches;
		ModuleInfo module = new("game.exe", new Address(ModuleBase), new MemorySize(0x10_0000), true, "game.exe");
		Address[] found = [.. Enumerable.Range(0, rows).Select(static index => new Address(ModuleBase + (ulong) index))];
		FakeAobScanPort port = new(new RecordingAobMatchList(
			[.. found.Select(static address => address.Value.ToString("X", CultureInfo.InvariantCulture))]))
		{
			Modules = [module],
			Selection = qualified ? AobHosts.Local() : AobHosts.Remote,
			BoundedRows = found
		};
		PatternScanner scanner = CreateScanner(port);

		Assert.True(scanner.TryScan(Request(new ModuleName("game.exe"), null, 100_000), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken), failure.Message);

		Assert.Equal(ScanResourceLimits.MaximumPatternMatches - 1, result.Matches.Length);
		Assert.True(result.IsTruncated);
		Assert.Equal(qualified ? 1 : 0, port.BoundedCalls);
	}

	/// <summary>One slot more than the limit proves truncation; the Client caps the destination.</summary>
	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(1, 2, 3, true)]
	[InlineData(2, 3, 3, true)]
	[InlineData(3, 4, 3, false)]
	[InlineData(int.MaxValue, ScanResourceLimits.MaximumPatternMatches, 3, false)]
	public void TheDestinationHoldsOneMoreThanTheLimitSoTruncationIsProvable(int maximumResults,
		int expectedDestination, int rows, bool expectedTruncation)
	{
		Address[] found = [0x4010, 0x4020, 0x4030];
		FakeAobScanPort port = QualifiedPort(found[..rows]);
		PatternScanner scanner = CreateScanner(port);

		Assert.True(scanner.TryScan(Request(new ModuleName("game.exe"), null, maximumResults),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken),
			failure.Message);

		Assert.Equal(expectedDestination, port.LastDestinationLength);
		Assert.Equal(expectedTruncation, result.IsTruncated);
		Assert.Equal(Math.Min(maximumResults, rows), result.Matches.Length);
	}

	[Fact]
	public void TheClientRangeAndModuleChecksStayAsDefensivePostFilters()
	{
		// CE honours the stop bound; an address outside the request is dropped and counted if it ever came back.
		FakeAobScanPort port = QualifiedPort([0x4010, 0x5000, 0x4020]);
		PatternScanner scanner = CreateScanner(port);

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), null, 5),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess);
		Assert.Equal([0x4010, 0x4020], outcome.Result!.Value.Matches);
		Assert.Equal(1UL, Assert.NotNull(outcome.Metrics).FilteredOutCount);
	}

	[Fact]
	public void AFullDestinationOfDroppedAddressesIsIndeterminate()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches) with
		{
			HostResultCount = 3,
			Written = 2,
			RowsRead = 2,
			UnreadHostRows = 1,
			IsMaterializationLimitReached = true
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([0x5000, 0x5010], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
	}

	[Fact]
	public void TheSdkCountsOfDroppedRowsAreReportedAsFilteredOut()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches) with
		{
			HostResultCount = 4,
			Written = 1,
			RowsRead = 4,
			BelowStartSkipped = 2,
			AtOrAfterStopSkipped = 1
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([0x4010], bounded));

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), null, 5),
			TestContext.Current.CancellationToken);

		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(4UL, metrics.HostResultCount);
		Assert.Equal(4UL, metrics.ExaminedCount);
		Assert.Equal(3UL, metrics.FilteredOutCount);
		Assert.Equal(1, metrics.MaterializedCount);
	}

	[Fact]
	public void InconsistentBoundedCountsAreAnInvalidHostResult()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches) with
		{
			HostResultCount = 1,
			Written = 1,
			RowsRead = 0
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([0x4010], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 5), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
	}

	/// <summary>An unqualified target or an SDK that cannot run the bounded route falls back to the global route.</summary>
	[Theory]
	[Trait("Qualification", "Q28")]
	[MemberData(nameof(Fallbacks))]
	public void TheRequestFallsBackToTheGlobalRouteWithManagedFilters(string reason)
	{
		RecordingAobMatchList matches = new(["3000", "4010", "5000"]);
		AobBoundedHostResult? bounded = reason switch
		{
			"SessionCreationFailed" => AobHosts.Bounded(AobBoundedScanOutcomeKind.SessionCreationFailed, false) with
			{
				CreationStatus = MemoryScanCreationStatus.TargetIdentityUnavailable
			},
			"TargetIdentityUnavailable" => AobHosts.Bounded(AobBoundedScanOutcomeKind.TargetIdentityUnavailable),
			_ => null
		};
		FakeAobScanPort port = new(matches)
		{
			Modules = [Module()],
			Selection = reason switch
			{
				"UnqualifiedTarget" => default,
				"FileAsProcess" => AobHosts.FileAsProcess,
				_ => AobHosts.Local()
			},
			BoundedResult = bounded
		};
		PatternScanner scanner = CreateScanner(port);

		PatternScanOutcome outcome = scanner.ScanDetailed(Request(new ModuleName("game.exe"), null, 5),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess, outcome.Failure?.Message);
		Assert.Equal([0x4010], outcome.Result!.Value.Matches);
		Assert.Equal(PatternScanScope.GlobalHostScanWithManagedFilter, Assert.NotNull(outcome.Metrics).Scope);
		Assert.Equal(PatternScanRouteReason.TargetIdentityNotQualified, outcome.RouteReason);
		Assert.False(outcome.TargetIdentityVerified);
		Assert.Equal(1, port.ScanCalls);
		Assert.Equal(bounded.HasValue ? 1 : 0, port.BoundedCalls);
		Assert.Equal(1, matches.ReleaseCount);
	}

	[Fact]
	public void ASessionWhoseRollbackWasNotConfirmedIsNeverHiddenBehindAFallback()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.SessionCreationFailed, false) with
		{
			CreationStatus = MemoryScanCreationStatus.RollbackUnconfirmed
		};
		FakeAobScanPort port = QualifiedPort([], bounded);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void AFallbackWhoseSessionReleaseWasNotConfirmedFailsInsteadOfScanningAgain()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.TargetIdentityUnavailable) with
		{
			FoundListRelease = TargetReleaseStatus.RefusedIdentityUnavailable
		};
		FakeAobScanPort port = QualifiedPort([], bounded);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.TargetIdentityUnavailable, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal(0, port.ScanCalls);
	}

	[Theory]
	[InlineData(TargetReleaseStatus.UnconfirmedAfterInvocation, TargetReleaseStatus.Released,
		MemoryScanTerminationStatus.NotRequired)]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.RefusedTargetChanged,
		MemoryScanTerminationStatus.NotRequired)]
	[InlineData(TargetReleaseStatus.Released, TargetReleaseStatus.Released, MemoryScanTerminationStatus.WaitTimedOut)]
	public void AnUnconfirmedSessionReleaseDiscardsTheCopy(TargetReleaseStatus foundList, TargetReleaseStatus memScan,
		MemoryScanTerminationStatus termination)
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches) with
		{
			HostResultCount = 1,
			Written = 1,
			RowsRead = 1,
			FoundListRelease = foundList,
			MemScanRelease = memScan,
			ReleaseTermination = termination
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([0x4010], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.StartsWith("The bounded AOB scan session release was not confirmed", failure.Message,
			StringComparison.Ordinal);
	}

	[Fact]
	public void AFailedScanWithAnUnconfirmedSessionReleaseKeepsItsKind()
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.TargetChanged) with
		{
			MemScanRelease = TargetReleaseStatus.RefusedTargetChanged
		};
		PatternScanner scanner = CreateScanner(QualifiedPort([], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.TargetChanged, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
	}

	[Theory]
	[InlineData(AobBoundedScanOutcomeKind.TargetChanged, CheatEngineFailureKind.TargetChanged)]
	[InlineData(AobBoundedScanOutcomeKind.RuntimeInvalidated, CheatEngineFailureKind.RuntimeChanged)]
	[InlineData(AobBoundedScanOutcomeKind.ScanFailed, CheatEngineFailureKind.LuaError)]
	[InlineData(AobBoundedScanOutcomeKind.InvalidResult, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(AobBoundedScanOutcomeKind.InvalidBounds, CheatEngineFailureKind.OperationRejected)]
	[InlineData(AobBoundedScanOutcomeKind.WaitTimedOut, CheatEngineFailureKind.IndeterminateHostResult)]
	[InlineData(AobBoundedScanOutcomeKind.Unknown, CheatEngineFailureKind.IndeterminateHostResult)]
	public void BoundedFailuresKeepTheirOwnKindsAndNeverFallBack(AobBoundedScanOutcomeKind kind,
		CheatEngineFailureKind expected)
	{
		FakeAobScanPort port = QualifiedPort([], AobHosts.Bounded(kind));
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(expected, failure.Kind);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void CancellationBeforeTheNativeCallStartsNoScan()
	{
		using CancellationTokenSource cancellation = new();
		FakeAobScanPort port = QualifiedPort([0x4010], onObserveSelection: cancellation.Cancel);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, cancellation.Token));

		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void CancellationAfterTheNativeCallPublishesNothingAndTheTokenReachesTheSdk()
	{
		using CancellationTokenSource cancellation = new();
		FakeAobScanPort port = QualifiedPort([0x4010], onScanWithinBounds: cancellation.Cancel);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out AobScanResult result,
			out CheatEngineFailure failure, cancellation.Token));

		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(cancellation.Token, port.LastBoundedToken);
	}

	/// <summary>The SDK observes the token between its calls; the reported milestone decides the effect.</summary>
	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(false, CheatEngineHostEffect.NotStarted)]
	[InlineData(true, CheatEngineHostEffect.Completed)]
	public void ACancellationTheSdkObservedReportsItsMilestone(bool scanCompleted,
		CheatEngineHostEffect expectedEffect)
	{
		AobBoundedHostResult bounded = AobHosts.Bounded(AobBoundedScanOutcomeKind.Cancelled, scanCompleted);
		PatternScanner scanner = CreateScanner(QualifiedPort([], bounded));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal(expectedEffect, failure.HostEffect);
	}

	[Fact]
	public void AnSdkFaultFromTheBoundedCallNeverCrossesTryScan()
	{
		InvalidOperationException fault = new("not on the main thread");
		PatternScanner scanner = CreateScanner(QualifiedPort([], onScanWithinBounds: () => throw fault));

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Same(fault, failure.Exception);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
	}

	[Fact]
	public void AnSdkFaultWhileObservingTheTargetStartsNoScan()
	{
		LuaException fault = new("observation failed");
		FakeAobScanPort port = QualifiedPort([], onObserveSelection: () => throw fault);
		PatternScanner scanner = CreateScanner(port);

		Assert.False(scanner.TryScan(Request(new ModuleName("game.exe"), null, 1), out _,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.NotStarted, failure.HostEffect);
		Assert.Equal(0, port.BoundedCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryAobBoundedScanOutcomeKindIsClassifiedAndAnUnknownKindFailsClosed()
	{
		Dictionary<AobBoundedScanOutcomeKind, (AobBoundedDisposition, CheatEngineFailureKind)> table = new()
		{
			[AobBoundedScanOutcomeKind.Unknown] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.IndeterminateHostResult),
			[AobBoundedScanOutcomeKind.Matches] = (AobBoundedDisposition.Publish, CheatEngineFailureKind.Unknown),
			[AobBoundedScanOutcomeKind.NoMatches] = (AobBoundedDisposition.Publish, CheatEngineFailureKind.Unknown),
			[AobBoundedScanOutcomeKind.InvalidBounds] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.OperationRejected),
			[AobBoundedScanOutcomeKind.SessionCreationFailed] =
				(AobBoundedDisposition.FallBack, CheatEngineFailureKind.CapabilityUnavailable),
			[AobBoundedScanOutcomeKind.ScanFailed] = (AobBoundedDisposition.Fail, CheatEngineFailureKind.LuaError),
			[AobBoundedScanOutcomeKind.WaitTimedOut] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.IndeterminateHostResult),
			[AobBoundedScanOutcomeKind.HostReportedError] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.OperationRejected),
			[AobBoundedScanOutcomeKind.InvalidResult] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.InvalidHostResult),
			[AobBoundedScanOutcomeKind.TargetChanged] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.TargetChanged),
			[AobBoundedScanOutcomeKind.TargetIdentityUnavailable] =
				(AobBoundedDisposition.FallBack, CheatEngineFailureKind.TargetIdentityUnavailable),
			[AobBoundedScanOutcomeKind.RuntimeInvalidated] =
				(AobBoundedDisposition.Fail, CheatEngineFailureKind.RuntimeChanged),
			[AobBoundedScanOutcomeKind.Cancelled] = (AobBoundedDisposition.Fail, CheatEngineFailureKind.Cancelled)
		};

		MappingTotality.AssertTotal<AobBoundedScanOutcomeKind>(
			kind => table.TryGetValue(kind, out (AobBoundedDisposition, CheatEngineFailureKind) expected) &&
					Classify(kind) == expected,
			static kind => Classify(kind) ==
						   (AobBoundedDisposition.Fail, CheatEngineFailureKind.IndeterminateHostResult));

		static (AobBoundedDisposition, CheatEngineFailureKind) Classify(AobBoundedScanOutcomeKind kind)
		{
			AobBoundedDisposition disposition = AobScanMapping.ClassifyBounded("Patterns.Scan",
				AobHosts.Bounded(kind), out CheatEngineFailure failure);
			return (disposition, failure.Kind);
		}
	}

	[Fact]
	public void TheSessionReleaseIsConfirmedOnlyWhenBothOwnersAreReleasedAndNoStopIsPending()
	{
		AobBoundedHostResult released = AobHosts.Bounded(AobBoundedScanOutcomeKind.Matches);
		AobBoundedHostResult confirmedStop = released with
		{
			ReleaseTermination = MemoryScanTerminationStatus.Confirmed
		};
		AobBoundedHostResult noSession = AobHosts.Bounded(AobBoundedScanOutcomeKind.InvalidBounds) with
		{
			CreationStatus = MemoryScanCreationStatus.Unknown,
			FoundListRelease = TargetReleaseStatus.Unspecified,
			MemScanRelease = TargetReleaseStatus.Unspecified
		};
		AobBoundedHostResult pendingStop = released with
		{
			ReleaseTermination = MemoryScanTerminationStatus.NotInvoked
		};

		Assert.True(AobScanMapping.IsSessionReleaseConfirmed(released, out _));
		Assert.True(AobScanMapping.IsSessionReleaseConfirmed(confirmedStop, out _));
		Assert.True(AobScanMapping.IsSessionReleaseConfirmed(noSession, out _));
		Assert.False(AobScanMapping.IsSessionReleaseConfirmed(pendingStop, out LeaseReleaseKind pendingKind));
		Assert.Equal(LeaseReleaseKind.CleanupUnconfirmed, pendingKind);
	}

	private static FakeAobScanPort QualifiedPort(Address[] rows, AobBoundedHostResult? result = null,
		Action? onObserveSelection = null, Action? onScanWithinBounds = null)
	{
		return new FakeAobScanPort(new RecordingAobMatchList(["4010"]))
		{
			Modules = [Module()],
			Selection = AobHosts.Local(),
			BoundedRows = rows,
			BoundedResult = result,
			OnObserveSelection = onObserveSelection,
			OnScanWithinBounds = onScanWithinBounds
		};
	}

	private static ModuleInfo Module()
	{
		return new ModuleInfo("game.exe", new Address(ModuleBase), new MemorySize(ModuleSize), true, "game.exe");
	}

	private static PatternScanner CreateScanner(FakeAobScanPort port)
	{
		return new PatternScanner(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), new InlineMainThreadInvoker()), port);
	}

	private static AobScanRequest Request(ModuleName? module, AobScanRange? range, int maximumResults)
	{
		return new AobScanRequest(new AobPattern("90 90"), maximumResults, module, range);
	}
}
