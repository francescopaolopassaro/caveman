/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor)
 * DESCRIPTION:
 * This system implements NLP-based "Prompt Contraction" logic.
 * The core objective is to drastically reduce the token count sent to LLMs 
 * (such as Gemma 3, Llama 3, or GPT-4) by selectively stripping low-semantic 
 * value grammatical elements (Stopwords, Determiners, Conjunctions).
 * 
 * THE "CAVEMAN" PRINCIPLE:
 * The guiding philosophy is to transform complex natural language into an essential, 
 * high-density format that preserves the original intent. 
 * (e.g., "I would like to order a pepperoni pizza" -> "Order pepperoni pizza").
 * 
 * BENEFITS:
 * 1. Reduced Inference Latency: Faster response times from local or cloud models.
 * 2. API Cost Optimization: Significant savings for token-based billing.
 * 3. Context Window Efficiency: Allows more information to fit within the model's memory.
 * 
 * TECHNOLOGY STACK:
 * - Core: Catalyst NLP (Universal Dependencies Standard)
 * - Methodology: POS (Part-of-Speech) filtering and Lemmatization.
 * 
 * AUTHOR: [Passaro Francesco Paolo]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/

using caveman.core;
using caveman.core.entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace caveman
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("**********************************************************");
            Console.WriteLine("*  Caveman v.1.0.2 - Eco-Friendly Prompt Optimizer        *");
            Console.WriteLine("*  Porting by Francesco Paolo Passaro                    *");
            Console.WriteLine("**********************************************************");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("License: Open Source (Non-Commercial). Green Metrics Enabled.");
            Console.WriteLine();
            Console.WriteLine(@"
               /       \
              |  -   -  |
              | (o) (o) |
              |    V    |
             / \  ---  / \
            /   |     |   \      ______
           /    |     |    \    /      \
          |     |     |     |  |  CLAVA |
          |     |     |     |   \______/
          |     |_____|     |      ||
          |     |     |     |      ||
          |    /       \    |_____||
          |   /         \   |-----'
          |  |           |  |
          |  |           |  |
          |__|           |__|
           ||             ||
           ||             ||
          /_ \           /_ \
         ");

            // Initialize the compression service
            var compressor = new CavemanCompressionService();

            var testCases = new List<(string Lang, string Text)>
            {
                ("Italiano", "Buongiorno, vorrei gentilmente sapere se fosse possibile ricevere alcune informazioni riguardo ai migliori ristoranti economici che si trovano a Roma, preferibilmente vicino alla stazione Termini."),
                ("English", "Hello there, I would really like to know if you could kindly provide me with some information regarding the best cheap restaurants located in London, specifically near Victoria Station.")
            };

            Console.WriteLine("=== PROMPT COMPRESSION & ECO-TEST ===\n");

            foreach (var test in testCases)
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine($"--- LANGUAGE: {test.Lang} ---");
                Console.WriteLine($"Original ({test.Text.Length} ch): \"{test.Text}\"\n");

                foreach (CavemanCompressionLevel level in Enum.GetValues(typeof(CavemanCompressionLevel)))
                {
                    var startTime = DateTime.Now;

                    CompressionResult result = await compressor.CompressAsync(test.Text, level);

                    var duration = DateTime.Now - startTime;

                    Console.ForegroundColor = GetColorForLevel(level);
                    Console.WriteLine($"[{level}] (Efficiency: {result.EfficiencyPercentage:F1}%)");
                    Console.ResetColor();

                    Console.WriteLine($"> {result.CompressedText}");

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"Details: {result.CompressedTokens}/{result.OriginalTokens} tokens | ");
                    Console.Write($"{duration.TotalMilliseconds:F0}ms | ");

                    if (level != CavemanCompressionLevel.None)
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"🌿 Saved: {result.EstimatedEnergySavedMWh:F3} mWh (~{result.EstimatedCO2SavedMg:F2} mg CO2)");
                    }
                    else
                    {
                        Console.WriteLine();
                    }

                    Console.ResetColor();
                    Console.WriteLine();
                }
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine(new string('=', 65) + "\n");
            }

            // ==================== WIKI FEATURE TEST PROMPT ====================

            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("=== 🪨 CAVEMAN.WIKI - PROJECT DOCUMENTATION GENERATOR ===");
            Console.ResetColor();
            Console.WriteLine("Do you want to test the Wiki functionality? (Y/N): ");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("> ");

            var wikiChoice = Console.ReadLine()?.Trim().ToUpperInvariant();

            if (wikiChoice == "Y" || wikiChoice == "YES")
            {
                await RunWikiTestAsync(compressor);
            }
            else if (wikiChoice == "N" || wikiChoice == "NO")
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("Wiki test skipped. Continuing to exit...");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("Invalid input. Wiki test skipped.");
                Console.ResetColor();
            }

            // ==================== END WIKI FEATURE ====================

            Console.ResetColor();
            Console.WriteLine("\nPress any key to exit...");
            Console.ReadKey();
        }

        /// <summary>
        /// Interactive test runner for the CavemanWiki functionality.
        /// </summary>
        /// <summary>
        /// Interactive test runner for the CavemanWiki functionality.
        /// FIXED: Added path validation, directory/file checks, and permission handling.
        /// </summary>
        static async Task RunWikiTestAsync(CavemanCompressionService compressor)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;

            // ==================== STEP 1: Get input project path ====================
            Console.WriteLine("📁 Enter the path to the project folder you want to scan:");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.Write("> ");
            string inputPath = Console.ReadLine()?.Trim();
            Console.ResetColor();

            if (string.IsNullOrEmpty(inputPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("❌ Error: Input path cannot be empty.");
                Console.ResetColor();
                return;
            }

            if (!Directory.Exists(inputPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error: Directory not found: '{inputPath}'");
                Console.ResetColor();
                return;
            }

            // Resolve to full path for consistency
            inputPath = Path.GetFullPath(inputPath);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"✓ Input resolved: {inputPath}");
            Console.ResetColor();

            // ==================== STEP 2: Get output markdown path ====================
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("📄 Enter the output path for the generated markdown file:");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("   (Tip: Enter a filename like 'PROJECT_WIKI.md' or a full path)");
            Console.Write("> ");
            string outputPath = Console.ReadLine()?.Trim();
            Console.ResetColor();

            // Handle empty output path - use default in current directory
            if (string.IsNullOrEmpty(outputPath))
            {
                outputPath = Path.Combine(Directory.GetCurrentDirectory(), "PROJECT_WIKI.md");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"✓ Using default output: {outputPath}");
                Console.ResetColor();
            }
            else
            {
                // Resolve to full path
                outputPath = Path.GetFullPath(outputPath);
            }

            // ==================== STEP 3: Validate output path ====================

            // Check 1: If output path is a directory, append default filename
            if (Directory.Exists(outputPath))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Output path is a directory. Appending default filename...");
                Console.ResetColor();
                outputPath = Path.Combine(outputPath, "PROJECT_WIKI.md");
            }

            // Check 2: Ensure output has a filename (not just a directory path)
            string outputFileName = Path.GetFileName(outputPath);
            if (string.IsNullOrEmpty(outputFileName) || !outputFileName.Contains("."))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Output path missing filename. Using 'PROJECT_WIKI.md'...");
                Console.ResetColor();
                outputPath = Path.Combine(Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory(), "PROJECT_WIKI.md");
            }

            // Check 3: Prevent output overwriting input directory
            string outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDir) &&
                string.Equals(outputDir, inputPath, StringComparison.OrdinalIgnoreCase))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"⚠ Output directory equals input project folder.");
                Console.WriteLine($"  This is allowed, but ensure you don't overwrite important files.");
                Console.ResetColor();
            }

            // Check 4: Ensure output directory exists and is writable
            if (!string.IsNullOrEmpty(outputDir))
            {
                if (!Directory.Exists(outputDir))
                {
                    try
                    {
                        Directory.CreateDirectory(outputDir);
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine($"✓ Created output directory: {outputDir}");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"❌ Error: Cannot create output directory '{outputDir}': {ex.Message}");
                        Console.ResetColor();
                        return;
                    }
                }

                // Check write permission by attempting a test write
                try
                {
                    string testFile = Path.Combine(outputDir, ".caveman_write_test.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                }
                catch (UnauthorizedAccessException)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ Error: No write permission for directory '{outputDir}'");
                    Console.ResetColor();
                    return;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"❌ Error: Cannot write to '{outputDir}': {ex.Message}");
                    Console.ResetColor();
                    return;
                }
            }

            // ==================== STEP 4: Execute Wiki generation ====================
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"🔍 Scanning project: {inputPath}");
            Console.WriteLine($"📝 Generating markdown: {outputPath}");
            Console.ResetColor();

            try
            {
                var startTime = DateTime.Now;

                // Initialize and run CavemanWiki
                var wiki = new CavemanWiki();
                string markdownContent = await wiki.GenerateAsync(
                    projectFolderPath: inputPath,
                    maxFileSizeBytes: 100 * 1024,  // 100KB limit per file
                    compressionLevel: CavemanCompressionLevel.Semantic
                );

                // Write to file with explicit UTF-8 encoding and error handling
                await File.WriteAllTextAsync(outputPath, markdownContent, Encoding.UTF8);

                var duration = DateTime.Now - startTime;
                var fileInfo = new FileInfo(outputPath);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"✅ Wiki generated successfully!");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"   ⏱ Time: {duration.TotalSeconds:F2}s");
                Console.WriteLine($"   📦 Output size: {FormatSize(fileInfo.Length)}");
                Console.WriteLine($"   📍 Location: {Path.GetFullPath(outputPath)}");
                Console.ResetColor();

                // Preview first lines
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("📋 Preview (first 12 lines):");
                Console.ForegroundColor = ConsoleColor.DarkGray;

                var previewLines = markdownContent.Split('\n').Take(12);
                foreach (var line in previewLines)
                {
                    Console.WriteLine(line);
                }
                Console.WriteLine("...");
                Console.ResetColor();

                // Offer to open the file
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.White;
                Console.Write("🗂️  Open the generated file in default editor? (Y/N): ");
                Console.ForegroundColor = ConsoleColor.Gray;
                var openChoice = Console.ReadLine()?.Trim().ToUpperInvariant();
                Console.ResetColor();

                if (openChoice == "Y" || openChoice == "YES")
                {
                    try
                    {
                        // Cross-platform file open
                        if (OperatingSystem.IsWindows())
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = outputPath,
                                UseShellExecute = true
                            });
                        }
                        else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                        {
                            System.Diagnostics.Process.Start("xdg-open", outputPath);
                        }
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("✓ File opened.");
                        Console.ResetColor();
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"⚠ Could not open file automatically: {ex.Message}");
                        Console.WriteLine($"   Please open manually: {outputPath}");
                        Console.ResetColor();
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Permission denied: {ex.Message}");
                Console.WriteLine($"💡 Try running as administrator or choose a different output path.");
                Console.ResetColor();
            }
            catch (IOException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ I/O error: {ex.Message}");
                Console.WriteLine($"💡 Check if the file is locked or the disk is full.");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"❌ Error generating Wiki: {ex.Message}");
                Console.WriteLine($"   Stack: {ex.StackTrace}");
                Console.ResetColor();
            }
        }
        /// <summary>
        /// Formats byte size into human-readable format.
        /// </summary>
        static string FormatSize(long bytes)
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

        static ConsoleColor GetColorForLevel(CavemanCompressionLevel level) => level switch
        {
            CavemanCompressionLevel.None => ConsoleColor.White,
            CavemanCompressionLevel.Light => ConsoleColor.Green,
            CavemanCompressionLevel.Semantic => ConsoleColor.Yellow,
            CavemanCompressionLevel.Aggressive => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }
}