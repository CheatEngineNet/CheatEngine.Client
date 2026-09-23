using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Internal generated bindings for CE globals that CheatEngine.SDK 1.0.0 does not expose as high-level services. Every
///     binding is a frozen ADR-01 exception registered in the architecture ratchet
///     (<c>tests/CheatEngine.Client.Tests/Architecture/ArchitectureRatchetTests.cs</c>), which names its own removal
///     condition; it is removed once the SDK 2.0 replacement it names ships.
/// </summary>
internal static partial class ClientLuaGlobals
{
	[LuaGlobal("getOpenedProcessID")]
	internal static partial long GetOpenedProcessId();

	[LuaGlobal("openProcess")]
	internal static partial void OpenProcess(long processId);

	[LuaGlobal("getCEVersion")]
	internal static partial double GetCheatEngineVersion();

	[LuaGlobal("getSystemArchitecture")]
	internal static partial int GetSystemArchitecture();

	[LuaGlobal("getABI")]
	internal static partial int GetTargetAbi();

	[LuaGlobal("targetIs64Bit")]
	internal static partial bool TargetIs64Bit();

	/// <summary>Reads whether the selected target belongs to the x86 ISA family (x86 or x64).</summary>
	/// <remarks>
	///     Temporary ADR-01 exception, frozen by the architecture ratchet; its removal condition is the SDK 2.0
	///     RuntimeProcessOperations.ObserveTargetArchitecture replacement. With no target opened, Cheat
	///     Engine still reports the x86 family, so callers read the opened process identifier first.
	/// </remarks>
	[LuaGlobal("targetIsX86")]
	internal static partial bool TargetIsX86();

	/// <summary>Reads whether the selected target belongs to the ARM ISA family (ARM32 or ARM64).</summary>
	/// <remarks>
	///     Temporary ADR-01 exception, frozen by the architecture ratchet; its removal condition is the SDK 2.0
	///     RuntimeProcessOperations.ObserveTargetArchitecture replacement.
	/// </remarks>
	[LuaGlobal("targetIsArm")]
	internal static partial bool TargetIsArm();

	/// <summary>Reads Cheat Engine's configured pointer size for the current attachment, as the raw integer.</summary>
	/// <remarks>
	///     Temporary ADR-01 exception, frozen by the architecture ratchet; its removal condition is the SDK 2.0
	///     RuntimeProcessOperations.TryGetConfiguredPointerSize replacement. The value is per-attachment
	///     state that any (re)attach resets, and it is independent of the target process width.
	/// </remarks>
	[LuaGlobal("getPointerSize")]
	internal static partial int GetConfiguredPointerSize();

	[LuaGlobal("loadTable")]
	internal static partial void LoadTable(string path, bool merge);

	[LuaGlobal("saveTable")]
	internal static partial void SaveTable(string path);

	[LuaGlobal("getNameFromAddress")]
	internal static partial bool TryGetNameFromAddress(nuint address, [MaybeNullWhen(false)] out string? name);

	[LuaGlobal("registerSymbol")]
	internal static partial void RegisterSymbol(string name, nuint address, bool doNotSave);

	[LuaGlobal("unregisterSymbol")]
	internal static partial void UnregisterSymbol(string name);
}
