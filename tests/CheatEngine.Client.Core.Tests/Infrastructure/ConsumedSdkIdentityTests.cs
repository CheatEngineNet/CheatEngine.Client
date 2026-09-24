using System.Globalization;

using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Runtime;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     The package gate follows the CheatEngine.SDK range the Client packages declare: a loaded CheatEngine.SDK.Engine of
///     the supported major at or above the pin, by SemVer precedence, is <see cref="ClientCapabilityEvidenceState.Satisfied" />,
///     and the gate says whether it is exactly the reviewed package (audit ADR-09, ADR-10).
/// </summary>
public sealed class ConsumedSdkIdentityTests
{
	private const string SdkVersion = "2.0.0";
	private const string SdkCommit = "325c47b573f8bd39a247f1d0101f110fa36c1696";
	private const string SdkSupportedMajor = "2";

	private const string SdkContentHash =
		"NLEdZYJ9LKW3EFNB4X5snKCQf7ZS86GkCQ+El7o+S1XQcxHQGjS45Q1ap8lfjQuIwm004mQ3TPxo+ph1yvRrlQ==";

	private const string OtherCommit = "0123456789abcdef0123456789abcdef01234567";

	[Theory]
	[InlineData(SdkVersion + "+" + SdkCommit, ClientCapabilityEvidenceState.Satisfied, true, "the reviewed package")]
	[InlineData("2.0.1", ClientCapabilityEvidenceState.Satisfied, false, "another release of 2.x at or above 2.0.0")]
	[InlineData("2.1.0-beta.1", ClientCapabilityEvidenceState.Satisfied, false,
		"another release of 2.x at or above 2.0.0")]
	[InlineData("2.0.0-rc.1", ClientCapabilityEvidenceState.Missing, false, "outside 2.x at or above 2.0.0")]
	[InlineData("1.0.0", ClientCapabilityEvidenceState.Missing, false, "outside 2.x at or above 2.0.0")]
	[InlineData("3.0.0", ClientCapabilityEvidenceState.Missing, false, "outside 2.x at or above 2.0.0")]
	[InlineData(null, ClientCapabilityEvidenceState.Unknown, false, "not compared")]
	public void PackageGateAcceptsEveryReleaseOfTheSupportedMajorAtOrAboveThePin(string? loaded,
		ClientCapabilityEvidenceState expected, bool exact, string label)
	{
		ConsumedSdkIdentity identity = Identity(loaded);

		Assert.Equal(expected, identity.PackageGate.State);
		Assert.Equal(exact, identity.ExactReviewedIdentity);
		Assert.Equal(label, identity.IdentityLabel);
		Assert.Equal(2, identity.SupportedMajor);
		Assert.DoesNotContain("refuse", identity.PackageGate.Reason, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(SdkVersion)]
	[InlineData(SdkVersion + "+" + OtherCommit)]
	[InlineData("2.0.1+" + SdkCommit)]
	public void AnotherBuildOfASupportedVersionIsAcceptedButIsNotTheReviewedPackage(string loaded)
	{
		ConsumedSdkIdentity identity = Identity(loaded);

		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, identity.PackageGate.State);
		Assert.False(identity.ExactReviewedIdentity);
		Assert.Contains(
			$"another release than the reviewed package this Client build consumed ({SdkVersion}+{SdkCommit})",
			identity.PackageGate.Reason, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("2.0.0-alpha", ClientCapabilityEvidenceState.Missing)]
	[InlineData("2.0.0-alpha.1", ClientCapabilityEvidenceState.Missing)]
	[InlineData("2.0.0-alpha.beta", ClientCapabilityEvidenceState.Missing)]
	[InlineData("2.0.0-beta", ClientCapabilityEvidenceState.Missing)]
	[InlineData("2.0.0-beta.2", ClientCapabilityEvidenceState.Satisfied)]
	[InlineData("2.0.0-beta.11", ClientCapabilityEvidenceState.Satisfied)]
	[InlineData("2.0.0-rc.1", ClientCapabilityEvidenceState.Satisfied)]
	[InlineData("2.0.0", ClientCapabilityEvidenceState.Satisfied)]
	[InlineData("2.0.0-beta.2+" + OtherCommit, ClientCapabilityEvidenceState.Satisfied)]
	public void PrereleasesCompareBySemVerPrecedence(string loaded, ClientCapabilityEvidenceState expected)
	{
		// The precedence chain of https://semver.org/#spec-item-11 around a prerelease pin: numeric identifiers compare
		// numerically (beta.11 is above beta.2), a shorter identifier list is lower, and a release is above its
		// prereleases. The build never embeds a prerelease pin (CHEATENGINECLIENT9016); the comparison does not rely on it.
		ConsumedSdkIdentity identity = new("2.0.0-beta.2", SdkCommit, SdkContentHash, SdkSupportedMajor, loaded);

		Assert.Equal(expected, identity.PackageGate.State);
	}

	[Theory]
	[InlineData("2.0")]
	[InlineData("2.0.0.0")]
	[InlineData("02.0.0")]
	[InlineData("2.0.0-")]
	[InlineData("2.0.0-01")]
	[InlineData("2.0.0-beta..1")]
	[InlineData("2.0.0+")]
	[InlineData("2.0.0+build/1")]
	[InlineData("99999999999.0.0")]
	[InlineData("not a version")]
	public void InformationalVersionThatIsNotASemanticVersionIsUnknown(string loaded)
	{
		ConsumedSdkIdentity identity = Identity(loaded);

		Assert.Equal(ClientCapabilityEvidenceState.Unknown, identity.PackageGate.State);
		Assert.False(identity.ExactReviewedIdentity);
		Assert.Contains("is not a semantic version", identity.PackageGate.Reason, StringComparison.Ordinal);
		Assert.Equal("not compared", identity.IdentityLabel);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("two")]
	[InlineData("02")]
	[InlineData("-2")]
	public void IdentityWithoutASupportedMajorIsNotEmbedded(string? supportedMajor)
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, supportedMajor,
			$"{SdkVersion}+{SdkCommit}");

