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
			"(indistinguishable with CheatEngine.SDK 1.0.0).",
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
