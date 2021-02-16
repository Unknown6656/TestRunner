#define DEBUG__RETHROW_ON_FAILED_TEST

#nullable enable

using Microsoft.VisualStudio.TestTools.UnitTesting;

using System.Runtime.ExceptionServices;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Linq;
using System.IO;
using System;

using Unknown6656.Testing;


return UnitTestRunner.RunTests(args.Select(arg => Assembly.LoadFrom(arg)));


namespace Unknown6656.Testing
{
    using static Console;


    public abstract class UnitTestRunner
    {
        public virtual void Test_StaticInit()
        {
        }

        public virtual void Test_StaticCleanup()
        {
        }

        [TestInitialize]
        public virtual void Test_Init()
        {
        }

        [TestCleanup]
        public virtual void Test_Cleanup()
        {
        }

        public static void Skip() => throw new SkippedException();

        private static void AddTime(ref long target, Stopwatch sw)
        {
            sw.Stop();
            target += sw.ElapsedTicks;
            sw.Restart();
        }

        private static void Print(string text, ConsoleColor color)
        {
            ForegroundColor = color;
            Write(text);
        }

        private static void PrintLine(string text, ConsoleColor color) => Print(text + '\n', color);

        private static void PrintHeader(string text, int width)
        {
            int line_width = width - text.Length - 2;
            string line = new('=', line_width / 2);

            WriteLine($"{line} {text} {line}{(line_width % 2 == 0 ? "" : "=")}");
        }

        private static void PrintColorDescription(ConsoleColor col, string description)
        {
            Print("       ### ", col);
            PrintLine(description, ConsoleColor.White);
        }

        private static void PrintGraph(int padding, int width, string description, params (double value, ConsoleColor color)[] values)
        {
            double sum = values.Sum(t => t.value);

            width -= 2;
            values = values.Select(v => (v.value / sum * width is double d && double.IsFinite(d) ? d : 0, v.color)).ToArray();

            double max = values.Append(default).Max(t => t.value);
            int rem = width - values.Append(default).Sum(t => (int)t.value);
            (double, ConsoleColor) elem = values.First(t => t.value == max);
            int ndx = Array.IndexOf(values, elem);

            values[ndx] = (elem.Item1 + rem, elem.Item2);

            Print($"{new string(' ', padding)}[", ConsoleColor.White);

            foreach ((double, ConsoleColor) v in values)
                Print(new string('#', (int)v.Item1), v.Item2);

            PrintLine($"] {description ?? ""}", ConsoleColor.White);
        }

