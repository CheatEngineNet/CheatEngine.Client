using CheatEngine.SDK.Annotations.Lua;
using CheatEngine.SDK.Engine.AddressList;
using CheatEngine.SDK.Engine.Enums;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Memory;
using CheatEngine.SDK.Engine.Objects;
using CheatEngine.SDK.Engine.Processes;
using CheatEngine.SDK.Engine.Runtime;
using CheatEngine.SDK.Engine.Scanning.Aob;
using CheatEngine.SDK.Engine.Scanning.Values;
using CheatEngine.SDK.Engine.Targets;
using CheatEngine.SDK.Engine.Values;
using CheatEngine.SDK.Hosting.Bootstrap;
using CheatEngine.SDK.Hosting.Context;
using CheatEngine.SDK.Hosting.Plugin;
using CheatEngine.SDK.Hosting.Threading;
using CheatEngine.SDK.Lua.Calls;
using CheatEngine.SDK.Lua.Marshalling;
using CheatEngine.SDK.Lua.References;
using CheatEngine.SDK.Lua.Runtime;
using CheatEngine.SDK.Lua.State;

namespace CheatEngine.Client.Tests.SdkContract;

/// <summary>
///     Compile-only map of every member of the consumed CheatEngine.SDK (the pin of <c>eng/CheatEngineSdk.props</c>)
///     that the shipped Client assemblies consume (Q48).
/// </summary>
/// <remarks>
///     <para>
///         <b>Never call these methods.</b> They exist so that a candidate SDK package that renames, removes, or changes the
///         signature of a consumed member fails to <em>compile</em> here, at an exact location, before any Client package
///         is published. <c>SdkConsumerContractTests.SdkApiUsageCoversEveryConsumedSdkMember</c> proves that this map covers
///         the committed inventory in <see cref="ConsumedSdkSurface" />.
///     </para>
///     <para>
///         Excluded: <c>CheatEngine.SDK.Lua.CompilerServices.*</c> helpers. They are called only by code that the SDK
///         LuaBindings generator emits into Client.Core (<c>ClientLuaGlobals</c>), so a change to them is caught by
///         compiling Client.Core against the candidate package; they remain in the inventory.
///     </para>
/// </remarks>
internal static class SdkApiUsage
{
	internal static void AddressListSurface(AddressList list, MemoryRecord record, MemoryRecordId id,
		MemoryRecordId otherId, LuaState state)
	{
		_ = list.TryCreateMemoryRecord(out MemoryRecord created);
		_ = list.TryGetCount(out int count);
		_ = list.TryGetMemoryRecord(count, out _);
		_ = list.TryGetMemoryRecordById(id, out _);
		_ = list.TryGetSelectedRecord(out _);
		_ = list.TrySetSelectedRecord(created);
		_ = AddressListAccess.TryGetCurrent(out _);
		_ = record.TryGetAddressExpression(out _);
		_ = record.TryGetChild(0, out _);
		_ = record.TryGetCurrentAddress(out _);
		_ = record.TryGetDescription(out _);
		_ = record.TryGetId(out _);
		_ = record.TryGetIndex(out _);
		_ = record.TryGetValue(out _);
		_ = record.TryGetVariableType(out _);
		_ = MemoryRecord.TryRead(state, -1, out _);
		_ = record.TrySetAddressExpression("expression");
		_ = record.TrySetDescription("description");
		_ = record.TrySetValue("value");
		_ = record.TrySetVariableType(VariableType.Dword);
		_ = record.Handle;
		_ = MemoryRecord.Null;
		_ = id == otherId;
	}

	internal static void ObjectSurface(CEObject handle, Owned<StringList> owner, StringList list, LuaState state)
	{
		_ = handle.TryCallMethod("destroy"u8);
		_ = handle.TryGetProperty(state, "Parent"u8);
		_ = handle.TryGetProperty<BooleanMarshaller, bool>("Active"u8, out _);
		_ = handle.TrySetProperty<BooleanMarshaller, bool>("Active"u8, true);
		_ = owner.Value;
		owner.Dispose();
		_ = list.TryGetCount(out _);
		_ = list.TryGetItem(0, out _);
	}

