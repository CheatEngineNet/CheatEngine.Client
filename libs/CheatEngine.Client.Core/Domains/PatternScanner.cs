using System.Collections.Immutable;
using System.Diagnostics;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Runs one global Cheat Engine AOB scan and copies post-filtered addresses from its owned result list.</summary>
/// <remarks>
///     <para>
///         <b>Truthful cost (audit F07).</b> Core resolves the optional module first, then Cheat Engine runs one global
///         <c>AOBScan</c> over the whole target, and Core copies only the addresses inside the module or range. Module and
///         range are managed post-filters; <see cref="AobScanRequest.MaximumResults" /> bounds only the copy. None of them
///         reduces Cheat Engine's scan time or memory, and a cancellation token cannot interrupt a started scan.
///         <see cref="ScanDetailed" /> reports the Cheat Engine scan time separately from the copy time.
///     </para>
///     <para>
///         <b>Host outcomes (audit F06).</b> The port calls <c>AobScanner.TryScanOutcome</c> with its target context, and
///         <see cref="AobScanMapping" /> classifies each outcome: <c>NoResult</c> stays
///         <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, because on Cheat Engine 7.7 zero matches and
///         host failures share that shape. A target that changed during the scan discards its addresses.
///     </para>
///     <para>
///         <b>Single release authority (audit F13).</b> The owned result list is released exactly once on every path,
///         inside the dispatched callback, through the SDK's never-throwing <c>ReleaseWithOutcome</c>. Any release
///         status but <c>Released</c> is <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />: a release that was not
///         confirmed is never hidden behind a success.
///     </para>
/// </remarks>
internal sealed class PatternScanner(SdkMainThreadDispatcher dispatcher, IAobScanPort? scanPort = null)
	: IPatternScanner, IPatternScanOutcomeClient
{
	private const int MaximumModuleSnapshot = 4096;
	private const string InModuleOperation = "Patterns.InModule";
	private const string ScanOperation = "Patterns.Scan";

	private readonly SdkMainThreadDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly IAobScanPort _scanPort = scanPort ?? new SdkAobScanPort();

	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		ScanOutcome outcome = Execute(request, cancellationToken);
		result = outcome.Result;
		failure = outcome.Failure;
		return outcome.Succeeded;
	}

	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default)
	{
		if (TryScan(request, out AobScanResult result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw(cancellationToken);
		return default;
	}

	public PatternScanOutcome ScanDetailed(AobScanRequest request, CancellationToken cancellationToken = default)
	{
		ScanOutcome outcome = Execute(request, cancellationToken);
		return outcome.Succeeded
			? new PatternScanOutcome(outcome.Result, null, outcome.Metrics)
			: new PatternScanOutcome(null, outcome.Failure, outcome.Metrics);
	}

	internal static bool TryValidateRequest(AobScanRequest request, out CheatEngineFailure failure)
	{
		if (string.IsNullOrWhiteSpace(request.Pattern.Value))
		{
			failure = Rejected("An AOB scan requires a normalized, non-empty pattern.");
			return false;
		}

		if (request.MaximumResults <= 0)
		{
			failure = Rejected("An AOB scan requires a positive materialization limit.");
			return false;
		}

		if (request.Module.HasValue && string.IsNullOrWhiteSpace(request.Module.Value.Value))
		{
			failure = Rejected("An AOB module filter must be non-empty.");
			return false;
		}

		if (request.Range.HasValue && request.Range.Value.End < request.Range.Value.Start)
		{
			failure = Rejected("An AOB range end address must not precede its start address.");
			return false;
		}

		failure = default;
		return true;
	}

	/// <summary>Validates, dispatches, and classifies one scan identically for every public entry point.</summary>
	private ScanOutcome Execute(AobScanRequest request, CancellationToken cancellationToken)
	{
		if (!TryValidateRequest(request, out CheatEngineFailure failure))
		{
			return ScanOutcome.Failed(failure, null);
		}

		ScanInput input = new(this, request, cancellationToken);
		if (!_dispatcher.TryInvoke(input, static current => current.Scanner.ScanOnDispatchThread(current),
				out ScanOutcome outcome, out failure, cancellationToken))
		{
			return ScanOutcome.Failed(failure, null);
		}

		if (outcome.Metrics is { } metrics)
		{
			// Counts and durations only, after the dispatched callback returned (A24-17).
			_dispatcher.Lifetime.Diagnostics.PatternScanCompleted(metrics.Scope, metrics.HostMatchCount,
				metrics.MaterializedCount, outcome.Succeeded && outcome.Result.IsTruncated,
				(long) metrics.HostScanElapsed.TotalMilliseconds, (long) metrics.MaterializationElapsed.TotalMilliseconds);
		}

		return outcome;
	}

	private ScanOutcome ScanOnDispatchThread(ScanInput input)
	{
		AobScanRequest request = input.Request;
		CancellationToken cancellationToken = input.CancellationToken;
		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledBeforeScan(), null);
		}

		ModuleRange moduleRange = ModuleRange.None;
		if (request.Module.HasValue &&
			!TryGetModuleRange(request.Module.Value, out moduleRange, out CheatEngineFailure moduleFailure))
		{
			return ScanOutcome.Failed(moduleFailure, null);
		}

		// Module resolution is deliberately completed before the global CE AOB scan. The range still acts as a managed
		// post-filter because the SDK AOB binding does not accept a module constraint.
		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledBeforeScan(), null);
		}

		long hostScanStarted = Stopwatch.GetTimestamp();
		AobHostOutcome host;
		IAobMatchList? matchList;
		try
		{
			host = _scanPort.TryScan(request.Pattern.Value, request.Options, out matchList);
		}
		catch (Exception scanFault) when (SdkBoundary.IsSdkFault(scanFault))
		{
			// The SDK call may or may not have run CE's scan. OwnershipHandoff already released a list that was acquired
			// before the fault, so nothing is left for this method to release.
			return ScanOutcome.Failed(
				SdkBoundary.Translate(ScanOperation, scanFault, CheatEngineHostEffect.Unknown, _dispatcher.Lifetime),
				null);
		}

		TimeSpan hostScanElapsed = Stopwatch.GetElapsedTime(hostScanStarted);
		if (matchList is null)
		{
			return ScanOutcome.Failed(ClassifyWithoutList(host), null);
		}

		// From here on this method is the single release authority for the owned list: every path below releases it
		// exactly once, on this dispatched callback, and a release that is not confirmed is never hidden behind a success.
		ScanOutcome outcome;
		try
		{
			outcome = Consume(matchList, host, request, moduleRange, hostScanElapsed, cancellationToken);
		}
		catch (Exception copyFault) when (SdkBoundary.IsSdkFault(copyFault))
		{
			// A fault while reading the SDK-owned list: CE's scan had returned, so no scan work is outstanding. The fault
			// is classified like every other SDK fault, including the external-reset rule.
			outcome = ScanOutcome.Failed(
				SdkBoundary.Classify(ScanOperation, copyFault, CheatEngineHostEffect.Completed), null);
			ScanOutcome released = Release(matchList, outcome);
			SdkBoundary.ThrowIfActivationEnded(ScanOperation, copyFault, _dispatcher.Lifetime);
			return released;
		}
		catch (Exception)
		{
			// A lifecycle fault propagates unchanged; the list is still released once, on this callback.
			_ = ReleaseList(matchList, out _);
			throw;
		}

		return Release(matchList, outcome);
	}

	/// <summary>Classifies an outcome that handed out no result list, by SDK outcome only (never by error text).</summary>
	/// <remarks>
	///     <c>NoResult</c> is explicitly indeterminate (<see cref="AobScanMapping.NoResultMessage" />): on the pinned
	///     profile Cheat Engine returns <c>nil</c> for zero matches, and a host failure can produce the same shape. It is
	///     never reported as <see cref="CheatEngineFailureKind.NotFound" />, and it is attributed to the target only when
	///     the selection did not change during the call.
	/// </remarks>
	private static CheatEngineFailure ClassifyWithoutList(AobHostOutcome host)
	{
		if (host.Kind == AobScanOutcomeKind.NoResult &&
			AobScanMapping.TryGetTargetFailure(ScanOperation,
				AobScanMapping.JudgeTarget(host.TargetBefore, host.TargetAfter), out CheatEngineFailure targetFailure))
		{
			return targetFailure;
		}

		return AobScanMapping.ToFailure(ScanOperation, host);
	}

	private static ScanOutcome Consume(IAobMatchList matchList, AobHostOutcome host, AobScanRequest request,
		ModuleRange moduleRange, TimeSpan hostScanElapsed, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledAfterScan(), null);
		}

		if (host.Kind is not (AobScanOutcomeKind.Matches or AobScanOutcomeKind.NoMatches))
		{
			// The SDK hands out a list only with a verified count; any other outcome with a list is a broken contract.
			return ScanOutcome.Failed(InvalidList(), null);
		}

		if (AobScanMapping.TryGetTargetFailure(ScanOperation,
				AobScanMapping.JudgeTarget(host.TargetBefore, host.TargetAfter), out CheatEngineFailure targetFailure))
		{
			return ScanOutcome.Failed(targetFailure, null);
		}

		long materializationStarted = Stopwatch.GetTimestamp();
		if (!matchList.TryGetCount(out int count) || count < 0)
		{
			return ScanOutcome.Failed(new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
				"Cheat Engine returned an invalid AOB result count.", null, CheatEngineHostEffect.Completed), null);
		}

		MaterializationProgress progress = new(count);
		ScanOutcome outcome = Materialize(matchList, request, moduleRange, cancellationToken, ref progress);
		PatternScanMetrics metrics = progress.ToMetrics(hostScanElapsed,
			Stopwatch.GetElapsedTime(materializationStarted));
		return outcome with
		{
			Metrics = metrics
		};
	}

	private static ScanOutcome Materialize(IAobMatchList matches, AobScanRequest request, ModuleRange moduleRange,
		CancellationToken cancellationToken, ref MaterializationProgress progress)
	{
		// Do not preallocate to a caller-controlled materialization limit. The limit remains strict below, while
		// storage grows only for addresses that survived every managed filter.
		ImmutableArray<Address>.Builder materialized = ImmutableArray.CreateBuilder<Address>();
		for (int index = 0; index < progress.HostMatchCount; index++)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				return ScanOutcome.Failed(CancelledAfterScan(), null);
			}

			if (!matches.TryGetItem(index, out string? text) || !Address.TryParse(text, out Address address))
			{
				return ScanOutcome.Failed(new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult,
					ScanOperation, $"AOB result {index} was not a hexadecimal address.", null,
					CheatEngineHostEffect.Completed), null);
			}

			progress.Examined++;
			if (!moduleRange.Contains(address) ||
				(request.Range.HasValue && !request.Range.Value.Contains(address)))
			{
				progress.FilteredOut++;
				continue;
			}

			if (materialized.Count == request.MaximumResults)
			{
				// One more post-filtered address proves that the copy is incomplete. It is examined but not copied.
				return ScanOutcome.Success(new AobScanResult(materialized.ToImmutable(), true));
			}

			materialized.Add(address);
			progress.Materialized = materialized.Count;
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledAfterScan(), null);
		}

		return ScanOutcome.Success(new AobScanResult(materialized.ToImmutable(), false));
	}

	/// <summary>Releases the owned list once and turns an unconfirmed release into the operation's failure.</summary>
	/// <remarks>
	///     Only <see cref="TargetReleaseStatus.Released" /> confirms the release (<see cref="SdkReleaseOutcomes" />). Any
	///     other status is never reported as success, even when every address was copied: the copied result is discarded
	///     (audit ch.24 cleanup row, ADR-08). When the operation had already failed, the unconfirmed release is added to
	///     the original failure instead of replacing its cause. The metrics are kept: they describe the work that
	///     happened.
	/// </remarks>
	private static ScanOutcome Release(IAobMatchList matchList, ScanOutcome outcome)
	{
		LeaseReleaseKind released = ReleaseList(matchList, out Exception? releaseFault);
		return released == LeaseReleaseKind.Released
			? outcome
			: ScanOutcome.Failed(
				CreateReleaseFailure(outcome.Succeeded ? null : outcome.Failure, released, releaseFault),
				outcome.Metrics);
	}

	/// <summary>Releases the list once and maps the SDK status to the Client lease vocabulary.</summary>
	/// <remarks>
	///     The SDK release never throws. An SDK fault from a port that breaks that contract is kept as an unconfirmed
	///     release, so it never crosses a Try method.
	/// </remarks>
	private static LeaseReleaseKind ReleaseList(IAobMatchList matchList, out Exception? releaseFault)
	{
		try
		{
			releaseFault = null;
			return SdkReleaseOutcomes.FromTarget(matchList.Release()).Kind;
		}
		catch (Exception fault) when (SdkBoundary.IsSdkFault(fault))
		{
			releaseFault = fault;
			return LeaseReleaseKind.Unknown;
		}
	}

	private static CheatEngineFailure CreateReleaseFailure(CheatEngineFailure? primaryFailure,
		LeaseReleaseKind released, Exception? releaseFault)
	{
		if (primaryFailure is not { } primary)
		{
			return new CheatEngineFailure(CheatEngineFailureKind.InvalidState, ScanOperation,
				$"The AOB result list release was not confirmed ({released}); copied results were discarded.",
				releaseFault, CheatEngineHostEffect.CleanupUnconfirmed);
		}

		Exception? exception = (primary.Exception, releaseFault) switch
		{
			({ } cause, { } fault) => new AggregateException(cause, fault),
			({ } cause, null) => cause,
			_ => releaseFault
		};
		return new CheatEngineFailure(primary.Kind, primary.Operation,
			$"{primary.Message} The AOB result list release was not confirmed ({released}).", exception,
			CheatEngineHostEffect.CleanupUnconfirmed);
	}

	private static CheatEngineFailure Rejected(string message)
	{
		return new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation, message, null,
			CheatEngineHostEffect.NotStarted);
	}

	private static CheatEngineFailure InvalidList()
	{
		return new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
			AobScanMapping.InvalidListMessage, null, CheatEngineHostEffect.Completed);
	}

	private static CheatEngineFailure CancelledBeforeScan()
	{
		return CancellationMapping.BeforeNativeCall(ScanOperation,
			"The AOB scan was cancelled before Cheat Engine started it.");
	}

	private static CheatEngineFailure CancelledAfterScan()
	{
		return CancellationMapping.AfterNativeCall(ScanOperation,
			"The AOB scan was cancelled after Cheat Engine completed it; no copied result was published.");
	}

	private bool TryGetModuleRange(ModuleName requested, out ModuleRange range, out CheatEngineFailure failure)
	{
		ModuleInfo[] modules = new ModuleInfo[MaximumModuleSnapshot];
		range = ModuleRange.None;
		InspectionStatus status;
		int written;
		try
		{
			status = _scanPort.EnumerateModules(modules, out written);
		}
		catch (Exception inspectionFault) when (SdkBoundary.IsSdkFault(inspectionFault))
		{
			// Module inspection failed before the AOB scan was started.
			failure = SdkBoundary.Translate(InModuleOperation, inspectionFault, CheatEngineHostEffect.NotStarted,
				_dispatcher.Lifetime);
			return false;
		}

		if (status != InspectionStatus.Success)
		{
			failure = ModuleFailure(
				status == InspectionStatus.DestinationTooSmall
					? CheatEngineFailureKind.ResultLimitExceeded
					: CheatEngineFailureKind.CapabilityUnavailable,
				$"Module inspection returned '{status}'.");
			return false;
		}

		if ((uint) written > modules.Length)
		{
			failure = ModuleFailure(CheatEngineFailureKind.InvalidHostResult,
				"Cheat Engine returned an invalid module count.");
			return false;
		}

		bool found = false;
		for (int index = 0; index < written; index++)
		{
			ModuleInfo module = modules[index];
			if (!string.Equals(module.Name, requested.Value, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (found)
			{
				failure = ModuleFailure(CheatEngineFailureKind.AmbiguousMatch,
					$"More than one module named '{requested.Value}' was present in the selected target.");
				return false;
			}

			if (!module.ImageSize.HasValue)
			{
				failure = ModuleFailure(CheatEngineFailureKind.CapabilityUnavailable,
					"Cheat Engine did not report the requested module's image size.");
				return false;
			}

			if (module.ImageSize.Value.Value == 0)
			{
				failure = ModuleFailure(CheatEngineFailureKind.InvalidHostResult,
					"Cheat Engine reported a zero-length requested module.");
				return false;
			}

			found = true;
			range = new ModuleRange(module.BaseAddress.Value, module.ImageSize.Value.Value);
		}

		if (found)
		{
			failure = default;
			return true;
		}

		failure = ModuleFailure(CheatEngineFailureKind.NotFound,
			$"Module '{requested.Value}' was not present in the selected target.");
		return false;
	}

	/// <summary>A module-resolution failure: the AOB scan itself never started.</summary>
	private static CheatEngineFailure ModuleFailure(CheatEngineFailureKind kind, string message)
	{
		return new CheatEngineFailure(kind, InModuleOperation, message, null, CheatEngineHostEffect.NotStarted);
	}

	private readonly record struct ScanInput(
		PatternScanner Scanner,
		AobScanRequest Request,
		CancellationToken CancellationToken);

	/// <summary>The single internal classification shared by <see cref="TryScan" /> and <see cref="ScanDetailed" />.</summary>
	private readonly record struct ScanOutcome(
		bool Succeeded,
		AobScanResult Result,
		CheatEngineFailure Failure,
		PatternScanMetrics? Metrics)
	{
		internal static ScanOutcome Success(AobScanResult result)
		{
			return new ScanOutcome(true, result, default, null);
		}

		internal static ScanOutcome Failed(CheatEngineFailure failure, PatternScanMetrics? metrics)
		{
			return new ScanOutcome(false, default, failure, metrics);
		}
	}

	/// <summary>Allocation-free counters of the copy loop.</summary>
	private struct MaterializationProgress(int hostMatchCount)
	{
		internal readonly int HostMatchCount = hostMatchCount;
		internal int Examined;
		internal int FilteredOut;
		internal int Materialized;

		internal readonly PatternScanMetrics ToMetrics(TimeSpan hostScanElapsed, TimeSpan materializationElapsed)
		{
			return new PatternScanMetrics(HostMatchCount, Examined, FilteredOut, Materialized,
				PatternScanScope.GlobalHostScanWithManagedFilter, hostScanElapsed, materializationElapsed);
		}
	}

	private readonly record struct ModuleRange(bool IsActive, ulong Start, ulong Size)
	{
		internal static ModuleRange None => default;

		internal ModuleRange(ulong start, ulong size)
			: this(true, start, size)
		{
		}

		internal bool Contains(Address address)
		{
			return !IsActive || (address.Value >= Start && address.Value - Start < Size);
		}
	}
}
