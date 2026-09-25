using System.Reflection;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

using CheatEngine.Client.Memory;
using CheatEngine.Client.Scanning;

namespace CheatEngine.Client.Fluent.Tests;

/// <summary>
///     Pins the shape of the Fluent public surface: one bound entry point per domain, and builders that are plain readonly
///     structs whose default value rejects its operations with a documented <see cref="InvalidOperationException" />.
/// </summary>
public sealed class FluentSurfaceTests
{
	private static readonly Type[] FluentTypes = typeof(CheatEngineMemoryFluentExtensions).Assembly.GetExportedTypes();

	private static readonly Type[] Builders =
	[
		typeof(MemoryAddressBuilder), typeof(MemoryPointerChainBuilder), typeof(MemoryPrimitiveBatchBuilder<>),
		typeof(AobScanBuilder), typeof(AobFirstMatchBuilder), typeof(AobManyMatchBuilder), typeof(AobSingleMatchBuilder)
	];

	[Fact]
	public void EveryFluentDomainHasOneBoundEntryPoint()
	{
		Dictionary<string, string> expected = new(StringComparer.Ordinal)
		{
			["CheatEngineAobFluentExtensions.Aob(IPatternScanner, AobPattern)"] = nameof(AobScanBuilder),
			["CheatEngineAobFluentExtensions.Aob(IPatternScanner, String)"] = nameof(AobScanBuilder),
			["CheatEngineMemoryFluentExtensions.At(IMemoryClient, Address)"] = nameof(MemoryAddressBuilder),
			["CheatEngineMemoryFluentExtensions.Batch(IMemoryClient)"] = "MemoryPrimitiveBatchBuilder`1"
		};

		Type[] entryPointClasses = [.. FluentTypes.Where(static type => type.IsAbstract && type.IsSealed)];
		Dictionary<string, string> actual = entryPointClasses
			.SelectMany(static type =>
				type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
			.ToDictionary(Describe, static method => method.ReturnType.Name, StringComparer.Ordinal);

		Assert.Equal(expected, actual);
		Assert.Equal(FluentTypes.Length, entryPointClasses.Length + Builders.Length);
		Assert.All(entryPointClasses, static type => Assert.True(type.IsDefined(typeof(ExtensionAttribute), false)));
	}

	[Fact]
	public void BuildersCanOnlyComeFromABoundEntryPoint()
	{
		Assert.All(Builders, static builder =>
		{
			Assert.Contains(builder, FluentTypes);
			Assert.Empty(builder.GetConstructors());
			Assert.DoesNotContain(builder.GetMethods(BindingFlags.Public | BindingFlags.Static), IsBuilderFactory);
			Assert.DoesNotContain(builder.GetMethods(BindingFlags.Public | BindingFlags.Instance),
				static method => method.Name == "Using");
		});
	}

	[Fact]
	public void BuildersArePlainReadonlyStructsWithoutPublicEquality()
	{
		string[] equalityMembers = ["Equals", "GetHashCode", "ToString", "op_Equality", "op_Inequality"];
		string[] recordMembers = ["PrintMembers", "EqualityContract", "<Clone>$", "Deconstruct"];

		Assert.All(Builders, builder =>
		{
			MemberInfo[] declared = builder.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
													   BindingFlags.Instance | BindingFlags.Static |
													   BindingFlags.DeclaredOnly);

			Assert.True(builder.IsValueType, $"{builder.Name} is not a struct.");
			Assert.True(builder.IsDefined(typeof(IsReadOnlyAttribute), false), $"{builder.Name} is not readonly.");
			Assert.DoesNotContain(declared, member => equalityMembers.Contains(member.Name) && IsPublic(member));
			Assert.DoesNotContain(declared, member => recordMembers.Contains(member.Name));
			Assert.DoesNotContain(builder.GetInterfaces(), static contract =>
				contract.IsGenericType && contract.GetGenericTypeDefinition() == typeof(IEquatable<>));
		});
	}

	[Fact]
	public void EveryDefaultBuilderRejectsItsOperationsAndNamesItsEntryPoint()
	{
		CancellationToken cancellationToken = TestContext.Current.CancellationToken;
		Action[] operations =
		[
			() => default(MemoryAddressBuilder).Read<int>(cancellationToken),
			() => default(MemoryAddressBuilder).Follow([0x10L]),
			() => default(MemoryPointerChainBuilder).Resolve(cancellationToken),
			() => default(MemoryPrimitiveBatchBuilder<int>).Read([], cancellationToken),
			() => default(AobScanBuilder).FirstOrNone(),
			() => default(AobScanBuilder).RequireSingle(),
			() => default(AobScanBuilder).Take(1),
			() => default(AobFirstMatchBuilder).TryExecute(out _, out _, cancellationToken),
			() => default(AobManyMatchBuilder).TryExecute(out _, out _, cancellationToken),
			() => default(AobSingleMatchBuilder).TryExecute(out _, out _, cancellationToken)
		];

		Assert.All(operations, static operation =>
		{
			InvalidOperationException exception = Assert.Throws<InvalidOperationException>(operation);
			Assert.Contains("is a default value without", exception.Message, StringComparison.Ordinal);
			Assert.Matches(@"client\.(Memory|Patterns)\.(At|Batch|Aob)", exception.Message);
		});
	}

