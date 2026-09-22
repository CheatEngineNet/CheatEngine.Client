using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class PatternScanner(SdkMainThreadDispatcher dispatcher, IAobScanPort? scanPort = null)
	: IPatternScanner
{
	private const int _maximumModuleSnapshot = 4096;
	private const string _inModuleOperation = "Patterns.InModule";
	private const string _scanOperation = "Patterns.Scan";

	private readonly SdkMainThreadDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

	private readonly IAobScanPort _scanPort = scanPort ?? new SdkAobScanPort();

	public bool TryScan(AobScanRequest request, out AobScanResult result, out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (!TryValidateRequest(request, out failure))
		{
			result = default;
			return false;
		}

		AobScanResult captured = default;
		CheatEngineFailure hostFailure = default;
		bool succeeded = false;
		if (!_dispatcher.TryInvoke(
				() => succeeded = TryScanCore(request, cancellationToken, out captured, out hostFailure),
				out failure, cancellationToken))
		{
			result = default;
			return false;
		}

		if (!succeeded)
		{
			result = default;
			failure = hostFailure;
			return false;
		}

		result = captured;
		failure = default;
		return true;
	}

	public AobScanResult Scan(AobScanRequest request, CancellationToken cancellationToken = default)
	{
		if (TryScan(request, out AobScanResult result, out CheatEngineFailure failure, cancellationToken))
		{
			return result;
		}

		failure.Throw();
		return default;
	}

	private bool TryScanCore(AobScanRequest request, CancellationToken cancellationToken, out AobScanResult result,
		out CheatEngineFailure failure)
	{
		if (TryGetCancellationFailure(cancellationToken, out failure))
		{
			result = default;
			return false;
		}

		bool hasModuleRange = false;
		ModuleRange moduleRange = default;
		if (request.Module.HasValue &&
			!TryGetModuleRange(request.Module.Value, out hasModuleRange, out moduleRange, out failure))
		{
			result = default;
			return false;
		}

		// Module resolution is deliberately completed before the unbounded CE AOB scan. The range still acts as a
		// managed post-filter because the SDK AOB binding does not accept a module constraint.
		if (TryGetCancellationFailure(cancellationToken, out failure))
		{
			result = default;
			return false;
		}

		AobScanHostStatus status =
			_scanPort.TryScan(request.Pattern.Value, request.Options, out IAobMatchList? matchList);
		if (TryGetCancellationFailure(cancellationToken, out failure))
		{
			matchList?.Dispose();
			result = default;
			return false;
		}

		if (status == AobScanHostStatus.Rejected)
		{
			result = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, _scanOperation,
				"Cheat Engine did not return an AOB result list.");
			return false;
		}

		if (status != AobScanHostStatus.Success || matchList is null)
		{
			result = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, _scanOperation,
				"Cheat Engine returned an invalid AOB result list.");
			return false;
		}

		using (matchList)
		{
			if (TryGetCancellationFailure(cancellationToken, out failure))
			{
				result = default;
				return false;
			}

			if (!matchList.TryGetCount(out int count) || count < 0)
			{
				result = default;
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, _scanOperation,
					"Cheat Engine returned an invalid AOB result count.");
				return false;
			}

			return TryMaterializeMatches(matchList, count, request, hasModuleRange, moduleRange, cancellationToken,
				out result, out failure);
		}
	}

	private static bool TryMaterializeMatches(
		IAobMatchList matches,
		int count,
		AobScanRequest request,
		bool hasModuleRange,
		ModuleRange moduleRange,
		CancellationToken cancellationToken,
		out AobScanResult result,
		out CheatEngineFailure failure)
	{
		// Do not preallocate to a caller-controlled materialization limit. The limit remains strict below, while
		// storage grows only for addresses that survived every managed filter.
		ImmutableArray<Address>.Builder materialized = ImmutableArray.CreateBuilder<Address>();
		for (int index = 0; index < count; index++)
		{
			if (TryGetCancellationFailure(cancellationToken, out failure))
			{
				result = default;
				return false;
			}

			if (!TryGetMatchAddress(matches, index, out Address address, out failure))
			{
				result = default;
				return false;
			}

			if (TryGetCancellationFailure(cancellationToken, out failure))
			{
				result = default;
				return false;
			}

			if (!IsIncluded(address, request, hasModuleRange, moduleRange))
			{
				continue;
			}

			if (materialized.Count == request.MaximumResults)
			{
				result = new AobScanResult(materialized.ToImmutable(), true);
				failure = default;
				return true;
			}

			materialized.Add(address);
		}

		if (TryGetCancellationFailure(cancellationToken, out failure))
		{
			result = default;
			return false;
		}

		result = new AobScanResult(materialized.ToImmutable(), false);
		failure = default;
		return true;
	}

	private static bool TryGetMatchAddress(IAobMatchList matches, int index, out Address address,
		out CheatEngineFailure failure)
	{
		if (matches.TryGetItem(index, out string? text) && Address.TryParse(text, out address))
		{
			failure = default;
			return true;
		}

		address = default;
		failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, _scanOperation,
			$"AOB result {index} was not a hexadecimal address.");
		return false;
	}

	private static bool TryGetCancellationFailure(CancellationToken cancellationToken, out CheatEngineFailure failure)
	{
		if (!cancellationToken.IsCancellationRequested)
		{
			failure = default;
			return false;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.Cancelled, _scanOperation,
			"The AOB scan was cancelled.");
		return true;
	}

	private static bool IsIncluded(Address address, AobScanRequest request, bool hasModuleRange,
		ModuleRange moduleRange)
	{
		return (!hasModuleRange || moduleRange.Contains(address)) &&
			   (!request.Range.HasValue || request.Range.Value.Contains(address));
	}

	internal static bool TryValidateRequest(AobScanRequest request, out CheatEngineFailure failure)
	{
		if (string.IsNullOrWhiteSpace(request.Pattern.Value))
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, _scanOperation,
				"An AOB scan requires a normalized, non-empty pattern.");
			return false;
		}

		if (request.MaximumResults <= 0)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, _scanOperation,
				"An AOB scan requires a positive materialization limit.");
			return false;
		}

		if (request.Module.HasValue && string.IsNullOrWhiteSpace(request.Module.Value.Value))
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, _scanOperation,
				"An AOB module filter must be non-empty.");
			return false;
		}

		if (request.Range.HasValue && request.Range.Value.End < request.Range.Value.Start)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, _scanOperation,
				"An AOB range end address must not precede its start address.");
			return false;
		}

		failure = default;
		return true;
	}

	private bool TryGetModuleRange(ModuleName requested, out bool hasRange, out ModuleRange range,
		out CheatEngineFailure failure)
	{
		ModuleInfo[] modules = new ModuleInfo[_maximumModuleSnapshot];
		hasRange = false;
		range = default;
		InspectionStatus status = _scanPort.EnumerateModules(modules, out int written);
		if (status != InspectionStatus.Success)
		{
			failure = new CheatEngineFailure(
				status == InspectionStatus.DestinationTooSmall
					? CheatEngineFailureKind.ResultLimitExceeded
					: CheatEngineFailureKind.CapabilityUnavailable,
				_inModuleOperation, $"Module inspection returned '{status}'.");
			return false;
		}

		if ((uint) written > modules.Length)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, _inModuleOperation,
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
				failure = new CheatEngineFailure(CheatEngineFailureKind.AmbiguousMatch, _inModuleOperation,
					$"More than one module named '{requested.Value}' was present in the selected target.");
				return false;
			}

			if (!module.ImageSize.HasValue)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable,
					_inModuleOperation, "Cheat Engine did not report the requested module's image size.");
				return false;
			}

			if (module.ImageSize.Value.Value == 0)
			{
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, _inModuleOperation,
					"Cheat Engine reported a zero-length requested module.");
				return false;
			}

			found = true;
			range = new ModuleRange(module.BaseAddress.Value, module.ImageSize.Value.Value);
		}

		if (found)
		{
			hasRange = true;
			failure = default;
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.NotFound, _inModuleOperation,
			$"Module '{requested.Value}' was not present in the selected target.");
		return false;
	}

	private readonly record struct ModuleRange(ulong Start, ulong Size)
	{
		internal bool Contains(Address address)
		{
			return address.Value >= Start && address.Value - Start < Size;
		}
	}
}
