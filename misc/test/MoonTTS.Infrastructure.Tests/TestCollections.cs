namespace MoonTTS.Infrastructure.Tests;

/* Tests that change process-wide state (environment variables, PATH, current directory) join this collection,
   which xUnit runs on its own after the parallel tests */
[CollectionDefinition(TestCollections.Environment, DisableParallelization = true)]
public sealed class EnvironmentCollection;