	internal static void InspectionSurface(Address address, ModuleName moduleName, SymbolExpression expression,
		AddressResolutionOptions options, ModuleInfo module, MemorySize size, TargetProcessId processId,
		TargetProcessId otherProcessId)
	{
		ModuleInfo[] modules = [];
		_ = EngineInspection.EnumerateMemoryRegions(Array.Empty<MemoryRegionInfo>(), out _);
		_ = EngineInspection.EnumerateModules(processId, modules, out _);
		_ = EngineInspection.EnumerateModules(modules, out _);
		_ = EngineInspection.EnumerateSections(moduleName, Array.Empty<ModuleSectionInfo>(), out _);
		_ = EngineInspection.GetMemoryRegionInfo(address, out _);
		_ = EngineInspection.GetSymbolInfo(expression, out _);
		_ = EngineInspection.ResolveAddress(expression, options, out _);
		_ = size.Value;
		_ = module.BaseAddress;
		_ = module.ImageSize;
		_ = module.Name;
		_ = moduleName.Value;
		_ = new ModuleName("module");
		_ = new SymbolExpression("symbol");
		_ = new TargetProcessId(1);
		_ = processId.Value;
		_ = processId != otherProcessId;
	}

	internal static void MemorySurface(Address address)
	{
		Span<byte> bytes = stackalloc byte[1];
		_ = TargetMemory.TryReadBytes(address, bytes, out MemoryAccessFailure _);
		_ = TargetMemory.TryReadDouble(address, out _, out _);
		_ = TargetMemory.TryReadInt16(address, out _, out _);
		_ = TargetMemory.TryReadInt32(address, out _, out _);
		_ = TargetMemory.TryReadInt64(address, out _, out _);
		_ = TargetMemory.TryReadInt8(address, out _, out _);
		_ = TargetMemory.TryReadPointer(address, PointerSize.Bit64, out _, out _);
		_ = TargetMemory.TryReadSingle(address, out _, out _);
		_ = TargetMemory.TryReadString(address, 1, false, out _, out _);
		_ = TargetMemory.TryReadUInt16(address, out _, out _);
		_ = TargetMemory.TryReadUInt32(address, out _, out _);
		_ = TargetMemory.TryReadUInt64(address, out _, out _);
		_ = TargetMemory.TryReadUInt8(address, out _, out _);
		_ = TargetMemory.TryWriteBytes(address, ReadOnlySpan<byte>.Empty, out _);
		_ = TargetMemory.TryWriteDouble(address, 0d, out _);
		_ = TargetMemory.TryWriteInt16(address, 0, out _);
		_ = TargetMemory.TryWriteInt32(address, 0, out _);
		_ = TargetMemory.TryWriteInt64(address, 0L, out _);
		_ = TargetMemory.TryWriteInt8(address, 0, out _);
		_ = TargetMemory.TryWritePointer(address, address, PointerSize.Bit64, out _);
		_ = TargetMemory.TryWriteSingle(address, 0f, out _);
		_ = TargetMemory.TryWriteString(address, ReadOnlySpan<char>.Empty, false, out _);
		_ = TargetMemory.TryWriteUInt16(address, 0, out _);
		_ = TargetMemory.TryWriteUInt32(address, 0u, out _);
		_ = TargetMemory.TryWriteUInt64(address, 0ul, out _);
		_ = TargetMemory.TryWriteUInt8(address, 0, out _);
	}

	internal static void RuntimeSurface(CheatEngineVersion version, PointerSize pointerSize, RuntimeCapabilityId id,
		RuntimeCapabilities capabilities, RuntimeInfo info, CheatEngineHostObservation host,
		TargetArchitectureObservation target)
	{
		_ = version.Major;
		_ = version.Minor;
		_ = CheatEngineVersion.Ce77010621;
		_ = PointerSize.Bit32;
		_ = PointerSize.Bit64;
		_ = PointerSize.Unknown;
		_ = pointerSize.Bytes;
		_ = pointerSize.IsKnown;
		_ = pointerSize != PointerSize.Bit32;
		_ = capabilities.TryGet(id, out RuntimeCapabilityAvailability availability);
		_ = availability.State;
		_ = capabilities.GetState(id);
		_ = RuntimeCapabilityId.CurrentProcess;
		_ = RuntimeCapabilityId.TargetArchitecture;
		_ = info.Capabilities;
		_ = info.Host;
		_ = info.Target;
		_ = new CheatEngineHostObservation(version, CheatEngineArchitecture.X64, true,
			CheatEngineOperatingSystem.Windows);
		_ = host.CheatEngineIs64Bit;
		_ = host.FileVersion;
		_ = host.OperatingSystem;
		_ = host.SystemArchitecture;
		_ = new TargetArchitectureObservation(new TargetProcessId(1), TargetBackend.LocalProcess, pointerSize, true,
			false, false, 0, 8);
		_ = target.Abi;
		_ = target.Architecture;
		_ = target.Backend;
		_ = target.Bitness;
		_ = target.ConfiguredPointerSize;
		_ = target.ConfiguredPointerSizeBytes;
		_ = target.ConfiguredPointerSizeDiffersFromBitness;
		_ = target.IsAndroid;
		_ = target.ProcessId;
	}

