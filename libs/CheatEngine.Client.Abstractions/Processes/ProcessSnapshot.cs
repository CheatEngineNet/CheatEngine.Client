using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Processes;

/// <summary>An immutable snapshot of the process currently selected in Cheat Engine.</summary>
/// <remarks>
///     <para>
///         The identifier, the backend, the architecture, the bitness and the configured pointer size are Cheat Engine
///         observations that CheatEngine.SDK reported. The start time is the creation time CheatEngine.SDK observed for
///         a local process together with the local backend; with the identifier it is the process incarnation. Name and
///         executable path are optional local operating-system enrichment. Start time, name and path describe a local
///         process only: a CEServer target, a file opened as a process or a target whose backend is not established
///         never has them, and they do not establish liveness.
///     </para>
///     <para>
///         Every value is stored as observed; none is derived from another. In particular the bitness is never derived
///         from the architecture or from the plugin's own process width, and the configured pointer size is never
///         taken for the bitness.
///     </para>
///     <para>
///         Cheat Engine's selected target is ambient: holding this snapshot does not stop the user, another plugin or a
///         script from selecting another process. <see cref="SelectionEpoch" /> reduces that risk for Client-owned
///         leases; it is not a transaction.
///     </para>
/// </remarks>
public readonly record struct ProcessSnapshot
{
	/// <summary>Creates a selected-process snapshot from copied host observations.</summary>
	/// <param name="id">The Cheat Engine selected process identifier.</param>
	/// <param name="name">The local process name, when the local catalog supplied one for a local process.</param>
	/// <param name="executablePath">The local executable path, when the local catalog supplied one for a local process.</param>
	/// <param name="backend">How Cheat Engine reaches the target, or unknown.</param>
	/// <param name="architecture">The ISA CheatEngine.SDK derived from Cheat Engine's family facts, or unknown.</param>
	/// <param name="bitness">The target bitness (<c>targetIs64Bit</c>), or unknown.</param>
	/// <param name="configuredPointerSizeBytes">
	///     The raw value of Cheat Engine's configured pointer size for the current attachment, or
	///     <see langword="null" /> when it was not observed. Any integer is kept.
	/// </param>
	/// <param name="startTimeUtc">
	///     The creation time CheatEngine.SDK observed for a local process, in UTC, or <see langword="null" />.
	/// </param>
	/// <param name="selectionEpoch">The target-selection epoch of the observation.</param>
	/// <exception cref="ArgumentException">
	///     <paramref name="name" /> or <paramref name="executablePath" /> is empty; a known
	///     <paramref name="architecture" /> and a known <paramref name="bitness" /> disagree;
	///     <paramref name="startTimeUtc" /> is not a UTC time; or local metadata or a start time is supplied for a
	///     backend other than <see cref="TargetBackend.LocalProcess" />.
	/// </exception>
	/// <exception cref="ArgumentOutOfRangeException">
	///     <paramref name="backend" /> is not a defined value, or <paramref name="selectionEpoch" /> is negative.
	/// </exception>
	public ProcessSnapshot(
		TargetProcessId id,
		string? name,
		string? executablePath,
		TargetBackend backend,
		CheatEngineArchitecture architecture,
		PointerSize bitness,
		int? configuredPointerSizeBytes,
		DateTimeOffset? startTimeUtc,
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

		if (!Enum.IsDefined(backend))
		{
			throw new ArgumentOutOfRangeException(nameof(backend), backend, "The target backend is not defined.");
		}

		if (backend != TargetBackend.LocalProcess && (name is not null || executablePath is not null ||
													   startTimeUtc is not null))
		{
			throw new ArgumentException(
				"Local process metadata and a start time describe a local process only, never another backend.",
				nameof(backend));
		}

		if (GetNaturalWidth(architecture) is { IsKnown: true } naturalWidth && bitness.IsKnown &&
			bitness.Bytes != naturalWidth.Bytes)
		{
			throw new ArgumentException("A known bitness must match the natural width of a known architecture.",
				nameof(bitness));
		}

		if (startTimeUtc is { Offset: var offset } && offset != TimeSpan.Zero)
		{
			throw new ArgumentException("A process start time must be expressed in UTC.", nameof(startTimeUtc));
		}

		ArgumentOutOfRangeException.ThrowIfNegative(selectionEpoch);

		Id = id;
		Name = name;
		ExecutablePath = executablePath;
		Backend = backend;
		Architecture = architecture;
		Bitness = bitness;
		ConfiguredPointerSizeBytes = configuredPointerSizeBytes;
		StartTimeUtc = startTimeUtc;
		SelectionEpoch = selectionEpoch;
	}

	/// <summary>Gets the selected process identifier, as observed from Cheat Engine.</summary>
	public TargetProcessId Id
	{
		get;
	}

	/// <summary>Gets the local process display name when the local catalog supplied one for a local process.</summary>
	public string? Name
	{
		get;
	}

	/// <summary>Gets the local executable path when the local catalog supplied one for a local process.</summary>
	public string? ExecutablePath
	{
		get;
	}

	/// <summary>
	///     Gets how Cheat Engine reaches the target: a local process, CEServer, or unknown when the backend fact was not
	///     established.
	/// </summary>
	public TargetBackend Backend
	{
		get;
	}

	/// <summary>
	///     Gets the ISA CheatEngine.SDK derived from Cheat Engine's <c>targetIsX86</c>/<c>targetIsArm</c> and
	///     <c>targetIs64Bit</c> facts, or unknown; never derived from the bitness alone.
	/// </summary>
	public CheatEngineArchitecture Architecture
	{
		get;
	}

	/// <summary>
	///     Gets the target bitness (<c>targetIs64Bit</c>, the width Cheat Engine's <c>readPointer</c> follows), or
	///     unknown. It is stored as observed and is not Cheat Engine's configured pointer size.
	/// </summary>
	public PointerSize Bitness
	{
		get;
	}

	/// <summary>
	///     Gets the raw value of Cheat Engine's configured pointer size (<c>getPointerSize</c>) for the current
	///     attachment, or <see langword="null" /> when it was not observed.
	/// </summary>
	/// <remarks>Any (re)attach resets it; it can hold a value other than 4 or 8.</remarks>
	public int? ConfiguredPointerSizeBytes
	{
		get;
	}

	/// <summary>Gets the configured pointer size as a width when it is 4 or 8 bytes; otherwise unknown.</summary>
	public PointerSize ConfiguredPointerSize => ConfiguredPointerSizeBytes switch
	{
		sizeof(uint) => PointerSize.Bit32,
		sizeof(ulong) => PointerSize.Bit64,
		_ => PointerSize.Unknown
	};

	/// <summary>
	///     Gets whether the configured pointer size differs from <see cref="Bitness" />, or <see langword="null" /> when
	///     either value is unknown.
	/// </summary>
	public bool? ConfiguredPointerSizeDiffersFromBitness =>
		ConfiguredPointerSizeBytes is { } configured && Bitness.IsKnown ? configured != Bitness.Bytes : null;

	/// <summary>
	///     Gets the creation time CheatEngine.SDK observed for a local process, in UTC, or <see langword="null" />. With
	///     <see cref="Id" /> it identifies the process incarnation.
	/// </summary>
	public DateTimeOffset? StartTimeUtc
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
