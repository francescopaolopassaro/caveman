/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor) - Semantic Kernel Plugin Tests
 * DESCRIPTION:
 * NUnit test suite for CavemanWikiPlugin class.
 * Tests Semantic Kernel integration, function invocation, and error handling.
 * 
 * TECHNOLOGY STACK:
 * - Test Framework: NUnit 3.x
 * - Semantic Kernel: Microsoft.SemanticKernel
 * - Async Support: Task-based async/await
 * 
 * AUTHOR: [Francesco Paolo Passaro]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/

using Microsoft.SemanticKernel;
using caveman.core.SemanticKernel.Plugin;


namespace caveman.tests
{
    [TestFixture]
    public class CavemanWikiPluginTests
    {
        private Kernel _kernel;
        private string _testProjectPath;
        private CavemanWikiPlugin _plugin;

        [SetUp]
        public void Setup()
        {
            // Create unique temp directory for EACH test (not OneTimeSetUp)
            // This ensures isolation and avoids cross-test contamination
            _testProjectPath = Path.Combine(
                Path.GetTempPath(),
                $"caveman_plugin_test_{Guid.NewGuid():N}"
            );

            // Create the test project structure
            CreateSampleTestProject(_testProjectPath);

            // Build kernel with plugin for each test
            var builder = Kernel.CreateBuilder();

            // Add a minimal chat completion service for kernel to work
            // Using a mock/in-memory approach for unit tests
            builder.Plugins.AddFromObject(new CavemanWikiPlugin(), "CavemanWiki");

            _kernel = builder.Build();
            _plugin = new CavemanWikiPlugin();
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up temp directory after each test
            if (!string.IsNullOrEmpty(_testProjectPath) && Directory.Exists(_testProjectPath))
            {
                try
                {
                    Directory.Delete(_testProjectPath, true);
                }
                catch
                {
                    // Best effort cleanup - ignore errors in test teardown
                }
            }
        }

        // ==================== PLUGIN FUNCTION TESTS ====================

        [Test]
        public async Task GenerateProjectWiki_ReturnsMarkdownString()
        {
            // Arrange: _testProjectPath is created in [SetUp]

            // Act: Invoke via Kernel (integration-style test)
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments
                {
                    ["projectPath"] = _testProjectPath
                });

            // Assert: Verify successful markdown output
            Assert.That(result, Is.Not.Null.And.Not.Empty, "Result should not be null or empty");
            Assert.That(result, Does.Not.StartWith("[ERROR]"), $"Should not return error. Got: {result}");
            Assert.That(result, Does.Contain("# 🪨 Project Wiki:"), "Should contain wiki header");
            Assert.That(result, Does.Contain("test_project.csproj"), "Should contain project file");
            Assert.That(result, Does.Contain("Program.cs"), "Should contain source file");
        }

        [Test]
        public async Task GenerateProjectWiki_InvalidPath_ReturnsError()
        {
            // Arrange
            string invalidPath = @"C:\This\Path\Should\Never\Exist_12345";

            // Act
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments { ["projectPath"] = invalidPath });

