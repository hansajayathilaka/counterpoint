using System.Runtime.CompilerServices;

// P1-T16: the performance regression guard (tests/Counterpoint.Integration.Tests/Performance)
// seeds the same 20,000-SKU/100,000-line dataset in-process, through the same
// PerformanceDatasetSeeder this project's own --skus/--lines/--output mode calls, and AC-15's
// acceptance test verifies a killed process's database through the same HashChainVerifier this
// project's own --verify mode calls. One seeder, one verifier, reused rather than duplicated.
[assembly: InternalsVisibleTo("Counterpoint.Integration.Tests")]
