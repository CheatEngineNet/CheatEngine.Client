using CheatEngine.Client.Core.Infrastructure;

namespace CheatEngine.Client.Core.Domains;

/// <summary>Internal boundary for Cheat Engine's table file primitives, after the Client trust policy admitted the path.</summary>
/// <remarks>
///     The file form of Cheat Engine's <c>loadTable</c> has no option to suppress the table's Lua script prompt, so a
///     table with scripts may prompt or execute Lua. A refused path is never retried through another overload and never
///     turned into a stream (audit A14-27, A14-41).
/// </remarks>
internal interface ITableFilePort
{
	/// <summary>Loads a table file into the current Address List, merging or replacing it.</summary>
	public void LoadTable(string path, bool merge);

	/// <summary>Saves the current table to a file.</summary>
	public void SaveTable(string path);
}

/// <summary>Production table-file port over the existing generated bindings; it adds no new binding.</summary>
internal sealed class SdkTableFilePort : ITableFilePort
{
	private SdkTableFilePort()
	{
	}

	internal static SdkTableFilePort Instance
	{
		get;
	} = new();

	public void LoadTable(string path, bool merge)
	{
		ClientLuaGlobals.LoadTable(path, merge);
	}

	public void SaveTable(string path)
	{
		ClientLuaGlobals.SaveTable(path);
	}
}
