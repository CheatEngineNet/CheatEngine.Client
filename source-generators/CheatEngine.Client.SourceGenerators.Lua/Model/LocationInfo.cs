using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace CheatEngine.Client.SourceGenerators.Lua.Model;

/// <summary>
///     A value copy of a source location: file path and spans only, never the syntax tree a <see cref="Location" /> roots.
/// </summary>
/// <remarks>
///     Converted back with <see cref="Location.Create(string, TextSpan, LinePositionSpan)" /> when a diagnostic is reported
///     (https://learn.microsoft.com/dotnet/api/microsoft.codeanalysis.location.create).
/// </remarks>
internal sealed class LocationInfo : IEquatable<LocationInfo>
{
	public LocationInfo(string filePath, TextSpan textSpan, LinePositionSpan lineSpan)
	{
		FilePath = filePath;
		TextSpan = textSpan;
		LineSpan = lineSpan;
	}

	public string FilePath
	{
		get;
	}

	public TextSpan TextSpan
	{
		get;
	}

	public LinePositionSpan LineSpan
	{
		get;
	}

	public static LocationInfo? From(Location? location)
	{
		if (location is null || !location.IsInSource)
		{
			return null;
		}

		FileLinePositionSpan lineSpan = location.GetLineSpan();
		return new LocationInfo(lineSpan.Path ?? string.Empty, location.SourceSpan, lineSpan.Span);
	}

	public static LocationInfo? From(SyntaxNode? node)
	{
		return node is null ? null : From(node.GetLocation());
	}

	public Location ToLocation()
	{
		return Location.Create(FilePath, TextSpan, LineSpan);
	}

	public bool Equals(LocationInfo? other)
	{
		return other is not null && string.Equals(FilePath, other.FilePath, StringComparison.Ordinal) &&
			   TextSpan.Equals(other.TextSpan) && LineSpan.Equals(other.LineSpan);
	}

	public override bool Equals(object? obj)
	{
		return Equals(obj as LocationInfo);
	}

	public override int GetHashCode()
	{
		unchecked
		{
			return (StringComparer.Ordinal.GetHashCode(FilePath) * 31) + TextSpan.GetHashCode();
		}
	}
}
