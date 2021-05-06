using Dayforce.CSharp.ProjectAssets;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenerateNuGetUsageReport
{
    public class SimpleSolutionsListFileReader : ISolutionsListFileReader
    {
        private const string PATTERN = @"[\w\.\\/-]+\.sln";
        private static readonly Regex s_regex = new Regex(PATTERN);

        public IEnumerable<string> YieldSolutionFilePaths(string slnListFilePath) => File
            .ReadAllLines(slnListFilePath)
            .Where(line => line.Contains(".sln"))
            .Select(line => s_regex.Match(line))
            .Where(m => m.Success)
            .Select(m => Path.GetFullPath(slnListFilePath + "\\..\\" + m.Value));
    }
}
