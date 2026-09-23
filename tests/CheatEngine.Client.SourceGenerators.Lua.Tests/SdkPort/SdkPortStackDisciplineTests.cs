using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.SdkPort;

/// <summary>
///     C0 stack discipline of the generated SDK port (DoD A.8, D.6). The port needs a real Lua state, which this repository
///     does not have (no C2 fixture), so its stack balance is proven on the syntax of the emitted code: every method that
///     touches the Lua stack records <c>Top</c> first and restores it in a <c>finally</c> block, and reads the error value
///     with <c>LuaError.FromStack</c> before that restoration. What each path decides is pinned by
///     <see cref="SdkPortDecisionTests" />.
/// </summary>
[Trait("Qualification", "Q16")]
public sealed class SdkPortStackDisciplineTests
{
	[Fact]
	public void GeneratedSdkPortRestoresTheRecordedTopInAFinallyOnEveryMethod()
	{
		StructDeclarationSyntax port = GeneratedSdkPort.Shared.Declaration;
		List<string> stackMethods = [];
		foreach (MethodDeclarationSyntax method in port.Members.OfType<MethodDeclarationSyntax>())
		{
			InvocationExpressionSyntax[] stackCalls =
				[.. method.DescendantNodes().OfType<InvocationExpressionSyntax>().Where(TouchesTheStack)];
			if (stackCalls.Length == 0)
			{
				continue;
			}

			string name = method.Identifier.ValueText;
			stackMethods.Add(name);
			BlockSyntax body = Assert.IsType<BlockSyntax>(method.Body);
			LocalDeclarationStatementSyntax recordTop = Assert.Single(body.Statements.OfType<LocalDeclarationStatementSyntax>());
			VariableDeclaratorSyntax topVariable = Assert.Single(recordTop.Declaration.Variables);
			Assert.Equal("top", topVariable.Identifier.ValueText);
			Assert.Equal("_state.Top", topVariable.Initializer?.Value.ToString());
			TryStatementSyntax guard = Assert.Single(body.Statements.OfType<TryStatementSyntax>());
			Assert.True(body.Statements.IndexOf(recordTop) + 1 == body.Statements.IndexOf(guard),
				$"{name}: the try block must directly follow the Top read.");
			Assert.Equal(body.Statements.Last(), guard);
			StatementSyntax restore = Assert.Single(Assert.IsType<FinallyClauseSyntax>(guard.Finally).Block.Statements);
			Assert.Equal("_state.SetTop(top);", restore.ToString());

			foreach (InvocationExpressionSyntax call in stackCalls)
			{
				Assert.True(guard.Block.Span.Contains(call.Span),
					$"{name}: '{call}' touches the Lua stack outside the guarded try block.");
			}

			foreach (InvocationExpressionSyntax fromStack in method.DescendantNodes().OfType<InvocationExpressionSyntax>()
						 .Where(static call => call.Expression.ToString().EndsWith("LuaError.FromStack", StringComparison.Ordinal)))
			{
				Assert.True(guard.Block.Span.Contains(fromStack.Span),
					$"{name}: LuaError.FromStack must read the error value inside the try block, before SetTop.");
			}
		}

		Assert.Equal(["ProbeVacant", "Publish", "Capture", "Observe", "Clear", "Release"], stackMethods);
	}

	private static bool TouchesTheStack(InvocationExpressionSyntax call)
	{
		string text = call.Expression.ToString();
		if (text is "_state.SetTop" || text.EndsWith("ExportName", StringComparison.Ordinal))
		{
			return false;
		}

		return text.StartsWith("_state.", StringComparison.Ordinal) ||
			   call.ArgumentList.Arguments.Any(static argument => argument.Expression.IsKind(SyntaxKind.IdentifierName) &&
																	argument.Expression.ToString() == "_state");
	}
}
