using System.Collections.Immutable;
using System.Reflection;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Core.Qualification;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Results;
using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Captures only independently observed, synchronous runtime facts from the active Cheat Engine host.</summary>
/// <remarks>
///     The facts are the read-only CheatEngine.SDK 2.0.0 runtime observations, through <see cref="RuntimeObserver" />:
///     the snapshot reports what the SDK established and leaves every other fact unknown.
/// </remarks>
internal sealed class RuntimeClient : ICheatEngineRuntime
{
	private const string SnapshotOperation = "Runtime.GetSnapshot";

	private const string CapabilityOperation = "Runtime.GetClientCapability";

	private readonly Version _clientAssemblyVersion;
	private readonly string? _clientVersion;
	private readonly ICoreDiagnostics _diagnostics;
	private readonly ICheatEngineDispatcher _dispatcher;
	private readonly Func<long> _getEpoch;
	private readonly Func<bool> _isActivationCurrent;
	private readonly CoreLifetime? _lifetime;
	private readonly CoreClientPolicy _policy;
	private readonly IRuntimeObservationPort _port;
	private readonly HostQualificationRecord? _qualificationEvidence;
	private readonly Version _sdkAssemblyVersion;
	private readonly ConsumedSdkIdentity _sdkIdentity;

	internal RuntimeClient(ICheatEngineDispatcher dispatcher, CoreLifetime lifetime, CoreClientPolicy policy)
		: this(
			dispatcher,
			SdkRuntimeObservationPort.Instance,
			() => lifetime.Epoch,
			typeof(ICheatEngineRuntime).Assembly.GetName().Version,
			typeof(RuntimeInfo).Assembly.GetName().Version,
			policy,
			() => lifetime.IsCurrent,
			ConsumedSdkIdentity.Current,
			lifetime.Diagnostics,
			HostQualificationEvidence.Recorded)
	{
		ArgumentNullException.ThrowIfNull(lifetime);
		_lifetime = lifetime;
	}

