using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Wormhole.Sync.Tests.Models;
using Xunit;

namespace Wormhole.Sync.Tests.Misc
{
    /// <summary>
    /// Synchronous <see cref="IProgress{T}"/> that records every reported <see cref="ProgressArgs"/> in report order,
    /// together with the percentage read at the moment it was reported.
    /// </summary>
    public class ProgressRecorder : IProgress<ProgressArgs>
    {
        private readonly object syncRoot = new();
        private readonly List<(double ReadAtReport, ProgressArgs Args)> reports = [];

        public void Report(ProgressArgs value)
        {
            lock (this.syncRoot)
                this.reports.Add((value.ProgressPercentage, value));
        }

        public IReadOnlyList<(double ReadAtReport, ProgressArgs Args)> Reports
        {
            get { lock (this.syncRoot) return this.reports.ToList(); }
        }

        /// <summary>
        /// Writes all reports and asserts that the percentages never go down, both when read at report time
        /// and when read later (like a <see cref="Progress{T}"/> handler running on the ThreadPool does).
        /// </summary>
        public void AssertNeverDecreases(ITestOutputHelper output)
        {
            var reports = this.Reports;
            Assert.NotEmpty(reports);

            var previous = 0d;
            var decreases = new List<string>();

            for (var i = 0; i < reports.Count; i++)
            {
                var (readAtReport, args) = reports[i];
                var readLater = args.ProgressPercentage;

                output.WriteLine($"{i,4} {readAtReport,7:P1} (later {readLater,7:P1}) {args.TypeName}");

                if (readLater != readAtReport)
                    decreases.Add($"#{i} {args.TypeName}: read at report {readAtReport:P1}, read later {readLater:P1}");

                if (readAtReport < previous)
                    decreases.Add($"#{i} {args.TypeName}: {previous:P1} -> {readAtReport:P1}");

                previous = Math.Max(previous, readAtReport);
            }

            Assert.True(decreases.Count == 0, "Progress is not monotonic:" + Environment.NewLine + string.Join(Environment.NewLine, decreases));
            Assert.InRange(reports.Max(r => r.ReadAtReport), 0d, 1d);
        }

        /// <summary>
        /// Adds products with a 40 KB photo each, so a first sync needs many batch parts
        /// (the server never uses batches smaller than 100 KB).
        /// </summary>
        public static async Task AddLargeProductsAsync(CoreProvider provider, int count = 40)
        {
            var photo = new byte[40 * 1024];
            new Random(42).NextBytes(photo);

            for (var i = 0; i < count; i++)
                await provider.AddProductAsync(thumbNailPhoto: photo);
        }
    }
}
