using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

using SdkSymbolRegistrationLease = CheatEngine.SDK.Engine.Inspection.SymbolRegistrationLease;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production adapter that delegates inspection and symbol operations to CheatEngine.SDK.</summary>
internal sealed class SdkInspectionPort : IInspectionPort
{
	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateModules(destination, out written);
	}

	public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateModules(processId, destination, out written);
	}

	public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateSections(moduleName, destination, out written);
	}

	public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written)
	{
		return EngineInspection.EnumerateMemoryRegions(destination, out written);
	}

	public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region)
	{
		return EngineInspection.GetMemoryRegionInfo(address, out region);
	}

	public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol)
	{
		return EngineInspection.GetSymbolInfo(expression, out symbol);
	}

	public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		out Address address)
	{
		return EngineInspection.ResolveAddress(expression, InspectionMapping.ToSdkResolutionOptions(mode),
			out address);
	}

	public LuaOperationStatus TryGetName(Address address, out string? name)
	{
		return SymbolRegistry.TryGetName(address, out name);
	}

	public SymbolRegistrationAttempt TryRegisterOwned(SymbolName name, Address address,
		SymbolRegistrationOptions options)
	{
		SymbolRegistrationAcquireOutcome outcome = SymbolRegistry.TryRegisterOwned(name, address, options);
		return new SymbolRegistrationAttempt(outcome.Status,
			outcome.Lease is { } lease ? new SdkSymbolRegistrationHandle(lease) : null);
	}

	/// <summary>Adapts the SDK's symbol registration lease, which has no public constructor, to the Core handle.</summary>
	private sealed class SdkSymbolRegistrationHandle(SdkSymbolRegistrationLease lease) : ISymbolRegistrationHandle
	{
		public SymbolRegistrationReleaseKind Release()
		{
			return lease.Release().Kind;
		}
	}
}