	/// <summary>Creates a runtime client with explicit seams; tests supply the port and the consumed-SDK identity.</summary>
	/// <param name="dispatcher">The dispatcher that runs the read-only observations on Cheat Engine's main thread.</param>
	/// <param name="port">The read-only runtime observation port.</param>
	/// <param name="getEpoch">Reads the current activation epoch.</param>
	/// <param name="clientAssemblyVersion">The Client assembly version, or the version of this build.</param>
	/// <param name="sdkAssemblyVersion">The SDK runtime assembly version, or the loaded one.</param>
	/// <param name="policy">The activation policy, or the safe defaults.</param>
	/// <param name="isActivationCurrent">Reads whether the activation is current; always current when omitted.</param>
	/// <param name="sdkIdentity">
	///     The consumed-SDK identity evidence; <see langword="null" /> means that no identity was embedded, so every
	///     operational package gate stays unknown.
	/// </param>
	/// <param name="diagnostics">The diagnostics sink; nothing is emitted when omitted.</param>
	/// <param name="qualificationEvidence">
	///     The host qualification evidence; <see langword="null" /> means that no run is recorded, so every qualification
	///     gate stays unknown.
	/// </param>
	/// <param name="clientVersion">
	///     The Client version the qualification gate compares with the evidence, without build metadata; the
	///     informational version of this build when omitted.
	/// </param>
	internal RuntimeClient(
		ICheatEngineDispatcher dispatcher,
		IRuntimeObservationPort port,
		Func<long> getEpoch,
		Version? clientAssemblyVersion = null,
		Version? sdkAssemblyVersion = null,
		CoreClientPolicy? policy = null,
		Func<bool>? isActivationCurrent = null,
		ConsumedSdkIdentity? sdkIdentity = null,
		ICoreDiagnostics? diagnostics = null,
		HostQualificationRecord? qualificationEvidence = null,
		string? clientVersion = null)
	{
		_dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
		_diagnostics = GuardedCoreDiagnostics.Wrap(diagnostics);
		_sdkIdentity = sdkIdentity ?? ConsumedSdkIdentity.NotEmbedded;
		_port = port ?? throw new ArgumentNullException(nameof(port));
		_getEpoch = getEpoch ?? throw new ArgumentNullException(nameof(getEpoch));
		_isActivationCurrent = isActivationCurrent ?? (static () => true);
		_policy = policy ?? CoreClientPolicy.SafeDefaults;
		_clientAssemblyVersion = clientAssemblyVersion ?? typeof(ICheatEngineRuntime).Assembly.GetName().Version ??
			throw new InvalidOperationException("The Client assembly does not declare an assembly version.");
		_sdkAssemblyVersion = sdkAssemblyVersion ?? typeof(RuntimeInfo).Assembly.GetName().Version ??
			throw new InvalidOperationException("The SDK runtime assembly does not declare an assembly version.");
		_qualificationEvidence = qualificationEvidence;
		_clientVersion = clientVersion ?? HostQualificationGate.WithoutMetadata(typeof(ICheatEngineRuntime).Assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
	}

	/// <summary>
	///     Creates the qualification gate reason of every capability while this build embeds no host qualification
	///     evidence (<see cref="HostQualificationEvidence" />; audit A20-19): receipts produced for the SDK branch never
	///     qualify the Client tuple. The tuple names the consumed CheatEngine.SDK identity this build embeds, never a version
	///     written in the source.
	/// </summary>
	/// <param name="sdkIdentity">The consumed-SDK identity evidence of this build.</param>
	internal static string QualificationUnknownReason(ConsumedSdkIdentity sdkIdentity)
	{
		ArgumentNullException.ThrowIfNull(sdkIdentity);
		string package = sdkIdentity.ExpectedInformationalVersion is { } identity
			? "CheatEngine.SDK " + identity
			: "the consumed CheatEngine.SDK (this build embeds no identity for it)";
		return $"No Client qualification receipt for profile {ConsumedSdkIdentity.SupportedHostProfileId} with {package} " +
			"is embedded in this build; SDK-branch receipts never qualify the Client tuple.";
	}

	public long Epoch => _getEpoch();

	public bool TryGetSnapshot(
		out CheatEngineRuntimeSnapshot snapshot,
		out CheatEngineFailure failure,
		CancellationToken cancellationToken = default)
	{
		return TryCapture(SnapshotOperation, out snapshot, out failure, cancellationToken);
	}

	public CheatEngineRuntimeSnapshot GetSnapshot(CancellationToken cancellationToken = default)
	{
		if (TryGetSnapshot(out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure, cancellationToken))
		{
			return snapshot;
		}

		failure.Throw(cancellationToken);
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

		if (!TryCapture(CapabilityOperation, out CheatEngineRuntimeSnapshot snapshot, out failure, cancellationToken))
		{
			availability = default;
			return false;
		}

		if (snapshot.Capabilities.TryGet(capability, out availability))
		{
			return true;
		}

		ClientCapabilityEvidenceGate undefined = UnknownEvidence(
			"This Client release does not define a probe or policy gate for the requested capability.");
		availability = new ClientCapabilityAvailability(capability,
			new ClientCapabilityEvidence(undefined, undefined, undefined, undefined, undefined, undefined));
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

		failure.Throw(cancellationToken);
		return default;
	}

	private CheatEngineRuntimeSnapshot Capture()
	{
		ObservedRuntime observed = RuntimeObserver.Observe(_port);
		CheatEngineHostObservation host = observed.Host;
		ObservedTarget target = observed.Target;
		return new CheatEngineRuntimeSnapshot(
			Epoch,
			new CheatEngineRuntimeVersionInfo(host.FileVersion, CheatEngineVersion.Ce77010621, _clientAssemblyVersion,
				_sdkAssemblyVersion, _sdkIdentity.LoadedInformationalVersion, _sdkIdentity.ExactReviewedIdentity),
			new CheatEngineRuntimePlatformInfo(
				host.OperatingSystem,
				host.SystemArchitecture,
				host.CheatEngineIs64Bit switch
				{
					true => PointerSize.Bit64,
					false => PointerSize.Bit32,
					null => PointerSize.Unknown
				},
				target.Backend,
				target.Architecture,
				target.Bitness,
				target.Abi,
				target.IsAndroid,
				target.ConfiguredPointerSizeBytes),
			CreateClientCapabilities(observed.ProcessSelectionHost, new HostQualificationContext(
				_sdkIdentity.ExactReviewedIdentity, _sdkIdentity.LoadedInformationalVersion, host.FileVersion,
				host.CheatEngineIs64Bit, host.OperatingSystem, target.Backend, target.Architecture, _clientVersion)),
			new CheatEngineRuntimeLuaInfo(_port.ExternalStateResetDetected));
	}

	private ClientCapabilities CreateClientCapabilities(ClientCapabilityEvidenceGate selectedProcess,
		HostQualificationContext qualificationContext)
	{
		ClientCapabilityEvidenceGate lifetime = _isActivationCurrent()
			? Satisfied("The Client activation is current.")
			: Missing("The Client activation is no longer current.");
		// ADR-09, ADR-10: the package gate of every capability comes from evidence (the embedded consumed-SDK identity
		// compared with the loaded CheatEngine.SDK.Engine), never from the presence of an interface or a version name.
		ClientCapabilityEvidenceGate package = _sdkIdentity.PackageGate;
		string noQualificationEvidence = QualificationUnknownReason(_sdkIdentity);
		ClientCapabilityEvidenceGate policyNotRequired = Satisfied(
			"This capability has no additional activation policy opt-in.");
		ClientCapabilityEvidenceGate unprobedHost = UnknownEvidence(
			"The runtime snapshot does not probe every host primitive required by this capability.");
		ClientCapabilityEvidenceGate implemented = Satisfied(
			"The Client composes an operational adapter for this capability.");

		ClientCapabilityEvidenceGate unsafeLuaPolicy = _policy.EnableUnsafeLuaExecution
			? Satisfied("Unsafe Lua execution was explicitly enabled for this activation.")
			: Missing("Unsafe Lua execution requires explicit EnableUnsafeLuaExecution opt-in for this activation.");
		ClientCapabilityEvidenceGate autoAssemblerPolicy = _policy.EnableAutoAssemblerPatches
			? Satisfied("Auto Assembler patches were explicitly enabled for this activation.")
			: Missing("Auto Assembler patches require explicit EnableAutoAssemblerPatches opt-in for this activation.");

		// Every capability is composed from its one catalog row; only the implementation reason (experimental or not),
		// the policy gate and the host gate vary.
		ImmutableArray<ClientCapabilityDescriptor> catalog = ClientCapabilityCatalog.Entries;
		ClientCapabilityAvailability[] capabilities = new ClientCapabilityAvailability[catalog.Length];
		for (int index = 0; index < catalog.Length; index++)
		{
			ClientCapabilityDescriptor entry = catalog[index];
			capabilities[index] = Describe(
				entry.Id,
				entry.ExperimentalDiagnosticId is { } experimental
					? Satisfied(ExperimentalImplementationReason(experimental))
					: implemented,
				package,
				entry.Host == CapabilityHostSource.SdkSelectedProcess ? selectedProcess : unprobedHost,
				HostQualificationGate.Evaluate(entry, _qualificationEvidence, qualificationContext, noQualificationEvidence),
				entry.Policy switch
				{
					CapabilityPolicySource.UnsafeLuaExecutionOptIn => unsafeLuaPolicy,
					CapabilityPolicySource.AutoAssemblerPatchesOptIn => autoAssemblerPolicy,
					_ => policyNotRequired
				},
				lifetime);
		}

		return ClientCapabilities.Create(capabilities);
	}

	/// <summary>Captures one snapshot for the public call <paramref name="operation" />, which names its failure.</summary>
	private bool TryCapture(string operation, out CheatEngineRuntimeSnapshot snapshot, out CheatEngineFailure failure,
		CancellationToken cancellationToken)
	{
		// The observations are read-only (Q45) and report their outcomes as statuses; an SDK fault (for example a
		// detached runtime) is returned as a failure, never thrown across a Try method.
		CheatEngineRuntimeSnapshot captured = default;
		if (!SdkBoundary.TryInvoke(_dispatcher, operation, () => captured = Capture(),
				CheatEngineHostEffect.Unknown, _lifetime, out failure, cancellationToken))
		{
			snapshot = default;
			return false;
		}

		snapshot = captured;
		_diagnostics.RuntimeSnapshotCaptured(captured.Epoch, captured.Platform.TargetArchitecture,
			captured.Platform.TargetBitness.Bytes, captured.Platform.ConfiguredPointerSizeBytes ?? 0,
			captured.Platform.ConfiguredPointerSizeDiffersFromBitness == true);
		return true;
	}

	/// <summary>The implementation gate reason of an operational capability whose public API is experimental.</summary>
	/// <param name="diagnosticId">The diagnostic id of the experimental API, for example <c>CECLIENT5001</c>.</param>
	/// <returns>The reason.</returns>
	internal static string ExperimentalImplementationReason(string diagnosticId)
	{
		return "The Client composes an operational adapter for this capability; its API is experimental (" +
			   diagnosticId + ") until its live scenarios pass.";
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
}
