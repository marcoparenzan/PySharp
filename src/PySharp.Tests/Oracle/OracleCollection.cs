namespace PySharp.Tests.Oracle;
// The golden runner changes process-wide current directory and some graphics libraries use shared state.
[CollectionDefinition("oracle-process-state", DisableParallelization = true)]
public class OracleCollection { }