using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml.Linq;

using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Results;

namespace CheatEngine.Client.Tests.Architecture;

/// <summary>
///     The XML documentation of the public surface of Abstractions, Fluent, dependency injection and Hosting is
///     complete and states the exceptions of the failure contract (Abstractions README, "Failure, exception and
///     cancellation contract" and "Public API charter").
/// </summary>
/// <remarks>
///     <para>
///         Each assembly is read with its generated XML documentation file, from the test output (the files the
///         packages contain); the public and protected members come from reflection and are matched by documentation
///         id. No Client code runs.
///     </para>
///     <para>
///         <b>Structure.</b> Every public type and member has its own summary (never <c>inheritdoc</c>), every
///         parameter a <c>param</c>, every type parameter a <c>typeparam</c>, every method, operator and delegate that
///         returns a value a <c>returns</c>, and every <c>exception</c> a condition. A property is described by its
///         summary.
///     </para>
///     <para>
///         <b>Operation forms.</b> A <c>Try</c> form (a <c>Try</c> method that returns <see cref="bool" /> with an
///         <c>out CheatEngineFailure failure</c>) and its <c>Detailed</c> twin return their failures, so they document
///         only what they can still throw: <see cref="CheatEngineActivationExpiredException" /> and
///         <see cref="CheatEngineInvalidStateException" />, never <see cref="CheatEngineOperationException" /> or
///         <see cref="CheatEngineOperationCanceledException" />. The throwing twin with the same inputs documents the
///         four exceptions <see cref="CheatEngineFailure.Throw(CancellationToken)" /> maps a failure to, the
///         cancellation only when it takes a token. The implementable callbacks of the charter keep the <c>Try</c>
///         shape but are implemented by the application, so the Client documents nothing they throw.
///     </para>
///     <para>
///         <b>Arguments.</b> Every form of one operation documents the same argument exceptions. An operation form
///         documents an <see cref="ArgumentException" /> (or <see cref="ArgumentNullException" /> or
///         <see cref="ArgumentOutOfRangeException" />) that names each enum parameter and each Client input value
///         (<c>*Request</c>, <c>*Definition</c>, <c>*Update</c>, <c>*Search</c>, <c>*Registration</c>, <c>*Script</c>)
///         with <c>paramref</c>, since an undefined value or a <see langword="default" /> request throws; every public
///         method and constructor does so for each reference-type parameter that is not nullable. Members that the
///         application implements or overrides are called by the Client and document no argument exception.
///     </para>
///     <para>
///         <b>Coverage.</b> Each rule asserts a floor on what it applied to (the members for the structure rules, the
///         classified operation forms, the validated parameters), and known members pin the classification, so a rule
///         that stops recognizing what it checks fails instead of passing on nothing.
///     </para>
/// </remarks>
public sealed class PublicApiDocumentationTests
{
	private const string TryPrefix = "Try";
	private const string DetailedSuffix = "Detailed";

	/// <summary>A floor below the 158 operation forms (78 Try, 76 throwing, 4 Detailed) of the 1.0 surface.</summary>
	private const int OperationFormFloor = 150;

	/// <summary>A floor below the 149 parameters the argument rule validates on the 1.0 surface.</summary>
	private const int ValidatedParameterFloor = 130;

	private const string ActivationExpired = "T:CheatEngine.Client.Results.CheatEngineActivationExpiredException";
	private const string InvalidState = "T:CheatEngine.Client.Results.CheatEngineInvalidStateException";
	private const string OperationFailed = "T:CheatEngine.Client.Results.CheatEngineOperationException";
	private const string OperationCanceled = "T:CheatEngine.Client.Results.CheatEngineOperationCanceledException";

	private static readonly string[] DocumentedAssemblies =
	[
		"CheatEngine.Client.Abstractions",
		"CheatEngine.Client.Fluent",
		"CheatEngine.Client.Extensions.DependencyInjection",
		"CheatEngine.Client.Hosting"
	];

	private static readonly string[] ArgumentExceptions =
		["T:System.ArgumentException", "T:System.ArgumentNullException", "T:System.ArgumentOutOfRangeException"];

	/// <summary>The suffixes of the charter's validated input values ("Value types and outcomes").</summary>
	private static readonly string[] InputSuffixes =
		["Request", "Definition", "Update", "Search", "Registration", "Script"];

