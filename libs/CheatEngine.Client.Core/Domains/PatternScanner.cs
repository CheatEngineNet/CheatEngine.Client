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

/// <summary>Runs one Cheat Engine AOB scan on the route its scope allows and copies the matching addresses.</summary>
/// <remarks>
///     <para>
///         <b>Routes and truthful cost (audit F07).</b> A request without a module or range runs one global
///         <c>AOBScan</c> over the whole target (<see cref="PatternScanScope.GlobalHostScan" />);
///         <see cref="AobScanRequest.MaximumResults" /> bounds only the copy. A request with a module and/or a range, on a
///         target that <c>TargetSelection.ObserveCurrent</c> qualifies, runs the SDK's bounded, exhaustive MemScan route
///         over the module intersected with the range (<see cref="PatternScanScope.HostBoundedRange" />): Cheat Engine's
///         work is limited to those bounds, and the call blocks Cheat Engine's main thread for the scan, the copy and the
///         session release. When the target is not qualified, or the SDK cannot create the session or qualify the target,
///         the request falls back to the global scan with the module and range as managed post-filters
///         (<see cref="PatternScanScope.GlobalHostScanWithManagedFilter" />). A cancellation token never interrupts a
///         Cheat Engine call that has started. <see cref="ScanDetailed" /> reports the Cheat Engine scan time separately
///         from the copy time.
///     </para>
///     <para>
///         <b>One scope rule on every route.</b> A module keeps a match only when all of its pattern bytes lie inside
///         <c>[BaseAddress, BaseAddress + ImageSize)</c>; a range keeps a match whose start lies in <c>[Start, End]</c>.
///         Both routes apply the same predicate (<see cref="IsInsideRequest" />) while copying, so the bounded route's
///         reliance on Cheat Engine honouring its stop bound becomes a Client guarantee, and the same request gives the
///         same addresses whichever route ran. Both routes copy at most
///         <c>ScanResourceLimits.MaximumPatternMatches - 1</c> addresses.
///     </para>
///     <para>
///         <b>Host outcomes (audit F06).</b> <see cref="AobScanMapping" /> classifies each outcome of both routes. On the
///         global route <c>NoResult</c> stays <see cref="CheatEngineFailureKind.IndeterminateHostResult" />, because on
///         Cheat Engine 7.7 zero matches and host failures share that shape; only the bounded route reports a factual zero,
///         and only when Cheat Engine's error text was readable. A target that changed during a scan discards its answer.
///     </para>
///     <para>
///         <b>Single release authority (audit F13).</b> The owned result list of the global route is released exactly
///         once on every path, inside the dispatched callback, through the SDK's never-throwing
///         <c>ReleaseWithOutcome</c>; the SDK releases the bounded route's session itself and reports it. Any release
///         status but <c>Released</c> is <see cref="CheatEngineHostEffect.CleanupUnconfirmed" />: a release that was not
///         confirmed is never hidden behind a success.
///     </para>
/// </remarks>
internal sealed class PatternScanner(SdkMainThreadDispatcher dispatcher, IAobScanPort? scanPort = null)
	: IPatternScanner
{
	private const int MaximumModuleSnapshot = 4096;
	private const string ScanOperation = "Patterns.Scan";
	private const string ListSubject = "AOB result list";
	private const string SessionSubject = "bounded AOB scan session";

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
			? new PatternScanOutcome(outcome.Result, null, outcome.Metrics, outcome.HostOutcome, outcome.RouteReason,
				outcome.TargetIdentityVerified)
			: new PatternScanOutcome(null, outcome.Failure, outcome.Metrics, outcome.HostOutcome, outcome.RouteReason,
				false);
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

		if (!ScanOptionTranslation.IsDefined(request.Protection) || !ScanOptionTranslation.IsDefined(request.Alignment))
		{
			failure = Rejected("An AOB protection filter or alignment rule is not a defined value.");
			return false;
		}

		failure = default;
		return true;
	}

	/// <summary>
	///     Builds the Cheat Engine work limit of the bounded route: the module's <c>[BaseAddress, BaseAddress +
	///     ImageSize)</c> intersected with the range's <c>[Start, End + pattern length)</c>.
	/// </summary>
	/// <param name="module">The resolved module, when the request names one.</param>
	/// <param name="range">The requested inclusive range of match starts, when the request has one.</param>
	/// <param name="patternLength">The number of byte positions of the pattern.</param>
	/// <param name="bounds">The half-open bounds when the method returns <see langword="true" />.</param>
	/// <param name="failure">The refusal otherwise; nothing was started.</param>
	/// <returns>
	///     <see langword="false" /> when the bounds cannot hold one whole match, or the module does not fit the address
	///     space.
	/// </returns>
	/// <remarks>
	///     Both routes use these bounds as a precondition. Bounds shorter than the pattern hold no match start that
	///     <see cref="IsInsideRequest" /> could keep (a range that ends before the module can hold a whole match, or a
	///     module smaller than the pattern), so such a request is refused before any scan instead of costing a global scan
	///     that can only return nothing.
	/// </remarks>
	internal static bool TryCreateBounds(ModuleInfo? module, AobScanRange? range, int patternLength,
		out AobScanBounds bounds, out CheatEngineFailure failure)
	{
		Address start = Address.Zero;
		Address stop = new(ulong.MaxValue);
		if (module is { } resolved)
		{
			if (!AobScanBounds.TryFromModule(in resolved, out AobScanBounds moduleBounds))
			{
				bounds = default;
				failure = ModuleFailure(CheatEngineFailureKind.InvalidHostResult,
					"Cheat Engine reported a module that does not fit in the 64-bit address space.");
				return false;
			}

			start = moduleBounds.Start;
			stop = moduleBounds.Stop;
		}

		if (range is { } requested)
		{
			Address rangeStop = ToStop(requested.End, patternLength);
			if (start < requested.Start)
			{
				start = requested.Start;
			}

			if (rangeStop < stop)
			{
				stop = rangeStop;
			}
		}

		if (start < stop && stop.Value - start.Value >= PatternLength(patternLength) &&
			AobScanBounds.TryCreate(start, stop, out bounds))
		{
			failure = default;
			return true;
		}

		bounds = default;
		failure = Rejected(module.HasValue
			? range.HasValue
				? "The AOB range leaves no room for a whole match inside the requested module."
				: "The requested module is smaller than the AOB pattern."
			: "The AOB range leaves no room for a match below the top of the 64-bit address space.");
		return false;
	}

	/// <summary>
	///     The one scope rule of both routes: a module keeps a match only when all of its pattern bytes lie inside it, and a
	///     range keeps a match whose start lies in <c>[Start, End]</c> and whose last byte lies below the top of the address
	///     space (the bounded route cannot express a stop above it).
	/// </summary>
	/// <param name="address">A match start that Cheat Engine returned.</param>
	/// <param name="request">The validated request.</param>
	/// <param name="moduleRange">The resolved module, or <see cref="ModuleRange.None" />.</param>
	/// <returns><see langword="true" /> when the match belongs to the request.</returns>
	private static bool IsInsideRequest(Address address, AobScanRequest request, ModuleRange moduleRange)
	{
		ulong length = PatternLength(request.Pattern.ByteLength);
		return moduleRange.ContainsMatch(address, length) &&
			   (request.Range is not { } range ||
				(range.Contains(address) && address.Value <= ulong.MaxValue - length));
	}

	/// <summary>Returns the byte length of a match, at least one.</summary>
	private static ulong PatternLength(int patternLength)
	{
		return (ulong) Math.Max(patternLength, 1);
	}

	/// <summary>
	///     Converts the inclusive end of a range of match starts into the exclusive stop Cheat Engine needs: a match
	///     starting at <paramref name="end" /> ends before <c>end + patternLength</c>.
	/// </summary>
	/// <param name="end">The last allowed match start.</param>
	/// <param name="patternLength">The number of byte positions of the pattern (at least one).</param>
	/// <returns>
	///     <c>end + patternLength</c>, checked and saturated at the last address: at the top of the address space a match
	///     whose last byte is the last address cannot be expressed and is not reported by the bounded route.
	/// </returns>
	internal static Address ToStop(Address end, int patternLength)
	{
		ulong length = PatternLength(patternLength);
		return end.Value > ulong.MaxValue - length ? new Address(ulong.MaxValue) : new Address(end.Value + length);
	}

	/// <summary>Returns how many addresses either route copies: the request limit, capped by the Client.</summary>
	/// <param name="maximumResults">The requested materialization limit.</param>
	/// <returns><c>min(maximumResults, ScanResourceLimits.MaximumPatternMatches - 1)</c>.</returns>
	/// <remarks>
	///     The same cap on both routes keeps one request's answer independent of the route: a result cut by the cap is
	///     truncated on either route.
	/// </remarks>
	internal static int GetMaterializationLimit(int maximumResults)
	{
		return Math.Min(maximumResults, ScanResourceLimits.MaximumPatternMatches - 1);
	}

	/// <summary>Returns the bounded route's destination length: one more than the copy limit, to prove truncation.</summary>
	/// <param name="maximumResults">The requested materialization limit.</param>
	/// <returns><c>min(maximumResults + 1, ScanResourceLimits.MaximumPatternMatches)</c>.</returns>
	internal static int GetBoundedDestinationLength(int maximumResults)
	{
		return GetMaterializationLimit(maximumResults) + 1;
	}

	/// <summary>Validates, dispatches, and classifies one scan identically for every public entry point.</summary>
	private ScanOutcome Execute(AobScanRequest request, CancellationToken cancellationToken)
	{
		// An ended or stopping activation throws before a refusal is reported, never the reverse.
		_dispatcher.Lifetime.ThrowIfDispatchRefused(ScanOperation);
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
			_dispatcher.Lifetime.Diagnostics.PatternScanCompleted(metrics.Scope,
				(long) Math.Min(metrics.HostResultCount, long.MaxValue),
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

		if (!request.Module.HasValue && !request.Range.HasValue)
		{
			ScanOutcome global = ScanGlobal(request, ModuleRange.None, PatternScanScope.GlobalHostScan,
				cancellationToken);
			return global with
			{
				RouteReason = PatternScanRouteReason.UnscopedRequest
			};
		}

		ModuleInfo? module = null;
		if (request.Module.HasValue)
		{
			if (!TryGetModule(request.Module.Value, out ModuleInfo resolved, out CheatEngineFailure moduleFailure))
			{
				return ScanOutcome.Failed(moduleFailure, null);
			}

			module = resolved;
		}

		if (!TryCreateBounds(module, request.Range, request.Pattern.ByteLength, out AobScanBounds bounds,
				out CheatEngineFailure boundsFailure))
		{
			return ScanOutcome.Failed(boundsFailure, null);
		}

		// Module resolution is deliberately completed before any scan, whichever route runs.
		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledBeforeScan(), null);
		}

		TargetSelectionFacts selection;
		try
		{
			selection = _scanPort.ObserveSelection();
		}
		catch (Exception observationFault) when (SdkBoundary.IsSdkFault(observationFault))
		{
			return ScanOutcome.Failed(SdkBoundary.Translate(ScanOperation, observationFault,
				CheatEngineHostEffect.NotStarted, _dispatcher.Lifetime), null);
		}

		if (cancellationToken.IsCancellationRequested)
		{
			return ScanOutcome.Failed(CancelledBeforeScan(), null);
		}

		ModuleRange moduleRange = module is { ImageSize: { } size } found
			? new ModuleRange(found.BaseAddress.Value, size.Value)
			: ModuleRange.None;
		return selection.IsQualified
			? ScanWithinBounds(request, bounds, moduleRange, cancellationToken)
			: FallBack(request, moduleRange, cancellationToken);
	}

	/// <summary>Runs the global route for a module or range request whose bounded route cannot run.</summary>
	/// <remarks>
	///     The route reason says that the bounded route could not qualify the target, so the result is never reported as
	///     verified, even when the global scan's own before and after observations named one qualified incarnation (for
	///     example after Cheat Engine returned no MemScan on a qualified target): the two public values never contradict
	///     each other, and a consumer that gates on either one reaches the same conclusion.
	/// </remarks>
	private ScanOutcome FallBack(AobScanRequest request, ModuleRange moduleRange, CancellationToken cancellationToken)
	{
		ScanOutcome global =
			ScanGlobal(request, moduleRange, PatternScanScope.GlobalHostScanWithManagedFilter, cancellationToken);
		return global with
		{
			RouteReason = PatternScanRouteReason.TargetIdentityNotQualified,
			TargetIdentityVerified = false
		};
	}

	/// <summary>Runs the bounded route and falls back to the global route when the SDK says it cannot run.</summary>
	private ScanOutcome ScanWithinBounds(AobScanRequest request, AobScanBounds bounds, ModuleRange moduleRange,
		CancellationToken cancellationToken)
	{
		Address[] destination = new Address[GetBoundedDestinationLength(request.MaximumResults)];
		AobBoundedHostResult bounded;
		try
		{
			bounded = _scanPort.TryScanWithinBounds(request.Pattern.Value, bounds,
				AobScanMapping.ToSdkOptions(request.Protection, request.Alignment), destination, cancellationToken);
		}
		catch (Exception scanFault) when (SdkBoundary.IsSdkFault(scanFault))
		{
			// The SDK releases its session on every exit before a fault propagates; the scan may or may not have run.
			CheatEngineFailure translated =
				SdkBoundary.Translate(ScanOperation, scanFault, CheatEngineHostEffect.Unknown, _dispatcher.Lifetime);
			return ScanOutcome.Failed(translated, null) with
			{
				RouteReason = PatternScanRouteReason.ScopedRequestOnQualifiedTarget
			};
		}

		AobBoundedDisposition disposition =
			AobScanMapping.ClassifyBounded(ScanOperation, bounded, out CheatEngineFailure failure);
		bool released = AobScanMapping.IsSessionReleaseConfirmed(bounded, out LeaseReleaseKind releaseKind);
		if (disposition == AobBoundedDisposition.FallBack && released)
		{
			if (cancellationToken.IsCancellationRequested)
			{
				// No global scan ran: the outcome reports the bounded attempt, the only route that did.
				CheatEngineFailure cancelled =
					bounded.HostScanElapsed > TimeSpan.Zero ? CancelledAfterScan() : CancelledBeforeScan();
				return ScanOutcome.Failed(cancelled, null) with
				{
					HostOutcome = AobScanMapping.ToHostOutcome(bounded.Kind),
					RouteReason = PatternScanRouteReason.ScopedRequestOnQualifiedTarget
				};
			}

			return WithPriorHostScan(FallBack(request, moduleRange, cancellationToken), bounded.HostScanElapsed);
		}

		// A failure after the SDK read the host count keeps the metrics of the work that happened.
		PatternScanMetrics? failureMetrics = BoundedFailureMetrics(bounded);
		ScanOutcome outcome = disposition == AobBoundedDisposition.Publish
			? Publish(bounded, destination, request, moduleRange, cancellationToken)
			: ScanOutcome.Failed(failure, failureMetrics);
		ScanOutcome final = released
			? outcome
			: ScanOutcome.Failed(CreateReleaseFailure(outcome.Succeeded ? null : outcome.Failure, SessionSubject,
				releaseKind, null), outcome.Metrics);

		// The SDK checks the session's target incarnation throughout: a published result is attributed to it.
		return final with
		{
			HostOutcome = AobScanMapping.ToHostOutcome(bounded.Kind),
			RouteReason = PatternScanRouteReason.ScopedRequestOnQualifiedTarget,
			TargetIdentityVerified = final.Succeeded
		};
	}

	/// <summary>Publishes the in-bounds addresses the SDK copied, after the Client's own scope check.</summary>
	/// <remarks>
	///     The SDK already dropped every address below the start or at or after the stop, but it tests the match start
	///     only: a match that straddles the module end is excluded only because Cheat Engine honours its stop bound (a
	///     host observation). <see cref="IsInsideRequest" />, the rule of the global route too, makes that exclusion a
	///     Client guarantee; an address it drops is counted as filtered out. The destination holds one more address than
	///     the limit, so a full destination proves truncation.
	/// </remarks>
	private static ScanOutcome Publish(AobBoundedHostResult bounded, Address[] destination, AobScanRequest request,
		ModuleRange moduleRange, CancellationToken cancellationToken)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			// The SDK had read the count and copied the rows: the metrics describe that work, nothing is published.
			return ScanOutcome.Failed(CancelledAfterScan(), BoundedFailureMetrics(bounded));
		}

		if ((uint) bounded.Written > (uint) destination.Length)
		{
			return ScanOutcome.Failed(new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
				"The bounded AOB scan reported more addresses than its destination holds.", null,
				CheatEngineHostEffect.Completed), null);
		}

		long filterStarted = Stopwatch.GetTimestamp();
		int limit = destination.Length - 1;
		ImmutableArray<Address>.Builder copied = ImmutableArray.CreateBuilder<Address>(Math.Min(bounded.Written, limit));
		int survivors = 0;
		int dropped = 0;
		for (int index = 0; index < bounded.Written; index++)
		{
			Address address = destination[index];
			if (!IsInsideRequest(address, request, moduleRange))
			{
				dropped++;
				continue;
			}

			survivors++;
			if (copied.Count < limit)
			{
				copied.Add(address);
			}
		}

		TimeSpan materializationElapsed = bounded.CopyElapsed + Stopwatch.GetElapsedTime(filterStarted);
		if (BoundedMetrics(bounded, dropped, copied.Count, materializationElapsed, true) is not { } metrics)
		{
			return ScanOutcome.Failed(new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
				"The bounded AOB scan reported inconsistent counts.", null, CheatEngineHostEffect.Completed), null);
		}

		bool truncated = survivors > limit;
		if (!truncated && bounded.IsMaterializationLimitReached && dropped > 0)
		{
			// A full destination whose addresses the Client dropped leaves unread rows unknown.
			if (survivors == 0)
			{
				return ScanOutcome.Failed(new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult,
					ScanOperation, "The bounded AOB scan filled its destination with addresses outside the request, " +
								   "so whether an in-range match exists is unknown.", null,
					CheatEngineHostEffect.Completed), metrics);
			}

			truncated = true;
		}

		return ScanOutcome.Success(new AobScanResult(copied.ToImmutable(), truncated)) with
		{
			Metrics = metrics
		};
	}

	/// <summary>
	///     Returns the metrics of a bounded scan that published nothing, when the SDK read its host count; otherwise, or
	///     when its counts contradict each other, <see langword="null" />.
	/// </summary>
	private static PatternScanMetrics? BoundedFailureMetrics(AobBoundedHostResult bounded)
	{
		return AobScanMapping.HasReadCount(bounded) ? BoundedMetrics(bounded, 0, 0, bounded.CopyElapsed, false) : null;
	}

	/// <summary>
	///     Adds the Cheat Engine time of a bounded scan that ran before its fallback to the fallback's metrics: the
	///     request cost both scans.
	/// </summary>
	private static ScanOutcome WithPriorHostScan(ScanOutcome fallback, TimeSpan priorHostScan)
	{
		if (priorHostScan <= TimeSpan.Zero || fallback.Metrics is not { } metrics)
		{
			return fallback;
		}

		return fallback with
		{
			Metrics = new PatternScanMetrics(metrics.Scope, metrics.HostResultCount, metrics.ExaminedCount,
				metrics.FilteredOutCount, metrics.MaterializedCount, metrics.BelowStartSkippedCount,
				metrics.AtOrAfterStopSkippedCount, metrics.UnreadHostRowCount, metrics.InBoundsCountIsExact,
				metrics.HostScanElapsed + priorHostScan, metrics.MaterializationElapsed)
		};
	}

	/// <summary>Builds the metrics of a bounded scan, or <see langword="null" /> when its counts contradict each other.</summary>
	/// <remarks>
	///     The SDK's skipped rows and the rows the Client's own checks dropped are the filtered-out rows. The in-request
	///     count is exact only for a published result whose rows were all read.
	/// </remarks>
	private static PatternScanMetrics? BoundedMetrics(AobBoundedHostResult bounded, int dropped, int materialized,
		TimeSpan materializationElapsed, bool published)
	{
		ulong skipped = bounded.BelowStartSkipped + bounded.AtOrAfterStopSkipped;
		ulong filtered = skipped + (ulong) dropped;
		if (skipped < bounded.BelowStartSkipped || filtered < skipped || bounded.RowsRead > bounded.HostResultCount ||
			bounded.UnreadHostRows != bounded.HostResultCount - bounded.RowsRead || filtered > bounded.RowsRead ||
			(ulong) materialized > bounded.RowsRead - filtered || bounded.HostScanElapsed < TimeSpan.Zero ||
			materializationElapsed < TimeSpan.Zero)
		{
			return null;
		}

		return new PatternScanMetrics(PatternScanScope.HostBoundedRange, bounded.HostResultCount, bounded.RowsRead,
			filtered, materialized, bounded.BelowStartSkipped, bounded.AtOrAfterStopSkipped, bounded.UnreadHostRows,
			published && bounded.UnreadHostRows == 0, bounded.HostScanElapsed, materializationElapsed);
	}

	/// <summary>Runs one global <c>AOBScan</c> and copies its post-filtered addresses.</summary>
	private ScanOutcome ScanGlobal(AobScanRequest request, ModuleRange moduleRange, PatternScanScope scope,
		CancellationToken cancellationToken)
	{
		long hostScanStarted = Stopwatch.GetTimestamp();
		AobHostOutcome host;
		IAobMatchList? matchList;
		try
		{
			host = _scanPort.TryScan(request.Pattern.Value,
				AobScanMapping.ToSdkOptions(request.Protection, request.Alignment), out matchList);
		}
		catch (OwnershipHandoffException handoff)
		{
			// CE's scan returned a list that the port could not publish, and the list's release was not confirmed: the
			// publication fault keeps its classification, and the unconfirmed release makes it CleanupUnconfirmed.
			return ScanOutcome.Failed(
				OwnershipHandoff.ToFailure(ScanOperation, handoff, ListSubject, _dispatcher.Lifetime), null);
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
			return Attribute(ScanOutcome.Failed(ClassifyWithoutList(host), null), host);
		}

		// From here on this method is the single release authority for the owned list: every path below releases it
		// exactly once, on this dispatched callback, and a release that is not confirmed is never hidden behind a success.
		ScanOutcome outcome;
		try
		{
			outcome = Consume(matchList, host, request, new GlobalCopy(moduleRange, scope, hostScanElapsed),
				cancellationToken);
		}
		catch (Exception copyFault) when (SdkBoundary.IsSdkFault(copyFault))
		{
			// A fault while reading the SDK-owned list: CE's scan had returned, so no scan work is outstanding. The fault
			// is classified like every other SDK fault, including the external-reset rule.
			outcome = ScanOutcome.Failed(
				SdkBoundary.Classify(ScanOperation, copyFault, CheatEngineHostEffect.Completed), null);
			ScanOutcome released = Release(matchList, outcome);
			SdkBoundary.ThrowIfActivationEnded(ScanOperation, copyFault, _dispatcher.Lifetime);
			return Attribute(released, host);
		}
		catch (Exception)
		{
			// A lifecycle fault propagates unchanged; the list is still released once, on this callback.
			_ = ReleaseList(matchList, out _);
			throw;
		}

		return Attribute(Release(matchList, outcome), host);
	}

	/// <summary>
	///     Records the host's own outcome of a global scan, and whether its addresses are attributed to one qualified
	///     incarnation: only a success whose selection was the same qualified incarnation before and after the call.
	/// </summary>
	private static ScanOutcome Attribute(ScanOutcome outcome, AobHostOutcome host)
	{
		return outcome with
		{
			HostOutcome = AobScanMapping.ToHostOutcome(host.Kind),
			TargetIdentityVerified = outcome.Succeeded && host.IsSameQualifiedIncarnation
		};
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
		GlobalCopy copy, CancellationToken cancellationToken)
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
		ScanOutcome outcome = Materialize(matchList, request, copy.ModuleRange, cancellationToken, ref progress);
		PatternScanMetrics metrics = progress.ToMetrics(copy.Scope, copy.HostScanElapsed,
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
		int limit = GetMaterializationLimit(request.MaximumResults);
		ImmutableArray<Address>.Builder materialized = ImmutableArray.CreateBuilder<Address>();
		for (int index = 0; index < progress.HostResultCount; index++)
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
			if (!IsInsideRequest(address, request, moduleRange))
			{
				progress.FilteredOut++;
				continue;
			}

			if (materialized.Count == limit)
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
				CreateReleaseFailure(outcome.Succeeded ? null : outcome.Failure, ListSubject, released, releaseFault),
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

	private static CheatEngineFailure CreateReleaseFailure(CheatEngineFailure? primaryFailure, string subject,
		LeaseReleaseKind released, Exception? releaseFault)
	{
		if (primaryFailure is not { } primary)
		{
			// An unconfirmed host release is an indeterminate host result, never a Client state.
			return new CheatEngineFailure(CheatEngineFailureKind.IndeterminateHostResult, ScanOperation,
				$"The {subject} release was not confirmed ({released}); copied results were discarded.",
				releaseFault, CheatEngineHostEffect.CleanupUnconfirmed);
		}

		return OwnershipHandoff.WithUnconfirmedRelease(primary, subject, released, releaseFault);
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

	private bool TryGetModule(ModuleName requested, out ModuleInfo module, out CheatEngineFailure failure)
	{
		ModuleInfo[] modules = new ModuleInfo[MaximumModuleSnapshot];
		module = default;
		InspectionStatus status;
		int written;
		try
		{
			status = _scanPort.EnumerateModules(modules, out written);
		}
		catch (Exception inspectionFault) when (SdkBoundary.IsSdkFault(inspectionFault))
		{
			// Module inspection failed before the AOB scan was started.
			failure = SdkBoundary.Translate(ScanOperation, inspectionFault, CheatEngineHostEffect.NotStarted,
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
			ModuleInfo candidate = modules[index];
			if (!string.Equals(candidate.Name, requested.Value, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (found)
			{
				failure = ModuleFailure(CheatEngineFailureKind.AmbiguousMatch,
					$"More than one module named '{requested.Value}' was present in the selected target.");
				return false;
			}

			if (!candidate.ImageSize.HasValue)
			{
				failure = ModuleFailure(CheatEngineFailureKind.CapabilityUnavailable,
					"Cheat Engine did not report the requested module's image size.");
				return false;
			}

			if (candidate.ImageSize.Value.Value == 0)
			{
				failure = ModuleFailure(CheatEngineFailureKind.InvalidHostResult,
					"Cheat Engine reported a zero-length requested module.");
				return false;
			}

			found = true;
			module = candidate;
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
		return new CheatEngineFailure(kind, ScanOperation, message, null, CheatEngineHostEffect.NotStarted);
	}

	/// <summary>What the global route's copy needs besides the list: its filter, its scope and the host scan time.</summary>
	private readonly record struct GlobalCopy(ModuleRange ModuleRange, PatternScanScope Scope, TimeSpan HostScanElapsed);

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
		/// <summary>Gets what the host reported for the scan that ran; unknown when none ran.</summary>
		internal PatternScanHostOutcomeKind HostOutcome
		{
			get;
			init;
		}

		/// <summary>Gets why the scan ran on its route; unknown when none ran.</summary>
		internal PatternScanRouteReason RouteReason
		{
			get;
			init;
		}

		/// <summary>Gets whether a published result is attributed to one qualified target incarnation.</summary>
		internal bool TargetIdentityVerified
		{
			get;
			init;
		}

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
	private struct MaterializationProgress(int hostResultCount)
	{
		internal readonly int HostResultCount = hostResultCount;
		internal int Examined;
		internal int FilteredOut;
		internal int Materialized;

		internal readonly PatternScanMetrics ToMetrics(PatternScanScope scope, TimeSpan hostScanElapsed,
			TimeSpan materializationElapsed)
		{
			// The global route reads every row it examines; the in-request count is exact once all rows were read.
			return new PatternScanMetrics(scope, (ulong) HostResultCount, (ulong) Examined, (ulong) FilteredOut,
				Materialized, 0, 0, (ulong) (HostResultCount - Examined), Examined == HostResultCount, hostScanElapsed,
				materializationElapsed);
		}
	}

	private readonly record struct ModuleRange(bool IsActive, ulong Start, ulong Size)
	{
		internal static ModuleRange None => default;

		internal ModuleRange(ulong start, ulong size)
			: this(true, start, size)
		{
		}

		/// <summary>Gets whether a match of <paramref name="length" /> bytes starting at the address lies entirely inside.</summary>
		/// <param name="address">The match start.</param>
		/// <param name="length">The match length in bytes, at least one.</param>
		/// <returns><see langword="true" /> without a module, or when <c>[address, address + length)</c> is inside it.</returns>
		internal bool ContainsMatch(Address address, ulong length)
		{
			return !IsActive || (length <= Size && address.Value >= Start && address.Value - Start <= Size - length);
		}
	}
}
