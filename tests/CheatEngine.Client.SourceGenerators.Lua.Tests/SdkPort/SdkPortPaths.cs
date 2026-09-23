using System.Collections.Immutable;
using System.Globalization;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CheatEngine.Client.SourceGenerators.Lua.Tests.SdkPort;

/// <summary>
///     Enumerates every execution path of one generated SDK port method as symbol-resolved steps, so a test states the
///     decisions of the port independently of its formatting, qualification and <c>if</c>/<c>?:</c> style.
/// </summary>
/// <remarks>
///     <para>
///         Steps: <c>name := value</c> (local or parameter assignment), a call, <c>assume condition</c> /
///         <c>assume !condition</c> on each side of an <c>if</c> or <c>?:</c>, <c>return value</c>, <c>throw value</c>,
///         <c>catch Type name</c> (a matching exception raised anywhere in the <c>try</c> block), and
///         <c>finally step</c>. Calls and members are rendered from their symbols (<c>state.TryGetGlobal(...)</c>,
///         <c>LuaError.FromStack(state, status)</c>, <c>Ownership.Owned</c>), constants from the semantic model
///         (<c>-2</c>), and the <c>_state</c> field as <c>state</c>; the reserved <c>__CheatEngineLua</c> prefix is
///         dropped from type names.
///     </para>
///     <para>
///         Any statement or expression outside the small subset the port uses throws <see cref="NotSupportedException" />,
///         so a new construct in the port can never be skipped silently: it fails the check until this reader and the
///         expected paths are extended.
///     </para>
/// </remarks>
internal sealed class SdkPortPaths
{
	private const string ReservedPrefix = "__CheatEngineLua";

	private readonly SemanticModel _model;

	private SdkPortPaths(SemanticModel model)
	{
		_model = model;
	}

	/// <summary>Returns the paths of one port method, true branches first, each path joined with <c>"; "</c>.</summary>
	internal static string[] Of(GeneratedSdkPort port, string methodName)
	{
		ArgumentNullException.ThrowIfNull(port);

		MethodDeclarationSyntax method = port.Method(methodName);
		BlockSyntax body = method.Body ??
						   throw new NotSupportedException($"{methodName}: expression-bodied port methods are not read.");
		SdkPortPaths reader = new(port.Model);
		return
		[
			.. reader.Walk(body.Statements, 0, PortPath.Empty)
				.Select(path => path.Terminated
					? string.Join("; ", path.Steps)
					: throw new NotSupportedException($"{methodName}: a path ends without return or throw."))
		];
	}

	private IEnumerable<PortPath> Walk(IReadOnlyList<StatementSyntax> statements, int index, PortPath path)
	{
		if (path.Terminated || index == statements.Count)
		{
			yield return path;
			yield break;
		}

		foreach (PortPath next in Step(statements[index], path))
		{
			foreach (PortPath complete in Walk(statements, index + 1, next))
			{
				yield return complete;
			}
		}
	}

