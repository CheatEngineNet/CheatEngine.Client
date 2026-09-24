using System.Diagnostics.CodeAnalysis;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class PatternScannerBehaviorTests
{
	[Fact]
	[Trait("Qualification", "Q28")]
	public void TryScanResolvesModuleBeforeTheGlobalScanAndAppliesModuleAndRangeAsPostFilters()
	{
		RecordingAobMatchList matches = new(["3FFF", "4000", "4010", "4020", "4100"]);
		FakeAobScanPort port = new(matches)
		{
			Modules = [Module("game.exe", 0x4000, 0x100)]
		};
		PatternScanner scanner = CreateScanner(port);
		AobScanRequest request = CreateRequest(
			new ModuleName("game.exe"), new AobScanRange(0x4010, 0x4020), 3);

		bool succeeded = scanner.TryScan(request, out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal([0x4010, 0x4020], result.Matches);
		Assert.False(result.IsTruncated);
		Assert.Equal(1, port.EnumerationCalls);
		Assert.Equal(1, port.ScanCalls);
		Assert.Equal(1, port.EnumerationCallsWhenScanStarted);
		Assert.Equal(5, matches.ItemCalls);
		Assert.True(matches.IsDisposed);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	[Trait("Qualification", "Q29")]
	public void TryScanCountsOnlyPostFilteredAddressesAgainstTheMaterializationLimit()
	{
		RecordingAobMatchList matches = new(["3FFF", "4000", "4001", "40FF"]);
		FakeAobScanPort port = new(matches)
		{
			Modules = [Module("game.exe", 0x4000, 0x100)]
		};
		PatternScanner scanner = CreateScanner(port);
		AobScanRequest request = CreateRequest(new ModuleName("game.exe"), null, 2);

		bool succeeded = scanner.TryScan(request, out AobScanResult result, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal([0x4000, 0x4001], result.Matches);
		Assert.True(result.IsTruncated);
		Assert.Equal(4, matches.ItemCalls);
		Assert.True(matches.IsDisposed);
	}

	[Fact]
	public void TryScanRejectsAnUnknownModuleWithoutStartingTheGlobalScan()
	{
		FakeAobScanPort port = new()
		{
			Modules = [Module("other.exe", 0x4000, 0x100)]
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.Equal("Patterns.InModule", failure.Operation);
		Assert.Equal(1, port.EnumerationCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanRejectsAnAmbiguousModuleWithoutStartingTheGlobalScan()
	{
		FakeAobScanPort port = new()
		{
			Modules = [Module("game.exe", 0x4000, 0x100), Module("GAME.EXE", 0x5000, 0x100)]
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.AmbiguousMatch, failure.Kind);
		Assert.Equal("Patterns.InModule", failure.Operation);
		Assert.Equal(1, port.EnumerationCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanRejectsAModuleWithoutAnImageSizeBeforeStartingTheGlobalScan()
	{
		FakeAobScanPort port = new()
		{
			Modules = [Module("game.exe", 0x4000, null)]
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.CapabilityUnavailable, failure.Kind);
		Assert.Equal("Patterns.InModule", failure.Operation);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanRejectsAZeroLengthModuleBeforeStartingTheGlobalScan()
	{
		FakeAobScanPort port = new()
		{
			Modules = [Module("game.exe", 0x4000, 0)]
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Patterns.InModule", failure.Operation);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanRejectsAnInvalidModuleEnumerationCountBeforeStartingTheGlobalScan()
	{
		FakeAobScanPort port = new()
		{
			ReportedModuleCount = 4097
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Patterns.InModule", failure.Operation);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanCancellationDuringModuleEnumerationPreventsTheGlobalScan()
	{
		using CancellationTokenSource cancellation = new();
		FakeAobScanPort port = new()
		{
			Modules = [Module("game.exe", 0x4000, 0x100)],
			OnEnumerateModules = cancellation.Cancel
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(new ModuleName("game.exe"), null, 1),
			out AobScanResult result, out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(1, port.EnumerationCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	public void TryScanObservesCancellationAtTheBeginningOfTheDispatchedCallback()
	{
		using CancellationTokenSource cancellation = new();
		FakeAobScanPort port = new();
		PatternScanner scanner = CreateScanner(port, new CancellingMainThreadInvoker(cancellation));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1),
			out AobScanResult result, out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(0, port.EnumerationCalls);
		Assert.Equal(0, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanObservesCancellationAfterTheGlobalScanAndDisposesTheOwnedList()
	{
		using CancellationTokenSource cancellation = new();
		RecordingAobMatchList matches = new(["400000"]);
		FakeAobScanPort port = new(matches)
		{
			OnScan = cancellation.Cancel
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1),
			out AobScanResult result, out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(1, port.ScanCalls);
		Assert.Equal(0, matches.CountCalls);
		Assert.Equal(0, matches.ItemCalls);
		Assert.True(matches.IsDisposed);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void TryScanObservesCancellationDuringCopyDisposesTheOwnedListAndDoesNotPublishAPrefix()
	{
		using CancellationTokenSource cancellation = new();
		RecordingAobMatchList matches = new(["400000", "400001", "400002"])
		{
			OnTryGetItem = index =>
			{
				if (index == 1)
				{
					cancellation.Cancel();
				}
			}
		};
		FakeAobScanPort port = new(matches);
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 3),
			out AobScanResult result, out CheatEngineFailure failure, cancellation.Token);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(1, port.ScanCalls);
		Assert.Equal(2, matches.ItemCalls);
		Assert.True(matches.IsDisposed);
	}

	/// <summary>
	///     The throwing form raises the cancellation exception, keeps the completed host effect, and still releases the
	///     owned list without publishing a prefix.
	/// </summary>
	[Fact]
	[Trait("Qualification", "Q29")]
	public void ScanThrowsOperationCanceledExceptionWhenCancellationIsObservedDuringCopy()
	{
		using CancellationTokenSource cancellation = new();
		RecordingAobMatchList matches = new(["400000", "400001", "400002"])
		{
			OnTryGetItem = index =>
			{
				if (index == 1)
				{
					cancellation.Cancel();
				}
			}
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		CheatEngineOperationCanceledException exception = Assert.Throws<CheatEngineOperationCanceledException>(() =>
			scanner.Scan(CreateRequest(null, null, 3), cancellation.Token));

		Assert.Equal(cancellation.Token, exception.CancellationToken);
		Assert.Equal(CheatEngineFailureKind.Cancelled, exception.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, exception.Failure.HostEffect);
		Assert.Equal("Patterns.Scan", exception.Failure.Operation);
		Assert.True(matches.IsDisposed);
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void ScanDetailedReportsHostExaminedFilteredAndMaterializedCountsForAModuleFilter()
	{
		RecordingAobMatchList matches = new(["3000", "4000", "4010", "5000", "6000"]);
		FakeAobScanPort port = new(matches)
		{
			Modules = [Module("game.exe", 0x4000, 0x100)]
		};
		PatternScanner scanner = CreateScanner(port);

		PatternScanOutcome outcome = scanner.ScanDetailed(CreateRequest(new ModuleName("game.exe"), null, 10),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.Succeeded);
		Assert.Null(outcome.Cause);
		Assert.Equal([0x4000, 0x4010], outcome.Result!.Value.Matches);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(5, metrics.HostMatchCount);
		Assert.Equal(5, metrics.ExaminedCount);
		Assert.Equal(3, metrics.FilteredOutCount);
		Assert.Equal(2, metrics.MaterializedCount);
		Assert.Equal(PatternScanScope.GlobalHostScanWithManagedFilter, metrics.Scope);
		Assert.True(metrics.HostScanElapsed >= TimeSpan.Zero);
		Assert.True(metrics.MaterializationElapsed >= TimeSpan.Zero);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void ScanDetailedReportsExaminedBelowHostCountWhenMaterializationStopsEarly()
	{
		RecordingAobMatchList matches = new(["400000", "400010", "400020", "400030", "400040"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		PatternScanOutcome outcome = scanner.ScanDetailed(CreateRequest(null, null, 1),
			TestContext.Current.CancellationToken);

		Assert.True(outcome.Succeeded);
		Assert.True(outcome.Result!.Value.IsTruncated);
		Assert.Equal([0x400000], outcome.Result.Value.Matches);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(5, metrics.HostMatchCount);
		Assert.Equal(2, metrics.ExaminedCount);
		Assert.True(metrics.ExaminedCount < metrics.HostMatchCount);
		Assert.Equal(0, metrics.FilteredOutCount);
		Assert.Equal(1, metrics.MaterializedCount);
		Assert.Equal(2, matches.ItemCalls);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Fact]
	[Trait("Qualification", "Q29")]
	public void ScanDetailedKeepsTheMetricsOfACopyCancelledAfterTheScan()
	{
		using CancellationTokenSource cancellation = new();
		RecordingAobMatchList matches = new(["400000", "400001", "400002"])
		{
			OnTryGetItem = index =>
			{
				if (index == 1)
				{
					cancellation.Cancel();
				}
			}
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		PatternScanOutcome outcome = scanner.ScanDetailed(CreateRequest(null, null, 3), cancellation.Token);

		Assert.False(outcome.Succeeded);
		Assert.Null(outcome.Result);
		Assert.Equal(CheatEngineFailureKind.Cancelled, outcome.Cause!.Value.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, outcome.Cause.Value.HostEffect);
		PatternScanMetrics metrics = Assert.NotNull(outcome.Metrics);
		Assert.Equal(3, metrics.HostMatchCount);
		Assert.Equal(2, metrics.ExaminedCount);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Theory]
	[InlineData(ClassificationPath.Success)]
	[InlineData(ClassificationPath.NoList)]
	[InlineData(ClassificationPath.InvalidList)]
	[InlineData(ClassificationPath.InvalidCount)]
	[InlineData(ClassificationPath.InvalidItem)]
	[InlineData(ClassificationPath.CancellationAfterScan)]
	public void ScanDetailedClassifiesExactlyLikeTryScan(ClassificationPath path)
	{
		CancellationTokenSource tryCancellation = new();
		CancellationTokenSource detailedCancellation = new();
		using (tryCancellation)
		using (detailedCancellation)
		{
			PatternScanner tryScanner = CreateScanner(CreatePort(path, tryCancellation));
			PatternScanner detailedScanner = CreateScanner(CreatePort(path, detailedCancellation));

			bool succeeded = tryScanner.TryScan(CreateRequest(null, null, 2), out AobScanResult result,
				out CheatEngineFailure failure, tryCancellation.Token);
			PatternScanOutcome outcome = detailedScanner.ScanDetailed(CreateRequest(null, null, 2),
				detailedCancellation.Token);

			Assert.Equal(succeeded, outcome.Succeeded);
			Assert.Equal(path == ClassificationPath.Success, succeeded);
			if (succeeded)
			{
				Assert.Equal(result.Matches, outcome.Result!.Value.Matches);
				Assert.Equal(result.IsTruncated, outcome.Result.Value.IsTruncated);
				Assert.NotNull(outcome.Metrics);
			}
			else
			{
				CheatEngineFailure detailed = Assert.NotNull(outcome.Cause);
				Assert.Equal(failure.Kind, detailed.Kind);
				Assert.Equal(failure.Operation, detailed.Operation);
				Assert.Equal(failure.Message, detailed.Message);
				Assert.Equal(failure.HostEffect, detailed.HostEffect);
				Assert.Equal(path is ClassificationPath.NoList or ClassificationPath.InvalidList
					or ClassificationPath.InvalidCount or ClassificationPath.CancellationAfterScan, outcome.Metrics is null);
			}
		}

		static FakeAobScanPort CreatePort(ClassificationPath path, CancellationTokenSource cancellation)
		{
			return path switch
			{
				ClassificationPath.NoList => new FakeAobScanPort { Status = AobScanHostStatus.NoResultList },
				ClassificationPath.InvalidList => new FakeAobScanPort(),
				ClassificationPath.InvalidCount => new FakeAobScanPort(
					new RecordingAobMatchList(["400000"]) { CountAvailable = false }),
				ClassificationPath.InvalidItem => new FakeAobScanPort(new RecordingAobMatchList(["zz"])),
				ClassificationPath.CancellationAfterScan => new FakeAobScanPort(new RecordingAobMatchList(["400000"]))
				{
					OnScan = cancellation.Cancel
				},
				_ => new FakeAobScanPort(new RecordingAobMatchList(["400000", "400010", "400020"]))
			};
		}
	}

	[Fact]
	[Trait("Qualification", "Q28")]
	public void FirstOrNoneReturnsTheFirstHostListElementEvenWhenALowerAddressFollows()
	{
		RecordingAobMatchList matches = new(["5000", "4000"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal([0x5000], result.Matches);
		Assert.True(result.IsTruncated);
	}

	[Fact]
	public void TryScanParsesUnpaddedX64AndZeroPaddedX86AddressFormats()
	{
		RecordingAobMatchList matches = new(["7FFC7A0A0000", "100000000", "00400000"]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 3), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Equal([0x7FFC7A0A0000, 0x100000000, 0x400000], result.Matches);
	}

	public enum ClassificationPath
	{
		Success,
		NoList,
		InvalidList,
		InvalidCount,
		InvalidItem,
		CancellationAfterScan
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void MissingAobResultListIsAmbiguousAndNotNotFound()
	{
		FakeAobScanPort port = new()
		{
			Status = AobScanHostStatus.NoResultList
		};
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, failure.Kind);
		Assert.NotEqual(CheatEngineFailureKind.NotFound, failure.Kind);
		Assert.NotEqual(CheatEngineFailureKind.OperationRejected, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(
			"Cheat Engine returned no AOB result list: zero matches or a host failure " +
			"(indistinguishable on this scan route).",
			failure.Message);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Null(failure.Exception);
		Assert.Equal(1, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void ScanThrowsWhenTheAobResultIsAmbiguousAndNotNotFound()
	{
		PatternScanner scanner = CreateScanner(new FakeAobScanPort
		{
			Status = AobScanHostStatus.NoResultList
		});

		CheatEngineOperationException exception = Assert.Throws<CheatEngineOperationException>(() =>
			scanner.Scan(CreateRequest(null, null, 2), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineFailureKind.IndeterminateHostResult, exception.Failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Completed, exception.Failure.HostEffect);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void InvalidResultListRemainsAnInvalidHostResult()
	{
		FakeAobScanPort port = new();
		PatternScanner scanner = CreateScanner(port);

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidHostResult, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.Completed, failure.HostEffect);
		Assert.Equal(1, port.ScanCalls);
	}

	[Fact]
	[Trait("Qualification", "Q27")]
	public void AnEmptyReturnedListIsARealNoMatchSuccess()
	{
		RecordingAobMatchList matches = new([]);
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 1), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.True(succeeded);
		Assert.Equal(default, failure);
		Assert.Empty(result.Matches);
		Assert.False(result.IsTruncated);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Fact]
	public void TryScanReportsCleanupUnconfirmedWhenTheResultListReleaseThrows()
	{
		InvalidOperationException releaseFailure = new("The SDK runtime detached before the list was destroyed.");
		RecordingAobMatchList matches = new(["400000", "400010"])
		{
			DisposeFailure = releaseFailure
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 5), out AobScanResult result,
			out CheatEngineFailure failure, TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(default, result);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal("Patterns.Scan", failure.Operation);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		Assert.Equal("The AOB result list release was not confirmed; copied results were discarded.",
			failure.Message);
		Assert.Same(releaseFailure, failure.Exception);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Fact]
	public void TryScanKeepsThePrimaryFailureWhenTheReleaseAlsoThrows()
	{
		InvalidOperationException releaseFailure = new("release failed");
		RecordingAobMatchList matches = new(["not-an-address"])
		{
			DisposeFailure = releaseFailure
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		bool succeeded = scanner.TryScan(CreateRequest(null, null, 5), out _, out CheatEngineFailure failure,
			TestContext.Current.CancellationToken);

		Assert.False(succeeded);
		Assert.Equal(CheatEngineFailureKind.InvalidState, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, failure.HostEffect);
		AggregateException aggregate = Assert.IsType<AggregateException>(failure.Exception);
		Assert.Collection(
			aggregate.InnerExceptions,
			primary => Assert.Equal(CheatEngineFailureKind.InvalidHostResult,
				Assert.IsType<CheatEngineOperationException>(primary).Failure.Kind),
			release => Assert.Same(releaseFailure, release));
		Assert.Equal(1, matches.DisposeCount);
	}

	[Fact]
	public void ScanNeverLetsAReleaseExceptionEscapeAsAnUnclassifiedException()
	{
		RecordingAobMatchList matches = new(["400000"])
		{
			DisposeFailure = new InvalidOperationException("release failed")
		};
		PatternScanner scanner = CreateScanner(new FakeAobScanPort(matches));

		CheatEngineClientLifecycleException exception = Assert.Throws<CheatEngineClientLifecycleException>(() =>
			scanner.Scan(CreateRequest(null, null, 1), TestContext.Current.CancellationToken));

		Assert.Equal(CheatEngineHostEffect.CleanupUnconfirmed, exception.Failure.HostEffect);
		Assert.Equal(1, matches.DisposeCount);
	}

	[Theory]
	[Trait("Qualification", "Q29")]
	[InlineData(ReleasePath.Success, CheatEngineFailureKind.Unknown)]
	[InlineData(ReleasePath.Truncation, CheatEngineFailureKind.Unknown)]
	[InlineData(ReleasePath.CancellationAfterScan, CheatEngineFailureKind.Cancelled)]
	[InlineData(ReleasePath.CancellationDuringCopy, CheatEngineFailureKind.Cancelled)]
	[InlineData(ReleasePath.InvalidCount, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(ReleasePath.UnavailableCount, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(ReleasePath.InvalidItem, CheatEngineFailureKind.InvalidHostResult)]
	[InlineData(ReleasePath.InvalidListStatus, CheatEngineFailureKind.InvalidHostResult)]
	public void TryScanReleasesTheListExactlyOnceOnEveryPath(ReleasePath path, CheatEngineFailureKind expectedKind)
	{
		using CancellationTokenSource cancellation = new();
		RecordingAobMatchList matches = path switch
		{
			ReleasePath.InvalidCount => new RecordingAobMatchList(["400000"]) { ReportedCount = -1 },
			ReleasePath.UnavailableCount => new RecordingAobMatchList(["400000"]) { CountAvailable = false },
			ReleasePath.InvalidItem => new RecordingAobMatchList(["400000", "not-an-address"]),
			ReleasePath.CancellationDuringCopy => new RecordingAobMatchList(["400000", "400001", "400002"])
			{
				OnTryGetItem = index =>
				{
					if (index == 1)
					{
						cancellation.Cancel();
					}
				}
			},
			_ => new RecordingAobMatchList(["400000", "400001", "400002"])
		};
		FakeAobScanPort port = new(matches)
		{
			OnScan = path == ReleasePath.CancellationAfterScan ? cancellation.Cancel : null,
			Status = path == ReleasePath.InvalidListStatus ? AobScanHostStatus.InvalidResult : null
		};
		PatternScanner scanner = CreateScanner(port);
		int invokingThread = Environment.CurrentManagedThreadId;
		bool expectedSuccess = path is ReleasePath.Success or ReleasePath.Truncation;

		bool succeeded = scanner.TryScan(CreateRequest(null, null, path == ReleasePath.Truncation ? 1 : 3),
			out AobScanResult result, out CheatEngineFailure failure, cancellation.Token);

		Assert.Equal(expectedSuccess, succeeded);
		Assert.Equal(expectedKind, failure.Kind);
		Assert.Equal(path == ReleasePath.Truncation, result.IsTruncated);
		Assert.Equal(expectedSuccess ? 0 : 1, string.IsNullOrEmpty(failure.Operation) ? 0 : 1);
		Assert.Equal(1, matches.DisposeCount);
		Assert.Equal(invokingThread, matches.DisposeThreadId);
	}

	public enum ReleasePath
	{
		Success,
		Truncation,
		CancellationAfterScan,
		CancellationDuringCopy,
		InvalidCount,
		UnavailableCount,
		InvalidItem,
		InvalidListStatus
	}

	private static PatternScanner CreateScanner(FakeAobScanPort port, IMainThreadInvoker? mainThread = null)
	{
		return new PatternScanner(
			new SdkMainThreadDispatcher(InertCoreLifetime.Create(), mainThread ?? new InlineMainThreadInvoker()), port);
	}

	private static AobScanRequest CreateRequest(ModuleName? module, AobScanRange? range, int maximumResults)
	{
		return new AobScanRequest(new AobPattern("90"), AobScanOptions.Default, maximumResults, module, range);
	}

	private static ModuleInfo Module(string name, ulong baseAddress, ulong? imageSize)
	{
		MemorySize? size = imageSize.HasValue ? new MemorySize(imageSize.Value) : null;
		return new ModuleInfo(name, new Address(baseAddress), size, true, name);
	}

	private sealed class FakeAobScanPort(RecordingAobMatchList? matchList = null) : IAobScanPort
	{
		internal ModuleInfo[] Modules
		{
			get;
			init;
		} = [];

		internal int? ReportedModuleCount
		{
			get;
			init;
		}

		internal AobScanHostStatus? Status
		{
			get;
			init;
		}

		internal Action? OnScan
		{
			get;
			init;
		}

		internal Action? OnEnumerateModules
		{
			get;
			init;
		}

		internal int EnumerationCalls
		{
			get;
			private set;
		}

		internal int ScanCalls
		{
			get;
			private set;
		}

		internal int? EnumerationCallsWhenScanStarted
		{
			get;
			private set;
		}

		public AobScanHostStatus TryScan(string pattern, AobScanOptions options,
			[NotNullWhen(true)] out IAobMatchList? matches)
		{
			ScanCalls++;
			EnumerationCallsWhenScanStarted ??= EnumerationCalls;
			OnScan?.Invoke();
			matches = matchList;
			return Status ?? (matchList is null ? AobScanHostStatus.InvalidResult : AobScanHostStatus.Success);
		}

		public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
		{
			EnumerationCalls++;
			OnEnumerateModules?.Invoke();
			Array.Copy(Modules, destination, Math.Min(Modules.Length, destination.Length));
			written = ReportedModuleCount ?? Modules.Length;
			return InspectionStatus.Success;
		}
	}

	private sealed class RecordingAobMatchList(IReadOnlyList<string> items) : IAobMatchList
	{
		internal Action<int>? OnTryGetItem
		{
			get;
			init;
		}

		internal Exception? DisposeFailure
		{
			get;
			init;
		}

		internal int? ReportedCount
		{
			get;
			init;
		}

		internal bool CountAvailable
		{
			get;
			init;
		} = true;

		internal int CountCalls
		{
			get;
			private set;
		}

		internal int ItemCalls
		{
			get;
			private set;
		}

		internal int DisposeCount
		{
			get;
			private set;
		}

		internal int? DisposeThreadId
		{
			get;
			private set;
		}

		internal bool IsDisposed => DisposeCount > 0;

		public bool TryGetCount(out int count)
		{
			CountCalls++;
			count = ReportedCount ?? items.Count;
			return CountAvailable;
		}

		public bool TryGetItem(int index, [NotNullWhen(true)] out string? value)
		{
			ItemCalls++;
			OnTryGetItem?.Invoke(index);
			if ((uint) index >= items.Count)
			{
				value = null;
				return false;
			}

			value = items[index];
			return true;
		}

		public void Dispose()
		{
			DisposeCount++;
			DisposeThreadId = Environment.CurrentManagedThreadId;
			if (DisposeFailure is not null)
			{
				throw DisposeFailure;
			}
		}
	}

	private sealed class CancellingMainThreadInvoker(CancellationTokenSource cancellation) : IMainThreadInvoker
	{
		public Exception? Invoke(Action callback)
		{
			cancellation.Cancel();
			try
			{
				callback();
				return null;
			}
			catch (Exception exception)
			{
				return exception;
			}
		}

		public MainThreadInvocationResult<T> Invoke<T>(Func<T> callback)
		{
			cancellation.Cancel();
			try
			{
				return new MainThreadInvocationResult<T>(callback(), null);
			}
			catch (Exception exception)
			{
				return new MainThreadInvocationResult<T>(default!, exception);
			}
		}
	}
}
