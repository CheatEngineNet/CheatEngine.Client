using CheatEngine.Client.Inspection;
using CheatEngine.SDK.Engine.Values;

namespace CheatEngine.Client.Abstractions.Tests.Inspection;

public sealed class InspectionContractsTests
{
	[Theory]
	[InlineData("")]
	[InlineData("   ")]
	public void SymbolRegistrationRejectsBlankNames(string name)
	{
		Assert.Throws<ArgumentException>(() => new SymbolRegistration(name, Address.Zero));
	}

	[Fact]
	public void SymbolRegistrationPreservesExplicitLeaseDefinition()
	{
		SymbolRegistration registration = new("sample.Health", Address.FromUInt64(0x1234), false);

		Assert.Equal("sample.Health", registration.Name);
		Assert.Equal(Address.FromUInt64(0x1234), registration.Address);
		Assert.False(registration.DoNotSave);
	}
}
