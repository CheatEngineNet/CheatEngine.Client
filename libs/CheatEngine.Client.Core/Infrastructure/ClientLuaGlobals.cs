using System.Diagnostics.CodeAnalysis;

using CheatEngine.SDK.Annotations.Lua;

namespace CheatEngine.Client.Core.Infrastructure;

/// <summary>Internal generated bindings for CE globals that the SDK does not expose as high-level services.</summary>
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
