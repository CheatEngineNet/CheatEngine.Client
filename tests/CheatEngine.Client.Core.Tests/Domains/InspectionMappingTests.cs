using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;
using CheatEngine.Client.Results;
using CheatEngine.SDK.Lua.Calls;

namespace CheatEngine.Client.Core.Tests.Domains;

/// <summary>
///     Every status of the CheatEngine.SDK 2.0.0 symbol registry has a deliberate Client counterpart, and a value the SDK
///     could add later fails closed (Q48).
/// </summary>
public sealed class InspectionMappingTests
{
	private static readonly Dictionary<LuaOperationStatusKind, CheatEngineFailureKind> NameLookupKinds = new()
	{
		[LuaOperationStatusKind.Unknown] = CheatEngineFailureKind.IndeterminateHostResult,
		[LuaOperationStatusKind.Success] = CheatEngineFailureKind.InvalidHostResult,
		[LuaOperationStatusKind.GlobalUnavailable] = CheatEngineFailureKind.CapabilityUnavailable,
		[LuaOperationStatusKind.LuaFailure] = CheatEngineFailureKind.LuaError,
		[LuaOperationStatusKind.NilResult] = CheatEngineFailureKind.NotFound,
		[LuaOperationStatusKind.InvalidResult] = CheatEngineFailureKind.InvalidHostResult,
		[LuaOperationStatusKind.StackUnavailable] = CheatEngineFailureKind.LuaError,
		[LuaOperationStatusKind.MissingResult] = CheatEngineFailureKind.InvalidHostResult,
		[LuaOperationStatusKind.ResultCapacityExceeded] = CheatEngineFailureKind.InvalidHostResult
	};

	private static readonly Dictionary<LuaOperationStatusKind, (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect)>
		RegistrationFailures = new()
		{
			[LuaOperationStatusKind.Unknown] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.Unknown),
			[LuaOperationStatusKind.Success] =
				(CheatEngineFailureKind.IndeterminateHostResult, CheatEngineHostEffect.CleanupUnconfirmed),
			[LuaOperationStatusKind.GlobalUnavailable] =
				(CheatEngineFailureKind.CapabilityUnavailable, CheatEngineHostEffect.Unknown),
			[LuaOperationStatusKind.LuaFailure] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.NilResult] = (CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.InvalidResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.StackUnavailable] = (CheatEngineFailureKind.LuaError, CheatEngineHostEffect.NotStarted),
			[LuaOperationStatusKind.MissingResult] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started),
			[LuaOperationStatusKind.ResultCapacityExceeded] =
				(CheatEngineFailureKind.InvalidHostResult, CheatEngineHostEffect.Started)
		};

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryNameLookupStatusIsMappedAndAnUnknownStatusFailsClosed()
	{
		MappingTotality.AssertTotal<LuaOperationStatusKind>(
			static status => NameLookupKinds.TryGetValue(status, out CheatEngineFailureKind expected) &&
							 InspectionMapping.ToNameLookupFailureKind(status) == expected,
			static status => InspectionMapping.ToNameLookupFailureKind(status) ==
							 CheatEngineFailureKind.IndeterminateHostResult);
	}

	[Fact]
	[Trait("Qualification", "Q48")]
	public void EveryRegistrationStatusIsMappedAndAnUnknownStatusFailsClosed()
	{
		MappingTotality.AssertTotal<LuaOperationStatusKind>(
			static status => RegistrationFailures.TryGetValue(status,
								 out (CheatEngineFailureKind Kind, CheatEngineHostEffect Effect) expected) &&
							 InspectionMapping.ToRegistrationFailureKind(status) == expected.Kind &&
							 InspectionMapping.ToRegistrationHostEffect(status) == expected.Effect,
			static status =>
				InspectionMapping.ToRegistrationFailureKind(status) == CheatEngineFailureKind.IndeterminateHostResult &&
				InspectionMapping.ToRegistrationHostEffect(status) == CheatEngineHostEffect.Unknown);
	}

	[Theory]
	[InlineData(LuaOperationStatusKind.NilResult, CheatEngineFailureKind.NotFound,
		"Cheat Engine did not return a symbol name for the requested address.")]
	[InlineData(LuaOperationStatusKind.Success, CheatEngineFailureKind.InvalidHostResult,
		"Cheat Engine returned an invalid symbol-name result.")]
	[InlineData(LuaOperationStatusKind.LuaFailure, CheatEngineFailureKind.LuaError,
		"The Cheat Engine symbol-name lookup returned 'LuaFailure'.")]
	public void NameLookupFailuresNameTheCategoryOnly(LuaOperationStatusKind status, CheatEngineFailureKind kind,
		string message)
	{
		CheatEngineFailure failure = InspectionMapping.NameLookupFailure("Inspection.ResolveName", status);

		Assert.Equal(kind, failure.Kind);
		Assert.Equal("Inspection.ResolveName", failure.Operation);
		Assert.Equal(message, failure.Message);
		Assert.Equal(CheatEngineHostEffect.Unknown, failure.HostEffect);
	}

	[Fact]
	public void ARegistrationFailureCarriesTheMappedKindAndEffect()
	{
		CheatEngineFailure failure =
			InspectionMapping.RegistrationFailure("Inspection.RegisterSymbol", LuaOperationStatusKind.LuaFailure);

		Assert.Equal(CheatEngineFailureKind.LuaError, failure.Kind);
		Assert.Equal(CheatEngineHostEffect.Started, failure.HostEffect);
		Assert.Equal("Inspection.RegisterSymbol", failure.Operation);
		Assert.Null(failure.Exception);
		Assert.Equal(
			"The symbol registration through the CheatEngine.SDK ownership coordinator returned 'LuaFailure' without a " +
			"lease.", failure.Message);
	}
}
