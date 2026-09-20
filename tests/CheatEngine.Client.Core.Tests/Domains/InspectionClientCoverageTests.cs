using CheatEngine.Client.Core.Dispatching;
using CheatEngine.Client.Core.Domains;
using CheatEngine.Client.Core.Tests.TestSupport;

namespace CheatEngine.Client.Core.Tests.Domains;

public sealed class InspectionClientCoverageTests
{
	[Fact]
	public void ConstructorRejectsANullDispatcher()
	{
		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
			new InspectionClient(null!, InertCoreLifetime.Create()));

		Assert.Equal("dispatcher", exception.ParamName);
	}

	[Fact]
	public void ConstructorRejectsANullLifetime()
	{
		SdkMainThreadDispatcher dispatcher = new(InertCoreLifetime.Create());

		ArgumentNullException exception = Assert.Throws<ArgumentNullException>(() =>
			new InspectionClient(dispatcher, null!));

		Assert.Equal("lifetime", exception.ParamName);
	}
}
