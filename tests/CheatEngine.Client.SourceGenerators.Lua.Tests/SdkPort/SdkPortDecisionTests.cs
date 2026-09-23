using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.SdkPort;

/// <summary>
///     C0 check of the decisions of the generated SDK port (Q16, F12): the only generated code that runs against the real
///     Lua state. The EndToEnd tests replace this port with a managed double, so they prove the algorithm around it, never
///     what it decides. This class pins, from the symbol-resolved paths of the emitted syntax, that <c>Observe</c> mirrors
///     the SDK 2.0 lease release (<c>TryGetGlobal</c>, <c>nil</c> before any <c>TryPushRef</c>, then <c>lua_rawequal</c>
///     of the global and the pinned value), that <c>CreateRef</c> is the only pin, that a clear writes <c>nil</c> under the
///     export name, and that <c>ExportName</c> returns each descriptor export.
/// </summary>
/// <remarks>
///     Changing an expected path below changes the Q16 contract of production code, not its formatting: record it in the
///     generator README and in the ADR-01 entry of docs/migration/sdk-2.0.md. <see cref="MutatedPortsAreRejected" /> proves
///     that the check fails for the identity and decision mutations it exists to catch. Execution against a real Lua 5.3
///     state (C2) is not available in this repository.
/// </remarks>
[Trait("Qualification", "Q16")]
public sealed class SdkPortDecisionTests
{
	private const string Top = "top := state.Top";
	private const string Restore = "finally state.SetTop(top)";
	private const string ReadGlobal = "status := state.TryGetGlobal(ExportName(export))";
	private const string StatusFailed = "assume !status.IsOk";
	private const string StatusOk = "assume status.IsOk";
	private const string StackError = "new LuaException(LuaError.FromStack(state, status))";
	private const string CaughtLuaException = "catch LuaException exception";

	/// <summary>
	///     Observe: read the global, then absent, unresolvable pin, identity; the order of the SDK 2.0
	///     <c>LuaRegistrationSet.ReleaseEntry</c>. <c>RawEquals(-2, -1)</c> compares the global (pushed first) with the
	///     pinned value (pushed by <c>TryPushRef</c>).
	/// </summary>
	private static readonly string[] ObservePaths =
	[
		Steps("failure := null", Top, ReadGlobal, StatusFailed, "failure := " + StackError, "return Ownership.ReadFailed",
			Restore),
		Steps("failure := null", Top, ReadGlobal, StatusOk, "assume state.IsNil(-1)", "return Ownership.Absent", Restore),
		Steps("failure := null", Top, ReadGlobal, StatusOk, "assume !state.IsNil(-1)", "assume !state.TryPushRef(token)",
			"return Ownership.OwnershipUnresolvable", Restore),
		Steps("failure := null", Top, ReadGlobal, StatusOk, "assume !state.IsNil(-1)", "assume state.TryPushRef(token)",
			"assume state.RawEquals(-2, -1)", "return Ownership.Owned", Restore),
		Steps("failure := null", Top, ReadGlobal, StatusOk, "assume !state.IsNil(-1)", "assume state.TryPushRef(token)",
			"assume !state.RawEquals(-2, -1)", "return Ownership.Replaced", Restore)
	];

	/// <summary>Capture: a published value that reads back <c>nil</c> is not pinned; <c>CreateRef</c> pops the global.</summary>
	private static readonly string[] CapturePaths =
	[
		Steps("token := null", Top, ReadGlobal, StatusFailed, "return " + StackError, Restore),
		Steps("token := null", Top, ReadGlobal, StatusOk, "assume state.IsNil(-1)", "return null", Restore),
		Steps("token := null", Top, ReadGlobal, StatusOk, "assume !state.IsNil(-1)", "token := state.CreateRef()",
			"return null", Restore),
		Steps("token := null", Top, CaughtLuaException, "return exception", Restore)
	];

