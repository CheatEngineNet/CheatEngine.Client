using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Validates the security-sensitive options that cannot be expressed as data annotations.</summary>
public sealed class CheatEngineClientOptionsSemanticValidator : IValidateOptions<CheatEngineClientOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, CheatEngineClientOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		if (options.AllowedTableRoots is null)
		{
			return ValidateOptionsResult.Fail("AllowedTableRoots must be an empty array or contain absolute paths.");
		}

		HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
		foreach (string root in options.AllowedTableRoots)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				return ValidateOptionsResult.Fail("AllowedTableRoots cannot contain blank paths.");
			}

			if (!Path.IsPathFullyQualified(root))
			{
				return ValidateOptionsResult.Fail("AllowedTableRoots can contain only fully qualified paths.");
			}

			string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
			if (!roots.Add(normalized))
			{
				return ValidateOptionsResult.Fail("AllowedTableRoots cannot contain the same normalized path twice.");
			}
		}

		return ValidateOptionsResult.Success;
	}
}
