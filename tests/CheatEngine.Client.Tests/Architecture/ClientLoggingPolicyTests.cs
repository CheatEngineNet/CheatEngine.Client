using System.Reflection;
using System.Text.RegularExpressions;

using CheatEngine.Client.Lua;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Engine.Inspection;
using CheatEngine.SDK.Engine.Values;

using Microsoft.Extensions.Logging;

using ReflectionAssembly = System.Reflection.Assembly;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     Enforces the Q46 redaction policy on every source-generated log event of the shipped Client assemblies: an event
///     parameter can never carry an address, expression, path, script, message, exception, or raw failure object.
/// </summary>
public sealed partial class ClientLoggingPolicyTests
{
	private const string LoggerMessageAttributeName = "Microsoft.Extensions.Logging.LoggerMessageAttribute";

	private static readonly Type[] SensitiveParameterTypes =
	[
		typeof(Address),
		typeof(SymbolExpression),
		typeof(ModuleName),
		typeof(CheatEngineFailure),
		typeof(Exception),
		typeof(LuaScript),
		typeof(FileInfo),
		typeof(FileSystemInfo)
	];

	[Fact]
	[Trait("Qualification", "Q46")]
	public void LoggerMessageEventsNeverAcceptSensitiveParameterTypes()
	{
		List<string> violations = [];
		int inspectedEvents = 0;
		foreach (ReflectionAssembly assembly in ClientAssemblyCatalog.LoadAll())
		{
			foreach (MethodInfo method in GetLoggerMessageMethods(assembly))
			{
				inspectedEvents++;
				violations.AddRange(FindViolations(method));
			}
		}

		Assert.True(inspectedEvents > 0, "No LoggerMessage event was found; the policy test would pass vacuously.");
		Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
	}

	[Fact]
	[Trait("Qualification", "Q46")]
	public void LoggingPolicyRejectsAddressesFailuresExceptionsAndSensitiveStringNames()
	{
		MethodInfo[] fixtures = GetLoggerMessageMethods(typeof(ClientLoggingPolicyTests).Assembly)
			.Where(static method => method.DeclaringType == typeof(SensitiveLoggingFixture))
			.OrderBy(static method => method.Name, StringComparer.Ordinal)
			.ToArray();

		Assert.Equal(
			["LeakAddress", "LeakException", "LeakFailure", "LeakPath", "SafeCounts"],
			fixtures.Select(static method => method.Name));
		Assert.All(fixtures.Where(static method => method.Name.StartsWith("Leak", StringComparison.Ordinal)),
			static method => Assert.NotEmpty(FindViolations(method)));
		Assert.Empty(FindViolations(fixtures.Single(static method => method.Name == "SafeCounts")));
	}

	internal static IEnumerable<MethodInfo> GetLoggerMessageMethods(ReflectionAssembly assembly)
	{
		const BindingFlags AllDeclared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static |
										 BindingFlags.Instance | BindingFlags.DeclaredOnly;
		foreach (Type type in GetLoadableTypes(assembly))
		{
			foreach (MethodInfo method in type.GetMethods(AllDeclared))
			{
				if (method.GetCustomAttributesData().Any(static attribute =>
						attribute.AttributeType.FullName == LoggerMessageAttributeName))
				{
					yield return method;
				}
			}
		}
	}

	private static IEnumerable<string> FindViolations(MethodInfo method)
	{
		string owner = $"{method.DeclaringType?.FullName}.{method.Name}";
		foreach (ParameterInfo parameter in method.GetParameters())
		{
			Type type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
			if (typeof(ILogger).IsAssignableFrom(type) || type == typeof(LogLevel))
			{
				continue;
			}

			if (SensitiveParameterTypes.Any(sensitive => sensitive.IsAssignableFrom(type)))
			{
				yield return $"{owner} parameter '{parameter.Name}' has user-data type '{type.FullName}'.";
			}
			else if (type == typeof(string) && SensitiveName().IsMatch(parameter.Name ?? string.Empty))
			{
				yield return $"{owner} string parameter '{parameter.Name}' is named like user data.";
			}
		}
	}

	private static Type[] GetLoadableTypes(ReflectionAssembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			return exception.Types.Where(static type => type is not null).ToArray()!;
		}
	}

	[GeneratedRegex("path|file|script|source|message|expression", RegexOptions.IgnoreCase)]
	private static partial Regex SensitiveName();

	/// <summary>Deliberate violations proving that the policy test is not vacuous. Never called.</summary>
	private static partial class SensitiveLoggingFixture
	{
		[LoggerMessage(1, LogLevel.Debug, "Leak {Address}.")]
		internal static partial void LeakAddress(ILogger logger, Address address);

		[LoggerMessage(2, LogLevel.Debug, "Leak {Failure}.")]
		internal static partial void LeakFailure(ILogger logger, CheatEngineFailure failure);

		[LoggerMessage(3, LogLevel.Debug, "Leak an exception.")]
		internal static partial void LeakException(ILogger logger, InvalidOperationException error);

		[LoggerMessage(4, LogLevel.Debug, "Leak {TablePath}.")]
		internal static partial void LeakPath(ILogger logger, string tablePath);

		[LoggerMessage(5, LogLevel.Debug, "Epoch {Epoch} attempted {Count} stage(s).")]
		internal static partial void SafeCounts(ILogger logger, long epoch, int count);
	}
}
