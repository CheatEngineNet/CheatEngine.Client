#pragma warning disable CECLIENT5003 // The operation names cover the experimental instruction client.
#pragma warning disable CECLIENT5004 // The operation names cover the experimental Auto Assembler client.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

using CheatEngine.Client.Allocations;
using CheatEngine.Client.Assembly;
using CheatEngine.Client.Core.Infrastructure;
using CheatEngine.Client.Dispatching;
using CheatEngine.Client.Inspection;
using CheatEngine.Client.Lua;
using CheatEngine.Client.Memory;
using CheatEngine.Client.Processes;
using CheatEngine.Client.Runtime;
using CheatEngine.Client.Scanning;
using CheatEngine.Client.Tables;

namespace CheatEngine.Client.Core.Tests.Infrastructure;

/// <summary>
///     Every failure operation name that Core writes is <c>&lt;Service&gt;.&lt;Member&gt;</c>, as
///     <see cref="Results.CheatEngineFailure.Operation" /> documents: <c>Service</c> is the
///     <see cref="ICheatEngineClient" /> property that exposes the service (<c>UnsafeLua</c> or <c>AutoAssembler</c> for
///     the services only dependency injection registers, <c>Client</c> for the activation itself), and <c>Member</c> is
///     one of its public methods without <c>Try</c> or <c>Detailed</c>, or <c>Release</c> for a lease.
/// </summary>
/// <remarks>
///     The names are read from the metadata of the Core assembly: every string literal of its code (the user-string
///     heap) and every string constant, so a name written inline or through a constant is checked alike, and no Core
///     code runs. Any literal of the shape <c>Word.Word</c> is an operation name.
/// </remarks>
public sealed partial class OperationNameTests
{
	private const string ClientService = "Client";
	private const string ReleaseMember = "Release";
	private const string TryPrefix = "Try";
	private const string DetailedSuffix = "Detailed";

	/// <summary>The services, by the name their failures carry, with the public contracts whose methods they report.</summary>
	private static readonly Dictionary<string, Type[]> Services = new(StringComparer.Ordinal)
	{
		["Runtime"] = [typeof(ICheatEngineRuntime)],
		["Dispatcher"] = [typeof(ICheatEngineDispatcher)],
		["Processes"] = [typeof(IProcessClient)],
		["Memory"] = [typeof(IMemoryClient)],
		["Patterns"] = [typeof(IPatternScanner)],
		["ValueScans"] = [typeof(IValueScanner), typeof(IValueScanSession)],
		["Allocations"] = [typeof(IAllocationClient)],
		["Inspection"] = [typeof(IInspectionClient)],
		["Tables"] = [typeof(ITableClient)],
		["Lua"] = [typeof(ILuaClient)],
		["Assembly"] = [typeof(IAssemblyClient)],
		["UnsafeLua"] = [typeof(IUnsafeLuaClient)],
		["AutoAssembler"] = [typeof(IAutoAssemblerClient)]
	};

	/// <summary>The services that only dependency injection registers: no <see cref="ICheatEngineClient" /> property.</summary>
	private static readonly string[] RegisteredOnly = ["UnsafeLua", "AutoAssembler"];

	/// <summary>The steps of the activation itself, reported under <c>Client</c>.</summary>
	private static readonly string[] ActivationSteps =
		["Activate", "DrainResources", "EnterCleanupScope", "TrackResource"];

	[Fact]
	public void EveryServiceNameIsTheClientPropertyThatExposesIt()
	{
		foreach ((string service, Type[] contracts) in Services)
		{
			PropertyInfo? property = typeof(ICheatEngineClient).GetProperty(service);
			if (RegisteredOnly.Contains(service, StringComparer.Ordinal))
			{
				Assert.True(property is null, $"{service} is registered by dependency injection only.");
				continue;
			}

			Assert.True(property is not null, $"ICheatEngineClient has no {service} property.");
			Assert.Equal(contracts[0], property.PropertyType);
		}
	}

