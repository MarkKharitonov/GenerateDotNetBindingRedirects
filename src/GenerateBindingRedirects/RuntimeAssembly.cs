using System;
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace GenerateBindingRedirects
{
    public class RuntimeAssembly : IEquatable<RuntimeAssembly>
    {
        public static readonly RuntimeAssembly Unresolved = new RuntimeAssembly();

        public readonly string RelativeFilePath;
        [JsonIgnore]
        public readonly string FilePath;
        [JsonIgnore]
        public readonly string AssemblyName;
        public readonly Version AssemblyVersion;
        [JsonIgnore]
        public readonly bool IsUnsigned;

        private RuntimeAssembly()
        {
            AssemblyName = RelativeFilePath = FilePath = "***";
            AssemblyVersion = new Version();
        }

        public RuntimeAssembly(string packageFolder, string filePath)
        {
            RelativeFilePath = filePath[packageFolder.Length..];
            FilePath = filePath;
            var asmName = System.Reflection.AssemblyName.GetAssemblyName(filePath);
            AssemblyVersion = asmName.Version;
            AssemblyName = asmName.Name;
            IsUnsigned = asmName.GetPublicKeyToken()?.Length == 0;
        }

        public override bool Equals(object obj) => Equals(obj as RuntimeAssembly);

        public bool Equals([AllowNull] RuntimeAssembly item) =>
            item != null &&
            RelativeFilePath.Equals(item.RelativeFilePath, C.IGNORE_CASE) &&
            AssemblyVersion.Equals(item.AssemblyVersion);

        public override int GetHashCode() => HashCode.Combine(RelativeFilePath, AssemblyVersion);

        public override string ToString() => $"{RelativeFilePath} ({AssemblyVersion})";
    }
}