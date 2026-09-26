using System.Collections.Immutable;

using Microsoft.CodeAnalysis;

namespace CheatEngine.Client.SourceGenerators.Lua.Model;

/// <summary>
///     A value description of a generator diagnostic: descriptor id, copied location, and string message arguments. It is
///     turned into a <see cref="Diagnostic" /> only when the output is produced, so pipeline models root no compilation.
/// </summary>
internal sealed class DiagnosticInfo : IEquatable<DiagnosticInfo>
{
	private DiagnosticInfo(string id, LocationInfo? location, EquatableArray<string> arguments)
	{
		Id = id;
		Location = location;
		Arguments = arguments;
	}

	public string Id
	{
		get;
	}

	public LocationInfo? Location
	{
		get;
	}

	public EquatableArray<string> Arguments
	{
		get;
	}

	public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location,
		params string[] arguments)
	{
		return new DiagnosticInfo(descriptor.Id, location, EquatableArray.Create(ImmutableArray.Create(arguments)));
	}

	public Diagnostic ToDiagnostic()
	{
		object[] arguments = new object[Arguments.Length];
		for (int index = 0; index < arguments.Length; index++)
		{
			arguments[index] = Arguments[index];
		}

		return Diagnostic.Create(CheatEngineLuaDiagnostics.Get(Id),
			Location?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, arguments);
	}

	public bool Equals(DiagnosticInfo? other)
	{
		return other is not null && string.Equals(Id, other.Id, StringComparison.Ordinal) &&
			   Equals(Location, other.Location) && Arguments.Equals(other.Arguments);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as DiagnosticInfo);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			return (StringComparer.Ordinal.GetHashCode(Id) * 31) + Arguments.GetHashCode();
		}
	}
}
