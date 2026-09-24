using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.AotProbe;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;

using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
#pragma warning disable CECLIENT5004 // The probe composes the experimental Auto Assembler opt-in under NativeAOT.
services.AddCheatEngineClient()
	.AddLuaModule<AotProbeLuaModule>()
	.EnableUnsafeLuaExecution()
	.EnableAutoAssemblerPatches();
#pragma warning restore CECLIENT5004

using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
{
	ValidateOnBuild = true,
	ValidateScopes = true
});

_ = typeof(ICheatEngineClient);
_ = typeof(CheatEngineClientPlugin);
_ = typeof(AobScanBuilder);
_ = typeof(MemoryAddressBuilder);
_ = typeof(MemoryPrimitiveBatchReadRequest<>);
_ = typeof(MemoryPrimitiveBatchWriteRequest<>);
_ = typeof(PointerChainRequest);
_ = typeof(CheatEngineLuaModuleAttribute);
_ = typeof(CheatEngineLuaOperationAttribute);
_ = typeof(ILuaModule);
_ = typeof(ILuaResultMapper<,>);
_ = typeof(LuaModuleDescriptor);
_ = typeof(AotProbeLuaModule);
_ = AotProbeMapperInvocation.Map<AotProbeScalarMapper>(42);
_ = typeof(IAssemblyClient);
#pragma warning disable CECLIENT5004 // The experimental Auto Assembler surface stays reachable under NativeAOT.
_ = typeof(IAutoAssemblerClient);
_ = typeof(IAutoAssemblerPatchLease);
_ = typeof(AutoAssemblerCheckResult);
#pragma warning restore CECLIENT5004
_ = typeof(AssemblyInstructionRequest);
_ = new AobPattern("90");

const string evidenceReason = "Activation evidence is current.";
ClientCapabilityEvidenceGate satisfiedGate = new(ClientCapabilityEvidenceState.Satisfied, evidenceReason);
ClientCapabilityEvidence evidence = new(
	satisfiedGate, satisfiedGate, satisfiedGate, satisfiedGate, satisfiedGate, satisfiedGate);
ClientCapabilityAvailability capabilityAvailability = new(ClientCapabilityId.ProcessSelection, evidence);
if (evidence.EffectiveReasonCode != ClientCapabilityEvidenceReasonCode.Lifetime ||
	evidence.EffectiveReason != evidenceReason ||
	capabilityAvailability.State != ClientCapabilityAvailabilityState.Available ||
	!capabilityAvailability.IsAvailable || !capabilityAvailability.IsKnown ||
	capabilityAvailability.Reason != evidenceReason)
{
	return 1;
}

#pragma warning disable CECLIENT5001 // The probe keeps the experimental value scans in the NativeAOT graph.
_ = typeof(IValueScanner);
_ = typeof(IValueScanSession);
ValueScanFirstRequest firstScan = ValueScanFirstRequest.Exact(ValueScanValue.FromInt32(100))
	.WithAlignment(ScanAlignment.AlignedTo(4));
if (firstScan.ValueType != ValueScanValueType.Integer32 || firstScan.Alignment.Divisor != 4 ||
	ValueScanValue.FromBytes([0x90, 0x0F]).Text != "90 0F" ||
	new ValueScanPage(0, 2, [new ValueScanMatch(default, "100")]).NextStartIndex != 1)
{
	return 1;
}
#pragma warning restore CECLIENT5001

#pragma warning disable CECLIENT5002 // The probe keeps the experimental target allocations in the NativeAOT graph.
_ = typeof(IAllocationClient);
_ = typeof(ITargetMemoryLease);
AllocationRequest allocation = new(4096, AllocationProtection.ExecuteReadWrite);
if (allocation.Size != 4096 || allocation.Protection != AllocationProtection.ExecuteReadWrite ||
	allocation.PreferredAddress is not null)
{
	return 1;
}
#pragma warning restore CECLIENT5002

return services.Count == 0 ? 1 : 0;
