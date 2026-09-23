using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Processes;

/// <summary>An immutable snapshot of the process currently selected in Cheat Engine.</summary>
/// <remarks>
///     <para>
///         The identifier, the architecture and the process width are Cheat Engine observations. Name and executable
///         path are optional local BCL enrichment: they describe a local process only, never a CEServer target or a file
///         opened as a process, and they do not establish liveness or authoritative target provenance.
///     </para>
///     <para>
///         Cheat Engine's selected target is ambient: holding this snapshot does not stop the user, another plugin or a
///         script from selecting another process. <see cref="SelectionEpoch" /> reduces that risk for Client-owned
///         leases; it is not a transaction.
///     </para>
/// </remarks>
public readonly record struct ProcessSnapshot
{
	/// <summary>Creates a selected-process snapshot without a target-architecture observation.</summary>
	public ProcessSnapshot(TargetProcessId id, string? name, string? executablePath)
		: this(id, name, executablePath, CheatEngineArchitecture.Unknown, PointerSize.Unknown, 0)
	{
	}

	/// <summary>Creates a selected-process snapshot whose process width is the natural width of its architecture.</summary>
	/// <remarks>
	///     Kept for source compatibility: the width is 4 bytes for X86 and Arm32, 8 bytes for X64 and Arm64, and unknown
	///     for an unknown architecture. Use the constructor that takes the observed width when the ISA is unknown.
	/// </remarks>
	public ProcessSnapshot(
		TargetProcessId id,
		string? name,
		string? executablePath,
		CheatEngineArchitecture targetArchitecture,
		long selectionEpoch)
		: this(id, name, executablePath, targetArchitecture, GetNaturalWidth(targetArchitecture), selectionEpoch)
	{
	}

	/// <summary>Creates a selected-process snapshot from copied host observations.</summary>
	/// <param name="id">The Cheat Engine opened process identifier.</param>
	/// <param name="name">The local process name, when the local catalog supplied one.</param>
	/// <param name="executablePath">The local executable path, when the local catalog supplied one.</param>
	/// <param name="targetArchitecture">The ISA derived from Cheat Engine's family facts, or unknown.</param>
	/// <param name="targetPointerSize">The process width observed from Cheat Engine's 64-bit fact, or unknown.</param>
	/// <param name="selectionEpoch">The target-selection epoch of the observation.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="name" /> or <paramref name="executablePath" /> is empty, or a known
	///     <paramref name="targetArchitecture" /> and a known <paramref name="targetPointerSize" /> disagree.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException"><paramref name="selectionEpoch" /> is negative.</exception>
	public ProcessSnapshot(
		TargetProcessId id,
		string? name,
		string? executablePath,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		long selectionEpoch)
	{
		if (name is { Length: 0 })
		{
			throw new ArgumentException("A process name must be null or non-empty.", nameof(name));
		}

		if (executablePath is { Length: 0 })
		{
			throw new ArgumentException("An executable path must be null or non-empty.", nameof(executablePath));
		}

		if (GetNaturalWidth(targetArchitecture) is { IsKnown: true } naturalWidth && targetPointerSize.IsKnown &&
			targetPointerSize.Bytes != naturalWidth.Bytes)
		{
			throw new ArgumentException(
				"A known target pointer size must match the natural width of a known target architecture.",
				nameof(targetPointerSize));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(selectionEpoch);

		Id = id;
		Name = name;
		ExecutablePath = executablePath;
		TargetArchitecture = targetArchitecture;
		TargetPointerSize = targetPointerSize;
		SelectionEpoch = selectionEpoch;
	}

	/// <summary>Gets the selected process identifier, as observed from Cheat Engine.</summary>
	public TargetProcessId Id
	{
		get;
	}

	/// <summary>Gets the local process display name when the local catalog supplied one.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the local executable path when the local catalog supplied one.</summary>
	public string? ExecutablePath
	{
		get;
	}

	/// <summary>
	///     Gets the target ISA derived from Cheat Engine's <c>targetIsX86</c>/<c>targetIsArm</c> and
	///     <c>targetIs64Bit</c> facts, or unknown when no probe established it.
	/// </summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets the process width observed from Cheat Engine's <c>targetIs64Bit</c>, or unknown. It is stored as observed,
	///     not derived from <see cref="TargetArchitecture" />, and it is not Cheat Engine's configured pointer size.
	/// </summary>
	public PointerSize TargetPointerSize
	{
		get;
	}

	/// <summary>
	///     Gets the target-selection epoch. A change means target-bound sessions and leases captured for an earlier
	///     selection are no longer valid.
	/// </summary>
	public long SelectionEpoch
	{
		get;
	}

	private static PointerSize GetNaturalWidth(CheatEngineArchitecture architecture)
	{
		return architecture switch
		{
			CheatEngineArchitecture.X86 or CheatEngineArchitecture.Arm32 => PointerSize.Bit32,
			CheatEngineArchitecture.X64 or CheatEngineArchitecture.Arm64 => PointerSize.Bit64,
			_ => PointerSize.Unknown
		};
	}
}
