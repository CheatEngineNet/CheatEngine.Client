using CheatEngine.SDK.Engine.Runtime;

namespace CheatEngine.Client.Runtime;

/// <summary>Immutable host and target platform observations captured during one active Cheat Engine activation.</summary>
/// <remarks>
///     <para>
///         Every fact is one Cheat Engine global that CheatEngine.SDK read, and none is inferred from another (audit F08):
///         the host operating system and architecture, the bitness of Cheat Engine itself, the target backend, the
///         target ISA, the target bitness, the target ABI, whether the target is Android, and Cheat Engine's configured
///         pointer size. Each fact names its subject (<c>Host</c>, <c>CheatEngine</c>, <c>Target</c>), and a fact that
///         was not observed stays unknown or <see langword="null" />.
///     </para>
///     <para>
///         Cheat Engine's configured pointer size is per-attachment state: any (re)attach resets it to the target
///         default, so a Client attach silently undoes an earlier override. Cheat Engine's <c>readPointer</c> follows the
///         bitness, not the configured size, and the configured size never changes <see cref="TargetBitness" />.
///     </para>
/// </remarks>
public readonly record struct CheatEngineRuntimePlatformInfo
{
	/// <summary>Creates runtime platform observations from copied host facts.</summary>
	/// <param name="hostOperatingSystem">The operating system Cheat Engine reports (<c>getOperatingSystem</c>).</param>
	/// <param name="hostArchitecture">The Cheat Engine host architecture (<c>getSystemArchitecture</c>).</param>
	/// <param name="cheatEngineBitness">
	///     The bitness of the Cheat Engine process itself (<c>cheatEngineIs64Bit</c>), or unknown.
	/// </param>
	/// <param name="targetBackend">How Cheat Engine reaches the selected target, or unknown.</param>
	/// <param name="targetArchitecture">The target ISA CheatEngine.SDK derived from the family facts, or unknown.</param>
	/// <param name="targetBitness">The target bitness (<c>targetIs64Bit</c>), or unknown.</param>
	/// <param name="targetAbi">The target ABI (<c>getABI</c>), or unknown.</param>
	/// <param name="targetIsAndroid">
	///     Whether the target is Android (<c>targetIsAndroid</c>), or <see langword="null" /> when unknown.
	/// </param>
	/// <param name="configuredPointerSizeBytes">
	///     The raw value of Cheat Engine's configured pointer size, or <see langword="null" /> when it was not observed.
	///     Any integer is kept, because Cheat Engine accepts any integer as its configured pointer size.
	/// </param>
	/// <exception cref="ArgumentException">
	///     <paramref name="targetArchitecture" /> and <paramref name="targetBitness" /> are both known and the bitness
	///     differs from the natural width of that architecture (4 bytes for X86 and Arm32, 8 bytes for X64 and Arm64).
	/// </exception>
	public CheatEngineRuntimePlatformInfo(
		CheatEngineOperatingSystem hostOperatingSystem,
		CheatEngineArchitecture hostArchitecture,
		PointerSize cheatEngineBitness,
		TargetBackend targetBackend,
		CheatEngineArchitecture targetArchitecture,
		PointerSize targetBitness,
		TargetAbi targetAbi,
		bool? targetIsAndroid,
		int? configuredPointerSizeBytes)
	{
		if (GetNaturalWidth(targetArchitecture) is { } naturalBytes && targetBitness.IsKnown &&
			targetBitness.Bytes != naturalBytes)
		{
			throw new ArgumentException(
				"A known target bitness must match the natural width of a known target architecture.",
				nameof(targetBitness));
		}

		HostOperatingSystem = hostOperatingSystem;
		HostArchitecture = hostArchitecture;
		CheatEngineBitness = cheatEngineBitness;
		TargetBackend = targetBackend;
		TargetArchitecture = targetArchitecture;
		TargetBitness = targetBitness;
		TargetAbi = targetAbi;
		TargetIsAndroid = targetIsAndroid;
		ConfiguredPointerSizeBytes = configuredPointerSizeBytes;
	}

	/// <summary>Gets the operating system Cheat Engine reports it runs on, or unknown.</summary>
	public CheatEngineOperatingSystem HostOperatingSystem
	{
		get;
	}

	/// <summary>Gets the Cheat Engine host architecture (<c>getSystemArchitecture</c>), or unknown.</summary>
	public CheatEngineArchitecture HostArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets the bitness of the Cheat Engine process itself (<c>cheatEngineIs64Bit</c>): <see cref="PointerSize.Bit64" />
	///     or <see cref="PointerSize.Bit32" /> as observed, <see cref="PointerSize.Unknown" /> when it was not observed;
	///     never derived from <see cref="HostArchitecture" />.
	/// </summary>
	public PointerSize CheatEngineBitness
	{
		get;
	}

	/// <summary>
	///     Gets how Cheat Engine reaches the selected target: a local process, a file opened as a process, CEServer, or
	///     unknown when no target is selected or the backend fact is not established.
	/// </summary>
	public TargetBackend TargetBackend
	{
		get;
	}

	/// <summary>
	///     Gets the target ISA CheatEngine.SDK derived from Cheat Engine's <c>targetIsX86</c>/<c>targetIsArm</c> family
	///     facts and its <c>targetIs64Bit</c> fact; unknown when a fact is missing or the families are contradictory.
	///     It is never derived from the 64-bit fact alone.
	/// </summary>
	public CheatEngineArchitecture TargetArchitecture
	{
		get;
	}

	/// <summary>
	///     Gets the target bitness (<c>targetIs64Bit</c>, the width Cheat Engine's <c>readPointer</c> follows); not Cheat
	///     Engine's configured pointer size. Unknown when no target is selected or the fact was not observed.
	/// </summary>
	public PointerSize TargetBitness
	{
		get;
	}

	/// <summary>Gets the target ABI (<c>getABI</c>), or unknown.</summary>
	public TargetAbi TargetAbi
	{
		get;
	}

	/// <summary>Gets whether the target is Android (<c>targetIsAndroid</c>), or <see langword="null" /> when unknown.</summary>
	public bool? TargetIsAndroid
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
	///     Gets whether Cheat Engine's configured pointer size differs from <see cref="TargetBitness" /> (audit Q31.a),
	///     or <see langword="null" /> when either value is unknown.
	/// </summary>
	public bool? ConfiguredPointerSizeDiffersFromBitness =>
		ConfiguredPointerSizeBytes is { } configured && TargetBitness.IsKnown
			? configured != TargetBitness.Bytes
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
