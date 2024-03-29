using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using GdUnit3.Executions;
using GdUnit3.Core;

namespace GdUnit3
{

    class TestReporter : ITestEventListener
    {

        private bool _isFailed = false;

        public TestReporter()
        { }

        public void PublishEvent(TestEvent testEvent)
        {
            //Console.WriteLine(testEvent);
            PrintStatus(testEvent);
        }

        void PrintStatus(TestEvent testEvent)
        {
            Console.ForegroundColor = ConsoleColor.White;
            switch (testEvent.Type)
            {
                case TestEvent.TYPE.TESTSUITE_BEFORE:
                    //_console.prints_color("Run Test Suite %s " % event.resource_path(), Color.antiquewhite)
                    Console.WriteLine($"Run Test Suite {testEvent.ResourcePath}");
                    break;
                case TestEvent.TYPE.TESTCASE_BEFORE:
                    //_console.print_color("	Run Test: %s > %s :" % [event.resource_path(), event.test_name()], Color.antiquewhite).prints_color("STARTED", Color.forestgreen)
                    Console.Write($"	Run Test: {testEvent.SuiteName} > {testEvent.TestName} :");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"STARTED");
                    break;
                case TestEvent.TYPE.TESTCASE_AFTER:
                    //_console.print_color("	Run Test: %s > %s :" % [event.resource_path(), event.test_name()], Color.antiquewhite)
                    // _print_status(event)
                    // _print_failure_report(event.reports())
                    Console.Write($"	Run Test: {testEvent.SuiteName} > {testEvent.TestName} :");
                    WriteStatus(testEvent);
                    break;
                case TestEvent.TYPE.TESTSUITE_AFTER:
                    //_print_status(event)
                    //_console.prints_color("	| %d total | %d error | %d failed | %d skipped | %d orphans |\n" % [_report.test_count(), _report.error_count(), _report.failure_count(), _report.skipped_count(), _report.orphan_count()], Color.antiquewhite)
                    //Console.WriteLine($"	| %d total | %d error | %d failed | %d skipped | %d orphans |");
                    if (_isFailed)
                    {
                        //_parent.GetTree().Quit(1);
                    }
                    break;
            }
            Console.ForegroundColor = ConsoleColor.White;

            void WriteStatus(TestEvent testEvent)
            {
                if (testEvent.IsSkipped)
                {
                    //_console.print_color("SKIPPED", Color.goldenrod, CmdConsole.BOLD | CmdConsole.ITALIC)
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write("SKIPPED");
                }
                else if (testEvent.IsFailed || testEvent.IsError)
                {
                    //_console.print_color("FAILED", Color.crimson, CmdConsole.BOLD)
                    _isFailed = true;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Write("FAILED");
                }
                else if (testEvent.OrphanCount > 0)
                {
                    //_console.print_color("PASSED", Color.goldenrod, CmdConsole.BOLD | CmdConsole.UNDERLINE)
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("PASSED");
                }
                else
                {
                    //_console.print_color("PASSED", Color.forestgreen, CmdConsole.BOLD)
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("PASSED");
                }
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("");
                //Console.Write(" %s" % LocalTime.elapsed(testEvent.elapsed_time()), Color.cornflower)

            }
        }
    }

    class TestRunner : Godot.Node
    {
        public override async void _Ready()
        {
            Console.ForegroundColor = ConsoleColor.White;
            var cmdArgs = Godot.OS.GetCmdlineArgs();
            Console.WriteLine($"This is From Console App {Assembly.GetExecutingAssembly()}");

            var currentDir = Directory.GetCurrentDirectory() + "/test";
            List<TestSuite> testSuites = ScanTestSuites(new DirectoryInfo(currentDir), new List<TestSuite>());
            using Executor executor = new Executor();
            executor.AddTestEventListener(new TestReporter());

            foreach (var testSuite in testSuites)
            {
                await executor.ExecuteInternally(testSuite);
            }
            Console.WriteLine("done");

            GetTree().Quit(0);
        }

        private static List<TestSuite> ScanTestSuites(DirectoryInfo currentDir, List<TestSuite> acc)
        {
            Console.WriteLine($"Scanning for test suites in: {currentDir.FullName}");
            foreach (var file in currentDir.GetFiles("*.cs"))
            {
                Type? type = GdUnitTestSuiteBuilder.ParseType(file.FullName);
                if (type != null && IsTestSuite(type))
                    acc.Add(new TestSuite(file.FullName));
            }
            foreach (var directory in currentDir.GetDirectories())
                ScanTestSuites(directory, acc);
            return acc;
        }

        public static List<TestSuite> GetTestSuites(Assembly assembly) =>
                    assembly.GetTypes()
                        .Where(type => type.IsClass && !type.IsAbstract && IsTestSuite(type))
                        .Select(type => new TestSuite(type)).ToList();

        public static bool IsTestSuite(Type type) =>
                type.IsClass && !type.IsAbstract && Attribute.IsDefined(type, typeof(TestSuiteAttribute));

    }
}
