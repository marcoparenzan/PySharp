// Copyright (c) 2026 Marco Parenzan
//
// Licensed under the MIT License. See the LICENSE file in the project
// root for full license information.

using PySharpLib;

namespace PySharp.Tests.ClrFixtures
{
    /// <summary>Top-level public types in their own namespace, imported by the clr tests below.</summary>
    public sealed class Greeter
    {
        public string Hello(string who) => $"hello {who}";
        public static int Double(int x) => x * 2;
        public static int Apply(Func<int, int> f, int x) => f(x);
        public static void Fail(string kind) => throw (kind switch
        {
            "argument" => new ArgumentException("bad argument"),
            "canceled" => new OperationCanceledException("window closed"),
            _ => new InvalidOperationException("broken state"),
        });
    }

    public sealed class Grid
    {
        private readonly int[,] cells = new int[3, 2];
        public int this[int x, int y] { get => cells[x, y]; set => cells[x, y] = value; }
    }
}

namespace PySharp.Tests.M11_Interop
{
    /// <summary>
    /// The clr module (pythonnet style): AddReference loads an assembly, its namespaces become importable modules;
    /// the core library (System...) is importable without it. Plus the interop that packages built on it rely on:
    /// .NET exceptions as Python exceptions, method groups as delegates, multi-argument indexers, .NET arrays.
    /// </summary>
    public class ClrModuleTests
    {
        private static string Run(string source)
        {
            var writer = new StringWriter();
            new PyEngine(writer).Run(source);
            return writer.ToString().TrimEnd('\n');
        }

        private static string FixturePath => typeof(ClrFixtures.Greeter).Assembly.Location.Replace("\\", "/");

        [Fact]
        public void Core_library_namespaces_are_importable_without_clr()
            => Assert.Equal("1.5 3", Run("from System import TimeSpan, Math\nprint(TimeSpan.FromSeconds(90).TotalMinutes, Math.Max(2, 3))"));

        [Fact]
        public void AddReference_by_path_makes_namespaces_importable()
            => Assert.Equal("hello clr 42", Run($"""
                import clr
                clr.AddReference("{FixturePath}")
                from PySharp.Tests.ClrFixtures import Greeter
                import PySharp.Tests.ClrFixtures as fixtures
                print(Greeter().Hello("clr"), fixtures.Greeter.Double(21))
                """));

        [Fact]
        public void AddReference_by_assembly_name()
            => Assert.Equal("2", Run("""
                import clr
                clr.AddReference("System.Collections.Specialized")
                from System.Collections.Specialized import StringCollection
                s = StringCollection()
                s.Add("a")
                s.Add("b")
                print(s.Count)
                """));

        [Fact]
        public void Missing_assembly_is_a_file_not_found_error()
            => Assert.Equal("FileNotFoundError", Run("""
                import clr
                try:
                    clr.AddReference("Z:/nowhere/Missing.dll")
                except FileNotFoundError:
                    print("FileNotFoundError")
                """));

        [Fact]
        public void Dotnet_exceptions_become_catchable_python_exceptions()
            => Assert.Equal("ValueError ArgumentException: bad argument\nRuntimeError broken state\nKeyboardInterrupt", Run($"""
                import clr
                clr.AddReference("{FixturePath}")
                from PySharp.Tests.ClrFixtures import Greeter
                try:
                    Greeter.Fail("argument")
                except ValueError as e:
                    print("ValueError", e)
                try:
                    Greeter.Fail("state")
                except RuntimeError as e:
                    print("RuntimeError", e.clr_exception.Message)
                try:
                    try:
                        Greeter.Fail("canceled")
                    except Exception:
                        print("cancellation must not be swallowed by except Exception")
                except KeyboardInterrupt:
                    print("KeyboardInterrupt")
                """));

        [Fact]
        public void Static_method_group_passes_as_delegate()
            => Assert.Equal("14", Run($"""
                import clr
                clr.AddReference("{FixturePath}")
                from PySharp.Tests.ClrFixtures import Greeter
                print(Greeter.Apply(Greeter.Double, 7))
                """));

        [Fact]
        public void Tuple_index_binds_to_multi_argument_indexer()
            => Assert.Equal("9 0", Run($"""
                import clr
                clr.AddReference("{FixturePath}")
                from PySharp.Tests.ClrFixtures import Grid
                g = Grid()
                g[2, 1] = 9
                print(g[2, 1], g[0, 0])
                """));

        [Fact]
        public void Dotnet_arrays_support_len_index_and_negative_index()
            => Assert.Equal("3 5 7 IndexError", Run("""
                import clr
                from System import Array, Int32
                a = Array.CreateInstance(clr.GetClrType(Int32), 3)
                a[0] = 5
                a[-1] = 7
                try:
                    a[3]
                except IndexError:
                    print(len(a), a[0], a[2], "IndexError")
                """));
    }
}
