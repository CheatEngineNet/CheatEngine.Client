using Microsoft.Extensions.Options;

namespace CheatEngine.Client.Extensions.DependencyInjection.Tests;

public sealed class CheatEngineClientOptionsSemanticValidatorTests
{
	private readonly CheatEngineClientOptionsSemanticValidator _validator = new();

	[Fact]
	public void ValidateAcceptsAnEmptyAllowedRootList()
	{
		ValidateOptionsResult result = _validator.Validate(null, new CheatEngineClientOptions());

		Assert.True(result.Succeeded);
	}

	[Fact]
	public void ValidateRejectsNullAllowedRootList()
	{
		ValidateOptionsResult result =
			_validator.Validate(null, new CheatEngineClientOptions { AllowedTableRoots = null });

		Assert.True(result.Failed);
		string? failureMessage = result.FailureMessage;
		Assert.NotNull(failureMessage);
		Assert.Contains("empty array", failureMessage, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("relative\\tables")]
	public void ValidateRejectsBlankOrRelativeAllowedRoots(string root)
	{
		ValidateOptionsResult result =
			_validator.Validate(null, new CheatEngineClientOptions { AllowedTableRoots = [root] });

		Assert.True(result.Failed);
	}

	[Fact]
	public void ValidateRejectsDuplicateNormalizedAllowedRoots()
	{
		string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests"));
		string rootWithTrailingSeparator = root + Path.DirectorySeparatorChar;

		ValidateOptionsResult result = _validator.Validate(null,
			new CheatEngineClientOptions { AllowedTableRoots = [root, rootWithTrailingSeparator] });

		Assert.True(result.Failed);
		string? failureMessage = result.FailureMessage;
		Assert.NotNull(failureMessage);
		Assert.Contains("same normalized path", failureMessage, StringComparison.Ordinal);
	}

	[Fact]
	public void ValidateAcceptsDistinctFullyQualifiedAllowedRoots()
	{
		string first = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", "one"));
		string second = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CheatEngine.Client.Tests", "two"));

		ValidateOptionsResult result =
			_validator.Validate(null, new CheatEngineClientOptions { AllowedTableRoots = [first, second] });

		Assert.True(result.Succeeded);
	}
}
