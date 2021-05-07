using System.Collections.Generic;

namespace Dayforce.CSharp.ProjectAssets
{
    public interface ISolutionsListFileReader
    {
        IEnumerable<string> YieldSolutionFilePaths(string slnListFilePath);
    }
}
