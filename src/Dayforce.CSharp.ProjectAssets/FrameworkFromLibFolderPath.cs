using System.IO;
using NuGet.Frameworks;

namespace Dayforce.CSharp.ProjectAssets
{
    public class FrameworkFromLibFolderPath : IFrameworkSpecific
    {
        public FrameworkFromLibFolderPath(string libFolderPath)
        {
            LibFolderPath = libFolderPath;
            TargetFramework = NuGetFramework.ParseFolder(Path.GetFileName(libFolderPath));
        }

        public readonly string LibFolderPath;

        public NuGetFramework TargetFramework { get; }

        public override string ToString() => TargetFramework.ToString();
    }
}