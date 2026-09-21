using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Running;

using CheatEngine.Client.Benchmarks;

string artifactsDirectory = Path.Combine(Environment.CurrentDirectory, "BenchmarkDotNet.Artifacts");
Directory.CreateDirectory(artifactsDirectory);
BenchmarkSuiteMetadata.WriteTo(artifactsDirectory);

IConfig config = DefaultConfig.Instance.AddExporter(JsonExporter.Full);
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
