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
	[LuaGlobal("loadTable")]
	internal static partial void LoadTable(string path, bool merge);

	[LuaGlobal("saveTable")]
	internal static partial void SaveTable(string path);
}