	/// <summary>Clear: <c>nil</c> is pushed, then popped into the export's global.</summary>
	private static readonly string[] ClearPaths =
	[
		Steps(Top, "state.PushNil()", "status := state.TrySetGlobal(ExportName(export))", StatusOk, "return null",
			Restore),
		Steps(Top, "state.PushNil()", "status := state.TrySetGlobal(ExportName(export))", StatusFailed,
			"return " + StackError, Restore)
	];

	private static readonly string[] ProbeVacantPaths =
	[
		Steps("vacant := false", Top, ReadGlobal, StatusFailed, "return " + StackError, Restore),
		Steps("vacant := false", Top, ReadGlobal, StatusOk, "vacant := state.IsNil(-1)", "return null", Restore)
	];

	private static readonly string[] PublishPaths =
	[
		Steps(Top, "status := PluginLuaBindings.RegisterLuaFunctions(state)", StatusOk, "return null", Restore),
		Steps(Top, "status := PluginLuaBindings.RegisterLuaFunctions(state)", StatusFailed, "return " + StackError,
			Restore)
	];

	private static readonly string[] IsCurrentPaths = [Steps("return token.IsCurrent")];

	private static readonly string[] ReleasePaths =
	[
		Steps(Top, "token.Release(state)", "return null", Restore),
		Steps(Top, CaughtLuaException, "return exception", Restore)
	];

	/// <summary>
	///     Every method of the port, in emission order; a new method must be added here with its paths. <c>ExportName</c>
	///     is checked by execution instead of paths.
	/// </summary>
	private static readonly (string Method, string[] Paths)[] ExpectedPort =
	[
		("ProbeVacant", ProbeVacantPaths),
		("Publish", PublishPaths),
		("Capture", CapturePaths),
		("IsCurrent", IsCurrentPaths),
		("Observe", ObservePaths),
		("Clear", ClearPaths),
		("Release", ReleasePaths),
		("ExportName", [])
	];

	private static GeneratedSdkPort Port => GeneratedSdkPort.Shared;

	[Fact]
	public void ObserveReadsTheGlobalThenDecidesAbsentUnresolvableOwnedOrReplacedByIdentity()
	{
		Assert.Equal(ObservePaths, SdkPortPaths.Of(Port, "Observe"));
	}

	[Fact]
	public void CaptureChecksForNilBeforeCreateRefAndCreateRefIsTheOnlyPin()
	{
		Assert.Equal(CapturePaths, SdkPortPaths.Of(Port, "Capture"));
		Assert.Equal(["Capture: LuaState.CreateRef"], PinningCalls(Port));
	}

	[Fact]
	public void ClearPushesNilThenSetsTheExportGlobal()
	{
		Assert.Equal(ClearPaths, SdkPortPaths.Of(Port, "Clear"));
	}

	[Fact]
	public void ProbePublishCurrentAndReleaseKeepTheirFailureContract()
	{
		Assert.Equal(ProbeVacantPaths, SdkPortPaths.Of(Port, "ProbeVacant"));
		Assert.Equal(PublishPaths, SdkPortPaths.Of(Port, "Publish"));
		Assert.Equal(IsCurrentPaths, SdkPortPaths.Of(Port, "IsCurrent"));
		Assert.Equal(ReleasePaths, SdkPortPaths.Of(Port, "Release"));
	}

	[Fact]
	public void ExportNameReturnsTheUtf8LiteralOfEachDescriptorExport()
	{
		(string[] descriptorExports, string[] exportNames, GeneratedSdkPort.ExportNameFunction exportName) = Port.Load();

		Assert.Equal(["status", "ping", "marker"], descriptorExports);
		Assert.Equal(descriptorExports, exportNames);
		for (int export = 0; export < descriptorExports.Length; export++)
		{
			Assert.Equal(Encoding.UTF8.GetBytes(descriptorExports[export]), exportName(export).ToArray());
		}

		Assert.Throws<ArgumentOutOfRangeException>(() => exportName(descriptorExports.Length).ToArray());
		Assert.Throws<ArgumentOutOfRangeException>(() => exportName(-1).ToArray());
	}

