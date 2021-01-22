using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.XPath;

namespace GenerateBindingRedirects
{
    public class ProjectContext
    {
        [Flags]
        private enum ActualAppConfigStatus
        {
            Normal = 0,
            NotSpecified = 1,
            FileNotFound = 2,
            Linked = 4
        }

        internal static int Count;

        private const string ASSEMBLY_BINDING_XMLNS = "urn:schemas-microsoft-com:asm.v1";
        private readonly SolutionsContext m_sc;
        public readonly int Index;
        public readonly string Solution;
        public readonly string ProjectFilePath;
        public readonly string AssemblyName;
        public readonly List<string> DllReferences;
        public readonly string ExpectedConfigFilePath;
        public readonly string ActualConfigFilePath;
        public readonly string ProjectName;
        public readonly string RelativeProjectFilePath;
        private readonly IReadOnlyList<string> m_referencedProjectNames;
        private static readonly XmlWriterSettings s_xmlWriterSettings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false)
        };
        private readonly Lazy<IReadOnlyList<ProjectContext>> m_referencedProjects;

        public IReadOnlyList<ProjectContext> ReferencedProjects => m_referencedProjects.Value;

        public static ProjectContext Create(SolutionsContext sc, string solution, string projectFilePath)
        {
            try
            {
                var (nav, nsmgr) = GetProjectXPathNavigator(projectFilePath);
                var assemblyName =
                    nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:AssemblyName/text()", nsmgr)?.Value ??
                    Path.GetFileNameWithoutExtension(projectFilePath);

                var projectTypeGuids = nav.SelectSingleNode("/p:Project/p:PropertyGroup/p:ProjectTypeGuids/text()", nsmgr)?.Value;
                if (projectTypeGuids?.Contains("{A1591282-1198-4647-A2B1-27E5FF5F6F3B}", C.IGNORE_CASE) == true)
                {
                    // Silverlight
                    return null;
                }

                var dllReferences = nav.Select("/p:Project/p:ItemGroup/p:Reference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => o.Value)
                    .Where(o => o != "System" &&
                        !o.StartsWith("System.") &&
                        !o.StartsWith("Microsoft.") &&
                        !o.Contains(","))
                    .Select(o => o.IsExecutable() ? o.Substring(0, o.Length - 4) : o)
                    .ToList();

                var projectReferences = nav.Select("/p:Project/p:ItemGroup/p:ProjectReference/@Include", nsmgr)
                    .Cast<XPathNavigator>()
                    .Select(o => Path.GetFileNameWithoutExtension(o.Value))
                    .ToList();

                bool isWebApplication = projectTypeGuids?.Contains("{349c5851-65df-11da-9384-00065b846f21}", C.IGNORE_CASE) == true;
                var configFileName = isWebApplication ? "web.config" : "app.config";
                var expectedConfigFilePath = Path.GetFullPath($"{projectFilePath}\\..\\{configFileName}");
                string actualConfigFilePath = null;

                if (!isWebApplication)
                {
                    var attr = LocateAppConfigInProjectXml(nav, nsmgr);
                    if (attr != null)
                    {
                        actualConfigFilePath = Path.GetFullPath(projectFilePath + "\\..\\" + attr.Value);
                    }
                }
                return new ProjectContext(sc, solution, projectFilePath, assemblyName, dllReferences, projectReferences, expectedConfigFilePath, actualConfigFilePath);
            }
            catch (Exception exc)
            {
                throw new ApplicationException("Failed to process " + projectFilePath, exc);
            }
        }

        private static XPathNavigator LocateAppConfigInProjectXml(XPathNavigator nav, XmlNamespaceManager nsmgr) =>
            nav.Select("/p:Project/p:ItemGroup/p:None[contains(translate(@Include, 'ABCDEFGHIJKLMNOPQRSTUVWXYZ', 'abcdefghijklmnopqrstuvwxyz'),'app.config')]/@Include", nsmgr)
            .Cast<XPathNavigator>()
            .FirstOrDefault(o => o.Value.Equals("app.config", C.IGNORE_CASE) || o.Value.EndsWith("\\app.config", C.IGNORE_CASE));

        private ProjectContext(SolutionsContext sc, string solution, string projectFilePath, string assemblyName, List<string> dllReferences, List<string> projectReferences,
            string expectedConfigFilePath, string actualConfigFilePath)
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
            ProjectName = Path.GetFileNameWithoutExtension(projectFilePath);
            RelativeProjectFilePath = Log.GetRelativeFilePath(ProjectFilePath);

            Log.WriteVerbose("ProjectContext({0}) : {1}", RelativeProjectFilePath, AssemblyName);

            m_referencedProjects = new Lazy<IReadOnlyList<ProjectContext>>(() => m_referencedProjectNames.Select(projectName => m_sc.GetProjectByName(Solution, projectName)).ToList());
        }

        public void WriteBindingRedirects(string bindingRedirects, bool assert)
        {
            var actualAppConfigStatus = ActualAppConfigStatus.Normal;
            if (ExpectedConfigFilePath.EndsWith("app.config", C.IGNORE_CASE))
            {
                if (ActualConfigFilePath == null)
                {
                    actualAppConfigStatus |= ActualAppConfigStatus.NotSpecified;
                }
                else
                {
                    if (!File.Exists(ActualConfigFilePath))
                    {
                        actualAppConfigStatus |= ActualAppConfigStatus.FileNotFound;
                    }
                    if (!ActualConfigFilePath.Equals(ExpectedConfigFilePath, C.IGNORE_CASE))
                    {
                        actualAppConfigStatus |= ActualAppConfigStatus.Linked;
                        if ((actualAppConfigStatus & ActualAppConfigStatus.FileNotFound) == 0)
                        {
                            File.Copy(ActualConfigFilePath, ExpectedConfigFilePath, true);
                        }
                    }
                }
            }

            if (!WriteConfigFile(bindingRedirects, assert) ||
                actualAppConfigStatus == ActualAppConfigStatus.Normal ||
                actualAppConfigStatus == ActualAppConfigStatus.FileNotFound)
            {
                return;
            }

            AddAppConfigToProjectFile(actualAppConfigStatus);
        }

        private void AddAppConfigToProjectFile(ActualAppConfigStatus actualAppConfigStatus)
        {
            var (doc, nsmgr) = GetProjectXmlDocument(ProjectFilePath);
            var node = doc.CreateElement("None", doc.DocumentElement.Attributes["xmlns"].Value);
            var attr = doc.CreateAttribute("Include");
            attr.Value = "app.config";
            node.Attributes.Append(attr);
            if (actualAppConfigStatus == ActualAppConfigStatus.NotSpecified)
            {
                var itemGroup = doc.SelectSingleNode("/p:Project/p:ItemGroup[count(./*) > 0]", nsmgr);
                itemGroup.AppendChild(doc.CreateWhitespace("  "));
                itemGroup.AppendChild(node);
                itemGroup.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
            }
            else
            {
                var oldAttr = LocateAppConfigInProjectXml(doc.CreateNavigator(), nsmgr);
                oldAttr.MoveToParent();
                var oldNode = (XmlNode)oldAttr.UnderlyingObject;
                oldNode.ParentNode.ReplaceChild(node, oldNode);
            }
            doc.Save(ProjectFilePath);
        }

        private bool WriteConfigFile(string bindingRedirects, bool assert)
        {
            if (!File.Exists(ExpectedConfigFilePath))
            {
                if (string.IsNullOrEmpty(bindingRedirects))
                {
                    return false;
                }
                if (assert)
                {
                    throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it does not exist.");
                }
                WriteNewConfigFile(bindingRedirects, assert);
                return true;
            }

            var doc = new XmlDocument
            {
                PreserveWhitespace = true
            };
            doc.Load(ExpectedConfigFilePath);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            nsmgr.AddNamespace("b", ASSEMBLY_BINDING_XMLNS);
            var assemblyBinding = doc.SelectSingleNode("/configuration/runtime/b:assemblyBinding", nsmgr);
            if (assemblyBinding == null)
            {
                if (bindingRedirects.Length == 0)
                {
                    return false;
                }
                if (assert)
                {
                    throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it has none.");
                }

                var cfg = doc.SelectSingleNode("/configuration");
                if (cfg == null)
                {
                    WriteNewConfigFile(bindingRedirects, assert);
                    return true;
                }

                var runtime = cfg.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "runtime");
                if (runtime == null)
                {
                    cfg.AppendChild(doc.CreateWhitespace("  "));
                    runtime = cfg.AppendChild(doc.CreateElement("runtime"));
                    runtime.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
                    cfg.AppendChild(doc.CreateWhitespace(Environment.NewLine));
                }
                assemblyBinding = runtime.ChildNodes.Cast<XmlNode>().FirstOrDefault(n => n.LocalName == "assemblyBinding");
                if (assemblyBinding == null)
                {
                    runtime.AppendChild(doc.CreateWhitespace("  "));
                    assemblyBinding = runtime.AppendChild(doc.CreateElement("assemblyBinding", ASSEMBLY_BINDING_XMLNS));
                    var attr = doc.CreateAttribute("xmlns", "http://www.w3.org/2000/xmlns/");
                    attr.Value = ASSEMBLY_BINDING_XMLNS;
                    assemblyBinding.Attributes.Append(attr);
                    runtime.AppendChild(doc.CreateWhitespace(Environment.NewLine + "  "));
                }
            }

            var newInnerXml = Environment.NewLine +
                bindingRedirects +
                Environment.NewLine + "    ";
            var curInnerXml = assemblyBinding.OuterXml
                .Replace(@"<assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">", "")
                .Replace(@"</assemblyBinding>", "");

            if (assert)
            {
                if (curInnerXml == newInnerXml)
                {
                    return false;
                }
                throw new ApplicationException($"{ExpectedConfigFilePath} does not have the expected set of binding redirects.");
            }

            if (curInnerXml != newInnerXml)
            {
                assemblyBinding.InnerXml = newInnerXml;
                using var writer = XmlWriter.Create(ExpectedConfigFilePath, s_xmlWriterSettings);
                doc.Save(writer);
            }

            return true;
        }

        private void WriteNewConfigFile(string bindingRedirects, bool assert)
        {
            if (bindingRedirects.Length == 0)
            {
                return;
            }
            if (assert)
            {
                throw new ApplicationException($"{ExpectedConfigFilePath} is expected to have some assembly binding redirects, but it is empty or does not exist.");
            }

            const string newConfigFileFormat = @"<?xml version=""1.0"" encoding=""utf-8""?>
<configuration>
  <runtime>
    <assemblyBinding xmlns=""urn:schemas-microsoft-com:asm.v1"">
{0}
    </assemblyBinding>
  </runtime>
</configuration>
";
            File.WriteAllText(ExpectedConfigFilePath, string.Format(newConfigFileFormat, bindingRedirects));
        }

        public override string ToString() => $"{AssemblyName} ({ProjectName} @ {Solution})";

        private static (XPathNavigator, XmlNamespaceManager) GetProjectXPathNavigator(string projectFile)
        {
            var doc = new XPathDocument(projectFile);
            var nav = doc.CreateNavigator();
            var nsmgr = new XmlNamespaceManager(nav.NameTable);
            nav.MoveToFollowing(XPathNodeType.Element);
            var ns = nav.GetNamespacesInScope(XmlNamespaceScope.Local).FirstOrDefault();
            nsmgr.AddNamespace("p", ns.Value);
            return (nav, nsmgr);
        }
        private static (XmlDocument, XmlNamespaceManager) GetProjectXmlDocument(string projectFile)
        {
            var doc = new XmlDocument
            {
                PreserveWhitespace = true
            };
            doc.Load(projectFile);
            var nsmgr = new XmlNamespaceManager(doc.NameTable);
            var ns = doc.DocumentElement.Attributes["xmlns"].Value;
            nsmgr.AddNamespace("p", ns);
            return (doc, nsmgr);
        }
    }
}