	/// <summary>
	///     The interfaces the application implements and the Client calls (charter, "Call-only and Implementable
	///     interfaces"), by full name.
	/// </summary>
	private static readonly HashSet<string> ImplementableInterfaces = new(StringComparer.Ordinal)
	{
		"CheatEngine.Client.Lua.ILuaModule",
		"CheatEngine.Client.Lua.ILuaOperation`1",
		"CheatEngine.Client.Lua.ILuaResultMapper`2",
		"CheatEngine.Client.Memory.IMemoryCodec`1",
		"CheatEngine.Client.Modules.ICheatEngineClientModule"
	};

	/// <summary>
	///     The operation forms that need no activation, by declaring type and name: they never throw the lifecycle
	///     exceptions, so their documentation must not list them (charter: <c>IProcessClient.TryGetLocalProcesses</c>
	///     needs no activation).
	/// </summary>
	private static readonly HashSet<string> ActivationFreeOperations = new(StringComparer.Ordinal)
	{
		"CheatEngine.Client.Processes.IProcessClient.TryGetLocalProcesses",
		"CheatEngine.Client.Processes.IProcessClient.GetLocalProcesses"
	};

	/// <summary>
	///     Members whose documentation may break one rule, by documentation id, with the reason. Empty: an entry needs
	///     a reason a reviewer accepts, and <see cref="EveryExemptionNamesAMemberThatStillBreaksARule" /> removes it
	///     once the documentation complies.
	/// </summary>
	private static readonly Dictionary<string, string> Exemptions = new(StringComparer.Ordinal);

	private static readonly Lazy<DocumentedMember[]> Members = new(static () => [.. ReadMembers()]);

	[Fact]
	public void EveryPublicTypeAndMemberHasItsOwnSummary()
	{
		AssertNoOffenders(Check(static member => CheckSummary(member), static _ => 1), 800,
			"public types and members",
			"A public type or member has no summary of its own; document it instead of inheriting its documentation:");
	}

	[Fact]
	public void EveryParameterTypeParameterAndReturnValueIsDocumented()
	{
		AssertNoOffenders(Check(static member => CheckSignature(member), static _ => 1), 800,
			"public types and members",
			"A public member leaves a parameter, a type parameter or its return value undocumented:");
	}

	[Fact]
	public void EveryOperationFormDocumentsTheClientExceptionsItCanRaise()
	{
		AssertNoOffenders(
			Check(static member => CheckOperationForm(member),
				static member => FormOf(member) == OperationForm.None ? 0 : 1), OperationFormFloor,
			"operation forms",
			"An operation form does not document the Client exceptions of the failure contract:");
	}

	[Fact]
	public void EveryValidatedArgumentDocumentsItsArgumentException()
	{
		AssertNoOffenders(
			Check(static member => CheckArguments(member), static member => ValidatedParameters(member).Count()),
			ValidatedParameterFloor, "validated parameters",
			"A public member does not document the argument exception of a parameter it validates:");
	}

	[Fact]
	public void EachOperationFormIsRecognizedOnTheSurface()
	{
		Dictionary<OperationForm, int> counts = DocumentedMembers()
			.GroupBy(static member => FormOf(member))
			.ToDictionary(static group => group.Key, static group => group.Count());
		int tryForms = counts.GetValueOrDefault(OperationForm.Try);
		int throwingForms = counts.GetValueOrDefault(OperationForm.Throwing);
		int detailedForms = counts.GetValueOrDefault(OperationForm.Detailed);

		Assert.True(tryForms > 70, $"Only {tryForms} Try forms were recognized.");
		Assert.True(throwingForms > 70, $"Only {throwingForms} throwing forms were recognized.");
		Assert.True(detailedForms >= 4, $"Only {detailedForms} Detailed forms were recognized.");
	}

