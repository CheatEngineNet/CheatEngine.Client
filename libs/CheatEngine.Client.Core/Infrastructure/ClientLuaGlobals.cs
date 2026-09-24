using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>
///     Internal generated bindings for the CE globals that the Client binds directly instead of calling a high-level
///     CheatEngine.SDK service. Every binding is a frozen ADR-01 exception registered in the architecture ratchet
///     (<c>tests/CheatEngine.Client.Tests/Architecture/ArchitectureRatchetTests.cs</c>), which names its own removal
///     condition; the list only shrinks.
/// </summary>
internal static partial class ClientLuaGlobals
{
	[LuaGlobal("openProcess")]
	internal static partial void OpenProcess(long processId);

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