	[Fact]
	public void TheSdkPortDeclaresOnlyTheCheckedMethods()
	{
		Assert.Equal(ExpectedPort.Select(static entry => entry.Method),
			Port.Declaration.Members.OfType<MethodDeclarationSyntax>().Select(static method => method.Identifier.ValueText));
		Assert.Empty(Violations(Port));
	}

	/// <summary>
	///     Each mutation is a plausible regression of the Q16 decision that still compiles; the check must name the mutated
	///     method and nothing else. The first two bring the F12 defect back into production code: comparing the global with
	///     itself reports every export as owned and clears a third-party replacement, and swapping the two outcomes clears
	///     exactly the replaced globals.
	/// </summary>
	[Theory]
	[InlineData("ObserveComparesTheGlobalWithItself", "Observe")]
	[InlineData("ObserveSwapsOwnedAndReplaced", "Observe")]
	[InlineData("ObservePushesThePinBeforeTheNilCheck", "Observe")]
	[InlineData("ObserveReportsAReadFailureAsAbsent", "Observe")]
	[InlineData("CapturePinsWithoutTheNilCheck", "Capture")]
	[InlineData("ClearSetsTheGlobalWithoutPushingNil", "Clear")]
	[InlineData("ExportNameSwapsTwoExports", "ExportName")]
	public void MutatedPortsAreRejected(string mutation, string mutatedMethod)
	{
		GeneratedSdkPort mutant = Port.Mutate(Mutation(mutation));

		Assert.Equal([mutatedMethod], Violations(mutant));
	}

	private static string[] Violations(GeneratedSdkPort port)
	{
		List<string> violations = [];
		foreach ((string method, string[] expected) in ExpectedPort)
		{
			bool matches = method == "ExportName"
				? ExportNameMatchesTheDescriptor(port)
				: SdkPortPaths.Of(port, method).SequenceEqual(expected, StringComparer.Ordinal);
			if (!matches)
			{
				violations.Add(method);
			}
		}

		return [.. violations];
	}

	private static bool ExportNameMatchesTheDescriptor(GeneratedSdkPort port)
	{
		(string[] descriptorExports, string[] exportNames, GeneratedSdkPort.ExportNameFunction exportName) = port.Load();
		bool matches = descriptorExports.SequenceEqual(exportNames, StringComparer.Ordinal);
		for (int export = 0; matches && export < descriptorExports.Length; export++)
		{
			matches = exportName(export).SequenceEqual(Encoding.UTF8.GetBytes(descriptorExports[export]));
		}

		return matches;
	}

	/// <summary>Every call in the generated module that creates an SDK reference, as <c>method: Type.Member</c>.</summary>
	private static string[] PinningCalls(GeneratedSdkPort port)
	{
		List<string> calls = [];
		foreach (InvocationExpressionSyntax call in port.Tree.GetRoot(TestContext.Current.CancellationToken)
					 .DescendantNodes().OfType<InvocationExpressionSyntax>())
		{
			if (port.Model.GetSymbolInfo(call, TestContext.Current.CancellationToken).Symbol is IMethodSymbol method &&
				method.ReturnType.ToDisplayString() == "CheatEngine.SDK.Lua.References.LuaRef")
			{
				calls.Add(call.FirstAncestorOrSelf<MethodDeclarationSyntax>()!.Identifier.ValueText + ": " +
						  method.ContainingType.Name + "." + method.Name);
			}
		}

		return [.. calls];
	}

