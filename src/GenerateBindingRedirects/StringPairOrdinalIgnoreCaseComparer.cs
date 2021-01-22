using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GenerateBindingRedirects
{
    public class StringPairOrdinalIgnoreCaseComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals([AllowNull] (string, string) x, [AllowNull] (string, string) y) =>
            string.Equals(x.Item1, y.Item1, C.IGNORE_CASE) && string.Equals(x.Item2, y.Item2, C.IGNORE_CASE);

        public int GetHashCode([DisallowNull] (string, string) obj) => HashCode.Combine(obj.Item1, obj.Item2);
    }
}