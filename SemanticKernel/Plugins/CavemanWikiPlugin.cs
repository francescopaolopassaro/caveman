/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor) - Semantic Kernel Plugin
 * DESCRIPTION:
 * Semantic Kernel plugin exposing CavemanWiki functionality as an AI-callable function.
 * Allows LLMs to dynamically request project documentation as context during conversations.
 * 
 * USE CASE:
 * When an AI assistant needs to understand a codebase, it can invoke:
 *   GenerateProjectWiki(projectPath: "C:/MyApp")
 * And receive a compressed, AI-optimized markdown summary of the project.
 * 
 * TECHNOLOGY STACK:
 * - Microsoft.SemanticKernel (KernelFunction, Description attributes)
 * - CavemanWiki (project scanning & markdown generation)
 * - CavemanCompressionService (semantic text compression)
 * 
 * AUTHOR: [Francesco Paolo Passaro]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/

using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace caveman.core.SemanticKernel.Plugin
{
    /// <summary>
    /// <para><strong>CavemanWikiPlugin</strong> - Semantic Kernel plugin for on-demand project documentation.</para>
    /// <para>This plugin exposes the CavemanWiki generator as a kernel function that AI agents 
    /// can invoke to retrieve structured, compressed documentation of any software project.</para>
    /// <para><strong>Typical AI Workflow:</strong></para>
    /// <code>
    /// User: "Explain how authentication works in my project at C:\Apps\MyApi"
    /// AI: [invokes GenerateProjectWiki("C:\Apps\MyApi")]
    /// AI: [receives markdown context] → [answers using project-specific knowledge]
    /// </code>
    /// </summary>
    public class CavemanWikiPlugin
    {
        private readonly CavemanWiki _wiki;
        private readonly CavemanCompressionService? _compressionService;

        /// <summary>
        /// Initializes a new instance with custom CavemanWiki instance.
        /// </summary>
        /// <param name="wiki">Pre-configured CavemanWiki instance.</param>
        /// <param name="compressionService">Optional compression service for content optimization.</param>
        public CavemanWikiPlugin(CavemanWiki wiki, CavemanCompressionService? compressionService = null)
        {
            _wiki = wiki ?? throw new ArgumentNullException(nameof(wiki));
            _compressionService = compressionService;
        }

        /// <summary>
        /// Initializes a new instance with default internal instances.
        /// </summary>
        public CavemanWikiPlugin()
        {
            _wiki = new CavemanWiki();
            _compressionService = new CavemanCompressionService();
        }

        /// <summary>
        /// <para>Generates AI-optimized markdown documentation for a software project.</para>
        /// <para><strong>Use this function when:</strong></para>
        /// <list type="bullet">
        ///   <item>You need to understand the structure of a codebase</item>
        ///   <item>You want to answer questions about project dependencies</item>
        ///   <item>You need context about file organization and architecture</item>
        /// </list>
        /// <para><strong>Output format:</strong> Markdown string with YAML metadata, dependency list, 
        /// file tree, and compressed file contents optimized for LLM context windows.</para>
        /// </summary>
        /// <param name="projectPath">
        /// The absolute or relative path to the project folder to scan.
        /// Example: "C:\Users\Dev\MyProject" or "./src/backend"
        /// </param>
        /// <param name="maxFileSizeKB">
        /// Maximum file size to include, in kilobytes. Files larger than this are skipped.
        /// Default: 100 KB. Range: 10-1000 KB.
        /// </param>
        /// <param name="compressionLevel">
        /// Compression intensity for file contents:
        /// 0 = None (raw text), 
        /// 1 = Light (remove stopwords), 
        /// 2 = Semantic (preserve meaning, reduce tokens), 
        /// 3 = Aggressive (max compression, may lose detail).
        /// Default: 2 (Semantic).
        /// </param>
        /// <param name="includeContents">
        /// Whether to include actual file contents in the output.
        /// If false, returns only metadata, structure, and dependencies.
        /// Default: true.
        /// </param>
        /// <returns>
        /// Markdown string containing the project documentation, or an error message 
        /// prefixed with "[ERROR]" if the operation fails.
        /// </returns>
        /// <example>
        /// <code>
        /// // AI invocation example:
        /// var wiki = await kernel.InvokeAsync&lt;string&gt;(
        ///     pluginName: "CavemanWiki",
        ///     functionName: "GenerateProjectWiki",
        ///     new KernelArguments {
        ///         ["projectPath"] = "C:/MyApp",
        ///         ["compressionLevel"] = 2
        ///     });
        /// // wiki now contains the markdown documentation as a string
        /// </code>
        /// </example>
        [KernelFunction("generate_project_wiki")]
        [Description("Generates AI-optimized markdown documentation for a software project. Returns a markdown string with project structure, dependencies, and compressed file contents. Use when you need to understand a codebase or answer project-specific questions.")]
        public async Task<string> GenerateProjectWiki(
            [Description("The absolute or relative path to the project folder to scan. Example: 'C:\\Users\\Dev\\MyProject' or './src/backend'")]
            string projectPath,

            [Description("Maximum file size to include in KB (10-1000). Files larger are skipped. Default: 100")]
            int maxFileSizeKB = 100,

            [Description("Compression level: 0=None, 1=Light, 2=Semantic (recommended), 3=Aggressive. Default: 2")]
            int compressionLevel = 2,

            [Description("Whether to include file contents in output. If false, returns only metadata and structure. Default: true")]
            bool includeContents = true)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(projectPath))
                {
                    return "[ERROR] projectPath cannot be empty. Please provide a valid folder path.";
                }

                // Normalize path
                string fullPath = Path.GetFullPath(projectPath.Trim());

                if (!Directory.Exists(fullPath))
                {
                    return $"[ERROR] Directory not found: '{fullPath}'. Please verify the path exists and is accessible.";
                }

                // Validate parameters
                maxFileSizeKB = Math.Clamp(maxFileSizeKB, 10, 1000);
                compressionLevel = Math.Clamp(compressionLevel, 0, 3);

                // Convert to CavemanCompressionLevel enum
                var level = (CavemanCompressionLevel)compressionLevel;

                // If contents not requested, use None compression for speed
                var effectiveLevel = includeContents ? level : CavemanCompressionLevel.None;

                // Generate the wiki markdown
                string markdown = await _wiki.GenerateAsync(
                    projectFolderPath: fullPath,
                    maxFileSizeBytes: maxFileSizeKB * 1024L,
                    compressionLevel: effectiveLevel
                );

                // If contents excluded, strip the File Contents section
                if (!includeContents)
                {
                    markdown = RemoveFileContentsSection(markdown);
                }

                // Return the markdown as a string (ready for AI context injection)
                return markdown;
            }
            catch (UnauthorizedAccessException ex)
            {
                return $"[ERROR] Permission denied accessing '{projectPath}'. {ex.Message}";
            }
            catch (IOException ex)
            {
                return $"[ERROR] I/O error while scanning '{projectPath}'. {ex.Message}";
            }
            catch (Exception ex)
            {
                // Log internally if logging is configured, but return safe message to AI
                return $"[ERROR] Failed to generate wiki: {ex.Message}";
            }
        }

        /// <summary>
        /// <para>Quickly retrieves only project metadata and dependencies (no file contents).</para>
        /// <para><strong>Use this lightweight function when:</strong></para>
        /// <list type="bullet">
        ///   <item>You only need to know project type, name, or dependencies</item>
        ///   <item>You want to minimize token usage for simple questions</item>
        ///   <item>You're doing a quick project assessment before deeper analysis</item>
        /// </list>
        /// </summary>
        /// <param name="projectPath">The path to the project folder to scan.</param>
        /// <returns>Markdown string with only header, dependencies, and file tree (no file contents).</returns>
        [KernelFunction("get_project_summary")]
        [Description("Retrieves lightweight project metadata and dependencies without file contents. Returns a concise markdown string. Use for quick project assessment or when minimizing token usage.")]
        public async Task<string> GetProjectSummary(
            [Description("The path to the project folder to scan")]
            string projectPath)
        {
            // Delegate to main function with optimized parameters
            return await GenerateProjectWiki(
                projectPath: projectPath,
                maxFileSizeKB: 50,        // Smaller limit for speed
                compressionLevel: 2,      // Semantic for metadata
                includeContents: false    // Exclude heavy file contents
            );
        }

        /// <summary>
        /// <para>Checks if a path contains a recognizable software project.</para>
        /// <para><strong>Use this function to:</strong></para>
        /// <list type="bullet">
        ///   <item>Validate a path before invoking heavier wiki generation</item>
        ///   <item>Quickly detect project type (C#, Python, Node.js, etc.)</item>
        /// </list>
        /// </summary>
        /// <param name="projectPath">The path to check.</param>
        /// <returns>
        /// JSON-like string with detection result:
        /// {"isValid": true, "type": "CSharp", "name": "MyProject"}
        /// or {"isValid": false, "reason": "No project files found"}
        /// </returns>
        [KernelFunction("detect_project_type")]
        [Description("Checks if a path contains a recognizable software project and returns its type. Use to validate paths before generating full documentation.")]
        public async Task<string> DetectProjectType(
            [Description("The path to check for a software project")]
            string projectPath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(projectPath) || !Directory.Exists(projectPath))
                {
                    return "{\"isValid\": false, \"reason\": \"Invalid or inaccessible path\"}";
                }

                string fullPath = Path.GetFullPath(projectPath.Trim());

                // Quick scan for project indicators
                var indicators = new System.Collections.Generic.Dictionary<string, string>
                {
                    { "*.csproj", "CSharp" },
                    { "*.sln", "VisualStudio" },
                    { "requirements.txt", "Python" },
                    { "package.json", "NodeJs" },
                    { "pom.xml", "Java" },
                    { "Cargo.toml", "Rust" },
                    { "pyproject.toml", "Python" },
                    { "setup.py", "Python" }
                };

                foreach (var indicator in indicators)
                {
                    var files = Directory.EnumerateFiles(fullPath, indicator.Key, SearchOption.TopDirectoryOnly);
                    if (files.Any())
                    {
                        var projectName = Path.GetFileName(fullPath) ?? "Unknown";
                        return $"{{\"isValid\": true, \"type\": \"{indicator.Value}\", \"name\": \"{projectName}\"}}";
                    }
                }

                // Fallback: check for common source files
                var sourceFiles = Directory.EnumerateFiles(fullPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(f => new[] { ".cs", ".py", ".js", ".ts", ".java", ".rs" }
                        .Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .Take(1)
                    .ToList();

                if (sourceFiles.Any())
                {
                    var ext = Path.GetExtension(sourceFiles[0]).ToLowerInvariant();
                    var type = ext switch
                    {
                        ".cs" => "CSharp",
                        ".py" => "Python",
                        ".js" or ".ts" => "NodeJs",
                        ".java" => "Java",
                        ".rs" => "Rust",
                        _ => "Generic"
                    };
                    var projectName = Path.GetFileName(fullPath) ?? "Unknown";
                    return $"{{\"isValid\": true, \"type\": \"{type}\", \"name\": \"{projectName}\"}}";
                }

                return "{\"isValid\": false, \"reason\": \"No recognizable project files found\"}";
            }
            catch (Exception)
            {
                return "{\"isValid\": false, \"reason\": \"Error during detection\"}";
            }
        }

        /// <summary>
        /// Helper method to remove the File Contents section from markdown 
        /// when includeContents=false.
        /// </summary>
        private string RemoveFileContentsSection(string markdown)
        {
            // Find the "## 📄 File Contents" section and remove everything from there
            // until the next top-level header (##) or end of string
            var lines = markdown.Split('\n');
            var result = new System.Collections.Generic.List<string>();
            bool skipSection = false;

            foreach (var line in lines)
            {
                if (line.StartsWith("## 📄 File Contents"))
                {
                    skipSection = true;
                    continue;
                }

                if (skipSection && line.StartsWith("## ") && !line.StartsWith("## 📄"))
                {
                    // Reached next section, stop skipping
                    skipSection = false;
                }

                if (!skipSection)
                {
                    result.Add(line);
                }
            }

            return string.Join('\n', result);
        }
    }
}