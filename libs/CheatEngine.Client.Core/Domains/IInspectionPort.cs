using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal adapter boundary for the protected SDK inspection primitives.</summary>
internal interface IInspectionPort
{
	public InspectionStatus EnumerateModules(ModuleInfo[] destination, out int written);

	public InspectionStatus EnumerateModules(TargetProcessId processId, ModuleInfo[] destination, out int written);

	public InspectionStatus EnumerateSections(ModuleName moduleName, ModuleSectionInfo[] destination, out int written);

	public InspectionStatus EnumerateMemoryRegions(MemoryRegionInfo[] destination, out int written);

	public InspectionStatus GetMemoryRegion(Address address, out MemoryRegionInfo region);

	public InspectionStatus GetSymbol(SymbolExpression expression, out SymbolInfo symbol);

	public InspectionStatus ResolveAddress(SymbolExpression expression, AddressResolutionMode mode,
		out Address address);

	/// <summary>Gets Cheat Engine's formatted name for a target address (<c>SymbolRegistry.TryGetName</c>).</summary>
	public LuaOperationStatus TryGetName(Address address, out string? name);

	/// <summary>
	///     Registers a name through CheatEngine.SDK's symbol ownership coordinator (<c>SymbolRegistry.TryRegisterOwned</c>).
	/// </summary>
	/// <exception cref="SymbolRegistrationHandoffException">
	///     Cheat Engine registered the name but the SDK could not publish its lease; the SDK compensated once.
	/// </exception>
	public SymbolRegistrationAttempt TryRegisterOwned(SymbolName name, Address address,
		SymbolRegistrationOptions options);
}
