namespace CheatEngine.Client.Inspection;

/// <summary>Selects how Cheat Engine resolves one address expression in the target process's symbol table.</summary>
/// <remarks>
///     The mode is forwarded, through CheatEngine.SDK and without reinterpretation, as the optional <c>shallow</c> argument
///     of Cheat Engine's <c>getAddressSafe</c>; its effect on the lookup is Cheat Engine's. Resolution always queries the
///     target process; resolving in Cheat Engine's own process is not offered.
/// </remarks>
public enum AddressResolutionMode
{
	/// <summary>Cheat Engine's ordinary resolution: <c>shallow</c> is <see langword="false" />.</summary>
	Default = 0,

	/// <summary>Cheat Engine's shallow resolution: <c>shallow</c> is <see langword="true" />.</summary>
	Shallow = 1
}