	[Fact]
	public void KnownMembersAreClassifiedAsTheCharterNamesThem()
	{
		AssertForm(OperationForm.Try, typeof(IMemoryClient), nameof(IMemoryClient.TryReadBytes));
		AssertForm(OperationForm.Throwing, typeof(IMemoryClient), nameof(IMemoryClient.ReadBytes));
		AssertForm(OperationForm.Detailed, typeof(IMemoryClient), nameof(IMemoryClient.ReadBytesDetailed));
		AssertForm(OperationForm.Try, typeof(IProcessClient), nameof(IProcessClient.TryGetLocalProcesses));
		AssertForm(OperationForm.Throwing, typeof(IProcessClient), nameof(IProcessClient.GetLocalProcesses));
		AssertForm(OperationForm.Throwing, typeof(ICheatEngineDispatcher), nameof(ICheatEngineDispatcher.Invoke));
		AssertForm(OperationForm.Throwing, typeof(MemoryAddressBuilder), nameof(MemoryAddressBuilder.ReadString));
		AssertForm(OperationForm.None, typeof(IMemoryCodec<>), nameof(IMemoryCodec<>.TryRead));

		AssertReason("a validated input value", typeof(IMemoryClient), nameof(IMemoryClient.ReadBytes), "request");
		AssertReason("an enum", typeof(MemoryAddressBuilder), nameof(MemoryAddressBuilder.ReadString), "encoding");
		AssertReason("a non-nullable reference", typeof(ICheatEngineDispatcher), nameof(ICheatEngineDispatcher.Invoke),
			"callback");
		Assert.Empty(DocumentedMembers().Where(static member =>
			member.Member.DeclaringType == typeof(IMemoryCodec<>)).SelectMany(ValidatedParameters));
	}

	[Fact]
	public void EveryActivationFreeOperationNamesAnOperationForm()
	{
		Assert.All(ActivationFreeOperations, operation => Assert.Contains(DocumentedMembers(), member =>
			member.Member is MethodInfo method && FormOf(member) != OperationForm.None &&
			$"{method.DeclaringType!.FullName}.{method.Name}" == operation));
	}

	[Fact]
	public void EveryExemptionNamesAMemberThatStillBreaksARule()
	{
		Dictionary<string, DocumentedMember> members = DocumentedMembers()
			.ToDictionary(static member => member.Id, StringComparer.Ordinal);
		Assert.All(Exemptions, exemption =>
		{
			Assert.False(string.IsNullOrWhiteSpace(exemption.Value), $"{exemption.Key} has no reason.");
			Assert.True(members.TryGetValue(exemption.Key, out DocumentedMember? member),
				$"{exemption.Key} is not a documented public member.");
			Assert.NotEmpty(Violations(member));
		});
	}

	private static IEnumerable<string> Violations(DocumentedMember member)
	{
		return CheckSummary(member).Concat(CheckSignature(member)).Concat(CheckOperationForm(member))
			.Concat(CheckArguments(member));
	}

	/// <summary>Applies a rule to every documented member and counts what the rule applied to.</summary>
	/// <param name="rule">The offences of one member.</param>
	/// <param name="applicability">How many things of one member the rule checks (forms, parameters).</param>
	private static (int Applicable, List<string> Offenders) Check(
		Func<DocumentedMember, IEnumerable<string>> rule, Func<DocumentedMember, int> applicability)
	{
		int applicable = 0;
		List<string> offenders = [];
		foreach (DocumentedMember member in DocumentedMembers())
		{
			applicable += applicability(member);
			if (!Exemptions.ContainsKey(member.Id))
			{
				offenders.AddRange(rule(member).Select(offence => $"{member.Id}: {offence}"));
			}
		}

		return (applicable, offenders);
	}

	private static void AssertNoOffenders((int Applicable, List<string> Offenders) result, int minimum,
		string applicableTo, string title)
	{
		Assert.True(result.Applicable > minimum,
			$"The rule applied to only {result.Applicable} {applicableTo}; it no longer recognizes what it checks.");
		Assert.True(result.Offenders.Count == 0,
			title + Environment.NewLine + string.Join(Environment.NewLine, result.Offenders));
	}

	private static void AssertForm(OperationForm expected, Type type, string name)
	{
		OperationForm[] forms = [.. MethodsNamed(type, name).Select(FormOf)];
		Assert.NotEmpty(forms);
		Assert.All(forms, form => Assert.Equal(expected, form));
	}

	private static void AssertReason(string expected, Type type, string name, string parameter)
	{
		string[] reasons =
		[
			.. MethodsNamed(type, name).SelectMany(ValidatedParameters)
				.Where(validated => validated.Parameter.Name == parameter)
				.Select(static validated => validated.Reason)
		];
		Assert.NotEmpty(reasons);
		Assert.All(reasons, reason => Assert.Equal(expected, reason));
	}

