using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable platform observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     <para>
///         The host architecture, the target ISA, the target process width, the target ABI and Cheat Engine's configured
///         pointer size are independent observations (audit F08). A fact that was not observed stays unknown; none of
///         them is inferred from another.
///     </para>
///     <para>
///         Cheat Engine's configured pointer size is per-attachment state: any (re)attach resets it to the target
///         default, so a Client attach silently undoes an earlier override. What it affects besides the value that
///         Cheat Engine reports is not established, and it never changes <see cref="TargetPointerSize" />.
///     </para>
/// </remarks>
public readonly record struct CheatEngineRuntimePlatformInfo
{
	/// <summary>Creates runtime platform observations from copied host facts, without a configured pointer size.</summary>
	/// <exception cref="ArgumentException">
	///     <paramref name="targetArchitecture" /> and <paramref name="targetPointerSize" /> are both known and the width
	///     differs from the natural width of that architecture.
	/// </exception>
	public CheatEngineRuntimePlatformInfo(
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		TargetAbi targetAbi)
		: this(systemArchitecture, targetArchitecture, targetPointerSize, targetAbi, null)
	{
	}

	/// <summary>Creates runtime platform observations from copied host facts.</summary>
	/// <param name="systemArchitecture">The Cheat Engine host architecture.</param>
	/// <param name="targetArchitecture">The target ISA derived from the observed ISA families, or unknown.</param>
	/// <param name="targetPointerSize">The target process width, or unknown.</param>
	/// <param name="targetAbi">The target ABI, or unknown.</param>
	/// <param name="configuredPointerSizeBytes">
	///     The raw value of Cheat Engine's configured pointer size, or <see langword="null" /> when it was not observed.
	///     Any integer is kept, because Cheat Engine accepts any integer as its configured pointer size.
	/// </param>
	/// <exception cref="ArgumentException">
	///     <paramref name="targetArchitecture" /> and <paramref name="targetPointerSize" /> are both known and the width
	///     differs from the natural width of that architecture (4 bytes for X86 and Arm32, 8 bytes for X64 and Arm64).
	/// </exception>
	public CheatEngineRuntimePlatformInfo(
		CheatEngineArchitecture systemArchitecture,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetPointerSize,
		TargetAbi targetAbi,
		int? configuredPointerSizeBytes)
	{
		if (GetNaturalWidth(targetArchitecture) is { } naturalBytes && targetPointerSize.IsKnown &&
			targetPointerSize.Bytes != naturalBytes)
		{
			throw new ArgumentException(
				"A known target pointer size must match the natural width of a known target architecture.",
				nameof(targetPointerSize));
		}

		SystemArchitecture = systemArchitecture;
		TargetArchitecture = targetArchitecture;
		TargetPointerSize = targetPointerSize;
		TargetAbi = targetAbi;
		ConfiguredPointerSizeBytes = configuredPointerSizeBytes;
	}

	/// <summary>Gets the CE host architecture observed from CE's system-architecture global.</summary>
	public CheatEngineArchitecture SystemArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets the target ISA derived from Cheat Engine's <c>targetIsX86</c>/<c>targetIsArm</c> family facts and its
	///     <c>targetIs64Bit</c> fact; unknown when a fact is missing or the families are contradictory. It is never
	///     derived from the 64-bit fact alone.
	/// </summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets the process width observed from Cheat Engine's <c>targetIs64Bit</c> for the selected target (the width
	///     Cheat Engine's <c>readPointer</c> uses); not Cheat Engine's configured pointer size. Unknown when no target is
	///     selected or the fact was not observed.
	/// </summary>
	public PointerSize TargetPointerSize
	{
		get;
	}

	/// <summary>Gets the target ABI observed from CE's ABI global, or unknown.</summary>
	public TargetAbi TargetAbi
	{
		get;
	}

	/// <summary>
	///     Gets the raw value of Cheat Engine's configured pointer size (<c>getPointerSize</c>) for the current
	///     attachment, or <see langword="null" /> when it was not observed.
	/// </summary>
	/// <remarks>
	///     This is per-attachment Cheat Engine state that any (re)attach resets; what it affects besides the value that
	///     Cheat Engine reports is not established. It can hold a value other than 4 or 8.
	/// </remarks>
	public int? ConfiguredPointerSizeBytes
	{
		get;
	}

	/// <summary>
	///     Gets Cheat Engine's configured pointer size as a width when it is 4 or 8 bytes; unknown when it was not observed
	///     or holds another value.
	/// </summary>
	public PointerSize ConfiguredPointerSize => ConfiguredPointerSizeBytes switch
	{
		sizeof(uint) => PointerSize.Bit32,
		sizeof(ulong) => PointerSize.Bit64,
		_ => PointerSize.Unknown
	};

	/// <summary>
	///     Gets whether Cheat Engine's configured pointer size differs from <see cref="TargetPointerSize" />, or
	///     <see langword="null" /> when either value is unknown.
	/// </summary>
	public bool? ConfiguredPointerSizeDiffersFromTargetPointerSize =>
		ConfiguredPointerSizeBytes is { } configured && TargetPointerSize.IsKnown
			? configured != TargetPointerSize.Bytes
			: null;

	private static int? GetNaturalWidth(CheatEngineArchitecture architecture)
	{
		return architecture switch
		{
			CheatEngineArchitecture.X86 or CheatEngineArchitecture.Arm32 => sizeof(uint),
			CheatEngineArchitecture.X64 or CheatEngineArchitecture.Arm64 => sizeof(ulong),
			_ => null
		};
	}
}