        public static int RunTests(IEnumerable<Assembly> assemblies)
        {
            const int WIDTH = 110;

            #region REFLECTION + INVOCATION

            ForegroundColor = ConsoleColor.White;
            OutputEncoding = Encoding.Default;

            List<(string Name, int Passed, int Failed, int Skipped, long TimeCtor, long TimeInit, long TimeMethod)> partial_results = new();
            int passed = 0, failed = 0, skipped = 0;

            Stopwatch sw = new Stopwatch();
            long sw_sinit, sw_init, sw_method;

            Type[] types = (from asm in assemblies
                            from type in asm.GetTypes()
                            let attr = type.GetCustomAttributes<TestClassAttribute>(true).FirstOrDefault()
                            where attr is { }
                            orderby type.Name ascending
                            orderby type.GetCustomAttributes<PriorityAttribute>(true).FirstOrDefault()?.Priority ?? 0 descending
                            select type).ToArray();

            PrintHeader("UNIT TESTS", WIDTH);
            WriteLine($@"
Testing {types.Length} type(s):
{string.Concat(types.Select(t => $"    [{new FileInfo(t.Assembly.Location).Name}] {t.FullName}\n"))}");

            foreach (Type t in types)
            {
                sw.Restart();
                sw_sinit = sw_init = sw_method = 0;

                bool skipclass = t.GetCustomAttributes<SkipAttribute>(true).FirstOrDefault() != null;
                dynamic? container = skipclass ? null : Activator.CreateInstance(t);
                MethodInfo? sinit = t.GetMethod(nameof(Test_StaticInit));
                MethodInfo? scleanup = t.GetMethod(nameof(Test_StaticInit));
                MethodInfo? init = t.GetMethod(nameof(Test_Init));
                MethodInfo? cleanup = t.GetMethod(nameof(Test_Cleanup));
                int tpassed = 0, tfailed = 0, tskipped = 0, pleft, ptop, rptop;

                WriteLine($"    Testing class '{t.FullName}'");

                sinit?.Invoke(container, Array.Empty<object>());

                AddTime(ref sw_sinit, sw);

                IEnumerable<(MethodInfo method, object[] args)> get_methods()
                {
                    foreach (MethodInfo nfo in t.GetMethods().OrderBy(m => m.Name))
                        if (nfo.GetCustomAttributes<TestMethodAttribute>()?.FirstOrDefault() is { })
                        {
                            TestWithAttribute[] attr = nfo.GetCustomAttributes<TestWithAttribute>()?.ToArray() ?? Array.Empty<TestWithAttribute>();

                            if (attr.Length > 0)
                                foreach (TestWithAttribute tw in attr)
                                    if (nfo.ContainsGenericParameters)
                                    {
                                        ParameterInfo[] pars = nfo.GetParameters();
                                        List<Type> types = new List<Type>();

                                        for (int i = 0; i < pars.Length; ++i)
                                            if (pars[i].ParameterType.IsGenericParameter)
                                                types.Add(tw.Arguments[i].GetType());

                                        MethodInfo concrete = nfo.MakeGenericMethod(types.ToArray());

                                        yield return (concrete, tw.Arguments);
                                    }
                                    else
                                        yield return (nfo, tw.Arguments);
                            else
                                yield return (nfo, Array.Empty<object>());
                        }
                }

                foreach ((MethodInfo nfo, object[] args) in get_methods())
                {
                    Write("        [");
                    ptop = CursorTop;
                    pleft = CursorLeft;
                    Write($"    ] Testing '{nfo.Name}({string.Join(", ", nfo.GetParameters().Select(p => p.ParameterType.FullName))})' with ({string.Join(", ", args)})");
                    rptop = CursorTop;

                    void WriteResult(ConsoleColor clr, string text)
                    {
                        int ttop = CursorTop;

                        ForegroundColor = clr;
                        CursorLeft = pleft;
                        CursorTop = ptop;

                        WriteLine(text);

                        ForegroundColor = ConsoleColor.White;
                        CursorTop = rptop + 1;
                    }

                    try
                    {
                        if ((nfo.GetCustomAttributes<SkipAttribute>().FirstOrDefault() != null) || skipclass)
                            Skip();

                        init?.Invoke(container, Array.Empty<object>());

                        AddTime(ref sw_init, sw);

                        nfo.Invoke(container, args);

                        AddTime(ref sw_method, sw);

                        cleanup?.Invoke(container, Array.Empty<object>());

                        AddTime(ref sw_init, sw);

                        WriteResult(ConsoleColor.Green, "PASS");

                        ++passed;
                        ++tpassed;
                    }
                    catch (Exception ex)
                    when ((ex is SkippedException) || (ex?.InnerException is SkippedException))
                    {
                        WriteResult(ConsoleColor.Yellow, "SKIP");

                        ++skipped;
                        ++tskipped;
                    }
                    catch (Exception ex)
                    {
#if DEBUG__RETHROW_ON_FAILED_TEST
                        if (ex is TargetInvocationException { InnerException: { } inner })
                        {
                            ExceptionDispatchInfo.Capture(inner).Throw();

                            throw;
                        }
#endif
                        WriteResult(ConsoleColor.Red, "FAIL");

                        ++failed;
                        ++tfailed;

                        ForegroundColor = ConsoleColor.Red;

                        while (ex?.InnerException is { })
                        {
                            ex = ex.InnerException;

                            WriteLine($"                  [{ex.GetType()}] {ex.Message}\n{string.Join("\n", ex.StackTrace?.Split('\n').Select(x => $"                {x}") ?? Array.Empty<string>())}");
                        }

                        ForegroundColor = ConsoleColor.White;
                    }

                    AddTime(ref sw_method, sw);
                }

                scleanup?.Invoke(container, Array.Empty<object>());

                AddTime(ref sw_sinit, sw);

                partial_results.Add((t.FullName!, tpassed, tskipped, tfailed, sw_sinit, sw_init, sw_method));
            }

            #endregion
            #region PRINT RESULTS

            int total = passed + failed + skipped;
            double time = partial_results.Select(r => r.TimeCtor + r.TimeInit + r.TimeMethod).Sum();
            double pr = total == 0 ? 0 : passed / (double)total;
            double sr = total == 0 ? 0 : skipped / (double)total;
            double tr;
            const int i_wdh = WIDTH - 35;

            WriteLine();
            PrintHeader("TEST RESULTS", WIDTH);

            PrintGraph(0, WIDTH, "", (pr, ConsoleColor.Green), (sr, ConsoleColor.Yellow), (total == 0 ? 0 : 1 - pr - sr, ConsoleColor.Red));
            Print($@"
    MODULES: {partial_results.Count,3}
    TOTAL:   {passed + failed + skipped,3}
    PASSED:  {passed,3} ({pr * 100,7:F3} %)
    SKIPPED: {skipped,3} ({sr * 100,7:F3} %)
    FAILED:  {failed,3} ({(total == 0 ? 0 : 1 - pr - sr) * 100,7:F3} %)
    TIME:    {time * 1000d / Stopwatch.Frequency,9:F3} ms
    DETAILS:", ConsoleColor.White);

            foreach (var res in partial_results)
            {
                double mtime = res.TimeCtor + res.TimeInit + res.TimeMethod;
                double tot = res.Passed + res.Failed + res.Skipped;

                pr = tot == 0 ? 0 : res.Passed / tot;
                sr = tot == 0 ? 0 : res.Failed / tot;
                tr = time == 0 ? 0 : mtime / time;

                double tdt_ct = mtime < double.Epsilon ? 0 : res.TimeCtor / mtime;
                double tdt_in = mtime < double.Epsilon ? 0 : res.TimeInit / mtime;
                double tdt_tt = mtime < double.Epsilon ? 0 : res.TimeMethod / mtime;

                WriteLine($@"
        MODULE:  {res.Name}
        PASSED:  {res.Passed,3} ({pr * 100,7:F3} %)
        SKIPPED: {res.Failed,3} ({sr * 100,7:F3} %)
        FAILED:  {res.Skipped,3} ({(tot == 0 ? 0 : 1 - pr - sr) * 100,7:F3} %)
        TIME:    {mtime * 1000d / Stopwatch.Frequency,9:F3} ms ({tr * 100d,7:F3} %)
            CONSTRUCTORS AND DESTRUCTORS: {res.TimeCtor * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_ct * 100d,7:F3} %)
            INITIALIZATION AND CLEANUP:   {res.TimeInit * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_in * 100d,7:F3} %)
            METHOD TEST RUNS:             {res.TimeMethod * 1000d / Stopwatch.Frequency,9:F3} ms ({tdt_tt * 100d,7:F3} %)");
                PrintGraph(8, i_wdh, "TIME/TOTAL", (tr, ConsoleColor.Magenta),
                                                   (1 - tr, ConsoleColor.Black));
                PrintGraph(8, i_wdh, "TIME DISTR", (tdt_ct, ConsoleColor.DarkBlue),
                                                   (tdt_in, ConsoleColor.Blue),
                                                   (tdt_tt, ConsoleColor.Cyan));
                PrintGraph(8, i_wdh, "PASS/SKIP/FAIL", (res.Passed, ConsoleColor.Green),
                                                       (res.Failed, ConsoleColor.Yellow),
                                                       (res.Skipped, ConsoleColor.Red));
            }

            WriteLine();

            if (partial_results.Count > 0)
            {
                WriteLine("    GRAPH COLORS:");
                PrintColorDescription(ConsoleColor.Green, "Passed test methods");
                PrintColorDescription(ConsoleColor.Yellow, "Skipped test methods");
                PrintColorDescription(ConsoleColor.Red, "Failed test methods");
                PrintColorDescription(ConsoleColor.Magenta, "Time used for testing (relative to the total time)");
                PrintColorDescription(ConsoleColor.DarkBlue, "Time used for the module's static and instance constructors/destructors (.cctor, .ctor and .dtor)");
                PrintColorDescription(ConsoleColor.Blue, "Time used for the test initialization and cleanup method (@before and @after)");
                PrintColorDescription(ConsoleColor.Cyan, "Time used for the test method (@test)");
            }

            WriteLine();
            //PrintHeader("DETAILED TEST METHOD RESULTS", wdh);
            //WriteLine();
            WriteLine(new string('=', WIDTH));

            //if (Debugger.IsAttached)
            //{
            //    WriteLine("\nPress any key to exit ....");
            //    ReadKey(true);
            //}

            return failed; // NO FAILED TEST --> EXITCODE = 0

            #endregion
        }
    }

    public static class AssertExtensions
    {
        public static void AreSequentialEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) => Assert.IsTrue(expected.SequenceEqual(actual));

        public static void AreSetEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual)
        {
            T[] a1 = expected.ToArray();
            T[] a2 = actual.ToArray();

            Assert.AreEqual(a1.Length, a2.Length);

            AreSequentialEqual(a1.Except(a2), Array.Empty<T>());
            AreSequentialEqual(a2.Except(a1), Array.Empty<T>());
        }
    }

    public sealed class SkippedException
        : Exception
    {
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class TestWithAttribute
        : Attribute
    {
        public object[] Arguments { get; }


        public TestWithAttribute(params object[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params bool[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params byte[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params sbyte[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params char[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params short[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params ushort[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params int[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params uint[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params long[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params ulong[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params float[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params double[] args) => Arguments = args.Select(t => (object)t).ToArray();

        public TestWithAttribute(params Type[] args) => Arguments = args;

        public TestWithAttribute(params Enum[] args) => Arguments = args;
    }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public sealed class SkipAttribute
        : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed class TestingPriorityAttribute
            : Attribute
    {
        public uint Priority { get; }


        public TestingPriorityAttribute(uint p = 0) => Priority = p;
    }
}
