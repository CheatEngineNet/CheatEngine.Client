using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The fail-closed gate of the qualification harness: only the exact phrase, a short-lived <c>ce77-live-probe-v1</c>
///     manifest of the SDK runner, the pinned Cheat Engine host and the declared, live disposable target authorize a
///     mutating harness function, and only on that target.
/// </summary>
public sealed class QualificationAuthorizationTests
{
	[Fact]
	public void AuthorizationIsDeniedWithoutTheAcknowledgement()
	{
		FakeQualificationEnvironment missing = new();
		missing.SetVariable(QualificationAuthorization.AcknowledgementVariable, null);
		FakeQualificationEnvironment wrongPhrase = new();
		wrongPhrase.SetVariable(QualificationAuthorization.AcknowledgementVariable, "yes");
		FakeQualificationEnvironment manifestPhrase = new();
		manifestPhrase.WriteManifest(acknowledgement: "I_AUTHORIZE_SOMETHING_ELSE");
		FakeQualificationEnvironment noManifest = new();
		noManifest.SetVariable(QualificationAuthorization.ManifestVariable, null);

		Assert.Equal(AuthorizationDenial.AcknowledgementMissing, QualificationAuthorization.Evaluate(missing).Denial);
		Assert.Equal(AuthorizationDenial.AcknowledgementMissing, QualificationAuthorization.Evaluate(wrongPhrase).Denial);
		Assert.Equal(AuthorizationDenial.ManifestAcknowledgementMismatch,
			QualificationAuthorization.Evaluate(manifestPhrase).Denial);
		Assert.Equal(AuthorizationDenial.ManifestMissing, QualificationAuthorization.Evaluate(noManifest).Denial);
	}

	[Fact]
	public void AuthorizationIsDeniedWhenTheManifestExpired()
	{
		FakeQualificationEnvironment expired = new();
		expired.WriteManifest(validFor: TimeSpan.FromSeconds(-1));
		FakeQualificationEnvironment tooLong = new();
		tooLong.WriteManifest(validFor: TimeSpan.FromHours(4));
		FakeQualificationEnvironment later = new();
		later.UtcNow = FakeQualificationEnvironment.Now + TimeSpan.FromMinutes(31);
		FakeQualificationEnvironment malformed = new();
		malformed.SetFile(FakeQualificationEnvironment.ManifestPath, "{ \"schema\": \"ce77-live-probe-v1\" }");
		FakeQualificationEnvironment notDisposable = new();
		notDisposable.WriteManifest(disposable: false);

		Assert.Equal(AuthorizationDenial.ManifestExpired, QualificationAuthorization.Evaluate(expired).Denial);
		Assert.Equal(AuthorizationDenial.ManifestLifetimeTooLong, QualificationAuthorization.Evaluate(tooLong).Denial);
		Assert.Equal(AuthorizationDenial.ManifestExpired, QualificationAuthorization.Evaluate(later).Denial);
		Assert.Equal(AuthorizationDenial.ManifestInvalid, QualificationAuthorization.Evaluate(malformed).Denial);
		Assert.Equal(AuthorizationDenial.TargetNotDisposable, QualificationAuthorization.Evaluate(notDisposable).Denial);
	}

	[Fact]
	public void AuthorizationIsDeniedWhenTheHostHashDiffers()
	{
		const string OtherHost = "9D861D651AB9D1DC3C09AE34C8ED5DEE3D1A29B080784C3C48773494C9350230";
		FakeQualificationEnvironment otherBinary = new();
		otherBinary.SetImage(FakeQualificationEnvironment.HostProcessId, new ProcessImage(OtherHost, "Amd64", "7.7.0.10621"));
		FakeQualificationEnvironment otherVersion = new();
		otherVersion.SetImage(FakeQualificationEnvironment.HostProcessId,
			new ProcessImage(QualificationAuthorization.ExactCheatEngineSha256, "Amd64", "7.6.0.9999"));
		FakeQualificationEnvironment manifestPinsAnotherHost = new();
		manifestPinsAnotherHost.WriteManifest(hostSha256: OtherHost);
		FakeQualificationEnvironment x86 = new();
		x86.Is64BitProcess = false;

		Assert.Equal(AuthorizationDenial.HostMismatch, QualificationAuthorization.Evaluate(otherBinary).Denial);
		Assert.Equal(AuthorizationDenial.HostMismatch, QualificationAuthorization.Evaluate(otherVersion).Denial);
		Assert.Equal(AuthorizationDenial.HostMismatch, QualificationAuthorization.Evaluate(manifestPinsAnotherHost).Denial);
		Assert.Equal(AuthorizationDenial.NotX64Process, QualificationAuthorization.Evaluate(x86).Denial);
	}

	[Fact]
	public void AuthorizationIsDeniedForAnotherProcessId()
	{
		FakeQualificationEnvironment hostAsTarget = new();
		hostAsTarget.WriteManifest(targetProcessId: FakeQualificationEnvironment.HostProcessId,
			targetSha256: QualificationAuthorization.ExactCheatEngineSha256);
		FakeQualificationEnvironment exitedTarget = new();
		exitedTarget.SetImage(FakeQualificationEnvironment.TargetProcessId, null);
		FakeQualificationEnvironment reusedProcessId = new();
		reusedProcessId.SetImage(FakeQualificationEnvironment.TargetProcessId,
			new ProcessImage("0000000000000000000000000000000000000000000000000000000000000000", "Amd64", null));
		AuthorizationDecision allowed = QualificationAuthorization.Evaluate(new FakeQualificationEnvironment());

		Assert.Equal(AuthorizationDenial.TargetIsHost, QualificationAuthorization.Evaluate(hostAsTarget).Denial);
		Assert.Equal(AuthorizationDenial.TargetUnavailable, QualificationAuthorization.Evaluate(exitedTarget).Denial);
		Assert.Equal(AuthorizationDenial.TargetMismatch, QualificationAuthorization.Evaluate(reusedProcessId).Denial);
		Assert.False(allowed.Allows(FakeQualificationEnvironment.TargetProcessId + 1));
		Assert.False(allowed.Allows(0));
	}

	[Fact]
	public void AuthorizedManifestAllowsOnlyTheDeclaredTarget()
	{
		AuthorizationDecision decision = QualificationAuthorization.Evaluate(new FakeQualificationEnvironment());

		Assert.True(decision.IsAllowed);
		Assert.Equal(AuthorizationDenial.None, decision.Denial);
		Assert.Equal(FakeQualificationEnvironment.TargetProcessId, decision.TargetProcessId);
		Assert.Equal(FakeQualificationEnvironment.TargetSha256, decision.TargetSha256);
		Assert.True(decision.Allows(FakeQualificationEnvironment.TargetProcessId));
		Assert.False(decision.Allows(FakeQualificationEnvironment.HostProcessId));
		Assert.False(AuthorizationDecision.Denied(AuthorizationDenial.ManifestMissing)
			.Allows(FakeQualificationEnvironment.TargetProcessId));
	}
}
