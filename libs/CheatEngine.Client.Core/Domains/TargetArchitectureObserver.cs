using CheatEngine.Client.Runtime;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Observes the facts of the selected target in one PID-bracketed sequence and derives its ISA without inference
///     from the 64-bit fact alone (audit F08, A10-17, Q31, Q32; spike C3 D2 and D3).
/// </summary>
/// <remarks>
///     <para>
///         Order: opened PID, then <c>targetIs64Bit</c>, <c>targetIsX86</c>, <c>targetIsArm</c>, optionally
///         <c>getPointerSize</c>, then the opened PID again. A PID outside [1, <see cref="int.MaxValue" />] (no target,
///         a negative or malformed value, or the file-as-process sentinel 4294967295) stops the sequence before any
///         target fact is read, because Cheat Engine reports x86 family, 64-bit and pointer size 8 when no target is
///         opened. A second PID that does not confirm the first discards every target fact.
///     </para>
///     <para>
///         The ISA is derived from the observed families: x86 family and 64-bit is X64, x86 family and 32-bit is X86,
///         ARM family and 64-bit is Arm64, ARM family and 32-bit is Arm32. Contradictory families, no known family, or a
///         missing fact leave the ISA unknown. The process width comes from <c>targetIs64Bit</c> independently of the ISA
///         result; the configured pointer size is kept raw and never replaces the process width.
///     </para>
/// </remarks>
internal static class TargetArchitectureObserver
{
	internal const string NoTargetReason = "No target process is selected, so target facts were not probed.";

	internal const string UnestablishedTargetReason =
		"The opened process identifier was not established as a local target, so target facts were not probed.";

	internal const string TargetChangedReason =
		"The selected target changed during observation, so its facts were discarded.";

	internal const string ConfiguredPointerSizeNotRequestedReason =
		"The configured pointer size was not requested by this observation.";

	/// <summary>Reads the opened PID, then observes the facts of the selected target.</summary>
	internal static ObservedTargetArchitecture Observe(ITargetArchitectureProbe probe, bool readConfiguredPointerSize)
	{
		ArgumentNullException.ThrowIfNull(probe);
		ProbeResult<long> processId = ValidateProcessId(ProbeClassifier.Probe(probe.GetOpenedProcessId));
		return ObserveFrom(probe, processId, readConfiguredPointerSize);
	}

	/// <summary>Observes the facts of a target whose PID the caller has just read as the opening of the bracket.</summary>
	internal static ObservedTargetArchitecture ObserveSelected(ITargetArchitectureProbe probe, long processId,
		bool readConfiguredPointerSize)
	{
		ArgumentNullException.ThrowIfNull(probe);
		return ObserveFrom(probe, ValidateProcessId(ProbeResult<long>.Available(processId)), readConfiguredPointerSize);
	}

	/// <summary>Returns the established selected-target PID, or <see langword="null" /> when no local target is selected.</summary>
	internal static int? GetSelectedTarget(ProbeResult<long> processId)
	{
		return processId.HasValue && processId.Value is > 0 and <= int.MaxValue ? (int) processId.Value : null;
	}

	/// <summary>Classifies an opened PID outside the supported range as malformed; zero stays a valid "no target".</summary>
	internal static ProbeResult<long> ValidateProcessId(ProbeResult<long> processId)
	{
		return processId.HasValue && processId.Value is < 0 or > int.MaxValue
			? ProbeResult<long>.Malformed(
				"Cheat Engine returned an opened process identifier outside the supported PID range.")
			: processId;
	}

	/// <summary>Maps a raw configured pointer size to a known width; any other value stays unknown.</summary>
	internal static PointerSize ToKnownPointerSize(int? bytes)
	{
		return bytes switch
		{
			sizeof(uint) => PointerSize.Bit32,
			sizeof(ulong) => PointerSize.Bit64,
			_ => PointerSize.Unknown
		};
	}

	private static ObservedTargetArchitecture ObserveFrom(ITargetArchitectureProbe probe, ProbeResult<long> processId,
		bool readConfiguredPointerSize)
	{
		if (GetSelectedTarget(processId) is not { } selectedTarget)
		{
			return Unobserved(processId, false, ProbeResult<bool>.Unknown(
				processId.HasValue && processId.Value == 0 ? NoTargetReason : UnestablishedTargetReason));
		}

		ProbeResult<bool> is64Bit = ProbeClassifier.Probe(probe.TargetIs64Bit);
		ProbeResult<bool> isX86Family = ProbeClassifier.Probe(probe.TargetIsX86);
		ProbeResult<bool> isArmFamily = ProbeClassifier.Probe(probe.TargetIsArm);
		ProbeResult<int> configuredPointerSize = readConfiguredPointerSize
			? ProbeClassifier.Probe(probe.GetConfiguredPointerSize)
			: ProbeResult<int>.Unknown(ConfiguredPointerSizeNotRequestedReason);
		ProbeResult<long> confirmation = ProbeClassifier.Probe(probe.GetOpenedProcessId);
		if (!confirmation.HasValue || confirmation.Value != selectedTarget)
		{
			return Unobserved(processId, true, ProbeResult<bool>.Faulted(TargetChangedReason));
		}

		PointerSize processPointerSize = !is64Bit.HasValue
			? PointerSize.Unknown
			: is64Bit.Value
				? PointerSize.Bit64
				: PointerSize.Bit32;
		ProbeResult<CheatEngineArchitecture> architecture = DeriveArchitecture(is64Bit, isX86Family, isArmFamily);
		int? configuredBytes = configuredPointerSize.HasValue ? configuredPointerSize.Value : null;
		PointerSize configuredKnown = ToKnownPointerSize(configuredBytes);
		if (configuredBytes is { } raw && !configuredKnown.IsKnown)
		{
			// setPointerSize accepts any integer (spike C3 D3(b)): keep the raw value, never map it to a width.
			configuredPointerSize = ProbeResult<int>.Malformed($"Configured pointer size {raw} is outside 4 and 8.");
		}

		return new ObservedTargetArchitecture(
			processId,
			true,
			false,
			is64Bit,
			isX86Family,
			isArmFamily,
			configuredPointerSize,
			architecture,
			architecture.HasValue ? architecture.Value : CheatEngineArchitecture.Unknown,
			processPointerSize,
			configuredBytes,
			configuredKnown);
	}

