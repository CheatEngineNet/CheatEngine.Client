using CheatEngine.SDK.Engine.Tables;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Production table-file port over CheatEngine.SDK's <see cref="CheatTableFiles" />.</summary>
/// <remarks>
///     CheatEngine.SDK passes the path to Cheat Engine unchanged and applies no file-root policy: the Client's
///     <c>AllowedTableRoots</c> policy has already admitted it. While <see cref="CheatTableFiles.TryLoad" /> runs, the
///     SDK refuses address-list mutations issued from the same thread (a script of the table being loaded) with
///     <c>TableLoadInProgress</c>.
/// </remarks>
internal sealed class SdkTableFilePort : ITableFilePort
{
	private SdkTableFilePort()
	{
	}

	internal static SdkTableFilePort Instance
	{
		get;
	} = new();

	public LuaOperationStatus TryLoad(string path, bool merge)
	{
		return CheatTableFiles.TryLoad(path, merge);
	}

	public LuaOperationStatus TrySave(string path)
	{
		return CheatTableFiles.TrySave(path);
	}
}
