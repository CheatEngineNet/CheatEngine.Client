using LivePlugin.Qualification.Harness;

namespace CheatEngine.Client.LivePlugin.Qualification.Tests;

/// <summary>
///     The session inputs of the runner (<c>CECLIENT_QUALIFICATION_*</c>): honored only in an authorized run, the Auto
///     Assembler opt-in only for the exact value <c>1</c>, and paths only when they are absolute.
/// </summary>
public sealed class QualificationInputsTests
{
	private const string TableRoot = @"C:\runs\20260924T101530Z-a1b2\tables";
	private const string LifecycleFile = @"C:\runs\20260924T101530Z-a1b2\sessions\S2\lifecycle.txt";

	[Fact]
	public void InputsAreIgnoredWhenTheGateDeniedTheRun()
	{
		FakeQualificationEnvironment environment = WithInputs("1", TableRoot, LifecycleFile);
		environment.SetVariable(QualificationAuthorization.AcknowledgementVariable, null);

		QualificationInputs inputs = QualificationInputs.Read(QualificationAuthorization.Evaluate(environment), environment);

		Assert.Same(QualificationInputs.None, inputs);
		Assert.False(inputs.EnableAutoAssembler);
		Assert.Null(inputs.TableRoot);
		Assert.Null(inputs.LifecycleFile);
	}

	[Fact]
	public void AnAuthorizedRunReadsEveryInput()
	{
		FakeQualificationEnvironment environment = WithInputs("1", TableRoot, LifecycleFile);

		QualificationInputs inputs = QualificationInputs.Read(QualificationAuthorization.Evaluate(environment), environment);

		Assert.True(inputs.EnableAutoAssembler);
		Assert.Equal(Path.GetFullPath(TableRoot), inputs.TableRoot);
		Assert.Equal(Path.GetFullPath(LifecycleFile), inputs.LifecycleFile);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("true")]
	[InlineData("01")]
	[InlineData(" 1")]
	public void OnlyTheExactValueOneComposesTheAutoAssemblerOptIn(string? value)
	{
		FakeQualificationEnvironment environment = WithInputs(value, null, null);

		Assert.False(QualificationInputs.Read(QualificationAuthorization.Evaluate(environment), environment)
			.EnableAutoAssembler);
	}

	[Theory]
	[InlineData("tables")]
	[InlineData(@"..\tables")]
	[InlineData(" ")]
	public void RelativeOrBlankPathsAreIgnored(string path)
	{
		FakeQualificationEnvironment environment = WithInputs(null, path, path);

		QualificationInputs inputs = QualificationInputs.Read(QualificationAuthorization.Evaluate(environment), environment);

		Assert.Null(inputs.TableRoot);
		Assert.Null(inputs.LifecycleFile);
	}

	private static FakeQualificationEnvironment WithInputs(string? enableAutoAssembler, string? tableRoot,
		string? lifecycleFile)
	{
		FakeQualificationEnvironment environment = new();
		environment.SetVariable(QualificationInputs.EnableAutoAssemblerVariable, enableAutoAssembler);
		environment.SetVariable(QualificationInputs.TableRootVariable, tableRoot);
		environment.SetVariable(QualificationInputs.LifecycleFileVariable, lifecycleFile);
		return environment;
	}
}
