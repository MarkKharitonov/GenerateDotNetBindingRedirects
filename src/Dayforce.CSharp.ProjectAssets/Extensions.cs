using Newtonsoft.Json;
using NuGet.Packaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Dayforce.CSharp.ProjectAssets
{
    public static class Extensions
    {
        public static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
        {
            foreach (var item in items)
            {
                action(item);
            }
        }

        public static int FindIndex<T>(this IList<T> source, int startIndex, Predicate<T> match)
        {
            for (int i = startIndex; i < source.Count; ++i)
            {
                if (match(source[i]))
                {
                    return i;
                }
            }
            return -1;
        }
    
        public static bool IsExecutable(this string path) => path.EndsWith(".dll", C.IGNORE_CASE) || path.EndsWith(".exe", C.IGNORE_CASE);

        public static void GenerateNuGetUsageReport(this ProjectAssets projectAssets, string projectName, string nuGetUsageReport)
        {
            if (Directory.Exists(nuGetUsageReport) || nuGetUsageReport[^1] == '\\')
            {
                nuGetUsageReport = nuGetUsageReport + (nuGetUsageReport[^1] == '\\' ? "" : "\\") + "NuGetUsageReport-" + projectName + ".json";
            }
            Directory.CreateDirectory(Path.GetDirectoryName(nuGetUsageReport));
            File.WriteAllText(nuGetUsageReport, JsonConvert.SerializeObject(projectAssets
                .Libraries
                .Where(o => o.Value.Type == C.PACKAGE && o.Value.HasRuntimeAssemblies)
                .ToDictionary(o => o.Key, o => new
                {
                    NuGetVersion = o.Value.Version.ToString(),
                    Metadata = GetMetadata(projectAssets.PackageFolder, o.Key, o.Value),
                    RuntimeAssemblies = o.Value.Library.RuntimeAssemblies.Select(o => Path.GetFileName(o.Path))
                }), Formatting.Indented));
        }

        private static object GetMetadata(string packageFolder, string packageId, LibraryItem value)
        {
            var nuSpecFile = Path.Combine(packageFolder, packageId, value.Version.ToString(), packageId + ".nuspec");
            var nuSpecReader = new NuspecReader(nuSpecFile);
            return new
            {
                Authors = nuSpecReader.GetAuthors(),
                ProjectUrl = nuSpecReader.GetProjectUrl()
            };
        }
    }
}
