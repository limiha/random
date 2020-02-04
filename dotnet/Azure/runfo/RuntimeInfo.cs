using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevOps.Util;
using DevOps.Util.DotNet;
using Mono.Options;
using static RuntimeInfoUtil;

internal sealed class RuntimeInfo
{
    internal static readonly (string BuildName, int DefinitionId)[] BuildDefinitions = new[] 
        {
            ("runtime", 686),
            ("coreclr", 655),
            ("libraries", 675),
            ("libraries windows", 676),
            ("libraries linux", 677),
            ("libraries osx", 678),
            ("crossgen2", 701)
        };

    internal DevOpsServer Server;

    internal RuntimeInfo(string personalAccessToken = null)
    {
        Server = new DevOpsServer("dnceng", personalAccessToken);
    }

    internal async Task PrintBuildResults(IEnumerable<string> args)
    {
        int count = 5;
        var optionSet = new OptionSet()
        {
            { "c|count=", "count of builds to return", (int c) => count = c }
        };

        ParseAll(optionSet, args); 

        var data = BuildDefinitions
            .AsParallel()
            .AsOrdered()
            .Select(async t => (t.BuildName, t.DefinitionId, await GetBuildResultsAsync("public", t.DefinitionId, count)));

        foreach (var task in data)
        {
            var (name, definitionId, builds) = await task;
            Console.Write($"{name,-20}");
            var percent = (builds.Count(x => x.Result == BuildResult.Succeeded) / (double)count) * 100;
            Console.Write($"{percent,4:G3}%  ");
            foreach (var build in builds)
            {
                var c = build.Result == BuildResult.Succeeded ? 'Y' : 'N';
                Console.Write(c);
            }

            Console.WriteLine();
        }
    }

    internal async Task<int> PrintHelix(IEnumerable<string> args)
    {
        int? buildId = null;;
        var verbose = false;
        var optionSet = new OptionSet()
        {
            { "b|build=", "build to print out", (int b) => buildId = b },
            { "v|verbose", "verbose output", v => verbose = v is object },
        };

        ParseAll(optionSet, args); 
        if (buildId is null)
        {
            Console.WriteLine("Build id (-b) is required");
            optionSet.WriteOptionDescriptions(Console.Out);
            return ExitFailure;
        }

        var buildResultInfo = await GetBuildTestInfoAsync(buildId.Value);
        var logs = buildResultInfo
            .DataList
            .AsParallel()
            .Select(async t => await HelixUtil.GetHelixLogInfoAsync(Server, "public", t.TestRun.Id, t.HelixTestResult.WorkItem.Id))
            .Select(async (Task<HelixLogInfo> task) => {
                var helixLogInfo = await task;
                string consoleText = null;
                if (verbose && helixLogInfo.ConsoleUri is object)
                {
                    consoleText = await HelixUtil.GetHelixConsoleText(Server, helixLogInfo.ConsoleUri);
                }
                return (helixLogInfo, consoleText);
            });

        var list = await RuntimeInfoUtil.ToList(logs);

        Console.WriteLine("Console Logs");
        foreach (var (helixLogInfo, consoleText) in list.Where(x => x.Item1.ConsoleUri is object))
        {
            Console.WriteLine($"{helixLogInfo.ConsoleUri}");
            if (verbose)
            {
                Console.WriteLine(consoleText);
            }
        }

        Console.WriteLine();
        var wroteHeader = false;
        foreach (var (helixLogInfo, _) in list.Where(x => x.helixLogInfo.TestResultsUri is object))
        {
            if (!wroteHeader)
            {
                Console.WriteLine("Test Results");
                wroteHeader = true;
            }
            Console.WriteLine($"{helixLogInfo.TestResultsUri}");
        }

        Console.WriteLine();
        wroteHeader = false;
        foreach (var (helixLogInfo, _) in list.Where(x => x.helixLogInfo.CoreDumpUri is object))
        {
            if (!wroteHeader)
            {
                Console.WriteLine("Core Logs");
                wroteHeader = true;
            }
            Console.WriteLine($"{helixLogInfo.CoreDumpUri}");
        }
        return ExitSuccess;
    }

    internal void PrintBuildDefinitions()
    {
        foreach (var (name, definitionId) in BuildDefinitions)
        {
            var uri = DevOpsUtil.GetBuildDefinitionUri(Server.Organization, "public", definitionId);
            Console.WriteLine($"{name,-20}{uri}");
        }
    }

