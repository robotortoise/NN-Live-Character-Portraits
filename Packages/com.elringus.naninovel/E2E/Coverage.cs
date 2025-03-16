using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using static Naninovel.E2E.Shortcuts;

#if TEST_AVAILABLE
using NUnit.Framework.Interfaces;
using UnityEngine.TestRunner;

[assembly: TestRunCallback(typeof(Naninovel.E2E.Coverage))]
#endif

namespace Naninovel.E2E
{
    /// <summary>
    /// Reports scenario script coverage info after test run.
    /// </summary>
    public class Coverage
        #if TEST_AVAILABLE
        : ITestRunCallback
    #endif
    {
        private struct ScriptCoverage
        {
            public Script Asset;
            public CoverageRatio Ratio;
            public IReadOnlyList<IntRange> UncoveredIndexes;
        }

        private struct CoverageRatio
        {
            public int Percent;
            public int Covered;
            public int Total;
        }

        /// <summary>
        /// Whether the coverage will be reported after tests finish.
        /// </summary>
        public static bool Enabled { get; private set; }

        /// <summary>
        /// Invoke before running the tests to enable reporting.
        /// </summary>
        public static void Enable () => Enabled = true;

        /// <summary>
        /// Invoke before running the tests to disable reporting.
        /// </summary>
        public static void Disable () => Enabled = false;

        private readonly PlayedScriptRegister register = new();

        #if TEST_AVAILABLE
        public void RunStarted (ITest tests) { }
        public void TestStarted (ITest test) { }
        public void TestFinished (ITestResult result)
        {
            if (Enabled) RegisterPlayed();
        }
        public void RunFinished (ITestResult results)
        {
            if (Enabled) ReportCoverage(register.GetPlayed()).Forget();
        }
        #endif

        private void RegisterPlayed ()
        {
            var global = new GlobalStateMap();
            (Service<IScriptPlayer>() as IStatefulService<GlobalStateMap>)?.SaveServiceState(global);
            var register = global.GetState<ScriptPlayer.GlobalState>()?.PlayedScriptRegister;
            if (register == null) return;
            foreach (var kv in register.GetPlayed())
            foreach (var range in kv.Value)
                for (int i = range.StartIndex; i <= range.EndIndex; i++)
                    this.register.RegisterPlayedIndex(kv.Key, i);
        }

        private static async UniTask ReportCoverage (IReadOnlyDictionary<string, IReadOnlyList<IntRange>> played)
        {
            var scripts = await Service<IScriptManager>().ScriptLoader.LoadAll();
            var coverage = scripts.Select(s => CoverScript(s,
                played.TryGetValue(s.Object.Path, out var idx) ? idx : Array.Empty<IntRange>())).ToArray();
            Engine.Log(BuildReport(coverage));
        }

        private static ScriptCoverage CoverScript (Script script, IReadOnlyList<IntRange> playedIndexes)
        {
            var uncoveredRegister = new PlayedScriptRegister();
            var list = script.Playlist;
            for (int i = 0; i < list.Count; i++)
                if (!playedIndexes.Any(r => r.Contains(i)))
                    uncoveredRegister.RegisterPlayedIndex(script.Path, i);
            uncoveredRegister.GetPlayed().TryGetValue(script.Path, out var uncovered);
            uncovered ??= Array.Empty<IntRange>();
            return new() {
                Asset = script,
                Ratio = GetRatio(list.Count, CountIndexes(uncovered)),
                UncoveredIndexes = uncovered
            };
        }

        private static int CountIndexes (IEnumerable<IntRange> ranges)
        {
            var count = 0;
            foreach (var range in ranges)
                count += range.EndIndex - range.StartIndex + 1;
            return count;
        }

        private static string BuildReport (IReadOnlyCollection<ScriptCoverage> coverage)
        {
            var builder = new StringBuilder();
            var total = GetRatio(coverage.Sum(c => c.Ratio.Total), coverage.Sum(c => c.Ratio.Total - c.Ratio.Covered));
            builder.AppendLine($"E2E scenario coverage: {FormatRatio(total)}");
            foreach (var script in coverage)
                builder.Append($" • {ObjectUtils.BuildAssetLink(script.Asset)} {FormatRatio(script.Ratio)} ")
                    .AppendLine(script.UncoveredIndexes.Count == 0 ? "" : BuildUncoveredLines(script.Asset.Playlist, script.UncoveredIndexes));
            return builder.ToString();
        }

        private static CoverageRatio GetRatio (int total, int uncovered) => new() {
            Percent = Mathf.CeilToInt((1 - (float)uncovered / total) * 100),
            Covered = total - uncovered,
            Total = total
        };

        private static string FormatRatio (CoverageRatio ratio)
        {
            var color = ratio.Percent == 100 ? "lime" : ratio.Percent > 50 ? "yellow" : "red";
            return $"<color={color}>{ratio.Percent}%</color> ({ratio.Covered}/{ratio.Total})";
        }

        private static string FormatRange (IntRange range) =>
            range.StartIndex == range.EndIndex
                ? range.StartIndex.ToString()
                : $"{range.StartIndex}-{range.EndIndex}";

        private static string BuildUncoveredLines (ScriptPlaylist list, IReadOnlyList<IntRange> indexes)
        {
            var lines = indexes.Select(i => new IntRange(
                list.GetCommandByIndex(i.StartIndex).PlaybackSpot.LineNumber,
                list.GetCommandByIndex(i.EndIndex).PlaybackSpot.LineNumber));
            return $"Lines: {string.Join(", ", lines.Select(FormatRange))}.";
        }
    }
}