	private static IEnumerable<DocumentedMember> MethodsNamed(Type type, string name)
	{
		return DocumentedMembers().Where(member =>
			member.Member is MethodInfo method && method.DeclaringType == type && method.Name == name);
	}

	private static OperationForm FormOf(DocumentedMember member)
	{
		return member.Member is MethodInfo method ? Classify(method) : OperationForm.None;
	}

	private static IEnumerable<string> CheckSummary(DocumentedMember member)
	{
		if (member.Documentation is not { } documentation)
		{
			yield return "no documentation";
			yield break;
		}

		if (documentation.Descendants("inheritdoc").Any())
		{
			yield return "inherits its documentation";
		}

		if (!HasText(documentation.Element("summary")))
		{
			yield return "no summary";
		}
	}

	private static IEnumerable<string> CheckSignature(DocumentedMember member)
	{
		if (member.Documentation is not { } documentation)
		{
			yield break;
		}

		foreach (string parameter in member.Parameters)
		{
			if (!HasText(Named(documentation, "param", parameter)))
			{
				yield return $"no param '{parameter}'";
			}
		}

		foreach (string typeParameter in member.TypeParameters)
		{
			if (!HasText(Named(documentation, "typeparam", typeParameter)))
			{
				yield return $"no typeparam '{typeParameter}'";
			}
		}

		if (member.HasReturnValue && !HasText(documentation.Element("returns")))
		{
			yield return "no returns";
		}

		foreach (XElement exception in documentation.Elements("exception"))
		{
			if (!HasText(exception))
			{
				yield return $"exception {(string?) exception.Attribute("cref")} states no condition";
			}
		}
	}

	private static IEnumerable<string> CheckOperationForm(DocumentedMember member)
	{
		if (member.Member is not MethodInfo method || member.Documentation is not { } documentation)
		{
			yield break;
		}

		OperationForm form = Classify(method);
		if (form == OperationForm.None)
		{
			yield break;
		}

		HashSet<string> documented = ExceptionCrefs(documentation);
		string operation = $"{method.DeclaringType!.FullName}.{method.Name}";
		bool needsActivation = !ActivationFreeOperations.Contains(operation);
		foreach (string lifecycle in (string[]) [ActivationExpired, InvalidState])
		{
			if (needsActivation && !documented.Contains(lifecycle))
			{
				yield return $"{form} form does not document {lifecycle[2..]}";
			}
			else if (!needsActivation && documented.Contains(lifecycle))
			{
				yield return $"{form} form needs no activation but documents {lifecycle[2..]}";
			}
		}

		bool takesToken = method.GetParameters().Any(static p => p.ParameterType == typeof(CancellationToken));
		if (form == OperationForm.Throwing)
		{
			if (!documented.Contains(OperationFailed))
			{
				yield return $"throwing form does not document {OperationFailed[2..]}";
			}

			if (takesToken && !documented.Contains(OperationCanceled))
			{
				yield return $"throwing form takes a token but does not document {OperationCanceled[2..]}";
			}
		}
		else
		{
			foreach (string returned in (string[]) [OperationFailed, OperationCanceled])
			{
				if (documented.Contains(returned))
				{
					yield return $"{form} form returns its failures but documents {returned[2..]}";
				}
			}
		}

		MethodInfo? tryForm = form == OperationForm.Try ? method : FindTryForm(method, form);
		if (tryForm is not null && tryForm != method &&
			DocumentationOf(tryForm) is { } tryDocumentation &&
			!ArgumentCrefs(tryDocumentation).SetEquals(ArgumentCrefs(documentation)))
		{
			yield return $"documents other argument exceptions than {tryForm.Name}";
		}
	}

	private static IEnumerable<string> CheckArguments(DocumentedMember member)
	{
		if (member.Documentation is not { } documentation)
		{
			return [];
		}

		return ValidatedParameters(member)
			.Where(validated => !DocumentsArgument(documentation, validated.Parameter.Name!))
			.Select(static validated =>
				$"'{validated.Parameter.Name}' is {validated.Reason} without an argument exception that names it");
	}

