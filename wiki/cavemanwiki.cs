using caveman.core.entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace caveman.core
{
    /// <summary>
    /// <para><strong>Caveman.Wiki</strong> - Markdown documentation generator for software projects.</para>
    /// <para>This class recursively scans a project folder, extracts metadata, 
    /// file structure, and dependencies, and produces a compressed, AI-optimized 
    /// markdown document (LLM-friendly context).</para>
    /// <para><strong>Key Features:</strong></para>
    /// <list type="bullet">
    ///   <item>Recursive scanning with intelligent filters (.git, bin, obj, node_modules, etc.)</item>
    ///   <item>Automatic project type detection (C#, Python, Node.js, VS Solution)</item>
    ///   <item>Dependency extraction from .csproj, packages.config, requirements.txt, package.json, etc.</item>
    ///   <item>Semantic content compression via CavemanCompressionService</item>
    ///   <item>Structured, readable markdown output for both AI and developers</item>
    /// </list>
    /// <para><strong>Usage:</strong></para>
    /// <code>
    /// var wiki = new CavemanWiki();
    /// string markdown = await wiki.GenerateAsync(@"C:\MyProject");
    /// await File.WriteAllTextAsync("PROJECT_CONTEXT.md", markdown);
    /// </code>
    /// </summary>
    /// <remarks>
    /// Designed to be dependency-free (except caveman.core).
    /// All methods are async-ready to avoid blocking I/O on file system operations.
    /// </remarks>
    public class CavemanWiki
    {
        // File/folder patterns to ignore during scanning
        private static readonly string[] IgnorePatterns = {
            ".git", ".svn", ".hg", "node_modules", "bin", "obj", "dist", "build",
            ".vs", ".idea", "__pycache__", "*.dll", "*.exe", "*.pdb", "*.so", "*.dylib",
            "*.min.js", "*.map", "*.lock", "packages", ".nuget"
        };

        // File extensions to include by default
        private static readonly string[] IncludeExtensions = {
            ".cs", ".csproj", ".sln", ".vb", ".fs",
            ".py", ".txt", ".json", ".xml", ".config", ".yml", ".yaml",
            ".js", ".ts", ".jsx", ".tsx", ".html", ".css", ".scss",
            ".md", ".sh", ".bat", ".ps1", ".sql", ".proto", ".graphql"
        };

        private readonly CavemanCompressionService _compressor;
        private readonly StringBuilder _output;
        private int _fileCount;
        private long _totalSize;

        /// <summary>
        /// Initializes a new instance of the CavemanWiki class.
        /// </summary>
        public CavemanWiki()
        {
            _compressor = new CavemanCompressionService();
            _output = new StringBuilder();
            _fileCount = 0;
            _totalSize = 0;
        }

        /// <summary>
        /// Generates markdown documentation for the specified project.
        /// </summary>
        /// <param name="projectFolderPath">Path to the project folder to scan.</param>
        /// <param name="maxFileSizeBytes">Maximum file size to include (default: 100KB).</param>
        /// <param name="compressionLevel">Compression level for file contents.</param>
        /// <returns>Markdown string containing complete project documentation.</returns>
        public async Task<string> GenerateAsync(
            string projectFolderPath,
            long maxFileSizeBytes = 100 * 1024,
            CavemanCompressionLevel compressionLevel = CavemanCompressionLevel.Semantic)
        {
            if (!Directory.Exists(projectFolderPath))
                throw new DirectoryNotFoundException($"Directory not found: {projectFolderPath}");

            _output.Clear();
            _fileCount = 0;
            _totalSize = 0;

            var projectInfo = await AnalyzeProjectAsync(projectFolderPath);

            WriteHeader(projectFolderPath, projectInfo);
            WriteDependencies(projectInfo.Dependencies);
            WriteStructure(await ScanFilesAsync(projectFolderPath, maxFileSizeBytes, compressionLevel));
            WriteSummary();

            return _output.ToString();
        }

        /// <summary>
        /// Analyzes the project to detect type, name, and metadata.
        /// </summary>
        private async Task<ProjectInfo> AnalyzeProjectAsync(string rootPath)
        {
            var info = new ProjectInfo
            {
                Name = Path.GetFileName(rootPath) ?? "UnknownProject",
                RootPath = rootPath
            };

            // Detect project files to identify type
            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file).ToLowerInvariant();

                if (fileName.EndsWith(".sln"))
                {
                    info.Type = ProjectType.VisualStudio;
                    info.SolutionFile = file;
                    await ParseSolutionAsync(file, info);
                }
                else if (fileName.EndsWith(".csproj"))
                {
                    info.Type = ProjectType.CSharp;
                    await ParseCsProjAsync(file, info);
                }
                else if (fileName == "requirements.txt")
                {
                    info.Type = ProjectType.Python;
                    await ParseRequirementsAsync(file, info);
                }
                else if (fileName == "package.json")
                {
                    info.Type = ProjectType.NodeJs;
                    await ParsePackageJsonAsync(file, info);
                }
                else if (fileName == "pyproject.toml" || fileName == "setup.py")
                {
                    info.Type = ProjectType.Python;
                }
                else if (fileName == "pom.xml")
                {
                    info.Type = ProjectType.Java;
                }
                else if (fileName == "cargo.toml")
                {
                    info.Type = ProjectType.Rust;
                }
            }

            // Fallback: if not detected, analyze dominant extensions
            if (info.Type == ProjectType.Unknown)
                info.Type = DetectTypeByExtensions(rootPath);

            return info;
        }

        /// <summary>
        /// Recursively scans files, applies filters, and processes contents.
        /// </summary>
        private async Task<List<FileEntry>> ScanFilesAsync(
            string rootPath,
            long maxFileSize,
            CavemanCompressionLevel compressionLevel)
        {
            var files = new List<FileEntry>();

            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories))
            {
                // Skip ignored files/folders
                if (IgnorePatterns.Any(p =>
                    file.Contains(p, StringComparison.OrdinalIgnoreCase) ||
                    Path.GetFileName(file).Equals(p, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // Skip non-included extensions
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!IncludeExtensions.Contains(ext) && ext != string.Empty)
                    continue;

                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > maxFileSize)
                    continue; // Skip files that are too large

                var relativePath = Path.GetRelativePath(rootPath, file);
                var content = await ReadAndCompressAsync(file, maxFileSize, compressionLevel);

                _fileCount++;
                _totalSize += fileInfo.Length;

                files.Add(new FileEntry
                {
                    RelativePath = relativePath,
                    Size = fileInfo.Length,
                    Extension = ext,
                    CompressedContent = content,
                    LineCount = content?.Split('\n').Length ?? 0
                });
            }

            return files.OrderBy(f => f.RelativePath).ToList();
        }

        /// <summary>
        /// Reads the file and applies semantic compression if needed.
        /// </summary>
        /// <summary>
        /// Reads the file and applies semantic compression if needed.
        /// </summary>
        private async Task<string> ReadAndCompressAsync(
            string filePath,
            long maxSize,
            CavemanCompressionLevel level)
        {
            try
            {
                // For small files, return raw content
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length < 2048) // < 2KB
                    return await File.ReadAllTextAsync(filePath, Encoding.UTF8);

                // For medium/large files, use compression
                var content = await File.ReadAllTextAsync(filePath, Encoding.UTF8);

                // Compress only if above threshold
                if (content.Length > 1000)
                {
                    var result = await _compressor.CompressAsync(content, level);

                    // FIXED: Use correct CompressionResult properties (tokens, not CompressedLength)
                    return $"[COMPRESSED:{result.CompressedTokens}/{result.OriginalTokens} tokens, {result.EfficiencyPercentage:F1}% efficiency]\n{result.CompressedText}";
                }

                return content;
            }
            catch (Exception ex)
            {
                return $"[ERROR: Unable to read file - {ex.Message}]";
            }
        }

        /// <summary>
        /// Writes the markdown header with project metadata.
        /// </summary>
        private void WriteHeader(string rootPath, ProjectInfo info)
        {
            _output.AppendLine($"# 🪨 Project Wiki: {info.Name}");
            _output.AppendLine();
            _output.AppendLine($"```yaml");
            _output.AppendLine($"project:");
            _output.AppendLine($"  name: {info.Name}");
            _output.AppendLine($"  type: {info.Type}");
            _output.AppendLine($"  path: {rootPath}");
            _output.AppendLine($"  generated: {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
            if (!string.IsNullOrEmpty(info.Description))
                _output.AppendLine($"  description: {info.Description}");
            if (!string.IsNullOrEmpty(info.Version))
                _output.AppendLine($"  version: {info.Version}");
            _output.AppendLine($"```");
            _output.AppendLine();
        }

        /// <summary>
        /// Writes the dependencies section.
        /// </summary>
        private void WriteDependencies(List<Dependency> dependencies)
        {
            if (dependencies == null || !dependencies.Any())
                return;

            _output.AppendLine("## 📦 Dependencies");
            _output.AppendLine();
            _output.AppendLine("```yaml");
            _output.AppendLine("dependencies:");

            foreach (var dep in dependencies.GroupBy(d => d.Source))
            {
                _output.AppendLine($"  {dep.Key}:");
                foreach (var d in dep)
                {
                    var version = string.IsNullOrEmpty(d.Version) ? "" : $" @ {d.Version}";
                    _output.AppendLine($"    - {d.Name}{version}");
                }
            }
            _output.AppendLine("```");
            _output.AppendLine();
        }

        /// <summary>
        /// Writes the file structure with compressed contents.
        /// </summary>
        private void WriteStructure(List<FileEntry> files)
        {
            _output.AppendLine("## 📁 File Structure");
            _output.AppendLine();

            // Tree view of structure
            var tree = BuildTree(files);
            _output.AppendLine("```");
            _output.AppendLine(tree);
            _output.AppendLine("```");
            _output.AppendLine();

            // File contents
            _output.AppendLine("## 📄 File Contents");
            _output.AppendLine();

            foreach (var file in files)
            {
                _output.AppendLine($"### `{file.RelativePath}`");
                _output.AppendLine($"*Extension:* `{file.Extension}` | *Size:* {FormatSize(file.Size)} | *Lines:* {file.LineCount}");
                _output.AppendLine();
                _output.AppendLine("```" + GetLanguageHint(file.Extension));

                // Truncate very long contents in markdown
                var content = file.CompressedContent;
                if (content != null && content.Length > 5000)
                {
                    _output.AppendLine(content.Substring(0, 5000));
                    _output.AppendLine($"// [... compressed content - see COMPRESSED tag ...]");
                }
                else
                {
                    _output.AppendLine(content ?? "// [EMPTY]");
                }
                _output.AppendLine("```");
                _output.AppendLine();
            }
        }

        /// <summary>
        /// Writes the statistical summary.
        /// </summary>
        private void WriteSummary()
        {
            _output.AppendLine("## 📊 Summary");
            _output.AppendLine();
            _output.AppendLine($"- **Total Files:** {_fileCount}");
            _output.AppendLine($"- **Total Size:** {FormatSize(_totalSize)}");
            _output.AppendLine($"- **Compression:** Enabled (CavemanCompressionService)");
            _output.AppendLine();
            _output.AppendLine("> 💡 *This document is optimized for AI context. File contents are semantically compressed to maximize useful information while keeping token count low.*");
        }

        /// <summary>
        /// Builds a tree representation of the file structure.
        /// </summary>
        private string BuildTree(List<FileEntry> files)
        {
            var root = new TreeNode("root");

            foreach (var file in files)
            {
                var parts = file.RelativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var current = root;

                for (int i = 0; i < parts.Length; i++)
                {
                    var part = parts[i];
                    if (!current.Children.ContainsKey(part))
                        current.Children[part] = new TreeNode(part);
                    current = current.Children[part];
                }
                current.IsFile = true;
                current.Size = file.Size;
            }

            return RenderTree(root, "", true);
        }

        private string RenderTree(TreeNode node, string prefix, bool isLast)
        {
            var sb = new StringBuilder();

            if (node.Name != "root")
            {
                sb.Append(prefix);
                sb.Append(isLast ? "└── " : "├── ");
                sb.Append(node.Name);
                if (node.IsFile)
                    sb.Append($" [{FormatSize(node.Size)}]");
                sb.AppendLine();
            }

            var children = node.Children.Values.ToList();
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var newPrefix = prefix + (isLast ? "    " : "│   ");
                sb.Append(RenderTree(child, newPrefix, i == children.Count - 1));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats size in human-readable format.
        /// </summary>
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;

            while (size >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                size /= 1024;
            }

            return $"{size:0.##} {suffixes[order]}";
        }

        /// <summary>
        /// Returns the language hint for markdown syntax highlighting.
        /// </summary>
        private string GetLanguageHint(string extension)
        {
            return extension switch
            {
                ".cs" => "csharp",
                ".vb" => "vbnet",
                ".fs" => "fsharp",
                ".py" => "python",
                ".js" => "javascript",
                ".ts" => "typescript",
                ".jsx" => "jsx",
                ".tsx" => "tsx",
                ".json" => "json",
                ".xml" => "xml",
                ".yml" or ".yaml" => "yaml",
                ".html" => "html",
                ".css" => "css",
                ".scss" => "scss",
                ".sql" => "sql",
                ".sh" => "bash",
                ".bat" or ".ps1" => "powershell",
                ".md" => "markdown",
                ".csproj" or ".sln" => "xml",
                _ => ""
            };
        }

        /// <summary>
        /// Detects project type by analyzing dominant file extensions.
        /// </summary>
        private ProjectType DetectTypeByExtensions(string rootPath)
        {
            var extCount = new Dictionary<string, int>();

            foreach (var file in Directory.EnumerateFiles(rootPath, "*.*", SearchOption.AllDirectories)
                .Take(100)) // Limit for performance
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (!string.IsNullOrEmpty(ext))
                    extCount[ext] = extCount.GetValueOrDefault(ext, 0) + 1;
            }

            if (extCount.ContainsKey(".cs") || extCount.ContainsKey(".csproj"))
                return ProjectType.CSharp;
            if (extCount.ContainsKey(".py"))
                return ProjectType.Python;
            if (extCount.ContainsKey(".js") || extCount.ContainsKey(".ts"))
                return ProjectType.NodeJs;
            if (extCount.ContainsKey(".java"))
                return ProjectType.Java;
            if (extCount.ContainsKey(".rs"))
                return ProjectType.Rust;

            return ProjectType.Generic;
        }

        // ==================== SPECIFIC PARSERS ====================

        private async Task ParseSolutionAsync(string solutionPath, ProjectInfo info)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(solutionPath);
                foreach (var line in lines)
                {
                    if (line.Trim().StartsWith("Project(\"{"))
                    {
                        // Extract project path from Solution line
                        var match = Regex.Match(line, @"=\s*""([^""]+)""\s*,\s*""([^""]+)""");
                        if (match.Success)
                        {
                            info.Projects.Add(match.Groups[1].Value);
                        }
                    }
                }
            }
            catch { /* Ignore parsing errors */ }
        }

        private async Task ParseCsProjAsync(string csprojPath, ProjectInfo info)
        {
            try
            {
                var xml = await File.ReadAllTextAsync(csprojPath);
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                // MSBuild namespace
                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("msb", "http://schemas.microsoft.com/developer/msbuild/2003");

                // Extract PackageReference
                var pkgRefs = doc.SelectNodes("//msb:PackageReference | //PackageReference", nsmgr);
                if (pkgRefs != null)
                {
                    foreach (XmlNode node in pkgRefs)
                    {
                        var name = node.Attributes?["Include"]?.Value ?? node.Attributes?["Update"]?.Value;
                        var version = node.Attributes?["Version"]?.Value;
                        if (!string.IsNullOrEmpty(name))
                            info.Dependencies.Add(new Dependency { Name = name, Version = version, Source = "NuGet" });
                    }
                }

                // Extract ProjectReference
                var projRefs = doc.SelectNodes("//msb:ProjectReference | //ProjectReference", nsmgr);
                if (projRefs != null)
                {
                    foreach (XmlNode node in projRefs)
                    {
                        var include = node.Attributes?["Include"]?.Value;
                        if (!string.IsNullOrEmpty(include))
                            info.Dependencies.Add(new Dependency { Name = Path.GetFileNameWithoutExtension(include), Source = "ProjectReference" });
                    }
                }

                // Metadata
                var nameNode = doc.SelectSingleNode("//msb:AssemblyName | //AssemblyName", nsmgr);
                if (nameNode != null) info.Name = nameNode.InnerText;

                var versionNode = doc.SelectSingleNode("//msb:Version | //Version", nsmgr);
                if (versionNode != null) info.Version = versionNode.InnerText;
            }
            catch { /* Ignore XML errors */ }
        }

        private async Task ParseRequirementsAsync(string reqPath, ProjectInfo info)
        {
            try
            {
                var lines = await File.ReadAllLinesAsync(reqPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        continue;

                    // Simple parsing: package==version or package>=version
                    var match = Regex.Match(trimmed, @"^([a-zA-Z0-9_-]+)\s*([=<>!]+)\s*([\d.]+)");
                    if (match.Success)
                    {
                        info.Dependencies.Add(new Dependency
                        {
                            Name = match.Groups[1].Value,
                            Version = match.Groups[3].Value,
                            Source = "PyPI"
                        });
                    }
                    else
                    {
                        info.Dependencies.Add(new Dependency { Name = trimmed, Source = "PyPI" });
                    }
                }
            }
            catch { /* Ignore errors */ }
        }

        private async Task ParsePackageJsonAsync(string pkgPath, ProjectInfo info)
        {
            try
            {
                var json = await File.ReadAllTextAsync(pkgPath);
                // Minimal manual JSON parsing (no external libraries)
                var nameMatch = Regex.Match(json, @"""name""\s*:\s*""([^""]+)""");
                if (nameMatch.Success) info.Name = nameMatch.Groups[1].Value;

                var versionMatch = Regex.Match(json, @"""version""\s*:\s*""([^""]+)""");
                if (versionMatch.Success) info.Version = versionMatch.Groups[1].Value;

                var descMatch = Regex.Match(json, @"""description""\s*:\s*""([^""]+)""");
                if (descMatch.Success) info.Description = descMatch.Groups[1].Value;

                // Extract dependencies and devDependencies
                foreach (var section in new[] { "dependencies", "devDependencies" })
                {
                    var sectionMatch = Regex.Match(json, $@"""{section}""\s*:\s*\{{([^}}]+)\}}");
                    if (sectionMatch.Success)
                    {
                        var deps = sectionMatch.Groups[1].Value;
                        var pkgMatches = Regex.Matches(deps, @"""([^""]+)""\s*:\s*""([^""]+)""");
                        foreach (Match m in pkgMatches)
                        {
                            info.Dependencies.Add(new Dependency
                            {
                                Name = m.Groups[1].Value,
                                Version = m.Groups[2].Value,
                                Source = section == "devDependencies" ? "npm:dev" : "npm"
                            });
                        }
                    }
                }
            }
            catch { /* Ignore errors */ }
        }

        // ==================== INTERNAL SUPPORT CLASSES ====================

        private class ProjectInfo
        {
            public string Name { get; set; } = "Unknown";
            public string RootPath { get; set; }
            public ProjectType Type { get; set; } = ProjectType.Unknown;
            public string Description { get; set; }
            public string Version { get; set; }
            public string SolutionFile { get; set; }
            public List<string> Projects { get; } = new();
            public List<Dependency> Dependencies { get; } = new();
        }

        private class Dependency
        {
            public string Name { get; set; }
            public string Version { get; set; }
            public string Source { get; set; } // NuGet, PyPI, npm, ProjectReference, etc.
        }

        private class FileEntry
        {
            public string RelativePath { get; set; }
            public long Size { get; set; }
            public string Extension { get; set; }
            public string CompressedContent { get; set; }
            public int LineCount { get; set; }
        }

        private class TreeNode
        {
            public string Name { get; }
            public bool IsFile { get; set; }
            public long Size { get; set; }
            public Dictionary<string, TreeNode> Children { get; } = new();

            public TreeNode(string name) => Name = name;
        }

        private enum ProjectType
        {
            Unknown, Generic, CSharp, Python, NodeJs, Java, Rust, VisualStudio
        }
    }
}
