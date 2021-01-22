using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Newtonsoft.Json;

namespace GenerateBindingRedirects
{
    public sealed class AssemblyBindingRedirect : IEquatable<AssemblyBindingRedirect>
    {
        public readonly string AssemblyName;
        public readonly string PublicKeyToken;
        public readonly string Culture;
        public readonly Version Version;
        public readonly string TargetFilePath;
        [JsonIgnore]
        public readonly bool IsFrameworkAssembly;

        public AssemblyBindingRedirect(string targetFilePath)
        {
            var asmName = System.Reflection.AssemblyName.GetAssemblyName(TargetFilePath = targetFilePath);
            AssemblyName = asmName.Name;
            Version = asmName.Version;
            PublicKeyToken = BitConverter.ToString(asmName.GetPublicKeyToken()).Replace("-", "");
            Culture = CultureInfo.InvariantCulture.Equals(asmName.CultureInfo) ? "neutral" : asmName.CultureName;
        }

        public AssemblyBindingRedirect(string name, Version version, string culture, string publicKeyToken)
        {
            IsFrameworkAssembly = true;
            TargetFilePath = $"[GAC] {name}, Version = {version}, PublicKeyToken = {publicKeyToken}, Culture = {culture}";
            AssemblyName = name;
            Version = version;
            PublicKeyToken = publicKeyToken;
            Culture = culture;
        }

        public AssemblyBindingRedirect(XmlNode node)
        {
            var assemblyIdentity = node.ChildNodes.Cast<XmlNode>().First(n => n.LocalName == "assemblyIdentity");
            var bindingRedirect = node.ChildNodes.Cast<XmlNode>().First(n => n.LocalName == "bindingRedirect");
            AssemblyName = assemblyIdentity.Attributes["name"].Value;
            PublicKeyToken = assemblyIdentity.Attributes["publicKeyToken"].Value;
            Culture = assemblyIdentity.Attributes["culture"].Value;
            Version = Version.Parse(bindingRedirect.Attributes["newVersion"].Value);
            Log.WriteVerbose("OldAssemblyBindingRedirect : {0}", this);
        }

        public override string ToString() => $"{AssemblyName}/{Version} ({PublicKeyToken}, {Culture})";

        public string Render(string privateProbingPath) => @$"      <dependentAssembly>
        <assemblyIdentity name=""{AssemblyName}"" publicKeyToken=""{PublicKeyToken}"" culture=""{Culture}"" />
        <bindingRedirect oldVersion=""0.0.0.0-{Version}"" newVersion=""{Version}"" />{RenderCodeBaseElement(privateProbingPath)}
      </dependentAssembly>";

        private object RenderCodeBaseElement(string privateProbingPath) =>
            privateProbingPath == null || TargetFilePath == null || IsFrameworkAssembly
            ? null
            : @$"
        <codeBase version=""{Version}"" href=""{privateProbingPath}/{Path.GetFileName(TargetFilePath)}"" />";

        public bool Equals([AllowNull] AssemblyBindingRedirect other) => other != null &&
            AssemblyName == other.AssemblyName &&
            PublicKeyToken == other.PublicKeyToken &&
            Culture == other.Culture &&
            Version == other.Version;

        public override int GetHashCode() => HashCode.Combine(AssemblyName, PublicKeyToken, Culture, Version);

        public override bool Equals(object obj) => Equals(obj as AssemblyBindingRedirect);
    }
}
