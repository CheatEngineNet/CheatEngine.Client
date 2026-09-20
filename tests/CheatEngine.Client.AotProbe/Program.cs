using CheatEngine.Client;
using CheatEngine.Client.Extensions.DependencyInjection;
using CheatEngine.Client.Hosting;
using CheatEngine.Client.Scanning;

using Microsoft.Extensions.DependencyInjection;

ServiceCollection services = new();
services.AddCheatEngineClient().EnableUnsafeLuaExecution();

using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
{
	ValidateOnBuild = true, ValidateScopes = true
});

_ = typeof(ICheatEngineClient);
_ = typeof(CheatEngineClientPlugin);
_ = typeof(AobScanBuilder);
_ = new AobPattern("90");

return services.Count == 0 ? 1 : 0;