		Assert.False(identity.IsEmbedded);
		Assert.Null(identity.SupportedMajor);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, identity.PackageGate.State);
		Assert.False(identity.ExactReviewedIdentity);
		Assert.Contains("embeds no consumed CheatEngine.SDK identity", identity.PackageGate.Reason,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("3")]
	[InlineData("1")]
	public void PinOfAnotherMajorThanTheSupportedOneIsUnknown(string supportedMajor)
	{
		ConsumedSdkIdentity identity = new(SdkVersion, SdkCommit, SdkContentHash, supportedMajor,
			$"{SdkVersion}+{SdkCommit}");

		Assert.True(identity.IsEmbedded);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, identity.PackageGate.State);
		Assert.False(identity.ExactReviewedIdentity);
		Assert.Contains("which is not a pin of that major", identity.PackageGate.Reason, StringComparison.Ordinal);
	}

	[Fact]
	public void NotEmbeddedIdentityComparesNothing()
	{
		ConsumedSdkIdentity identity = ConsumedSdkIdentity.NotEmbedded;

		Assert.False(identity.IsEmbedded);
		Assert.Null(identity.ExpectedInformationalVersion);
		Assert.Equal(ClientCapabilityEvidenceState.Unknown, identity.PackageGate.State);
		Assert.False(identity.ExactReviewedIdentity);
		Assert.Equal("not compared", identity.IdentityLabel);
	}

	[Fact]
	public void CurrentIdentityCarriesTheSupportedMajorOfItsPin()
	{
		// The Core assembly under test embeds the pin and the supported major of eng/CheatEngineSdk.props, and the test
		// process loads the reviewed package.
		ConsumedSdkIdentity current = ConsumedSdkIdentity.Current;

		Assert.True(current.IsEmbedded, "The Core assembly under test embeds no consumed CheatEngine.SDK identity.");
		Assert.Equal(current.Version!.Split('.')[0],
			current.SupportedMajor?.ToString(CultureInfo.InvariantCulture));
		Assert.True(current.ExactReviewedIdentity);
		Assert.Equal(ClientCapabilityEvidenceState.Satisfied, current.PackageGate.State);
	}

	private static ConsumedSdkIdentity Identity(string? loaded)
	{
		return new ConsumedSdkIdentity(SdkVersion, SdkCommit, SdkContentHash, SdkSupportedMajor, loaded);
	}
}
