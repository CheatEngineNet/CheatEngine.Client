using CheatEngine.Client.Core.Domains;

using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection;

/// <summary>Validates the security-sensitive options that cannot be expressed as data annotations.</summary>
/// <remarks>
///     A composition detail: <c>AddCheatEngineClient</c> registers it, and Hosting resolves the options before any Client
///     work, so it runs at every enable.
/// </remarks>
internal sealed class CheatEngineClientOptionsSemanticValidator : IValidateOptions<CheatEngineClientOptions>
{
	/// <inheritdoc />
	public ValidateOptionsResult Validate(string? name, CheatEngineClientOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);

		try
		{
			_ = MemoryResourceLimitsCopy.CreateValidated(options.MemoryResourceLimits);
		}
		catch (ArgumentOutOfRangeException exception)
		{
			return ValidateOptionsResult.Fail($"MemoryResourceLimits is invalid: {exception.Message}");
		}

		HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
		foreach (string? root in options.AllowedTableRoots)
		{
			if (string.IsNullOrWhiteSpace(root))
			{
				return ValidateOptionsResult.Fail("AllowedTableRoots cannot contain blank paths.");
			}

			string normalized;
			try
			{
				if (!Path.IsPathFullyQualified(root))
				{
					return ValidateOptionsResult.Fail("AllowedTableRoots can contain only fully qualified paths.");
				}

				normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
			}
			catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
			{
				return ValidateOptionsResult.Fail(
					"AllowedTableRoots must contain paths that can be normalized safely.");
			}

			if (!roots.Add(normalized))
			{
				return ValidateOptionsResult.Fail("AllowedTableRoots cannot contain the same normalized path twice.");
			}
		}

		return ValidateOptionsResult.Success;
	}
}
