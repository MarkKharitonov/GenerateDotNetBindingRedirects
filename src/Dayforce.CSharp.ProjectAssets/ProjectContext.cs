using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.XPath;

namespace Dayforce.CSharp.ProjectAssets
{
    public class ProjectContext
    {
        [JsonIgnore]
        internal static int Count;

        [JsonIgnore]
        private readonly SolutionsContext m_sc;
        public readonly int Index;
        public readonly string Solution;
        public readonly string ProjectFilePath;
        public readonly string AssemblyName;
        public readonly List<string> DllReferences;
        public readonly string ExpectedConfigFilePath;
        public readonly string ActualConfigFilePath;
        public readonly bool SDKStyle;
        public readonly string ProjectName;
        public readonly string RelativeProjectFilePath;
        public readonly string RelativeSolutionFilePath;
        private readonly IReadOnlyList<string> m_referencedProjectNames;
        [JsonIgnore]
        private readonly Lazy<IReadOnlyList<ProjectContext>> m_referencedProjects;

        [JsonIgnore]
        public IReadOnlyList<ProjectContext> ReferencedProjects => m_referencedProjects.Value;

        // Used for the JSON output only
        public IEnumerable<string> ProjectReferences => ReferencedProjects.Select(o => o.AssemblyName);

        public static ProjectContext Create(SolutionsContext sc, string solution, string projectFilePath)
        {
            try
            {
                var (nav, nsmgr) = GetProjectXPathNavigator(projectFilePath);
                var assemblyName =
                    nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:AssemblyName/text()", nsmgr)?.Value ??
                    Path.GetFileNameWithoutExtension(projectFilePath);

                var dllReferences = nav.Select("/p:Project/p:ItemGroup/p:Reference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => o.Value)
                    .Where(o => o != "System" &&
                        !o.StartsWith("System.") &&
                        !o.StartsWith("Microsoft.") &&
                        !o.Contains(","))
                    .Select(o => o.IsExecutable() ? o[0..^4] : o)
                    .ToList();

                var projectReferences = nav.Select("/p:Project/p:ItemGroup/p:ProjectReference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => Path.GetFileNameWithoutExtension(o.Value))
                    .ToList();

                var projectTypeGuids = nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:ProjectTypeGuids/text()", nsmgr)?.Value;
                bool isWebApplication = projectTypeGuids?.Contains("{349c5851-65df-11da-9384-00065b846f21}", C.IGNORE_CASE) == true;
                var configFileName = isWebApplication ? "web.config" : "app.config";
                var expectedConfigFilePath = Path.GetFullPath($"{projectFilePath}\\..\\{configFileName}");
                string actualConfigFilePath = null;
                var sdkStyle = false;

                if (!isWebApplication)
                {
                    var attr = LocateAppConfigInProjectXml(nav, nsmgr);
                    if (attr != null)
                    {
                        actualConfigFilePath = Path.GetFullPath(projectFilePath + "\\..\\" + attr.Value);
                    }
                    else if (!(bool)nav.Evaluate("boolean(/p:Project/p:PropertyGroup/p:ProjectGuid)", nsmgr))
                    {
                        // An SDK style project, in which case just look for the app.config file on disk
                        sdkStyle = true;
                        actualConfigFilePath = $"{projectFilePath}\\..\\app.config";
                        actualConfigFilePath = File.Exists(actualConfigFilePath) ? Path.GetFullPath(actualConfigFilePath) : null;
                    }
                }
                return new ProjectContext(sc, solution, projectFilePath, assemblyName, dllReferences, projectReferences, expectedConfigFilePath, actualConfigFilePath, sdkStyle);
            }
            catch (Exception exc) when (exc is not ApplicationException)
            {
                throw new ApplicationException("Failed to process " + projectFilePath, exc);
            }
        }

        public static XPathNavigator LocateAppConfigInProjectXml(XPathNavigator nav, XmlNamespaceManager nsmgr) =>
            nav.Select("/p:Project/p:ItemGroup/p:None[contains(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'),'app.config')]/@Include", nsmgr)
            .Cast<XPathNavigator>()
            .FirstOrDefault(o => o.Value.Equals("app.config", C.IGNORE_CASE) || o.Value.EndsWith("\\app.config", C.IGNORE_CASE));

        private ProjectContext(SolutionsContext sc, string solution, string projectFilePath, string assemblyName, List<string> dllReferences, List<string> projectReferences,
            string expectedConfigFilePath, string actualConfigFilePath, bool sdkStyle)
        {
            m_sc = sc;
            Index = Count++;
            Solution = solution;
            ProjectFilePath = projectFilePath;
            AssemblyName = assemblyName;
            DllReferences = dllReferences;
            m_referencedProjectNames = projectReferences;
            ExpectedConfigFilePath = expectedConfigFilePath;
            ActualConfigFilePath = actualConfigFilePath;
            SDKStyle = sdkStyle;
            ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
            RelativeProjectFilePath = Log.Instance.GetRelativeFilePath(ProjectFilePath);
            RelativeSolutionFilePath = Log.Instance.GetRelativeFilePath(Solution);

            Log.Instance.WriteVerbose("ProjectContext({0}) : {1}", RelativeProjectFilePath, AssemblyName);

            m_referencedProjects = new Lazy<IReadOnlyList<ProjectContext>>(() => m_referencedProjectNames.Select(projectName => m_sc.GetProjectByName(Solution, projectName)).ToList());
        }

        public override string ToString() => $"{AssemblyName} ({ProjectName} @ {RelativeSolutionFilePath})";

        private static (XPathNavigator, XmlNamespaceManager) GetProjectXPathNavigator(string projectFile)
        {
            var doc = new XPathDocument(projectFile);
            var nav = doc.CreateNavigator();
            var nsmgr = new XmlNamespaceManager(nav.NameTable);
            nav.MoveToFollowing(XPathNodeType.Element);
            var ns = nav.GetNamespacesInScope(XmlNamespaceScope.Local).FirstOrDefault();
            var nsValue = ns.Value ?? "";
            nsmgr.AddNamespace("p", nsValue);
            return (nav, nsmgr);
        }
    }
}