	private IEnumerable<PortPath> Step(StatementSyntax statement, PortPath path)
	{
		switch (statement)
		{
			case BlockSyntax block:
				return Walk(block.Statements, 0, path);
			case LocalDeclarationStatementSyntax
			{
				Declaration.Variables: [{ Initializer: { } initializer } variable]
			}:
				return Fork(initializer.Value, path, value => variable.Identifier.ValueText + " := " + value, false);
			case ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
				when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression):
				string target = Render(assignment.Left);
				return Fork(assignment.Right, path, value => target + " := " + value, false);
			case ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call }:
				return [path.With(Render(call))];
			case ReturnStatementSyntax { Expression: { } returned }:
				return Fork(returned, path, static value => "return " + value, true);
			case ThrowStatementSyntax { Expression: { } thrown }:
				return [path.With("throw " + Render(thrown)).Terminate()];
			case IfStatementSyntax conditional:
				return If(conditional, path);
			case TryStatementSyntax guarded:
				return Try(guarded, path);
			default:
				throw new NotSupportedException($"Unsupported statement in the SDK port: {statement.Kind()} '{statement}'.");
		}
	}

	private IEnumerable<PortPath> If(IfStatementSyntax conditional, PortPath path)
	{
		string condition = Render(conditional.Condition);
		foreach (PortPath taken in Step(conditional.Statement, path.With("assume " + condition)))
		{
			yield return taken;
		}

		PortPath skipped = path.With("assume " + Negate(condition));
		if (conditional.Else is null)
		{
			yield return skipped;
			yield break;
		}

		foreach (PortPath otherwise in Step(conditional.Else.Statement, skipped))
		{
			yield return otherwise;
		}
	}

	private IEnumerable<PortPath> Try(TryStatementSyntax guarded, PortPath path)
	{
		string[] cleanup = [];
		if (guarded.Finally is not null)
		{
			PortPath final = Assert.Single(Walk(guarded.Finally.Block.Statements, 0, PortPath.Empty));
			if (final.Terminated)
			{
				throw new NotSupportedException("A finally block of the SDK port may not return or throw.");
			}

			cleanup = [.. final.Steps.Select(static step => "finally " + step)];
		}

		foreach (PortPath attempt in Walk(guarded.Block.Statements, 0, path))
		{
			yield return attempt.WithAll(cleanup);
		}

		foreach (CatchClauseSyntax handler in guarded.Catches)
		{
			if (handler.Declaration is null || handler.Filter is not null)
			{
				throw new NotSupportedException("Only typed, unfiltered catch clauses are read in the SDK port.");
			}

			ITypeSymbol caught = _model.GetTypeInfo(handler.Declaration.Type, TestContext.Current.CancellationToken).Type!;
			PortPath entered = path.With("catch " + TypeName(caught) + " " + handler.Declaration.Identifier.ValueText);
			foreach (PortPath handled in Walk(handler.Block.Statements, 0, entered))
			{
				yield return handled.WithAll(cleanup);
			}
		}
	}

	/// <summary>Forks on a top-level conditional expression; any other expression is rendered as one value.</summary>
	private IEnumerable<PortPath> Fork(ExpressionSyntax expression, PortPath path, Func<string, string> step,
		bool terminates)
	{
		while (expression is ParenthesizedExpressionSyntax parenthesized)
		{
			expression = parenthesized.Expression;
		}

		if (expression is ConditionalExpressionSyntax choice)
		{
			string condition = Render(choice.Condition);
			return Fork(choice.WhenTrue, path.With("assume " + condition), step, terminates)
				.Concat(Fork(choice.WhenFalse, path.With("assume " + Negate(condition)), step, terminates));
		}

		PortPath next = path.With(step(Render(expression)));
		return [terminates ? next.Terminate() : next];
	}

	private string Render(ExpressionSyntax expression)
	{
		switch (expression)
		{
			case ParenthesizedExpressionSyntax parenthesized:
				return Render(parenthesized.Expression);
			case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.NullLiteralExpression):
				return "null";
			case LiteralExpressionSyntax literal when literal.IsKind(SyntaxKind.Utf8StringLiteralExpression):
				return "u8\"" + literal.Token.ValueText + "\"";
			case PrefixUnaryExpressionSyntax negation when negation.IsKind(SyntaxKind.LogicalNotExpression):
				return "!" + Render(negation.Operand);
			case ConditionalExpressionSyntax:
				throw new NotSupportedException($"Nested conditional expressions are not read in the SDK port: '{expression}'.");
		}

		ISymbol? symbol = _model.GetSymbolInfo(expression, TestContext.Current.CancellationToken).Symbol;
		if (symbol is IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } enumMember)
		{
			return TypeName(enumMember.ContainingType) + "." + enumMember.Name;
		}

		Optional<object?> constant = _model.GetConstantValue(expression, TestContext.Current.CancellationToken);
		if (constant.HasValue)
		{
			return constant.Value switch
			{
				null => "null",
				bool flag => flag ? "true" : "false",
				string text => "\"" + text + "\"",
				IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
				_ => throw new NotSupportedException($"Unsupported constant in the SDK port: '{expression}'.")
			};
		}

		switch (expression)
		{
			case InvocationExpressionSyntax call when symbol is IMethodSymbol method:
				return Receiver(call.Expression, method) + method.Name + "(" + Arguments(call.ArgumentList) + ")";
			case ObjectCreationExpressionSyntax creation when symbol is IMethodSymbol constructor:
				return "new " + TypeName(constructor.ContainingType) + "(" +
					   (creation.ArgumentList is null ? string.Empty : Arguments(creation.ArgumentList)) + ")";
			case MemberAccessExpressionSyntax access when symbol is IPropertySymbol or IFieldSymbol:
				return Receiver(access, symbol) + symbol.Name;
			case IdentifierNameSyntax when symbol is IParameterSymbol or ILocalSymbol:
				return symbol.Name;
			case IdentifierNameSyntax when symbol is IFieldSymbol field:
				return field.Name.TrimStart('_');
			default:
				throw new NotSupportedException(
					$"Unsupported expression in the SDK port: {expression.Kind()} '{expression}' ({symbol?.Kind}).");
		}
	}

	private string Receiver(ExpressionSyntax callee, ISymbol member)
	{
		if (callee is not MemberAccessExpressionSyntax access)
		{
			return string.Empty;
		}

		return member.IsStatic ? TypeName(member.ContainingType) + "." : Render(access.Expression) + ".";
	}

	private string Arguments(BaseArgumentListSyntax arguments)
	{
		return string.Join(", ", arguments.Arguments.Select(argument =>
			(argument.RefKindKeyword.IsKind(SyntaxKind.None) ? string.Empty : argument.RefKindKeyword.ValueText + " ") +
			Render(argument.Expression)));
	}

	private static string TypeName(ITypeSymbol type)
	{
		return type.Name.StartsWith(ReservedPrefix, StringComparison.Ordinal)
			? type.Name[ReservedPrefix.Length..]
			: type.Name;
	}

	private static string Negate(string condition)
	{
		return condition.StartsWith('!') ? condition[1..] : "!" + condition;
	}

	/// <summary>An immutable, possibly terminated, sequence of steps.</summary>
	private sealed record PortPath(ImmutableList<string> Steps, bool Terminated)
	{
		public static readonly PortPath Empty = new(ImmutableList<string>.Empty, false);

		public PortPath With(string step)
		{
			return this with
			{
				Steps = Steps.Add(step)
			};
		}

		public PortPath WithAll(IEnumerable<string> steps)
		{
			return this with
			{
				Steps = Steps.AddRange(steps)
			};
		}

		public PortPath Terminate()
		{
			return this with
			{
				Terminated = true
			};
		}
	}
}