	private static ProbeResult<CheatEngineArchitecture> DeriveArchitecture(ProbeResult<bool> is64Bit,
		ProbeResult<bool> isX86Family, ProbeResult<bool> isArmFamily)
	{
		// An unavailable fact wins: its evidence explains why the ISA stays unknown.
		foreach (ProbeResult<bool> fact in (ReadOnlySpan<ProbeResult<bool>>) [is64Bit, isX86Family, isArmFamily])
		{
			if (fact.Evidence.State == ClientCapabilityEvidenceState.Missing)
			{
				return new ProbeResult<CheatEngineArchitecture>(fact.Evidence, CheatEngineArchitecture.Unknown);
			}
		}

		foreach (ProbeResult<bool> fact in (ReadOnlySpan<ProbeResult<bool>>) [is64Bit, isX86Family, isArmFamily])
		{
			if (!fact.HasValue)
			{
				return new ProbeResult<CheatEngineArchitecture>(fact.Evidence, CheatEngineArchitecture.Unknown);
			}
		}

		return (isX86Family.Value, isArmFamily.Value) switch
		{
			(true, false) => ProbeResult<CheatEngineArchitecture>.Available(
				is64Bit.Value ? CheatEngineArchitecture.X64 : CheatEngineArchitecture.X86),
			(false, true) => ProbeResult<CheatEngineArchitecture>.Available(
				is64Bit.Value ? CheatEngineArchitecture.Arm64 : CheatEngineArchitecture.Arm32),
			(true, true) => ProbeResult<CheatEngineArchitecture>.Malformed(
				"Cheat Engine reported contradictory ISA families (both x86 and ARM)."),
			_ => ProbeResult<CheatEngineArchitecture>.Unknown(
				"Cheat Engine reported a target ISA family outside x86 and ARM.")
		};
	}

	private static ObservedTargetArchitecture Unobserved(ProbeResult<long> processId, bool targetChanged,
		ProbeResult<bool> reason)
	{
		ProbeResult<int> configured = new(reason.Evidence, default);
		return new ObservedTargetArchitecture(
			processId,
			false,
			targetChanged,
			reason,
			reason,
			reason,
			configured,
			new ProbeResult<CheatEngineArchitecture>(reason.Evidence, CheatEngineArchitecture.Unknown),
			CheatEngineArchitecture.Unknown,
			PointerSize.Unknown,
			null,
			PointerSize.Unknown);
	}
}

/// <summary>The copied facts of one PID-bracketed target observation, with the evidence of each fact.</summary>
/// <param name="ProcessId">The opening PID read and its evidence.</param>
/// <param name="HasTarget">Whether a local target was selected and confirmed by the closing PID read.</param>
/// <param name="TargetChangedDuringObservation">Whether the closing PID read did not confirm the opening one.</param>
/// <param name="Is64Bit">The <c>targetIs64Bit</c> fact.</param>
/// <param name="IsX86Family">The <c>targetIsX86</c> fact.</param>
/// <param name="IsArmFamily">The <c>targetIsArm</c> fact.</param>
/// <param name="ConfiguredPointerSize">The <c>getPointerSize</c> fact; malformed when outside 4 and 8.</param>
/// <param name="ArchitectureEvidence">The evidence of the ISA derivation.</param>
/// <param name="Architecture">The derived ISA, or unknown.</param>
/// <param name="ProcessPointerSize">The process width from <c>targetIs64Bit</c>, or unknown.</param>
/// <param name="ConfiguredPointerSizeBytes">The raw configured pointer size, or <see langword="null" /> when unobserved.</param>
/// <param name="ConfiguredPointerSizeKnown">The configured size as a width when it is 4 or 8, otherwise unknown.</param>
internal readonly record struct ObservedTargetArchitecture(
	ProbeResult<long> ProcessId,
	bool HasTarget,
	bool TargetChangedDuringObservation,
	ProbeResult<bool> Is64Bit,
	ProbeResult<bool> IsX86Family,
	ProbeResult<bool> IsArmFamily,
	ProbeResult<int> ConfiguredPointerSize,
	ProbeResult<CheatEngineArchitecture> ArchitectureEvidence,
	CheatEngineArchitecture Architecture,
	PointerSize ProcessPointerSize,
	int? ConfiguredPointerSizeBytes,
	PointerSize ConfiguredPointerSizeKnown)
{
	/// <summary>
	///     Gets whether an observed configured pointer size differs from a known process width; <see langword="false" />
	///     when either side is unknown (no evidence of a mismatch).
	/// </summary>
	internal bool ConfiguredPointerSizeDiffersFromProcessWidth =>
		ConfiguredPointerSizeBytes is { } configured && ProcessPointerSize.IsKnown &&
		configured != ProcessPointerSize.Bytes;
}
