using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Assembly;

/// <summary>The jump and call encoding that Cheat Engine should prefer when it assembles one instruction.</summary>
/// <remarks>
///     <para>
///         An option enum: <see cref="None" /> (0) is the default and lets Cheat Engine choose the encoding. The other
///         values are passed to Cheat Engine's <c>assemble</c> unchanged through CheatEngine.SDK, as Cheat Engine's own
///         <c>apShort</c>, <c>apLong</c> and <c>apFar</c> preferences. A preference only changes the encoding Cheat
///         Engine picks for a relative jump or call; it never reconfigures Cheat Engine's assembler.
///     </para>
///     <para>Values are stable; a minor release can add one.</para>
/// </remarks>
[Experimental("CECLIENT5003", UrlFormat = "https://github.com/CheatEngineNet/CheatEngine.Client/blob/main/libs/CheatEngine.Client.Abstractions/README.md#{0}")]
[SuppressMessage("Naming", "CA1720:Identifiers should not contain type names",
	Justification = "The members mirror Cheat Engine's apShort, apLong and apFar jump preferences.")]
public enum InstructionEncodingPreference
{
	/// <summary>No preference: Cheat Engine chooses the encoding.</summary>
	None = 0,

	/// <summary>Prefer the short encoding of a relative jump or call.</summary>
	Short = 1,

	/// <summary>Prefer the long encoding of a relative jump or call.</summary>
	Long = 2,

	/// <summary>Prefer the far encoding of a jump or call.</summary>
	Far = 3
}