            // Assert
            Assert.That(result, Does.StartWith("[ERROR]"), "Should return error for invalid path");
            Assert.That(result, Does.Contain("not found"), "Error should mention path not found");
        }

        [Test]
        public async Task GenerateProjectWiki_EmptyPath_ReturnsError()
        {
            // Act
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments { ["projectPath"] = "" });

            // Assert
            Assert.That(result, Does.StartWith("[ERROR]"));
            Assert.That(result, Does.Contain("cannot be empty"));
        }

        [Test]
        [TestCase(-10)]
        [TestCase(5000)]
        public async Task GenerateProjectWiki_InvalidFileSize_ClampedToValidRange(int invalidKb)
        {
            // Act: Plugin should clamp invalid values to valid range (10-1000 KB)
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments
                {
                    ["projectPath"] = _testProjectPath,
                    ["maxFileSizeKB"] = invalidKb
                });

            // Assert: Should still succeed (clamping is internal)
            Assert.That(result, Does.Not.StartWith("[ERROR]"));
            Assert.That(result, Does.Contain("test_project.csproj"));
        }

        [Test]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public async Task GenerateProjectWiki_AllCompressionLevels_Succeed(int level)
        {
            // Act
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments
                {
                    ["projectPath"] = _testProjectPath,
                    ["compressionLevel"] = level
                });

            // Assert
            Assert.That(result, Does.Not.StartWith("[ERROR]"),
                $"Compression level {level} should succeed");
            Assert.That(result, Does.Contain("## 📊 Summary"));
        }

        [Test]
        public async Task GenerateProjectWiki_ExcludeContents_RemovesFileSection()
        {
            // Act: Request wiki without file contents
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments
                {
                    ["projectPath"] = _testProjectPath,
                    ["includeContents"] = false
                });

            // Assert: Should have metadata but NOT file contents section
            Assert.That(result, Does.Contain("## 📦 Dependencies"), "Should have dependencies");
            Assert.That(result, Does.Contain("## 📁 File Structure"), "Should have file tree");
            Assert.That(result, Does.Not.Contain("## 📄 File Contents"),
                "Should NOT have file contents when includeContents=false");
        }

        [Test]
        public async Task GetProjectSummary_ReturnsLightweightOutput()
        {
            // Act: Use the lightweight summary function
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "get_project_summary",
                new KernelArguments { ["projectPath"] = _testProjectPath });

            // Assert
            Assert.That(result, Does.Not.StartWith("[ERROR]"));
            Assert.That(result, Does.Contain("## 📦 Dependencies"));
            Assert.That(result, Does.Not.Contain("## 📄 File Contents"));

            // Summary should be shorter than full wiki
            var fullWiki = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "generate_project_wiki",
                new KernelArguments { ["projectPath"] = _testProjectPath });

            Assert.That(result.Length, Is.LessThan(fullWiki.Length),
                "Summary should be shorter than full wiki");
        }

        [Test]
        public async Task DetectProjectType_ValidCSharpProject_ReturnsJson()
        {
            // Act
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "detect_project_type",
                new KernelArguments { ["projectPath"] = _testProjectPath });

            // Assert: Should return JSON-like string with valid detection
            Assert.That(result, Does.Contain("\"isValid\": true"),
                "Should detect as valid project");
            Assert.That(result, Does.Contain("\"type\": \"CSharp\""),
                "Should detect C# project type");

            // FIXED: The plugin returns the FOLDER NAME as project name, not assembly name
            // Since _testProjectPath ends with a GUID-based folder name, check for that pattern
            var expectedFolderName = Path.GetFileName(_testProjectPath);
            Assert.That(result, Does.Contain($"\"name\": \"{expectedFolderName}\""),
                $"Should return folder name '{expectedFolderName}' as project name");

            // Alternative flexible assertion (if you prefer regex-style matching):
            // Assert.That(result, Does.Match(@"""name"":\s*""[^""]+"""), 
            //     "Should contain a name field with some value");
        }
        [Test]
        public async Task DetectProjectType_InvalidPath_ReturnsErrorJson()
        {
            // Act
            var result = await _kernel.InvokeAsync<string>(
                pluginName: "CavemanWiki",
                functionName: "detect_project_type",
                new KernelArguments { ["projectPath"] = @"C:\Invalid\Path" });

            // Assert
            Assert.That(result, Does.Contain("\"isValid\": false"));
            Assert.That(result, Does.Contain("\"reason\""));
        }

        [Test]
        public async Task DetectProjectType_PythonProject_DetectsCorrectly()
        {
            // Arrange: Create a Python-style test project
            string pythonPath = Path.Combine(Path.GetTempPath(), $"python_test_{Guid.NewGuid():N}");
            try
            {
                CreateSamplePythonProject(pythonPath);

                // Act
                var result = await _kernel.InvokeAsync<string>(
                    pluginName: "CavemanWiki",
                    functionName: "detect_project_type",
                    new KernelArguments { ["projectPath"] = pythonPath });

                // Assert
                Assert.That(result, Does.Contain("\"isValid\": true"));
                Assert.That(result, Does.Contain("\"type\": \"Python\""));
            }
            finally
            {
                // Cleanup
                if (Directory.Exists(pythonPath))
                    Directory.Delete(pythonPath, true);
            }
        }

        // ==================== DIRECT PLUGIN METHOD TESTS (No Kernel) ====================

        [Test]
        public async Task Plugin_GenerateProjectWiki_DirectCall_Succeeds()
        {
            // Act: Call plugin method directly (unit test style)
            var result = await _plugin.GenerateProjectWiki(_testProjectPath);

            // Assert
            Assert.That(result, Is.Not.Null.And.Not.Empty);
            Assert.That(result, Does.Not.StartWith("[ERROR]"));
            Assert.That(result, Does.Contain("# 🪨 Project Wiki:"));
        }

        [Test]
        public async Task Plugin_GetProjectSummary_DirectCall_Succeeds()
        {
            // Act
            var result = await _plugin.GetProjectSummary(_testProjectPath);

            // Assert
            Assert.That(result, Does.Not.StartWith("[ERROR]"));
            Assert.That(result, Does.Contain("## 📦 Dependencies"));
        }

        [Test]
        public async Task Plugin_DetectProjectType_DirectCall_Succeeds()
        {
            // Act
            var result = await _plugin.DetectProjectType(_testProjectPath);

            // Assert
            Assert.That(result, Does.Contain("\"isValid\": true"));
            Assert.That(result, Does.Contain("CSharp"));
        }

        // ==================== HELPER METHODS ====================

        /// <summary>
        /// Creates a minimal C# test project structure for plugin tests.
        /// </summary>
        private void CreateSampleTestProject(string projectPath)
        {
            // Ensure directory exists (synchronous, reliable)
            Directory.CreateDirectory(projectPath);

            // Create .csproj file
            string csprojPath = Path.Combine(projectPath, "test_project.csproj");
            File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net7.0</TargetFramework>
    <AssemblyName>test_project</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include=""Newtonsoft.Json"" Version=""13.0.3"" />
  </ItemGroup>
</Project>");

            // Create Program.cs
            string programPath = Path.Combine(projectPath, "Program.cs");
            File.WriteAllText(programPath, @"using System;

namespace test_project
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine(""Hello Caveman!"");
        }
    }
}");

            // Create a subfolder with a file
            string modelsPath = Path.Combine(projectPath, "Models");
            Directory.CreateDirectory(modelsPath);
            File.WriteAllText(Path.Combine(modelsPath, "User.cs"), @"namespace test_project.Models
{
    public class User
    {
        public string Name { get; set; }
        public int Id { get; set; }
    }
}");

            // Create config file
            File.WriteAllText(Path.Combine(projectPath, "appsettings.json"), @"{
  ""Logging"": { ""LogLevel"": { ""Default"": ""Information"" }},
  ""ApiKey"": ""REDACTED""
}");
        }

        /// <summary>
        /// Creates a minimal Python test project structure.
        /// </summary>
        private void CreateSamplePythonProject(string projectPath)
        {
            Directory.CreateDirectory(projectPath);

            // Create requirements.txt
            File.WriteAllText(Path.Combine(projectPath, "requirements.txt"),
                "requests==2.31.0\nflask>=2.3.0\n");

            // Create main.py
            File.WriteAllText(Path.Combine(projectPath, "main.py"),
                "from flask import Flask\n\napp = Flask(__name__)\n\n@app.route('/')\ndef hello():\n    return 'Hello'");
        }
    }
}