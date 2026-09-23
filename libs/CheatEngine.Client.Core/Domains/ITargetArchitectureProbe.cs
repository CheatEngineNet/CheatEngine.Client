namespace CheatEngine.Client.Core.Domains;

/// <summary>
///     Internal, read-only port for the Cheat Engine facts that describe the selected target: its identifier, its
///     process width, its ISA family and Cheat Engine's configured pointer size.
/// </summary>
/// <remarks>
///     <para>
///         This port is the single source of target facts for the Runtime, Processes and Memory domains. It never selects,
///         opens, pauses or configures a target, and it never changes Cheat Engine's configured pointer size: every
///         member is an observation (audit ADR-09a, Q45).
///     </para>
///     <para>
///         With no target opened, Cheat Engine reports the same family, width and pointer size as an x64 target, so a
///         caller always reads <see cref="GetOpenedProcessId" /> first (spike C3 D2). The production implementations use
///         temporary, frozen ADR-01 bindings registered in the architecture ratchet
///         (<c>tests/CheatEngine.Client.Tests/Architecture/ArchitectureRatchetTests.cs</c>), which names the SDK 2.0
///         replacement that retires each one.
///     </para>
/// </remarks>
internal interface ITargetArchitectureProbe
{
	/// <summary>Reads Cheat Engine's opened process identifier; zero means that no target is selected.</summary>
	public long GetOpenedProcessId();

	/// <summary>Reads whether the selected target process is 64-bit (its process width).</summary>
	public bool TargetIs64Bit();

	/// <summary>Reads whether the selected target belongs to the x86 ISA family.</summary>
	public bool TargetIsX86();

	/// <summary>Reads whether the selected target belongs to the ARM ISA family.</summary>
	public bool TargetIsArm();

	/// <summary>Reads Cheat Engine's configured pointer size for the current attachment, unvalidated.</summary>
	public int GetConfiguredPointerSize();
}
