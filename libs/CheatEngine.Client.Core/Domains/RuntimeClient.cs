using System.Globalization;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Errors;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Captures only independently observed, synchronous runtime facts from the active Cheat Engine host.</summary>
internal sealed class RuntimeClient : ICheatEngineRuntime
{
	private readonly Version _clientAssemblyVersion;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Func<long> _getEpoch;
	private readonly CoreClientPolicy _policy;
	private readonly IRuntimeProbe _probe;
	private readonly Version _sdkAssemblyVersion;

	internal RuntimeClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime, CoreClientPolicy policy)
		: this(
			dispatcher,
			new LuaRuntimeProbe(),
			() => lifetime.Epoch,
			typeof(ICheatEngineRuntime).Assembly.GetName().Version,
			typeof(RuntimeInfo).Assembly.GetName().Version,
			policy)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
	}

	internal RuntimeClient(
		ICheatEngineDispatcher dispatcher,
		IRuntimeProbe probe,
		Func<long> getEpoch,
		Version? clientAssemblyVersion = null,
		Version? sdkAssemblyVersion = null,
		CoreClientPolicy? policy = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_probe = probe ?? throw new ArgumentNullException(nameof(probe));
		_getEpoch = getEpoch ?? throw new ArgumentNullException(nameof(getEpoch));
		_policy = policy ?? CoreClientPolicy.SafeDefaults;
		_clientAssemblyVersion = clientAssemblyVersion ?? typeof(ICheatEngineRuntime).Assembly.GetName().Version ??
			throw new InvalidOperationException("The Client assembly does not declare an assembly version.");
		_sdkAssemblyVersion = sdkAssemblyVersion ?? typeof(RuntimeInfo).Assembly.GetName().Version ??
			throw new InvalidOperationException("The SDK runtime assembly does not declare an assembly version.");
	}

	public long Epoch => _getEpoch();

	public bool TryGetSnapshot(
		out CheatEngineRuntimeSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		CheatEngineRuntimeSnapshot captured = default;
		if (!_dispatcher.TryInvoke(() => captured = Capture(), out failure, cancellationToken))
		{
			snapshot = default;
			return false;
		}

		snapshot = captured;
		return true;
	}

	public CheatEngineRuntimeSnapshot GetSnapshot(CancellationToken cancellationToken = default)
	{
		if (TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetSdkCapability(
		RuntimeCapabilityId capability,
		out RuntimeCapabilityAvailability availability,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (capability.IsEmpty)
		{
			throw new ArgumentException("A runtime capability identifier is required.", nameof(capability));
		}

		if (!TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out failure, cancellationToken))
		{
			availability = default;
			return false;
		}

		if (snapshot.SdkCapabilities.TryGet(capability, out availability))
		{
			return true;
		}

		availability = new RuntimeCapabilityAvailability(
			capability,
			RuntimeCapabilityAvailabilityState.Unknown,
			RuntimeCapabilityContract.Unknown);
		failure = default;
		return true;
	}

	public RuntimeCapabilityAvailability GetSdkCapability(
		RuntimeCapabilityId capability,
		CancellationToken cancellationToken = default)
	{
		if (TryGetSdkCapability(capability, out RuntimeCapabilityAvailability availability,
			    out CheatEngineFailure failure, cancellationToken))
		{
			return availability;
		}

		failure.Throw();
		return default;
	}

	public bool TryGetClientCapability(
		ClientCapabilityId capability,
		out ClientCapabilityAvailability availability,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		if (capability.IsEmpty)
		{
			throw new ArgumentException("A Client capability identifier is required.", nameof(capability));
		}

		if (!TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out failure, cancellationToken))
		{
			availability = default;
			return false;
		}

		if (snapshot.ClientCapabilities.TryGet(capability, out availability))
		{
			return true;
		}

		availability = new ClientCapabilityAvailability(
			capability,
			ClientCapabilityAvailabilityState.Unknown,
			"This Client release does not define a probe or policy gate for the requested capability.");
		failure = default;
		return true;
	}

	public ClientCapabilityAvailability GetClientCapability(
		ClientCapabilityId capability,
		CancellationToken cancellationToken = default)
	{
		if (TryGetClientCapability(capability, out ClientCapabilityAvailability availability,
			    out CheatEngineFailure failure, cancellationToken))
		{
			return availability;
		}

		failure.Throw();
		return default;
	}

	private CheatEngineRuntimeSnapshot Capture()
	{
		ProbeResult<double> version = Probe(RuntimeCapabilityId.CheatEngineVersion, _probe.GetCheatEngineVersion);
		ProbeResult<int> systemArchitecture =
			Probe(RuntimeCapabilityId.SystemArchitecture, _probe.GetSystemArchitecture);
		ProbeResult<int> targetAbi = Probe(RuntimeCapabilityId.TargetAbi, _probe.GetTargetAbi);

		bool hasTarget = TryReadOpenedProcess(out long openedProcessId) && openedProcessId > 0;
		ProbeResult<bool> targetArchitecture = hasTarget
			? Probe(RuntimeCapabilityId.TargetArchitecture, _probe.TargetIs64Bit)
			: ProbeResult<bool>.Unknown(RuntimeCapabilityId.TargetArchitecture);

		double? observedVersion = version.State == RuntimeCapabilityAvailabilityState.Available
			? version.Value
			: null;
		if (observedVersion is { } reported && (!double.IsFinite(reported) || reported < 0))
		{
			throw new EngineMarshallingException(
				"Runtime.GetCheatEngineVersion",
				EngineMarshallingDirection.Result,
				"a finite non-negative number",
				reported.ToString(CultureInfo.InvariantCulture));
		}

		CheatEngineArchitecture decodedSystemArchitecture = DecodeSystemArchitecture(systemArchitecture);
		TargetAbi decodedTargetAbi = DecodeTargetAbi(targetAbi);
		CheatEngineArchitecture decodedTargetArchitecture =
			DecodeTargetArchitecture(targetArchitecture, decodedTargetAbi);

		RuntimeCapabilityAvailability[] capabilities =
		[
			version.ToAvailability(),
			systemArchitecture.ToAvailability(),
			targetArchitecture.ToAvailability(),
			targetAbi.ToAvailability()
		];

		return new CheatEngineRuntimeSnapshot(
			Epoch,
			new CheatEngineRuntimeVersionInfo(observedVersion, CheatEngineVersion.Ce77010621, _clientAssemblyVersion,
				_sdkAssemblyVersion),
			new CheatEngineRuntimePlatformInfo(
				decodedSystemArchitecture,
				decodedTargetArchitecture,
				PointerSize.FromArchitecture(decodedTargetArchitecture),
				decodedTargetAbi),
			RuntimeCapabilities.Create(capabilities),
			CreateClientCapabilities());
	}

	private ClientCapabilities CreateClientCapabilities()
	{
		ClientCapabilityAvailability[] capabilities =
		[
			Unknown(ClientCapabilityId.ProcessSelection,
				"The runtime snapshot does not probe every backing process-selection primitive, including openProcess."),
			Unknown(ClientCapabilityId.TypedMemory,
				"The runtime snapshot does not probe every backing typed-memory primitive."),
			Unknown(ClientCapabilityId.PatternScanning,
				"The runtime snapshot does not probe every backing AOB scan primitive."),
			new(
				ClientCapabilityId.ValueScanning,
				ClientCapabilityAvailabilityState.Unavailable,
				"Value scan sessions remain unavailable until the CE 7.7 live ownership and cleanup gate passes."),
			Unknown(ClientCapabilityId.Inspection,
				"The runtime snapshot does not probe every backing inspection primitive."),
			Unknown(ClientCapabilityId.Tables,
				"The runtime snapshot does not probe every backing Address List and table primitive."),
			Unknown(ClientCapabilityId.ProtectedLua,
				"The runtime snapshot does not probe every backing protected Lua primitive."),
			_policy.EnableUnsafeLuaExecution
				? new ClientCapabilityAvailability(
					ClientCapabilityId.UnsafeLuaExecution,
					ClientCapabilityAvailabilityState.Available,
					"Unsafe Lua execution was explicitly enabled for this activation.")
				: new ClientCapabilityAvailability(
					ClientCapabilityId.UnsafeLuaExecution,
					ClientCapabilityAvailabilityState.Unavailable,
					"Unsafe Lua execution requires explicit EnableUnsafeLuaExecution opt-in for this activation."),
			Unavailable(ClientCapabilityId.Allocations,
				"Owned target allocations remain unavailable until the SDK production owner and CE 7.7 x64 cleanup gate pass."),
			Unknown(ClientCapabilityId.Assembly,
				"Assembly primitives and Auto Assembler ownership have not yet passed their CE 7.7 x64 live gate."),
			Unavailable(ClientCapabilityId.RemoteExecution,
				"Remote execution remains unavailable until allocation, timeout, and cleanup behavior pass the CE 7.7 x64 live gate."),
			Unavailable(ClientCapabilityId.Debugger,
				"Debugger callback ownership and synchronous continuation have not yet passed the CE 7.7 x64 live gate."),
			Unavailable(ClientCapabilityId.Hotkeys,
				"Hotkey callback ownership has not yet passed the CE 7.7 x64 live gate."),
			Unavailable(ClientCapabilityId.Timers,
				"Timer callback ownership has not yet passed the CE 7.7 x64 live gate."),
			Unknown(ClientCapabilityId.Speed,
				"The runtime snapshot does not yet probe the complete speed-control contract."),
			Unknown(ClientCapabilityId.Hashing,
				"The runtime snapshot does not yet probe target-memory and file hashing independently."),
			Unavailable(ClientCapabilityId.Dbvm,
				"DBVM observation, explicit initialization, and watch ownership have not yet passed the CE 7.7 x64 live gate.")
		];

		return ClientCapabilities.Create(capabilities);
	}

	private static ClientCapabilityAvailability Unknown(ClientCapabilityId capability, string reason)
	{
		return new ClientCapabilityAvailability(capability, ClientCapabilityAvailabilityState.Unknown, reason);
	}

	private static ClientCapabilityAvailability Unavailable(ClientCapabilityId capability, string reason)
	{
		return new ClientCapabilityAvailability(capability, ClientCapabilityAvailabilityState.Unavailable, reason);
	}

	private static CheatEngineArchitecture DecodeSystemArchitecture(ProbeResult<int> probe)
	{
		return probe is { State: RuntimeCapabilityAvailabilityState.Available, Value: int architecture } &&
		       RuntimeInfo.TryDecodeSystemArchitecture(architecture, out CheatEngineArchitecture decoded)
			? decoded
			: CheatEngineArchitecture.Unknown;
	}

	private static TargetAbi DecodeTargetAbi(ProbeResult<int> probe)
	{
		return probe is { State: RuntimeCapabilityAvailabilityState.Available, Value: int abi } &&
		       RuntimeInfo.TryDecodeTargetAbi(abi, out TargetAbi decoded)
			? decoded
			: TargetAbi.Unknown;
	}

	private static CheatEngineArchitecture DecodeTargetArchitecture(ProbeResult<bool> probe, TargetAbi targetAbi)
	{
		if (targetAbi != TargetAbi.Windows ||
		    probe is not { State: RuntimeCapabilityAvailabilityState.Available, Value: bool is64Bit })
		{
			return CheatEngineArchitecture.Unknown;
		}

		return is64Bit ? CheatEngineArchitecture.X64 : CheatEngineArchitecture.X86;
	}

	private bool TryReadOpenedProcess(out long processId)
	{
		try
		{
			processId = _probe.GetOpenedProcessId();
			return true;
		}
		catch (EngineGlobalUnavailableException)
		{
			processId = 0;
			return false;
		}
		catch (EngineCapabilityUnavailableException)
		{
			processId = 0;
			return false;
		}
	}

	private static ProbeResult<T> Probe<T>(RuntimeCapabilityId capability, Func<T> probe)
	{
		try
		{
			return ProbeResult<T>.Available(capability, probe());
		}
		catch (EngineGlobalUnavailableException)
		{
			return ProbeResult<T>.Unavailable(capability);
		}
		catch (EngineCapabilityUnavailableException)
		{
			return ProbeResult<T>.Unavailable(capability);
		}
	}
}
