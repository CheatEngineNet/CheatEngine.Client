using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Client.Assembly;

/// <summary>Describes an Auto Assembler script supplied to <see cref="IAutoAssemblerClient" />.</summary>
[Experimental(ClientExperimentalDiagnostics.AutoAssemblerPatches, UrlFormat = ClientExperimentalDiagnostics.UrlFormat)]
public readonly record struct AutoAssemblerScript
{
	/// <summary>Creates an Auto Assembler script request.</summary>
	/// <param name="source">The complete Auto Assembler source.</param>
	/// <param name="name">A diagnostic name for the patch, or <see langword="null" /> for none.</param>
	/// <exception cref="ArgumentNullException"><paramref name="source" /> is <see langword="null" />.</exception>
	/// <exception cref="ArgumentException"><paramref name="source" /> is blank or <paramref name="name" /> is blank.</exception>
	public AutoAssemblerScript(string source, string? name = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(source);
		if (name is not null)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(name);
		}

		Source = source;
		Name = name;
	}

	/// <summary>Gets the Auto Assembler source.</summary>
	public string Source
	{
		get;
	}

	/// <summary>Gets the optional Client diagnostic name for the patch.</summary>
	public string? Name
	{
		get;
	}
}