	/// <summary>The parameters the argument rule applies to, each with the reason it is validated.</summary>
	private static IEnumerable<(ParameterInfo Parameter, string Reason)> ValidatedParameters(DocumentedMember member)
	{
		if (member.Member is not MethodBase method || IsImplementedByTheApplication(method))
		{
			yield break;
		}

		bool isOperation = method is MethodInfo info && Classify(info) != OperationForm.None;
		NullabilityInfoContext nullability = new();
		foreach (ParameterInfo parameter in method.GetParameters())
		{
			Type type = parameter.ParameterType;
			if (parameter.IsOut || type.IsByRef)
			{
				continue;
			}

			string? reason = null;
			if (!type.IsValueType && !type.IsGenericParameter &&
				nullability.Create(parameter).ReadState == NullabilityState.NotNull)
			{
				reason = "a non-nullable reference";
			}
			else if (isOperation && type.IsEnum)
			{
				reason = "an enum";
			}
			else if (isOperation && IsClientInput(type))
			{
				reason = "a validated input value";
			}

			if (reason is not null)
			{
				yield return (parameter, reason);
			}
		}
	}

	private static bool DocumentsArgument(XElement documentation, string parameter)
	{
		return documentation.Elements("exception")
			.Where(static exception => ArgumentExceptions.Contains((string?) exception.Attribute("cref")))
			.Any(exception => exception.Descendants("paramref")
				.Any(reference => (string?) reference.Attribute("name") == parameter));
	}

