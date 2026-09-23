using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The write guard of every mutating harness function: a write reaches the Client memory API only for the authorized
///     target the Client observes, and only inside a region declared for that process.
/// </summary>
public sealed class QualificationWriteGuardTests
{
	private const ulong ScratchBase = 0x0000_01E2_8800_0000;
	private const int ScratchLength = 4096;

	private static AuthorizationDecision Allowed =>
		QualificationAuthorization.Evaluate(new FakeQualificationEnvironment());

	private static TargetDeclaration Declaration(int processId = FakeQualificationEnvironment.TargetProcessId)
	{
		return new TargetDeclaration(processId, [new WritableRegion("scratch", ScratchBase, ScratchLength)]);
	}

	[Fact]
	public void WritesOutsideADeclaredRegionAreRefused()
	{
		const int Target = FakeQualificationEnvironment.TargetProcessId;

		Assert.Equal(WriteRefusal.None, QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ScratchBase, 8));
		Assert.Equal(WriteRefusal.None,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ScratchBase + ScratchLength - 8, 8));
		Assert.Equal(WriteRefusal.OutsideDeclaredRegion,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ScratchBase + ScratchLength - 4, 8));
		Assert.Equal(WriteRefusal.OutsideDeclaredRegion,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ScratchBase - 1, 2));
		Assert.Equal(WriteRefusal.OutsideDeclaredRegion,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), 0x10, 4));
		Assert.Equal(WriteRefusal.OutsideDeclaredRegion,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ScratchBase, 0));
		Assert.Equal(WriteRefusal.OutsideDeclaredRegion,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(), ulong.MaxValue - 2, 8));
		Assert.True(QualificationWriteGuard.IsNeverMapped(0x10));
		Assert.False(QualificationWriteGuard.IsNeverMapped(ScratchBase));
	}

	[Fact]
	public void WritesAreRefusedWhenTheDeclarationOrTheClientTargetNamesAnotherProcess()
	{
		const int Target = FakeQualificationEnvironment.TargetProcessId;
		FakeQualificationEnvironment unauthorized = new();
		unauthorized.SetVariable(QualificationAuthorization.AcknowledgementVariable, null);

		Assert.Equal(WriteRefusal.DeclarationForAnotherProcess,
			QualificationWriteGuard.Evaluate(Allowed, Target, Declaration(Target + 1), ScratchBase, 8));
		Assert.Equal(WriteRefusal.TargetNotAuthorized,
			QualificationWriteGuard.Evaluate(Allowed, Target + 1, Declaration(Target + 1), ScratchBase, 8));
		Assert.Equal(WriteRefusal.TargetNotAuthorized,
			QualificationWriteGuard.Evaluate(Allowed, 0, Declaration(), ScratchBase, 8));
		Assert.Equal(WriteRefusal.NotAuthorized,
			QualificationWriteGuard.Evaluate(QualificationAuthorization.Evaluate(unauthorized), Target, Declaration(),
				ScratchBase, 8));
	}

	[Fact]
	public void AbsentDeclarationRefusesEveryWrite()
	{
		const int Target = FakeQualificationEnvironment.TargetProcessId;

		Assert.Equal(WriteRefusal.NoDeclaration, QualificationWriteGuard.Evaluate(Allowed, Target, null, ScratchBase, 8));
		Assert.Equal(WriteRefusal.NoDeclaration,
			QualificationWriteGuard.Evaluate(Allowed, Target, new TargetDeclaration(Target, []), ScratchBase, 8));
	}
}