    internal async Task<int> PrintBuilds(IEnumerable<string> args)
    {
        string definition = null;
        int count = 5;
        var optionSet = new OptionSet()
        {
            { "d|definition=", "definition to print tests for", d => definition = d },
            { "c|count=", "count of builds to return", (int c) => count = c }
        };

        ParseAll(optionSet, args);

        if (!TryGetDefinitionId(definition, out int definitionId))
        {
            OptionFailureDefinition(definition, optionSet);
            return ExitFailure;
        }

        foreach (var build in await GetBuildResultsAsync("public", definitionId, count))
        {
            var uri = DevOpsUtil.GetBuildUri(build);
            Console.WriteLine($"{build.Id}\t{build.Result}\t{uri}");
        }

        return ExitSuccess;
    }

    internal async Task<int> PrintFailedTests(IEnumerable<string> args)
    {
        int? buildId = null;
        int count = 5;
        bool verbose = false;
        bool markdown = false;
        string definition = null;
        string grouping = "builds";
        var optionSet = new OptionSet()
        {
            { "b|build=", "build id to print tests for", (int b) => buildId = b },
            { "d|definition=", "build definition name / id", d => definition = d },
            { "c|count=", "count of builds to show for a definition", (int c) => count = c},
            { "g|grouping=", "output grouping: builds*, tests, jobs", g => grouping = g },
            { "m|markdown", "output in markdown", m => markdown = m  is object},
            { "v|verbose", "verobes output", d => verbose = d is object }
        };

        ParseAll(optionSet, args);

        if (buildId is object && definition is object)
        {
            OptionFailure("Cannot specified build and definition", optionSet);
            return ExitFailure;
        }

        if (buildId is null && definition is null)
        {
            OptionFailure("Need either a build or definition", optionSet);
            return ExitFailure;
        }

        if (definition is object)
        {
            if (!TryGetDefinitionId(definition, out int definitionId))
            {
                OptionFailureDefinition(definition, optionSet);
                return ExitFailure;
            }

            await PrintFailedTestsForDefinition("public", definitionId, count, grouping, verbose, markdown);
            return ExitSuccess;
        }

        Debug.Assert(buildId is object);
        PrintFailedTests(await GetBuildTestInfoAsync(buildId.Value));
        return ExitSuccess;
    }

