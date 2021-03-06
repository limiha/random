﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Roslyn.Jenkins
{
    internal static class ConsoleTextUtil
    {
        private static readonly Regex s_csharpError = new Regex(@".*error CS\d+.*", RegexOptions.Compiled);
        private static readonly Regex s_basicError = new Regex(@".*error BC\d+.*", RegexOptions.Compiled);
        private static readonly Regex s_msbuildError = new Regex(@".*error MSB\d+.*", RegexOptions.Compiled);
        private static readonly Regex s_githubTimeout = new Regex(@"ERROR: Timeout after (\d)+ minutes", RegexOptions.Compiled);

        /// <summary>
        /// This happens if a developer merges a PR before Jenkins job runs.  This deletes the PR 
        /// branch in the repo and Jenkins simply can't find anything to build. 
        /// </summary>
        private static readonly Regex s_prMerged = new Regex(@"ERROR: Couldn't find any revision to build", RegexOptions.Compiled);

        /// <summary>
        /// Happened on several *nix jobs.  The deployment of xunit was the symptom of a larger package
        /// restore problem.
        /// </summary>
        private static readonly Regex s_xunitNotFound = new Regex(@"Cannot open assembly.*xunit.console.x86.exe", RegexOptions.Compiled);

        private static readonly Regex s_jenkinsFailure = new Regex(@"java\.io\.\w*Exception: \w+.*", RegexOptions.Compiled);

        private static readonly Tuple<Regex, JobFailureReason>[] s_allChecks = new[]
        {
            Tuple.Create(s_csharpError, JobFailureReason.Build),
            Tuple.Create(s_basicError, JobFailureReason.Build),
            Tuple.Create(s_msbuildError, JobFailureReason.Build),
            Tuple.Create(s_githubTimeout, JobFailureReason.Infrastructure),
            Tuple.Create(s_prMerged, JobFailureReason.Infrastructure),
            Tuple.Create(s_xunitNotFound, JobFailureReason.NuGet),
            Tuple.Create(s_jenkinsFailure, JobFailureReason.Infrastructure),
        };

        internal static bool TryGetFailureInfo(string consoleText, out JobFailureInfo failureInfo)
        {
            var lines = consoleText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            return TryGetFailureInfo(lines, out failureInfo);
        }

        internal static bool TryGetFailureInfo(string[] consoleTextLines, out JobFailureInfo failureInfo)
        {
            JobFailureReason? reason = null;
            var list = new List<string>();

            foreach (var line in consoleTextLines)
            {
                foreach (var tuple in s_allChecks)
                {
                    if (tuple.Item1.IsMatch(line))
                    {
                        reason = reason ?? tuple.Item2;
                        list.Add(line);
                        break;
                    }
                }
            }

            if (reason != null)
            {
                failureInfo = new JobFailureInfo(reason.Value, list);
                return true;
            }

            failureInfo = null;
            return false;
        }
    }
}
