using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using DevOps.Util;

internal sealed class HelixTestResult
{
    // The TestCaseResult representing the actual test failure
    internal TestCaseResult Test { get; }

    // The TestCaseResult representing the helix work item. This is where logs will be 
    // stored
    //
    // Can be null if we can't find the associated work item even though it should always
    // be there
    internal TestCaseResult WorkItem { get; }

    internal bool IsWorkItemResult => Test.Id == WorkItem.Id;

    internal string TestCaseTitle => Test.TestCaseTitle;

    internal HelixTestResult(TestCaseResult test, TestCaseResult workItem)
    {
        Test = test;
        WorkItem = workItem;
    }

    internal HelixTestResult(TestCaseResult workItem)
    {
        Test = workItem;
        WorkItem = workItem;
    }
}

internal sealed class HelixTestRunResult
{
    internal Build Build { get; }
    internal TestRun TestRun { get; }

    internal HelixTestResult HelixTestResult { get; }

    internal HelixTestRunResult(Build build, TestRun testRun, HelixTestResult helixTestResult)
    {
        Build = build;
        TestRun = testRun;
        HelixTestResult = helixTestResult;
    }
}

// TODO: make this type use actual dictionaries and hashes instead of crappy lists.
// just wrote this for functionality at the moment. Perf fix ups later.
internal sealed class BuildTestInfo
{
    public List<HelixTestRunResult> DataList;

    public Build Build { get; }

    internal BuildTestInfo(Build build, List<HelixTestRunResult> dataList)
    {
        Build = build;
        DataList = dataList;
    }

    internal IEnumerable<string> GetTestCaseTitles() => DataList
        .Select(x => x.HelixTestResult.TestCaseTitle)
        .Distinct()
        .OrderBy(x => x);

    internal IEnumerable<TestRun> GetTestRuns() => DataList.Select(x => x.TestRun);

    internal IEnumerable<string> GetTestRunNames() => GetTestRuns().Select(x => x.Name).OrderBy(x => x);

    internal IEnumerable<TestRun> GetTestRunsForTestCaseTitle(string testCaseTitle) => DataList
        .Where(x => x.HelixTestResult.TestCaseTitle == testCaseTitle)
        .Select(x => x.TestRun);

    internal IEnumerable<string> GetTestRunNamesForTestCaseTitle(string testCaseTitle) => this
        .GetTestRunsForTestCaseTitle(testCaseTitle)
        .Select(x => x.Name)
        .Distinct()
        .OrderBy(x => x);

    internal IEnumerable<HelixTestRunResult> GetHelixTestRunResultsForTestCaseTitle(string testCaseTitle) => DataList
        .Where(x => x.HelixTestResult.TestCaseTitle == testCaseTitle)
        .ToList();

    internal IEnumerable<HelixTestRunResult> GetHelixTestRunResultsForTestRunName(string testRunName) => DataList
        .Where(x => x.TestRun.Name == testRunName)
        .ToList();

    public bool ContainsTestCaseTitle(string testCaseTitle) => GetTestCaseTitles().Contains(testCaseTitle);

    public bool ContainsTestRunName(string testRunName) => DataList.Exists(x => x.TestRun.Name == testRunName);
}

internal sealed class BuildTestInfoCollection : IEnumerable<BuildTestInfo>
{
    public ReadOnlyCollection<BuildTestInfo> BuildTestInfos { get; }

    public BuildTestInfoCollection(ReadOnlyCollection<BuildTestInfo> buildTestInfos)
    {
        BuildTestInfos = buildTestInfos;
    }

    public BuildTestInfoCollection(IEnumerable<BuildTestInfo> buildTestInfos)
        : this(new ReadOnlyCollection<BuildTestInfo>(buildTestInfos.ToList()))
    {

    }

    public List<string> GetTestCaseTitles() => BuildTestInfos
        .SelectMany(x => x.GetTestCaseTitles())
        .Distinct()
        .ToList();

    internal List<HelixTestRunResult> GetHelixTestRunResultsForTestCaseTitle(string testCaseTitle) => BuildTestInfos
        .SelectMany(x => x.GetHelixTestRunResultsForTestCaseTitle(testCaseTitle))
        .OrderBy(x => x.TestRun.Name)
        .ToList();

    internal List<Build> GetBuildsForTestCaseTitle(string testCaseTitle) => this
        .GetBuildTestInfosForTestCaseTitle(testCaseTitle)
        .Select(x => x.Build)
        .ToList();

    internal List<BuildTestInfo> GetBuildTestInfosForTestCaseTitle(string testCaseTitle) => BuildTestInfos
        .Where(x => x.ContainsTestCaseTitle(testCaseTitle))
        .OrderBy(x => x.Build.Id)
        .ToList();

    internal List<TestRun> GetTestRunsForTestCaseTitle(string testCaseTitle) => BuildTestInfos
        .SelectMany(x => x.GetTestRunsForTestCaseTitle(testCaseTitle))
        .OrderBy(x => x.Id)
        .ToList();

    internal List<string> GetTestRunNamesForTestCaseTitle(string testCaseTitle) => BuildTestInfos
        .SelectMany(x => x.GetTestRunNamesForTestCaseTitle(testCaseTitle))
        .Distinct()
        .OrderBy(x => x)
        .ToList();

    internal List<string> GetTestRunNames() => BuildTestInfos
        .SelectMany(x => x.GetTestRuns().Select(x => x.Name))
        .Distinct()
        .OrderBy(x => x)
        .ToList();

    public IEnumerator<BuildTestInfo> GetEnumerator() => BuildTestInfos.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}