    private async Task PrintFailedTestsForDefinition(string project, int definitionId, int count, string grouping, bool verbose, bool markdown)
    {
        switch (grouping)
        {
            case "tests":
                await (markdown ? GroupByTestsMarkdown() : GroupByTestsConsole());
                break;
            case "builds":
                await GroupByBuilds();
                break;
            case "jobs":
                await GroupByJobs();
                break;
            default:
                throw new Exception($"{grouping} is not a valid grouping");
        }

        async Task GroupByBuilds()
        {
            var buildTestInfoList = await ListBuildTestInfosAsync(project, definitionId, count);
            foreach (var buildTestInfo in buildTestInfoList)
            {
                PrintFailedTests(buildTestInfo);
            }
        }

        async Task GroupByTestsConsole()
        {
            var buildTestInfoList = await ListBuildTestInfosAsync(project, definitionId, count);
            foreach (var testCaseTitle in buildTestInfoList.GetTestCaseTitles())
            {
                var testRunList = buildTestInfoList.GetHelixTestRunResultsForTestCaseTitle(testCaseTitle);
                Console.WriteLine($"{testCaseTitle} {testRunList.Count}");
                if (verbose)
                {
                    Console.WriteLine($"{GetIndent(1)}Builds");
                    foreach (var build in buildTestInfoList.GetBuildsForTestCaseTitle(testCaseTitle))
                    {
                        var uri = DevOpsUtil.GetBuildUri(build);
                        Console.WriteLine($"{GetIndent(2)}{uri}");
                    }

                    Console.WriteLine($"{GetIndent(1)}Test Runs");
                    foreach (var helixTestRunResult in testRunList)
                    {
                        var testRun = helixTestRunResult.TestRun;
                        var count = testRunList.Count(t => t.TestRun.Name == testRun.Name);
                        Console.WriteLine($"{GetIndent(2)}{count}\t{testRun.Name}");
                    }
                }
            }
        }

        async Task GroupByTestsMarkdown()
        {
            var buildTestInfoList = await ListBuildTestInfosAsync(project, definitionId, count);
            foreach (var testCaseTitle in buildTestInfoList.GetTestCaseTitles())
            {
                var testRunList = buildTestInfoList.GetHelixTestRunResultsForTestCaseTitle(testCaseTitle);
                Console.WriteLine($"## {testCaseTitle}");
                Console.WriteLine("");
                Console.WriteLine("### Console Log Summary");
                Console.WriteLine("");
                Console.WriteLine("### Builds");
                Console.WriteLine("|Build|Test Failure Count|");
                Console.WriteLine("| --- | --- |");
                foreach (var buildTestInfo in buildTestInfoList.GetBuildTestInfosForTestCaseTitle(testCaseTitle))
                {
                    var build = buildTestInfo.Build;
                    var uri = DevOpsUtil.GetBuildUri(build);
                    var testFailureCount = buildTestInfo.GetHelixTestRunResultsForTestCaseTitle(testCaseTitle).Count();
                    Console.WriteLine($"|[#{build.Id}]({uri})|{testFailureCount}|");
                }

                Console.WriteLine($"### Configurations");
                foreach (var testRunName in buildTestInfoList.GetTestRunNamesForTestCaseTitle(testCaseTitle))
                {
                    Console.WriteLine($"- {EscapeAtSign(testRunName)}");
                }

                Console.WriteLine($"### Helix Logs");
                Console.WriteLine("|Build|Console|Core|Test Results|");
                Console.WriteLine("| --- | --- | --- | --- |");
                foreach (var (build, helixLogInfo) in await GetHelixLogs(buildTestInfoList, testCaseTitle))
                {
                    var uri = DevOpsUtil.GetBuildUri(build);
                    Console.Write($"|[#{build.Id}]({uri})");
                    PrintUri(helixLogInfo.ConsoleUri, "console");
                    PrintUri(helixLogInfo.CoreDumpUri, "core");
                    PrintUri(helixLogInfo.TestResultsUri, "testResults.xml");
                    Console.WriteLine("|");
                }

                static void PrintUri(string uri, string defaultDisplayName)
                {
                    if (uri is null)
                    {
                        Console.Write("|");
                        return;
                    }
                    
                    try
                    {
                        if (Uri.TryCreate(uri, UriKind.Absolute, out var realUri))
                        {
                            var name = Path.GetFileName(realUri.LocalPath);
                            Console.Write($"|[{name}]({uri})");
                            return;
                        }
                    }
                    catch
                    {
                        // Badly formatted URI
                    }

                    Console.Write($"|[{defaultDisplayName}]({uri})");
                }

                static string EscapeAtSign(string text) => text.Replace("@", "@<!-- -->");

                Console.WriteLine();
            }
        }

        async Task GroupByJobs()
        {
            var buildTestInfoList = await ListBuildTestInfosAsync(project, definitionId, count);
            var testRunNames = buildTestInfoList.GetTestRunNames();
            foreach (var testRunName in testRunNames)
            {
                var list = buildTestInfoList.Where(x => x.ContainsTestRunName(testRunName));
                Console.WriteLine($"{testRunName}");
                if (verbose)
                {
                    Console.WriteLine($"{GetIndent(1)}Builds");
                    foreach (var build in list)
                    {
                        var uri = DevOpsUtil.GetBuildUri(build.Build);
                        Console.WriteLine($"{GetIndent(2)}{uri}");
                    }

                    Console.WriteLine($"{GetIndent(1)}Test Cases");
                    var testCaseTitles = list
                        .SelectMany(x => x.GetHelixTestRunResultsForTestRunName(testRunName))
                        .Select(x => x.HelixTestResult.TestCaseTitle)
                        .Distinct()
                        .OrderBy(x => x);
                    foreach (var testCaseTitle in testCaseTitles)
                    {
                        var count = list
                            .SelectMany(x => x.GetHelixTestRunResultsForTestCaseTitle(testCaseTitle))
                            .Count(x => x.TestRun.Name == testRunName);
                        Console.WriteLine($"{GetIndent(2)}{testCaseTitle} ({count})");
                    }
                }
                else
                {
                    var buildCount = list.Count();
                    var testCaseCount = list.Sum(x => x.GetHelixTestRunResultsForTestRunName(testRunName).Count());
                    Console.WriteLine($"{GetIndent(1)}Builds {buildCount}");
                    Console.WriteLine($"{GetIndent(1)}Test Cases {testCaseCount}");
                }
            }
        }
    }

    private static void PrintFailedTests(BuildTestInfo buildTestInfo)
    {
        var build = buildTestInfo.Build;
        Console.WriteLine($"{build.Id} {DevOpsUtil.GetBuildUri(build)}");
        foreach (var testRunName in buildTestInfo.GetTestRunNames())
        {
            Console.WriteLine($"{GetIndent(1)}{testRunName}");
            foreach (var testResult in buildTestInfo.GetHelixTestRunResultsForTestRunName(testRunName))
            {
                var suffix = "";
                var testCaseResult = testResult.HelixTestResult.Test;
                if (testCaseResult.FailingSince.Build.Id != build.Id)
                {
                    suffix = $"(since {testCaseResult.FailingSince.Build.Id})";
                }
                Console.WriteLine($"{GetIndent(2)}{testCaseResult.TestCaseTitle} {suffix}");
            }
        }
    }

