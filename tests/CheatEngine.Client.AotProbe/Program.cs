using CheatEngine.Client;
using CheatEngine.Client.Allocations;
using CheatEngine.Client.AotProbe;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Dbvm;
using CheatEngine.Client.Debugger;
using CheatEngine.Client.Events;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hashing;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Hotkeys;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.RemoteExecution;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Speed;
using CheatEngine.Client.Timers;

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
_ = typeof(IDescribedLuaModule);
_ = typeof(ILuaResultMapper<,>);
_ = typeof(LuaModuleDescriptor);
_ = typeof(AotProbeLuaModule);
_ = AotProbeMapperInvocation.Map<AotProbeScalarMapper>(42);
_ = typeof(IAllocationClient);
_ = typeof(ITargetMemoryLease);
_ = typeof(IAssemblyClient);
_ = typeof(IAutoAssemblerPatchLease);
_ = typeof(IRemoteExecutionClient);
_ = typeof(IDebuggerClient);
_ = typeof(IBreakpointLease);
_ = typeof(IHotkeyClient);
_ = typeof(IHotkeyLease);
_ = typeof(ITimerClient);
_ = typeof(ITimerLease);
_ = typeof(ISpeedClient);
_ = typeof(IHashingClient);
_ = typeof(IDbvmClient);
_ = typeof(IDbvmWatchLease);
_ = typeof(IEventStreamLease<>);
_ = typeof(EventStreamOptions);
_ = typeof(BreakpointRequest);
_ = typeof(HotkeyRegistration);
_ = typeof(TimerRequest);
_ = typeof(RemoteCallRequest);
_ = typeof(MemoryHashRequest);
_ = typeof(DbvmWatchRequest);
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
