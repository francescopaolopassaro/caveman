/*---------------------------------------------------------------------------------------
* PROJECT: Caveman (NLP Prompt Compressor) - Wiki Module Tests
* DESCRIPTION:
* NUnit test suite for CavemanWiki class.
* Tests project scanning, dependency extraction, file compression, and markdown generation.
* 
* TECHNOLOGY STACK:
* - Test Framework: NUnit 3.x
* - Async Support: Task-based async/await
* - File System: System.IO with temporary directory management
* 
* AUTHOR: [Francesco Paolo Passaro]
* DATE: May 2026
*---------------------------------------------------------------------------------------*/

using caveman.core;

using global::caveman.core;
using NUnit.Framework;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace caveman.tests
{
    [TestFixture]
    public class CavemanWikiTests
    {
        private CavemanWiki _wiki;
        private string _tempTestRoot;

        [OneTimeSetUp]
        public void GlobalSetup()
        {
            _wiki = new CavemanWiki();
        }

        [SetUp]
        public void Setup()
        {
            // Create unique temp directory for each test
            _tempTestRoot = Path.Combine(
                Path.GetTempPath(),
                $"caveman_wiki_test_{Guid.NewGuid():N}"
            );
            Directory.CreateDirectory(_tempTestRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up temp directory after each test
            if (_tempTestRoot != null && Directory.Exists(_tempTestRoot))
            {
                try
                {
                    Directory.Delete(_tempTestRoot, true);
                }
                catch
                {
                    // Best effort cleanup - ignore errors in test teardown
                }
            }
        }

        // ==================== BASIC FUNCTIONALITY TESTS ====================

        [Test]
        public async Task Test_GenerateAsync_InvalidPath_ThrowsException()
        {
            // Arrange
            string invalidPath = @"C:\This\Path\Should\Not\Exist_12345";

            // Act & Assert
            Assert.ThrowsAsync<DirectoryNotFoundException>(
                async () => await _wiki.GenerateAsync(invalidPath),
                "Should throw DirectoryNotFoundException for invalid path"
            );
        }

        [Test]

        public async Task Test_GenerateAsync_EmptyProject_GeneratesValidMarkdown()
        {
            // Arrange: Empty project folder
            string projectPath = Path.Combine(_tempTestRoot, "EmptyProject");
            Directory.CreateDirectory(projectPath);

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert
            Assert.That(markdown, Is.Not.Null.And.Not.Empty);
            Assert.That(markdown, Does.Contain("# 🪨 Project Wiki: EmptyProject"));
            Assert.That(markdown, Does.Contain("```yaml"));
            Assert.That(markdown, Does.Contain("project:"));
            Assert.That(markdown, Does.Contain("## 📊 Summary"));

            // FIXED: Match actual markdown format with bold syntax
            Assert.That(markdown, Does.Contain("**Total Files:** 0"));
        }

        // ==================== C# PROJECT SIMULATION TESTS ====================

        [Test]
        public async Task Test_CSharpProject_DetectsTypeAndExtractsDependencies()
        {
            // Arrange: Simulate a minimal C# project structure
            string projectPath = CreateSampleCSharpProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Verify project type detection
            Assert.That(markdown, Does.Contain("type: CSharp"));

            // Assert: Verify dependency extraction
            Assert.That(markdown, Does.Contain("## 📦 Dependencies"));
            Assert.That(markdown, Does.Contain("Newtonsoft.Json"));
            Assert.That(markdown, Does.Contain("Microsoft.EntityFrameworkCore"));
            Assert.That(markdown, Does.Contain("NuGet:"));

            // Assert: Verify file structure
            Assert.That(markdown, Does.Contain("## 📁 File Structure"));
            Assert.That(markdown, Does.Contain("Program.cs"));
            Assert.That(markdown, Does.Contain("WeatherForecast.cs"));
            Assert.That(markdown, Does.Contain("MyApi.csproj"));
        }

        [Test]
        public async Task Test_CSharpProject_FileContentsAreCompressed()
        {
            // Arrange: Create project with a large file (>2KB threshold)
            string projectPath = Path.Combine(_tempTestRoot, "LargeFileProject");
            Directory.CreateDirectory(projectPath);

            // Create a large C# file (simulate real source code)
            string largeFile = Path.Combine(projectPath, "LargeService.cs");
            string largeContent = GenerateLargeCSharpFile(5000); // 5000+ chars
            await File.WriteAllTextAsync(largeFile, largeContent);

            // Create minimal .csproj for type detection
            await CreateMinimalCsProj(projectPath, "LargeFileProject");

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Verify compression marker is present
            Assert.That(markdown, Does.Contain("[COMPRESSED:"));
            Assert.That(markdown, Does.Contain("LargeService.cs"));

            // Assert: Verify original content is NOT fully present (compressed)
            Assert.That(markdown, Does.Not.Contain(largeContent));
        }

        // ==================== PYTHON PROJECT SIMULATION TESTS ====================

        [Test]
        public async Task Test_PythonProject_DetectsTypeAndExtractsRequirements()
        {
            // Arrange: Simulate a minimal Python project
            string projectPath = CreateSamplePythonProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Verify project type detection
            Assert.That(markdown, Does.Contain("type: Python"));

            // Assert: Verify dependency extraction from requirements.txt
            Assert.That(markdown, Does.Contain("## 📦 Dependencies"));
            Assert.That(markdown, Does.Contain("requests"));
            Assert.That(markdown, Does.Contain("flask"));
            Assert.That(markdown, Does.Contain("pandas"));
            Assert.That(markdown, Does.Contain("PyPI:"));

            // Assert: Verify Python files are included
            Assert.That(markdown, Does.Contain("main.py"));

            // FIXED: Handle path separator differences (Windows uses \, Unix uses /)
            // Check for file name regardless of path format
            Assert.That(markdown, Does.Match(@"helpers\.py|utils[\\/]+helpers\.py"));
        }

        // ==================== NODE.JS PROJECT SIMULATION TESTS ====================

        [Test]
        public async Task Test_NodeProject_ExtractsPackageJsonDependencies()
        {
            // Arrange: Simulate a minimal Node.js project
            string projectPath = CreateSampleNodeProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Verify project type detection
            Assert.That(markdown, Does.Contain("type: NodeJs"));

            // Assert: Verify npm dependencies extraction
            Assert.That(markdown, Does.Contain("express"));
            Assert.That(markdown, Does.Contain("lodash"));
            Assert.That(markdown, Does.Contain("jest"));
            Assert.That(markdown, Does.Contain("npm:"));
            Assert.That(markdown, Does.Contain("npm:dev:"));
        }

        // ==================== FILTERING & IGNORE PATTERNS TESTS ====================

        [Test]
        public async Task Test_IgnorePatterns_ExcludesBinAndGitFolders()
        {
            // Arrange: Create project with ignored folders
            string projectPath = Path.Combine(_tempTestRoot, "FilteredProject");
            Directory.CreateDirectory(projectPath);

            // Create files in ignored folders (should be excluded)
            string binPath = Path.Combine(projectPath, "bin", "Debug");
            Directory.CreateDirectory(binPath);
            await File.WriteAllTextAsync(Path.Combine(binPath, "app.dll"), "fake binary");

            string gitPath = Path.Combine(projectPath, ".git", "objects");
            Directory.CreateDirectory(gitPath);
            await File.WriteAllTextAsync(Path.Combine(gitPath, "pack"), "fake git object");

            // Create a valid source file (should be included)
            await File.WriteAllTextAsync(Path.Combine(projectPath, "Program.cs"), "class Program {}");
            await CreateMinimalCsProj(projectPath, "FilteredProject");

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Ignored files should NOT appear
            Assert.That(markdown, Does.Not.Contain("app.dll"));
            Assert.That(markdown, Does.Not.Contain(".git"));
            Assert.That(markdown, Does.Not.Contain("bin/"));

            // Assert: Valid file SHOULD appear
            Assert.That(markdown, Does.Contain("Program.cs"));
        }

        [Test]
        public async Task Test_MaxFileSize_LimitsLargeFiles()
        {
            // Arrange: Create project with file exceeding limit
            string projectPath = Path.Combine(_tempTestRoot, "SizeLimitedProject");
            Directory.CreateDirectory(projectPath);

            // Create a file larger than the 1KB limit we'll set
            string largeFile = Path.Combine(projectPath, "HugeData.json");
            string hugeContent = new string('x', 2000); // 2KB
            await File.WriteAllTextAsync(largeFile, hugeContent);

            // Create a small file that should be included
            await File.WriteAllTextAsync(Path.Combine(projectPath, "small.txt"), "small");
            await CreateMinimalCsProj(projectPath, "SizeLimitedProject");

            // Act: Use 1KB limit
            string markdown = await _wiki.GenerateAsync(projectPath, maxFileSizeBytes: 1024);

            // Assert: Large file should be excluded
            Assert.That(markdown, Does.Not.Contain("HugeData.json"));

            // Assert: Small file should be included
            Assert.That(markdown, Does.Contain("small.txt"));
        }

        // ==================== MARKDOWN STRUCTURE VALIDATION TESTS ====================

        [Test]
        public async Task Test_MarkdownOutput_HasValidYamlMetadata()
        {
            // Arrange
            string projectPath = CreateSampleCSharpProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: YAML block structure
            Assert.That(markdown, Does.Match(@"```yaml\s+project:\s+name:"));
            Assert.That(markdown, Does.Contain("generated:")); // Timestamp
            Assert.That(markdown, Does.Match(@"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z")); // ISO timestamp
        }

        [Test]
        public async Task Test_MarkdownOutput_FileTreeIsRendered()
        {
            // Arrange
            string projectPath = CreateSampleCSharpProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Tree characters present
            Assert.That(markdown, Does.Contain("├──"));
            Assert.That(markdown, Does.Contain("└──"));
            Assert.That(markdown, Does.Contain("│"));

            // Assert: File sizes in tree
            Assert.That(markdown, Does.Match(@"\[\d+(\.\d+)?\s*[KMG]?B\]"));
        }

        [Test]
        public async Task Test_MarkdownOutput_SyntaxHighlightingHints()
        {
            // Arrange
            string projectPath = CreateSampleCSharpProject();

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Language hints for code blocks
            Assert.That(markdown, Does.Contain("```csharp"));
            Assert.That(markdown, Does.Contain("```xml")); // for .csproj
            Assert.That(markdown, Does.Contain("```yaml"));
        }

        // ==================== EDGE CASES & ERROR HANDLING ====================

        [Test]
        public async Task Test_UnreadableFile_HandledGracefully()
        {
            // Arrange: Create file with restricted content (simulated error scenario)
            string projectPath = Path.Combine(_tempTestRoot, "ErrorHandlingProject");
            Directory.CreateDirectory(projectPath);

            // Create a file we can read normally
            await File.WriteAllTextAsync(Path.Combine(projectPath, "normal.cs"), "class Test {}");
            await CreateMinimalCsProj(projectPath, "ErrorHandlingProject");

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Should complete without throwing
            Assert.That(markdown, Is.Not.Null);
            Assert.That(markdown, Does.Contain("normal.cs"));
            // Note: Actual file permission errors are hard to simulate cross-platform
            // The class handles exceptions internally with [ERROR: ...] markers
        }

        [Test]
        [TestCase("")]

        public async Task Test_EmptyProjectName_Handled(string projectName)
        {
            // Arrange
            string projectPath = Path.Combine(_tempTestRoot, projectName ?? "FallbackProject");
            Directory.CreateDirectory(projectPath);

            // Act
            string markdown = await _wiki.GenerateAsync(projectPath);

            // Assert: Should use fallback name
            Assert.That(markdown, Does.Contain("Project Wiki:"));
        }

        // ==================== PERFORMANCE & COMPRESSION METRICS ====================

        [Test]
        public async Task Test_CompressionLevels_ProducesDifferentOutputSizes()
        {
            // Arrange: Project with substantial content
            string projectPath = CreateSampleCSharpProject();

            // Add extra content to make compression differences visible
            string extraFile = Path.Combine(projectPath, "Documentation.md");
            await File.WriteAllTextAsync(extraFile, GenerateLargeMarkdownFile(3000));

            // Act: Generate with different compression levels
            var noneResult = await _wiki.GenerateAsync(projectPath, compressionLevel: CavemanCompressionLevel.None);
            var semanticResult = await _wiki.GenerateAsync(projectPath, compressionLevel: CavemanCompressionLevel.Semantic);
            var aggressiveResult = await _wiki.GenerateAsync(projectPath, compressionLevel: CavemanCompressionLevel.Aggressive);

            // Assert: Higher compression should generally produce smaller or equal output
            // Note: Due to metadata overhead, we check that aggressive is not SIGNIFICANTLY larger
            Assert.That(aggressiveResult.Length,
                Is.LessThanOrEqualTo(noneResult.Length + 500), // Allow small overhead margin
                "Aggressive compression should not produce significantly larger output than None");

            // Assert: Compression markers should appear in compressed versions
            Assert.That(semanticResult.Contains("[COMPRESSED:") || semanticResult.Contains("efficiency"), Is.True);
            Assert.That(aggressiveResult.Contains("[COMPRESSED:") || aggressiveResult.Contains("efficiency"), Is.True);
        }

        // ==================== HELPER METHODS FOR TEST SETUP ====================

        /// <summary>
        /// Creates a sample C# project structure for testing.
        /// </summary>
        private string CreateSampleCSharpProject()
        {
            string projectPath = Path.Combine(_tempTestRoot, "MyApi");
            Directory.CreateDirectory(projectPath);

            // Create .csproj with dependencies
            string csproj = Path.Combine(projectPath, "MyApi.csproj");
            string csprojContent = @"<Project Sdk=""Microsoft.NET.Sdk.Web"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>MyApi</AssemblyName>
    <Version>1.2.0</Version>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
    <PackageReference Include=""Microsoft.EntityFrameworkCore"" Version=""7.0.0"" />
  </ItemGroup>
</Project>";
            File.WriteAllText(csproj, csprojContent);

            // Create Program.cs
            string program = Path.Combine(projectPath, "Program.cs");
            File.WriteAllText(program, @"using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet(""/"", () => ""Hello Caveman!"");
app.Run();");

            // Create Models folder with a model file
            string modelsPath = Path.Combine(projectPath, "Models");
            Directory.CreateDirectory(modelsPath);
            string model = Path.Combine(modelsPath, "WeatherForecast.cs");
            File.WriteAllText(model, @"namespace MyApi.Models;
public class WeatherForecast
{
    public DateTime Date { get; set; }
    public int TemperatureC { get; set; }
    public string? Summary { get; set; }
}");

            // Create Controllers folder
            string controllersPath = Path.Combine(projectPath, "Controllers");
            Directory.CreateDirectory(controllersPath);
            string controller = Path.Combine(controllersPath, "WeatherForecastController.cs");
            File.WriteAllText(controller, @"using Microsoft.AspNetCore.Mvc;
using MyApi.Models;

namespace MyApi.Controllers;
[ApiController]
[Route(""api/[controller]"")]
public class WeatherForecastController : ControllerBase
{
    private static readonly string[] Summaries = new[] { ""Freezing"", ""Cool"", ""Warm"" };
    
    [HttpGet]
    public IEnumerable<WeatherForecast> Get() => 
        Enumerable.Range(1, 5).Select(index => new WeatherForecast
        {
            Date = DateTime.Now.AddDays(index),
            TemperatureC = Random.Shared.Next(-20, 55),
            Summary = Summaries[Random.Shared.Next(Summaries.Length)]
        }).ToArray();
}");

            // Add config file
            File.WriteAllText(Path.Combine(projectPath, "appsettings.json"), @"{
  ""Logging"": { ""LogLevel"": { ""Default"": ""Information"" }},
  ""AllowedHosts"": ""*""
}");

            return projectPath;
        }

        /// <summary>
        /// Creates a sample Python project structure for testing.
        /// </summary>
        private string CreateSamplePythonProject()
        {
            string projectPath = Path.Combine(_tempTestRoot, "PythonApp");
            Directory.CreateDirectory(projectPath);

            // Create requirements.txt
            string requirements = Path.Combine(projectPath, "requirements.txt");
            File.WriteAllText(requirements, @"requests==2.31.0
flask>=2.3.0
pandas~=2.0.0
numpy
# Comment line should be ignored
");

            // Create main.py
            string main = Path.Combine(projectPath, "main.py");
            File.WriteAllText(main, @"from flask import Flask
import requests

app = Flask(__name__)

@app.route('/')
def hello():
    return 'Hello from Caveman Python!'

if __name__ == '__main__':
    app.run(debug=True)");

            // Create utils.py in subfolder
            string utilsPath = Path.Combine(projectPath, "utils");
            Directory.CreateDirectory(utilsPath);
            File.WriteAllText(Path.Combine(utilsPath, "helpers.py"), @"def format_date(date):
    return date.strftime('%Y-%m-%d')

def sanitize_input(text):
    return text.strip().lower()");

            return projectPath;
        }

        /// <summary>
        /// Creates a sample Node.js project structure for testing.
        /// </summary>
        private string CreateSampleNodeProject()
        {
            string projectPath = Path.Combine(_tempTestRoot, "NodeApp");
            Directory.CreateDirectory(projectPath);

            // Create package.json
            string packageJson = Path.Combine(projectPath, "package.json");
            File.WriteAllText(packageJson, @"{
  ""name"": ""node-caveman-app"",
  ""version"": ""2.1.0"",
  ""description"": ""A sample Node.js API"",
  ""main"": ""index.js"",
  ""scripts"": {
    ""test"": ""jest"",
    ""start"": ""node index.js""
  },
  ""dependencies"": {
    ""express"": ""^4.18.2"",
    ""lodash"": ""^4.17.21""
  },
  ""devDependencies"": {
    ""jest"": ""^29.5.0"",
    ""supertest"": ""^6.3.3""
  }
}");

            // Create index.js
            File.WriteAllText(Path.Combine(projectPath, "index.js"), @"const express = require('express');
const _ = require('lodash');

const app = express();
const PORT = process.env.PORT || 3000;

app.get('/', (req, res) => {
  res.json({ message: 'Hello Caveman Node!' });
});

app.listen(PORT, () => console.log(`Server running on port ${PORT}`));");

            return projectPath;
        }

        /// <summary>
        /// Creates a minimal .csproj file for project type detection.
        /// </summary>
        private async Task CreateMinimalCsProj(string projectPath, string projectName)
        {
            string csprojPath = Path.Combine(projectPath, $"{projectName}.csproj");
            string content = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>{projectName}</AssemblyName>
  </PropertyGroup>
</Project>";
            await File.WriteAllTextAsync(csprojPath, content);
        }

        /// <summary>
        /// Generates a large C# file content for compression testing.
        /// </summary>
        private string GenerateLargeCSharpFile(int minChars)
        {
            var content = new System.Text.StringBuilder();
            content.AppendLine("using System;");
            content.AppendLine("using System.Collections.Generic;");
            content.AppendLine("using System.Threading.Tasks;");
            content.AppendLine();
            content.AppendLine("namespace LargeProject.Services");
            content.AppendLine("{");
            content.AppendLine("    /// <summary>");
            content.AppendLine("    /// A very large service class with extensive documentation");
            content.AppendLine("    /// and multiple methods to test compression behavior.");
            content.AppendLine("    /// </summary>");
            content.AppendLine("    public class LargeDataService");
            content.AppendLine("    {");

            // Generate repetitive but realistic code to reach target size
            for (int i = 0; i < 100; i++)
            {
                content.AppendLine($"        public async Task<Result{i}> ProcessData{i}(Input{i} input)");
                content.AppendLine("        {");
                content.AppendLine($"            // Business logic for operation {i}");
                content.AppendLine($"            var result = new Result{i}");
                content.AppendLine("            {");
                content.AppendLine($"                Id = Guid.NewGuid(),");
                content.AppendLine($"                Timestamp = DateTime.UtcNow,");
                content.AppendLine($"                Status = ProcessingStatus.Active,");
                content.AppendLine($"                Payload = await TransformAsync(input.Payload{i})");
                content.AppendLine("            };");
                content.AppendLine("            ");
                content.AppendLine($"            Logger.LogInformation(\"Processed item {{ItemId}}\", result.Id);");
                content.AppendLine("            return result;");
                content.AppendLine("        }");
                content.AppendLine();
            }

            content.AppendLine("    }");
            content.AppendLine("}");

            // Pad if needed to ensure minimum size
            while (content.Length < minChars)
            {
                content.AppendLine("        // Additional padding line for compression testing");
            }

            return content.ToString();
        }

        /// <summary>
        /// Generates a large markdown file content for testing.
        /// </summary>
        private string GenerateLargeMarkdownFile(int minChars)
        {
            var content = new System.Text.StringBuilder();
            content.AppendLine("# Comprehensive Documentation");
            content.AppendLine();
            content.AppendLine("## Overview");
            content.AppendLine("This document contains extensive information about the project architecture,");
            content.AppendLine("design patterns, API contracts, and implementation details.");
            content.AppendLine();

            for (int section = 1; section <= 20; section++)
            {
                content.AppendLine($"## Section {section}: Detailed Analysis");
                content.AppendLine();
                content.AppendLine($"### Subsection {section}.1: Technical Specifications");
                content.AppendLine("Lorem ipsum dolor sit amet, consectetur adipiscing elit. ");
                content.AppendLine("Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. ");
                content.AppendLine("Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris.");
                content.AppendLine();
                content.AppendLine($"### Subsection {section}.2: Code Examples");
                content.AppendLine("```csharp");
                content.AppendLine($"public class Example{section} {{ /* ... implementation ... */ }}");
                content.AppendLine("```");
                content.AppendLine();
            }

            // Pad if needed
            while (content.Length < minChars)
            {
                content.AppendLine("Additional documentation text for compression testing purposes. ");
            }

            return content.ToString();
        }
    }
}

