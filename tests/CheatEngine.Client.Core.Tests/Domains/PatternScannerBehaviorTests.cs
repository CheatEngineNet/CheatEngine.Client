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
			return matchList is null ? AobScanHostStatus.InvalidResult : AobScanHostStatus.Success;
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

		internal bool IsDisposed
		{
			get;
			private set;
		}

		public bool TryGetCount(out int count)
		{
			CountCalls++;
			count = items.Count;
			return true;
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
			IsDisposed = true;
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
