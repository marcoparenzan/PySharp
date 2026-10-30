using System.Reflection;
namespace PySharp.Tests;
internal static class TestRepository
{
    public static string Root => typeof(TestRepository).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a=>a.Key=="RepositoryRoot").Value!;
}