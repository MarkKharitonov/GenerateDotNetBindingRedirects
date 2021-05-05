using System;
using NuGet.Versioning;

namespace Dayforce.CSharp.ProjectAssets
{
    public static class C
    {
        public static class V1
        {
            public static readonly NuGetVersion Value = new NuGetVersion(1, 0, 0);
            public static readonly VersionRange Range = new VersionRange(Value);
        }
        public const StringComparison IGNORE_CASE = StringComparison.OrdinalIgnoreCase;
        public static readonly StringComparer IgnoreCase = StringComparer.OrdinalIgnoreCase;
        public static readonly StringPairOrdinalIgnoreCaseComparer IgnoreCase2 = new StringPairOrdinalIgnoreCaseComparer();
        public const string PROJECT = "project";
        public const string PACKAGE = "package";
    }
}
