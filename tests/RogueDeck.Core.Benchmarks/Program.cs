using BenchmarkDotNet.Running;
using RogueDeck.Core.Benchmarks;

BenchmarkSwitcher.FromAssembly(typeof(CombatEngineBenchmarks).Assembly).Run(args);