	[Fact]
	public void EveryBuilderOperationDocumentsTheDefaultBuilderException()
	{
		XDocument documentation =
			XDocument.Load(Path.ChangeExtension(typeof(MemoryAddressBuilder).Assembly.Location, ".xml"));
		XElement[] members = [.. documentation.Descendants("member")];

		// Every operation but the configuration methods, which return a new builder of their own type.
		string[] operations =
		[
			.. Builders.SelectMany(static builder => builder
					.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
					.Where(method => !method.IsSpecialName && method.ReturnType != builder)
					.Select(method => $"M:{builder.FullName}.{method.Name}"))
				.Distinct(StringComparer.Ordinal)
		];

		Assert.NotEmpty(operations);
		Assert.All(operations, operation =>
		{
			XElement[] documented = [.. members.Where(member => IsDocumentationOf((string) member.Attribute("name")!,
				operation))];

			Assert.NotEmpty(documented);
			Assert.All(documented, static member => Assert.Contains(member.Elements("exception"), static exception =>
				(string?) exception.Attribute("cref") == "T:System.InvalidOperationException"));
		});
	}

	/// <summary>
	///     Every terminal documents the Client exceptions it can raise: a throwing terminal the four types that
	///     <c>CheatEngineFailure.Throw</c> maps a failure to, and a <c>Try</c> terminal, which returns the failure, the two
	///     lifecycle exceptions that the bound service throws once the activation has ended or while it is stopping.
	/// </summary>
	[Fact]
	public void EveryTerminalDocumentsTheClientExceptionsItCanRaise()
	{
		XDocument documentation =
			XDocument.Load(Path.ChangeExtension(typeof(MemoryAddressBuilder).Assembly.Location, ".xml"));
		XElement[] members = [.. documentation.Descendants("member")];
		string[] lifecycle =
		[
			"T:CheatEngine.Client.Results.CheatEngineActivationExpiredException",
			"T:CheatEngine.Client.Results.CheatEngineInvalidStateException"
		];
		string[] throwing =
		[
			.. lifecycle, "T:CheatEngine.Client.Results.CheatEngineOperationException",
			"T:CheatEngine.Client.Results.CheatEngineOperationCanceledException"
		];

		// Every operation that runs work: configuration and terminal selection return a builder.
		string[] terminals =
		[
			.. Builders.SelectMany(static builder => builder
					.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
					.Where(static method => !method.IsSpecialName && !IsBuilder(method.ReturnType))
					.Select(method => $"M:{builder.FullName}.{method.Name}"))
				.Distinct(StringComparer.Ordinal)
		];

		Assert.Equal(28, terminals.Length);
		Assert.All(terminals, terminal =>
		{
			bool isTry = terminal[(terminal.LastIndexOf('.') + 1)..].StartsWith("Try", StringComparison.Ordinal);
			XElement[] documented = [.. members.Where(member => IsDocumentationOf((string) member.Attribute("name")!,
				terminal))];

			Assert.NotEmpty(documented);
			Assert.All(documented, member =>
			{
				string[] exceptions =
					[.. member.Elements("exception").Select(static exception => (string) exception.Attribute("cref")!)];
				Assert.All(isTry ? lifecycle : throwing, expected => Assert.Contains(expected, exceptions));
			});
		});
	}

	private static bool IsBuilder(Type type)
	{
		return Builders.Contains(type.IsGenericType ? type.GetGenericTypeDefinition() : type);
	}

	private static bool IsDocumentationOf(string memberId, string operation)
	{
		return memberId == operation || memberId.StartsWith(operation + "(", StringComparison.Ordinal) ||
			   memberId.StartsWith(operation + "``", StringComparison.Ordinal);
	}

	private static bool IsPublic(MemberInfo member)
	{
		return member is MethodBase { IsPublic: true } or PropertyInfo { GetMethod.IsPublic: true };
	}

	private static bool IsBuilderFactory(MethodInfo method)
	{
		return IsBuilder(method.ReturnType);
	}

	private static string Describe(MethodInfo method)
	{
		Assert.True(method.IsDefined(typeof(ExtensionAttribute), false), $"{method} is not an extension method.");
		return $"{method.DeclaringType!.Name}.{method.Name}(" +
			   string.Join(", ", method.GetParameters().Select(static parameter => parameter.ParameterType.Name)) + ")";
	}
}
