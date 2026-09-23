using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Captures only independently observed, synchronous runtime facts from the active Cheat Engine host.</summary>
internal sealed class RuntimeClient : ICheatEngineRuntime
{
	/// <summary>
	///     The capability identifier of Cheat Engine's configured pointer size. It is the same string as the SDK 2.0
	///     <c>RuntimeCapabilityId.ConfiguredPointerSize</c>; replace it with the SDK static on migration
	///     (docs/migration/sdk-2.0.md).
	/// </summary>
	internal const string ConfiguredPointerSizeCapabilityValue = "Runtime.ConfiguredPointerSize";

	private const string _snapshotOperation = "Runtime.GetSnapshot";

	private readonly Version _clientAssemblyVersion;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Func<long> _getEpoch;
	private readonly Func<bool> _isActivationCurrent;
	private readonly CoreLifetime? _lifetime;
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
			policy,
			() => lifetime.IsCurrent)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		_lifetime = lifetime;
	}

	internal RuntimeClient(
		ICheatEngineDispatcher dispatcher,
		IRuntimeProbe probe,
		Func<long> getEpoch,
		Version? clientAssemblyVersion = null,
		Version? sdkAssemblyVersion = null,
		CoreClientPolicy? policy = null,
		Func<bool>? isActivationCurrent = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_probe = probe ?? throw new ArgumentNullException(nameof(probe));
		_getEpoch = getEpoch ?? throw new ArgumentNullException(nameof(getEpoch));
		_isActivationCurrent = isActivationCurrent ?? (static () => true);
		_policy = policy ?? CoreClientPolicy.SafeDefaults;
		_clientAssemblyVersion = clientAssemblyVersion ?? typeof(ICheatEngineRuntime).Assembly.GetName().Version ??
			throw new InvalidOperationException("The Client assembly does not declare an assembly version.");
		_sdkAssemblyVersion = sdkAssemblyVersion ?? typeof(RuntimeInfo).Assembly.GetName().Version ??
			throw new InvalidOperationException("The SDK runtime assembly does not declare an assembly version.");
	}

	/// <summary>Gets the SDK runtime capability identifier of Cheat Engine's configured pointer size.</summary>
	internal static RuntimeCapabilityId ConfiguredPointerSizeCapability => new(ConfiguredPointerSizeCapabilityValue);

	public long Epoch => _getEpoch();

	public bool TryGetSnapshot(
		out CheatEngineRuntimeSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		// Runtime probes are read-only (Q45). ProbeClassifier turns every SDK Engine or Lua exception into evidence; any
		// other SDK fault (for example a detached runtime) is returned as a failure, never thrown across this Try method.
		CheatEngineRuntimeSnapshot captured = default;
		if (!SdkBoundary.TryInvoke(_dispatcher, _snapshotOperation, () => captured = Capture(),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
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
		ProbeResult<double> version = ValidateVersion(ProbeClassifier.Probe(_probe.GetCheatEngineVersion));
		ProbeResult<int> systemArchitecture = ProbeClassifier.Probe(_probe.GetSystemArchitecture);
		ProbeResult<int> targetAbi = ProbeClassifier.Probe(_probe.GetTargetAbi);
		ObservedTargetArchitecture target = TargetArchitectureObserver.Observe(_probe, readConfiguredPointerSize: true);

		double? observedVersion = version.HasValue ? version.Value : null;
		CheatEngineArchitecture decodedSystemArchitecture = DecodeSystemArchitecture(ref systemArchitecture);
		TargetAbi decodedTargetAbi = DecodeTargetAbi(ref targetAbi);

		RuntimeCapabilityAvailability[] capabilities =
		[
			version.ToAvailability(RuntimeCapabilityId.CheatEngineVersion),
			systemArchitecture.ToAvailability(RuntimeCapabilityId.SystemArchitecture),
			target.ArchitectureEvidence.ToAvailability(RuntimeCapabilityId.TargetArchitecture),
			targetAbi.ToAvailability(RuntimeCapabilityId.TargetAbi),
			target.ConfiguredPointerSize.ToAvailability(ConfiguredPointerSizeCapability)
		];

		return new CheatEngineRuntimeSnapshot(
			Epoch,
			new CheatEngineRuntimeVersionInfo(observedVersion, CheatEngineVersion.Ce77010621, _clientAssemblyVersion,
				_sdkAssemblyVersion),
			new CheatEngineRuntimePlatformInfo(
				decodedSystemArchitecture,
				target.Architecture,
				target.ProcessPointerSize,
				decodedTargetAbi,
				target.ConfiguredPointerSizeBytes),
			RuntimeCapabilities.Create(capabilities),
			CreateClientCapabilities(target.ProcessId));
	}

	private ClientCapabilities CreateClientCapabilities(ProbeResult<long> openedProcess)
	{
		ClientCapabilityEvidenceGate lifetime = _isActivationCurrent()
			? Satisfied("The Client activation is current.")
			: Missing("The Client activation is no longer current.");
		ClientCapabilityEvidenceGate packageUnknown = UnknownEvidence(
			"The runtime snapshot does not establish the identity of the consumed SDK package artifact.");
		ClientCapabilityEvidenceGate qualificationUnknown = UnknownEvidence(
			"No complete Cheat Engine 7.7 x64 live qualification record is attached to this capability observation.");
		ClientCapabilityEvidenceGate policyNotRequired = Satisfied(
			"This capability has no additional activation policy opt-in.");
		ClientCapabilityEvidenceGate unprobedHost = UnknownEvidence(
			"The runtime snapshot does not probe every host primitive required by this capability.");
		ClientCapabilityEvidenceGate implemented = Satisfied(
			"The Client composes an operational adapter for this capability.");
		ClientCapabilityEvidenceGate contractOnly = Missing(
			"The Client package currently composes only an unavailable adapter for this capability.");

		ClientCapabilityAvailability[] capabilities =
		[
			Describe(ClientCapabilityId.ProcessSelection, implemented, packageUnknown, openedProcess.Evidence,
				qualificationUnknown, policyNotRequired, lifetime),
			Describe(ClientCapabilityId.TypedMemory, implemented, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.PatternScanning, implemented, packageUnknown, unprobedHost,
				qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.ValueScanning, contractOnly,
				Missing(
					"CheatEngine.SDK 1.0.0 does not provide the public MemScan and FoundList ownership factory required by Client."),
				unprobedHost, qualificationUnknown, policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Inspection, implemented, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Tables, implemented, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.ProtectedLua, implemented, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.UnsafeLuaExecution, implemented, packageUnknown, unprobedHost,
				qualificationUnknown,
				_policy.EnableUnsafeLuaExecution
					? Satisfied("Unsafe Lua execution was explicitly enabled for this activation.")
					: Missing(
						"Unsafe Lua execution requires explicit EnableUnsafeLuaExecution opt-in for this activation."),
				lifetime),
			Describe(ClientCapabilityId.Allocations, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Assembly, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.RemoteExecution, contractOnly, packageUnknown, unprobedHost,
				qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Debugger, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Hotkeys, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Timers, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Speed, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Hashing, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime),
			Describe(ClientCapabilityId.Dbvm, contractOnly, packageUnknown, unprobedHost, qualificationUnknown,
				policyNotRequired, lifetime)
		];

		return ClientCapabilities.Create(capabilities);
	}

	private static ClientCapabilityAvailability Describe(
		ClientCapabilityId capability,
		ClientCapabilityEvidenceGate implementation,
		ClientCapabilityEvidenceGate package,
		ClientCapabilityEvidenceGate host,
		ClientCapabilityEvidenceGate qualification,
		ClientCapabilityEvidenceGate policy,
		ClientCapabilityEvidenceGate lifetime)
	{
		return new ClientCapabilityAvailability(capability,
			new ClientCapabilityEvidence(implementation, package, host, qualification, policy, lifetime));
	}

	private static ClientCapabilityEvidenceGate Satisfied(string reason)
	{
		return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Satisfied, reason);
	}

	private static ClientCapabilityEvidenceGate Missing(string reason)
	{
		return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Missing, reason);
	}

	private static ClientCapabilityEvidenceGate UnknownEvidence(string reason)
	{
		return new ClientCapabilityEvidenceGate(ClientCapabilityEvidenceState.Unknown, reason);
	}

	private static ProbeResult<double> ValidateVersion(ProbeResult<double> probe)
	{
		return probe.HasValue && probe.Value is { } version && (!double.IsFinite(version) || version < 0)
			? ProbeResult<double>.Malformed("Cheat Engine returned a version that is not a finite non-negative number.")
			: probe;
	}

	private static CheatEngineArchitecture DecodeSystemArchitecture(ref ProbeResult<int> probe)
	{
		if (!probe.HasValue)
		{
			return CheatEngineArchitecture.Unknown;
		}

		if (RuntimeInfo.TryDecodeSystemArchitecture(probe.Value, out CheatEngineArchitecture architecture))
		{
			return architecture;
		}

		probe = ProbeResult<int>.Malformed("Cheat Engine returned an unsupported system architecture code.");
		return CheatEngineArchitecture.Unknown;
	}

	private static TargetAbi DecodeTargetAbi(ref ProbeResult<int> probe)
	{
		if (!probe.HasValue)
		{
			return TargetAbi.Unknown;
		}

		if (RuntimeInfo.TryDecodeTargetAbi(probe.Value, out TargetAbi targetAbi))
		{
			return targetAbi;
		}

		probe = ProbeResult<int>.Malformed("Cheat Engine returned an unsupported target ABI code.");
		return TargetAbi.Unknown;
	}
}