	[Fact]
	public void EveryCoreOperationNameIsAServiceAndOneOfItsPublicMembers()
	{
		string[] names = ReadCoreOperationNames();
		List<string> offenders = [];
		foreach (string name in names)
		{
			Match match = OperationName().Match(name);
			string service = match.Groups["service"].Value;
			string member = match.Groups["member"].Value;
			bool valid = service == ClientService
				? ActivationSteps.Contains(member, StringComparer.Ordinal)
				: Services.TryGetValue(service, out Type[]? contracts) &&
				  (member == ReleaseMember || contracts.Any(contract => PublicMembers(contract).Contains(member)));
			if (!valid)
			{
				offenders.Add(name);
			}
		}

		// The scan cannot pass vacuously: names written inline and through constants are both present.
		Assert.Contains("Patterns.Scan", names);
		Assert.Contains("Memory.Read", names);
		Assert.Contains("Allocations.Release", names);
		Assert.True(offenders.Count == 0,
			"A Core operation name must be <Service>.<Member>: a service of ICheatEngineClient (or UnsafeLua, " +
			"AutoAssembler, Client) and one of its public methods without Try or Detailed, or Release:" +
			Environment.NewLine + string.Join(Environment.NewLine, offenders));
	}

	[Theory]
	[InlineData("TryReadBytes", "ReadBytes")]
	[InlineData("ReadBytesDetailed", "ReadBytes")]
	[InlineData("Scan", "Scan")]
	public void AMemberNameDropsTryAndDetailed(string method, string expected)
	{
		Assert.Equal(expected, MemberName(method));
	}

	/// <summary>Reads every string literal and string constant of Core that has the shape of an operation name.</summary>
	private static string[] ReadCoreOperationNames()
	{
		HashSet<string> names = new(StringComparer.Ordinal);
		using FileStream stream = File.OpenRead(typeof(CoreLifetime).Assembly.Location);
		using PEReader pe = new(stream);
		MetadataReader reader = pe.GetMetadataReader();
		if (reader.GetHeapSize(HeapIndex.UserString) > 1)
		{
			for (UserStringHandle handle = MetadataTokens.UserStringHandle(1);
				 !handle.IsNil;
				 handle = reader.GetNextHandle(handle))
			{
				Add(reader.GetUserString(handle));
			}
		}

		for (int row = 1; row <= reader.GetTableRowCount(TableIndex.Constant); row++)
		{
			Constant constant = reader.GetConstant(MetadataTokens.ConstantHandle(row));
			if (constant.TypeCode == ConstantTypeCode.String)
			{
				BlobReader value = reader.GetBlobReader(constant.Value);
				Add(value.ReadConstant(ConstantTypeCode.String) as string);
			}
		}

		return [.. names.Order(StringComparer.Ordinal)];

		void Add(string? value)
		{
			if (value is not null && OperationName().IsMatch(value))
			{
				_ = names.Add(value);
			}
		}
	}

	/// <summary>Gets the member names a contract exposes: its methods and those of the contracts it extends.</summary>
	private static HashSet<string> PublicMembers(Type contract)
	{
		return
		[
			.. contract.GetInterfaces().Prepend(contract)
				.SelectMany(static type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
				.Where(static method => !method.IsSpecialName)
				.Select(static method => MemberName(method.Name))
		];
	}

	private static string MemberName(string method)
	{
		string member = method.StartsWith(TryPrefix, StringComparison.Ordinal) && method.Length > TryPrefix.Length &&
						char.IsUpper(method[TryPrefix.Length])
			? method[TryPrefix.Length..]
			: method;
		return member.EndsWith(DetailedSuffix, StringComparison.Ordinal)
			? member[..^DetailedSuffix.Length]
			: member;
	}

	[GeneratedRegex(@"^(?<service>[A-Z][A-Za-z]*)\.(?<member>[A-Z][A-Za-z]*)$", RegexOptions.CultureInvariant, 1000)]
	private static partial Regex OperationName();
}
