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
services.AddCheatEngineClient()
	.AddLuaModule<AotProbeLuaModule>()
	.EnableUnsafeLuaExecution();

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
_ = typeof(IAllocationClient);
_ = typeof(ITargetMemoryLease);
_ = typeof(IAssemblyClient);
_ = typeof(IAutoAssemblerPatchLease);
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

return services.Count == 0 ? 1 : 0;
