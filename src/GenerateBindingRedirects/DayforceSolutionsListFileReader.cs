using Dayforce.CSharp.ProjectAssets;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GenerateBindingRedirects
{
    public class DayforceSolutionsListFileReader : ISolutionsListFileReader
    {
        public IEnumerable<string> YieldSolutionFilePaths(string slnListFilePath)
        {
            var res = File.ReadAllLines(slnListFilePath);
            if (slnListFilePath.EndsWith("Solutions.txt"))
            {
                return res.Select(path => Path.GetFullPath(slnListFilePath + "\\..\\" + path));
            }

            return res
                .Where(line => line.StartsWith("      - name: "))
                .Select(name => Path.GetFullPath(slnListFilePath + "\\..\\..\\" + name.Replace("      - name: ", "") + ".sln"))
                .ToList();
        }
    }
}
