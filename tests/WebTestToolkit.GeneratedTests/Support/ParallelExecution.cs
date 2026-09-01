using NUnit.Framework;

// Feature-level parallelism, not scenario-level, for two reasons that both matter here:
//
//   * The toolkit generates one feature per recorded flow, and flows are independent by
//     construction — so features are exactly the boundary the work is already split along.
//     Scenario-level would add contention without adding much parallelism.
//   * Reqnroll documents [BeforeFeature]/[AfterFeature] as unreliable under scenario-level
//     parallelism. Nothing here uses them today, but feature-level keeps that door open
//     rather than quietly making a future hook flaky.
//
// Every scenario still gets its own DriverContext (Reqnroll scenario-scoped injection), so
// each parallel feature drives its own Chrome. That is also why the degree is capped well
// below Environment.ProcessorCount: the limit is how many real browsers a machine can run,
// not how many cores it has.
//
// Thread-safety this depends on: no ScenarioContext.Current anywhere (Reqnroll throws on the
// static contexts under parallel execution — this project has always used constructor
// injection), and LocatorRepository's cache being concurrent.
[assembly: Parallelizable(ParallelScope.Fixtures)]
[assembly: LevelOfParallelism(4)]