    private async Task<List<(Build, HelixLogInfo)>> GetHelixLogs(BuildTestInfoCollection collection, string testCaseTitle)
    {
        var query = collection
            .GetHelixTestRunResultsForTestCaseTitle(testCaseTitle)
            .ToList()
            .AsParallel()
            .AsOrdered()
            .Select(async testRunResult => {
                var helixLogInfo = await GetHelixLogInfoAsync(testRunResult);
                return (testRunResult.Build, helixLogInfo);
            });
        var list = await RuntimeInfoUtil.ToList(query);
        return list;
    }

    private async Task<BuildTestInfoCollection> ListBuildTestInfosAsync(string project, int definitionId, int count)
    {
        var list = new List<BuildTestInfo>();
        foreach (var build in await GetBuildResultsAsync(project, definitionId, count))
        {
            list.Add(await GetBuildTestInfoAsync(build));
        }

        return new BuildTestInfoCollection(new ReadOnlyCollection<BuildTestInfo>(list));
    }

    private async Task<BuildTestInfo> GetBuildTestInfoAsync(int buildId)
    {
        var build = await Server.GetBuildAsync("public", buildId);
        return await GetBuildTestInfoAsync(build);
    }

    private async Task<BuildTestInfo> GetBuildTestInfoAsync(Build build)
    {
        var taskList = new List<Task<(TestRun, List<TestCaseResult>)?>>();
        var testRuns = await Server.ListTestRunsAsync("public", build.Id);
        foreach (var testRun in testRuns)
        {
            var task = GetTestRunResultsAsync(testRun);
            taskList.Add(task);
        }

        await Task.WhenAll(taskList);

        var list = new List<HelixTestRunResult>();
        foreach (var task in taskList)
        {
            var tuple = task.Result;
            if (!tuple.HasValue)
            {
                continue;
            }

            var testCaseResults = tuple.Value.Item2;
            foreach (var testCaseResult in testCaseResults)
            {
                HelixTestResult helixTestResult;
                if (HelixUtil.IsHelixWorkItem(testCaseResult))
                {
                    helixTestResult = new HelixTestResult(testCaseResult);
                }
                else
                {
                    var workItem = testCaseResults.FirstOrDefault(x => HelixUtil.IsHelixWorkItemAndTestCaseResult(workItem: x, test: testCaseResult));
                    helixTestResult = new HelixTestResult(test: testCaseResult, workItem: workItem);
                }

                list.Add(new HelixTestRunResult(build, tuple.Value.Item1, helixTestResult));
            }
        }

        return new BuildTestInfo(build, list);

        async Task<(TestRun, List<TestCaseResult>)?> GetTestRunResultsAsync(TestRun testRun)
        {
            var all = await Server.ListTestResultsAsync("public", testRun.Id, outcomes: new[] { TestOutcome.Failed });
            if (all.Length == 0)
            {
                return null;
            }

            return (testRun, all.ToList());
        }
    }

    private bool TryGetDefinitionId(string definition, out int definitionId)
    {
        definitionId = 0;
        if (definition is null)
        {
            return false;
        }

        if (int.TryParse(definition, out definitionId))
        {
            return true;
        }

        foreach (var (name, id) in BuildDefinitions)
        {
            if (name == definition)
            {
                definitionId = id;
                return true;
            }
        }

        return false;
    }

    private async Task<List<Build>> GetBuildResultsAsync(string project, int definitionId, int count)
    {
        var builds = await Server.ListBuildsAsync(
            project,
            new[] { definitionId },
            statusFilter: BuildStatus.Completed,
            queryOrder: BuildQueryOrder.FinishTimeDescending,
            top: count * 20);
        return builds
            .Where(x => x.Reason != BuildReason.PullRequest)
            .Take(count)
            .ToList();
    }

    private static void OptionFailure(string message, OptionSet optionSet)
    {
        Console.WriteLine(message);
        optionSet.WriteOptionDescriptions(Console.Out);
    }

    private static void OptionFailureDefinition(string definition, OptionSet optionSet)
    {
        Console.WriteLine($"{definition} is not a valid definition name or id");
        Console.WriteLine("Supported definition names");
        foreach (var (name, id) in BuildDefinitions)
        {
            Console.WriteLine($"{id}\t{name}");
        }

        optionSet.WriteOptionDescriptions(Console.Out);
    }

    // The logs for the failure always exist on the associated work item, not on the 
    // individual test result
    private async Task<HelixLogInfo> GetHelixLogInfoAsync(HelixTestRunResult testRunResult) => 
        await HelixUtil.GetHelixLogInfoAsync(Server, "public", testRunResult.TestRun.Id, testRunResult.HelixTestResult.WorkItem.Id);

    private static string GetIndent(int level) => level == 0 ? string.Empty : new string(' ', level * 2);
}