	private static bool IsClientInput(Type type)
	{
		Type definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
		string name = definition.Name.Split('`')[0];
		return definition.IsValueType &&
			   DocumentedAssemblies.Contains(definition.Assembly.GetName().Name, StringComparer.Ordinal) &&
			   InputSuffixes.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal));
	}

	private static bool IsImplementedByTheApplication(MethodBase method)
	{
		Type type = method.DeclaringType!;
		string name = type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName!;
		return ImplementableInterfaces.Contains(name) ||
			   (method is MethodInfo { IsAbstract: true } or MethodInfo { IsVirtual: true, IsFinal: false } &&
				!type.IsInterface && !type.IsSealed);
	}

	private static OperationForm Classify(MethodInfo method)
	{
		Type type = method.DeclaringType!;
		string typeName = type.IsGenericType ? type.GetGenericTypeDefinition().FullName! : type.FullName!;
		if (ImplementableInterfaces.Contains(typeName) || method.IsSpecialName)
		{
			return OperationForm.None;
		}

		if (IsTryForm(method))
		{
			return OperationForm.Try;
		}

		if (method.Name.EndsWith(DetailedSuffix, StringComparison.Ordinal) &&
			FindTryForm(method, OperationForm.Detailed) is not null)
		{
			return OperationForm.Detailed;
		}

		return FindTryForm(method, OperationForm.Throwing) is not null ? OperationForm.Throwing : OperationForm.None;
	}

	private static bool IsTryForm(MethodInfo method)
	{
		return method.Name.StartsWith(TryPrefix, StringComparison.Ordinal) && method.ReturnType == typeof(bool) &&
			   method.GetParameters().Any(static parameter =>
				   parameter.IsOut && parameter.ParameterType.GetElementType() == typeof(CheatEngineFailure));
	}

	/// <summary>Finds the <c>Try</c> form with the same inputs as a throwing or <c>Detailed</c> form.</summary>
	private static MethodInfo? FindTryForm(MethodInfo method, OperationForm form)
	{
		string name = form == OperationForm.Detailed ? method.Name[..^DetailedSuffix.Length] : method.Name;
		if (name.StartsWith(TryPrefix, StringComparison.Ordinal))
		{
			return null;
		}

		string[] inputs = Inputs(method);
		return method.DeclaringType!
			.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.FirstOrDefault(candidate => candidate.Name == TryPrefix + name && IsTryForm(candidate) &&
										 candidate.GetGenericArguments().Length ==
										 method.GetGenericArguments().Length &&
										 Inputs(candidate).SequenceEqual(inputs, StringComparer.Ordinal));

		static string[] Inputs(MethodInfo method)
		{
			return
			[
				.. method.GetParameters()
					.Where(static parameter => !parameter.IsOut && parameter.ParameterType != typeof(CancellationToken))
					.Select(static parameter => parameter.ParameterType.IsGenericMethodParameter
						? $"!!{parameter.ParameterType.GenericParameterPosition}"
						: parameter.ParameterType.ToString())
			];
		}
	}

	private static XElement? DocumentationOf(MethodInfo method)
	{
		return Members.Value.FirstOrDefault(member => member.Member == method)?.Documentation;
	}

	private static HashSet<string> ExceptionCrefs(XElement documentation)
	{
		return
		[
			.. documentation.Elements("exception").Select(static exception => (string?) exception.Attribute("cref"))
				.OfType<string>()
		];
	}

	private static HashSet<string> ArgumentCrefs(XElement documentation)
	{
		HashSet<string> crefs = ExceptionCrefs(documentation);
		crefs.IntersectWith(ArgumentExceptions);
		return crefs;
	}

	private static XElement? Named(XElement documentation, string tag, string name)
	{
		return documentation.Elements(tag).FirstOrDefault(element => (string?) element.Attribute("name") == name);
	}

	private static bool HasText(XElement? element)
	{
		return element is not null && (element.HasElements || !string.IsNullOrWhiteSpace(element.Value));
	}

	private static DocumentedMember[] DocumentedMembers()
	{
		return Members.Value;
	}

	private static IEnumerable<DocumentedMember> ReadMembers()
	{
		foreach (string name in DocumentedAssemblies)
		{
			System.Reflection.Assembly assembly = ClientAssemblyCatalog.Load(name);
			Dictionary<string, XElement> documentation = XDocument
				.Load(Path.ChangeExtension(assembly.Location, ".xml"))
				.Descendants("member")
				.ToDictionary(static member => (string) member.Attribute("name")!, StringComparer.Ordinal);
			foreach (Type type in assembly.GetExportedTypes())
			{
				yield return DescribeType(type, documentation);
				foreach (MemberInfo member in type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
															  BindingFlags.Instance | BindingFlags.Static |
															  BindingFlags.DeclaredOnly))
				{
					if (member is not Type && IsVisible(member) && !IsGenerated(member))
					{
						yield return DescribeMember(member, documentation);
					}
				}
			}
		}
	}

	private static DocumentedMember DescribeType(Type type, Dictionary<string, XElement> documentation)
	{
		string id = "T:" + DocumentationIds.TypeName(type);
		int inherited = type.DeclaringType?.GetGenericArguments().Length ?? 0;
		string[] typeParameters = [.. type.GetGenericArguments().Skip(inherited).Select(static t => t.Name)];
		string[] parameters = [];
		bool returns = false;
		if (type.IsSubclassOf(typeof(Delegate)))
		{
			MethodInfo invoke = type.GetMethod("Invoke")!;
			parameters = [.. invoke.GetParameters().Select(static parameter => parameter.Name!)];
			returns = invoke.ReturnType != typeof(void);
		}

		return new DocumentedMember(id, type, documentation.GetValueOrDefault(id), parameters, typeParameters, returns);
	}

	private static DocumentedMember DescribeMember(MemberInfo member, Dictionary<string, XElement> documentation)
	{
		string id = DocumentationIds.Of(member);
		string[] parameters = member switch
		{
			MethodBase method => [.. method.GetParameters().Select(static parameter => parameter.Name!)],
			PropertyInfo property => [.. property.GetIndexParameters().Select(static parameter => parameter.Name!)],
			_ => []
		};
		string[] typeParameters = member is MethodInfo { IsGenericMethodDefinition: true } generic
			? [.. generic.GetGenericArguments().Select(static t => t.Name)]
			: [];
		bool returns = member is MethodInfo { ReturnType: var returnType } && returnType != typeof(void);
		return new DocumentedMember(id, member, documentation.GetValueOrDefault(id), parameters, typeParameters,
			returns);
	}

	private static bool IsVisible(MemberInfo member)
	{
		return member switch
		{
			MethodBase method => (method is ConstructorInfo || !method.IsSpecialName ||
								  method.Name.StartsWith("op_", StringComparison.Ordinal)) && IsVisible(method),
			PropertyInfo property => property.GetAccessors(true).Any(IsVisible),
			EventInfo @event => IsVisible(@event.AddMethod!),
			FieldInfo field => !field.IsSpecialName && (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly),
			_ => false
		};
	}

	private static bool IsVisible(MethodBase method)
	{
		return method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly;
	}

	/// <summary>Members the compiler synthesizes (record members), which carry no documentation of their own.</summary>
	private static bool IsGenerated(MemberInfo member)
	{
		return member.IsDefined(typeof(CompilerGeneratedAttribute), false) ||
			   (member is PropertyInfo { Name: "EqualityContract" } property &&
				property.GetAccessors(true).All(static accessor =>
					accessor.IsDefined(typeof(CompilerGeneratedAttribute), false)));
	}

	private enum OperationForm
	{
		None = 0,
		Try = 1,
		Throwing = 2,
		Detailed = 3
	}

	private sealed record DocumentedMember(
		string Id,
		MemberInfo Member,
		XElement? Documentation,
		string[] Parameters,
		string[] TypeParameters,
		bool HasReturnValue);

	/// <summary>
	///     Computes the documentation id the compiler writes for a type or member (ECMA-334, annex D.4.2).
	/// </summary>
	private static class DocumentationIds
	{
		internal static string Of(MemberInfo member)
		{
			string type = TypeName(member.DeclaringType!);
			return member switch
			{
				ConstructorInfo constructor =>
					$"M:{type}.{(constructor.IsStatic ? "#cctor" : "#ctor")}{Parameters(constructor.GetParameters())}",
				MethodInfo method => $"M:{type}.{method.Name.Replace('.', '#')}" +
									 (method.IsGenericMethodDefinition
										 ? "``" + method.GetGenericArguments().Length
										 : string.Empty) +
									 Parameters(method.GetParameters()) +
									 (method.Name is "op_Implicit" or "op_Explicit"
										 ? "~" + ParameterType(method.ReturnType)
										 : string.Empty),
				PropertyInfo property => $"P:{type}.{property.Name}{Parameters(property.GetIndexParameters())}",
				FieldInfo field => $"F:{type}.{field.Name}",
				EventInfo @event => $"E:{type}.{@event.Name}",
				_ => throw new ArgumentOutOfRangeException(nameof(member), member, "Unsupported member.")
			};
		}

		internal static string TypeName(Type type)
		{
			return type.FullName!.Replace('+', '.');
		}

		private static string Parameters(ParameterInfo[] parameters)
		{
			return parameters.Length == 0
				? string.Empty
				: "(" + string.Join(",", parameters.Select(static p => ParameterType(p.ParameterType))) + ")";
		}

		private static string ParameterType(Type type)
		{
			if (type.IsByRef)
			{
				return ParameterType(type.GetElementType()!) + "@";
			}

			if (type.IsPointer)
			{
				return ParameterType(type.GetElementType()!) + "*";
			}

			if (type.IsArray)
			{
				return ParameterType(type.GetElementType()!) + (type.IsSZArray
					? "[]"
					: "[" + string.Join(",", Enumerable.Repeat("0:", type.GetArrayRank())) + "]");
			}

			if (type.IsGenericParameter)
			{
				return (type.DeclaringMethod is null ? "`" : "``") + type.GenericParameterPosition;
			}

			return type.IsGenericType ? GenericTypeName(type) : TypeName(type);
		}

		/// <summary>Writes a constructed type with its arguments in braces, level by level of nesting.</summary>
		private static string GenericTypeName(Type type)
		{
			Type[] arguments = type.GetGenericArguments();
			List<Type> levels = [];
			for (Type? level = type.GetGenericTypeDefinition(); level is not null; level = level.DeclaringType)
			{
				levels.Insert(0, level);
			}

			StringBuilder text = new(levels[0].Namespace is { Length: > 0 } space ? space + "." : string.Empty);
			int used = 0;
			for (int index = 0; index < levels.Count; index++)
			{
				string name = levels[index].Name;
				int tick = name.IndexOf('`', StringComparison.Ordinal);
				int arity = tick < 0 ? 0 : int.Parse(name[(tick + 1)..], CultureInfo.InvariantCulture);
				text.Append(index == 0 ? string.Empty : ".").Append(tick < 0 ? name : name[..tick]);
				if (arity > 0)
				{
					text.Append('{')
						.Append(string.Join(",", arguments.Skip(used).Take(arity).Select(ParameterType)))
						.Append('}');
					used += arity;
				}
			}

			return text.ToString();
		}
	}
}