	private static Func<GeneratedSdkPort, (SyntaxNode Original, SyntaxNode Replacement)> Mutation(string name)
	{
		return name switch
		{
			"ObserveComparesTheGlobalWithItself" => static port =>
			{
				InvocationExpressionSyntax rawEquals = Call(port, "Observe", "RawEquals");
				return (rawEquals.ArgumentList, SyntaxFactory.ParseArgumentList("(-2, -2)"));
			}
			,
			"ObserveSwapsOwnedAndReplaced" => static port =>
			{
				ConditionalExpressionSyntax choice = Assert.Single(port.Method("Observe").DescendantNodes()
					.OfType<ConditionalExpressionSyntax>());
				return (choice, choice.WithWhenTrue(choice.WhenFalse).WithWhenFalse(choice.WhenTrue));
			}
			,
			"ObservePushesThePinBeforeTheNilCheck" => static port =>
			{
				BlockSyntax block = TryBlock(port, "Observe");
				IfStatementSyntax nilCheck = IfCalling(block, "IsNil");
				IfStatementSyntax pinCheck = IfCalling(block, "TryPushRef");
				StatementSyntax[] reordered = [.. block.Statements];
				reordered[block.Statements.IndexOf(nilCheck)] = pinCheck;
				reordered[block.Statements.IndexOf(pinCheck)] = nilCheck;
				return (block, block.WithStatements(SyntaxFactory.List(reordered)));
			}
			,
			"ObserveReportsAReadFailureAsAbsent" => static port =>
			{
				ReturnStatementSyntax readFailed = Assert.Single(port.Method("Observe").DescendantNodes()
					.OfType<ReturnStatementSyntax>(), static statement =>
					statement.Expression?.ToString().EndsWith(".ReadFailed", StringComparison.Ordinal) == true);
				return (readFailed.Expression!, SyntaxFactory.ParseExpression("__CheatEngineLuaOwnership.Absent"));
			}
			,
			"CapturePinsWithoutTheNilCheck" => static port =>
			{
				BlockSyntax block = TryBlock(port, "Capture");
				return (block, block.WithStatements(block.Statements.Remove(IfCalling(block, "IsNil"))));
			}
			,
			"ClearSetsTheGlobalWithoutPushingNil" => static port =>
			{
				BlockSyntax block = TryBlock(port, "Clear");
				StatementSyntax pushNil = Assert.Single(block.Statements, static statement =>
					statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } &&
					call.Expression.ToString().EndsWith("PushNil", StringComparison.Ordinal));
				return (block, block.WithStatements(block.Statements.Remove(pushNil)));
			}
			,
			"ExportNameSwapsTwoExports" => static port =>
			{
				SwitchStatementSyntax table = Assert.Single(port.Method("ExportName").DescendantNodes()
					.OfType<SwitchStatementSyntax>());
				ExpressionSyntax first =
					Assert.IsType<ReturnStatementSyntax>(table.Sections[0].Statements[0]).Expression!;
				ExpressionSyntax second =
					Assert.IsType<ReturnStatementSyntax>(table.Sections[1].Statements[0]).Expression!;
				ExpressionSyntax[] swapped = [first, second];
				return (table, table.ReplaceNodes(swapped, (original, _) => original == first ? second : first));
			}
			,
			_ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown SDK port mutation.")
		};
	}

	private static InvocationExpressionSyntax Call(GeneratedSdkPort port, string method, string member)
	{
		return Assert.Single(port.Method(method).DescendantNodes().OfType<InvocationExpressionSyntax>(),
			call => call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText == member);
	}

	private static BlockSyntax TryBlock(GeneratedSdkPort port, string method)
	{
		return Assert.Single(port.Method(method).Body!.Statements.OfType<TryStatementSyntax>()).Block;
	}

	private static IfStatementSyntax IfCalling(BlockSyntax block, string member)
	{
		return Assert.Single(block.Statements.OfType<IfStatementSyntax>(), statement =>
			statement.Condition.DescendantNodesAndSelf().OfType<MemberAccessExpressionSyntax>()
				.Any(access => access.Name.Identifier.ValueText == member));
	}

	private static string Steps(params string[] steps)
	{
		return string.Join("; ", steps);
	}
}