	internal static void RuntimeObservationSurface(ProcessOperationStatus status, LuaOperationStatus luaStatus,
		CurrentProcessObservation current, TargetProcessId processId, TargetProcessId otherProcessId)
	{
		_ = RuntimeObservations.TryObserveRuntimeInfo(out _);
		_ = RuntimeHostOperations.ObserveHost(out _);
		_ = RuntimeHostOperations.TryGetCheatEngineFileVersion(out _);
		_ = RuntimeHostOperations.TryGetOperatingSystem(out _);
		_ = RuntimeHostOperations.TryGetSystemArchitecture(out _);
		_ = RuntimeHostOperations.TryIsCheatEngine64Bit(out _);
		_ = RuntimeProcessOperations.ObserveCurrent(out _);
		_ = RuntimeProcessOperations.ObserveTargetArchitecture(out _);
		_ = RuntimeProcessOperations.TryGetConfiguredPointerSize(out _, out _);
		_ = RuntimeProcessOperations.SelectAndObserve(processId, out _);
		_ = ProcessOperationStatus.GlobalUnavailable;
		_ = ProcessOperationStatus.Success;
		_ = ProcessOperationStatus.TargetChanged;
		_ = ProcessOperationStatus.TargetNotAttached;
		_ = status.IsSuccess;
		_ = status.Kind;
		_ = luaStatus.IsSuccess;
		_ = luaStatus.Kind;
		_ = current.Id;
		_ = current.PointerSize;
		_ = processId == otherProcessId;
	}

	internal static void TargetSelectionSurface(TargetProcessIncarnation incarnation, TargetProcessIncarnation other)
	{
		TargetSelectionObservation observation = TargetSelection.ObserveCurrent();
		TargetIdentityCheck check = TargetSelection.ValidateCurrent(incarnation);
		_ = check.Kind;
		_ = check.Observed;
		_ = observation.Backend;
		_ = observation.Incarnation;
		_ = observation.SelectedProcessId;
		_ = observation.Status;
		_ = incarnation.StartedAtUtcTicks;
		_ = incarnation != other;
	}

	internal static void ScanningAndValueSurface(AobScanOptions options, Address address, Address other)
	{
		_ = new AobScanOptions("+X-C-W", FastScanMethod.Aligned, "4");
		_ = options.AlignmentMethod;
		_ = options.AlignmentParameter;
		_ = AobScanOptions.Default;
		_ = options.ProtectionFlags;
		_ = AobScanner.TryScan("90", options, out _);
		_ = Address.FromUInt64(0);
		_ = Address.TryParse("400000", out _);
		_ = address.Value;
		_ = Address.Zero;
		_ = address + 1L;
		_ = address < other;
		_ = address <= other;
		_ = address >= other;
	}

	internal static void HostingSurface(PluginContext context)
	{
		_ = PluginHost.Context;
		_ = context.Epoch;
		_ = context.IsCurrent;
		_ = context.IsMainThread;
		_ = context.ShutdownToken;
		_ = MainThread.Invoke(static (int state) => state, 1);
		_ = MainThread.IsMainThread;
		_ = new CompileOnlyPlugin();
	}

	internal static void LuaSurface(LuaState state, LuaStatus status)
	{
		LuaError error = LuaError.FromStack(state, status);
		_ = error.Message;
		_ = status.IsOk;
		AddressMarshaller.Push(state, 0);
		BooleanMarshaller.Push(state, true);
		StringMarshaller.Push(state, "value");
		_ = StringMarshaller.TryRead(state, -1, out _);
		using LuaRef reference = new();
		using LuaRuntimeOperation operation = LuaRuntime.AcquireOperation();
		_ = LuaRuntime.TryAcquireOperationWithOutcome(out _);
		_ = LuaRuntime.ExternalStateResetDetected;
		_ = operation.State;
		using LuaFrame frame = new(state);
		_ = state.IsNil(-1);
		state.SetTop(state.Top);
		_ = state.TryCall(0, 0);
		_ = state.TryExecute(ReadOnlySpan<byte>.Empty, 0, ReadOnlySpan<byte>.Empty);
		_ = new EngineMarshallingException("operation", EngineMarshallingDirection.Result, "expected", "actual");
		_ = new LuaGlobalAttribute("global");
	}

	internal static void FailureSurface(EngineException engineFailure, MemoryScanException scanFailure)
	{
		_ = engineFailure.Kind;
		_ = scanFailure.FailureKind;
	}

	private sealed class CompileOnlyPlugin : CheatEnginePlugin
	{
		protected override void OnEnable()
		{
		}

		protected override void OnDisable()
		{
		}
	}
}
