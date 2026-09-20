using System.Collections.Immutable;

using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Scanning;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Core.Domains;

internal sealed class PatternScanner(SdkMainThreadDispatcher dispatcher) : IPatternScanner
{
	private const int MaximumModuleSnapshot = 4096;
	private const string InModuleOperation = "Patterns.InModule";
	private const string ScanOperation = "Patterns.Scan";

	private readonly SdkMainThreadDispatcher _dispatcher =
		dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

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
		if (!_dispatcher.TryInvoke(() => succeeded = TryScanCore(request, out captured, out hostFailure),
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

	private static bool TryScanCore(AobScanRequest request, out AobScanResult result,
		out CheatEngineFailure failure)
	{
		if (!AobScanner.TryScan(request.Pattern.Value, request.Options, out Owned<StringList>? owner))
		{
			result = default;
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation,
				"Cheat Engine did not return an AOB result list.");
			return false;
		}

		using (owner)
		{
			StringList list = owner.Value;
			if (!TryGetResultCount(list, out int count, out failure))
			{
				result = default;
				return false;
			}

			bool hasModuleRange = false;
			ModuleRange moduleRange = default;
			if (request.Module.HasValue &&
			    !TryGetModuleRange(request.Module.Value, out hasModuleRange, out moduleRange,
				    out failure))
			{
				result = default;
				return false;
			}

			return TryMaterializeMatches(list, count, request, hasModuleRange, moduleRange, out result, out failure);
		}
	}

	private static bool TryGetResultCount(StringList list, out int count, out CheatEngineFailure failure)
	{
		if (list.TryGetCount(out count) && count >= 0)
		{
			failure = default;
			return true;
		}

		failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
			"Cheat Engine returned an invalid AOB result count.");
		return false;
	}

	private static bool TryMaterializeMatches(
		StringList list,
		int count,
		AobScanRequest request,
		bool hasModuleRange,
		ModuleRange moduleRange,
		out AobScanResult result,
		out CheatEngineFailure failure)
	{
		// Do not preallocate to a caller-controlled materialization limit. The limit remains strict below, while
		// storage grows only for addresses that survived every managed filter.
		ImmutableArray<Address>.Builder matches = ImmutableArray.CreateBuilder<Address>();
		for (int index = 0; index < count; index++)
		{
			if (!TryGetMatchAddress(list, index, out Address address, out failure))
			{
				result = default;
				return false;
			}

			if (!IsIncluded(address, request, hasModuleRange, moduleRange))
			{
				continue;
			}

			if (matches.Count == request.MaximumResults)
			{
				result = new AobScanResult(matches.ToImmutable(), true);
				failure = default;
				return true;
			}

			matches.Add(address);
		}

		result = new AobScanResult(matches.ToImmutable(), false);
		failure = default;
		return true;
	}

	private static bool TryGetMatchAddress(StringList list, int index, out Address address,
		out CheatEngineFailure failure)
	{
		if (list.TryGetItem(index, out string? text) && Address.TryParse(text, out address))
		{
			failure = default;
			return true;
		}

		address = default;
		failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, ScanOperation,
			$"AOB result {index} was not a hexadecimal address.");
		return false;
	}

	private static bool IsIncluded(Address address, AobScanRequest request, bool hasModuleRange, ModuleRange moduleRange)
	{
		return (!hasModuleRange || moduleRange.Contains(address)) &&
		       (!request.Range.HasValue || request.Range.Value.Contains(address));
	}

	internal static bool TryValidateRequest(AobScanRequest request, out CheatEngineFailure failure)
	{
		if (string.IsNullOrWhiteSpace(request.Pattern.Value))
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation,
				"An AOB scan requires a normalized, non-empty pattern.");
			return false;
		}

		if (request.MaximumResults <= 0)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation,
				"An AOB scan requires a positive materialization limit.");
			return false;
		}

		if (request.Module.HasValue && string.IsNullOrWhiteSpace(request.Module.Value.Value))
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation,
				"An AOB module filter must be non-empty.");
			return false;
		}

		if (request.Range.HasValue && request.Range.Value.End < request.Range.Value.Start)
		{
			failure = new CheatEngineFailure(CheatEngineFailureKind.OperationRejected, ScanOperation,
				"An AOB range end address must not precede its start address.");
			return false;
		}

		failure = default;
		return true;
	}

	private static bool TryGetModuleRange(ModuleName requested, out bool hasRange, out ModuleRange range,
		out CheatEngineFailure failure)
	{
		ModuleInfo[] modules = new ModuleInfo[MaximumModuleSnapshot];
		InspectionStatus status = EngineInspection.EnumerateModules(modules, out int written);
		if (status != InspectionStatus.Success)
		{
			hasRange = false;
			range = default;
			failure = new CheatEngineFailure(
				status == InspectionStatus.DestinationTooSmall
					? CheatEngineFailureKind.ResultLimitExceeded
					: CheatEngineFailureKind.CapabilityUnavailable,
				InModuleOperation, $"Module inspection returned '{status}'.");
			return false;
		}

		bool found = false;
		range = default;
		for (int index = 0; index < written; index++)
		{
			ModuleInfo module = modules[index];
			if (!string.Equals(module.Name, requested.Value, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (found)
			{
				hasRange = false;
				failure = new CheatEngineFailure(CheatEngineFailureKind.AmbiguousMatch, InModuleOperation,
					$"More than one module named '{requested.Value}' was present in the selected target.");
				return false;
			}

			if (!module.ImageSize.HasValue)
			{
				hasRange = false;
				failure = new CheatEngineFailure(CheatEngineFailureKind.CapabilityUnavailable,
					InModuleOperation, "Cheat Engine did not report the requested module's image size.");
				return false;
			}

			if (module.ImageSize.Value.Value == 0)
			{
				hasRange = false;
				failure = new CheatEngineFailure(CheatEngineFailureKind.InvalidHostResult, InModuleOperation,
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

		hasRange = false;
		failure = new CheatEngineFailure(CheatEngineFailureKind.NotFound, InModuleOperation,
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
