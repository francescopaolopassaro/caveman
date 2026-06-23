// -----------------------------------------------------------------------------
// <copyright file="Program.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Console demo / entry point for exercising Caveman compression and services.</summary>
// -----------------------------------------------------------------------------
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
 * - Core: self-contained lookup engine over embedded per-language word data
 *   (function words, lemmas and verbs derived from Universal Dependencies).
 * - Methodology: stop-word removal, heuristic content selection and lemmatization.
 * 
 * AUTHOR: [Passaro Francesco Paolo]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/

using caveman.core;
using caveman.core.entities;
using caveman.core.services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace caveman
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--benchmark")
            {
                await RunBenchmarkAsync();
                return;
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("**********************************************************");
            Console.WriteLine("*  Caveman v.1.3.0 - Content-Aware Prompt Compressor      *");
            Console.WriteLine("*  C# - Version by Francesco Paolo Passaro                *");
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

            // ==================== CAVEMAN COMMAND SHELL ====================

            var stats = new CavemanStatsTracker();
            var contextCompressor = new CavemanContextCompressor(compressor);
            var commitGen = new CavemanCommitGenerator();
            var reviewer = new CavemanReviewService();
            var cavecrew = new CavecrewService();
            var safety = new CavemanSafetyGuard();
            var router = CavemanContentRouter.FromProfile(CompressionProfile.Balanced);
            var outputShaper = new CavemanOutputShaper();
            var wasteAnalyzer = new CavemanWasteAnalyzer();

            Console.ResetColor();
            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("=== CAVEMAN 1.3.0 — COMMAND SHELL ===");
            Console.ResetColor();
            Console.WriteLine("Available commands:");
            Console.WriteLine("  /caveman-compress <dir>     - Compress context files (CLAUDE.md, TODO)");
            Console.WriteLine("  /caveman-commit [diff|EOF]  - Generate compact conventional commit");
            Console.WriteLine("  /caveman-review [diff|EOF]  - Single-line code review on diff");
            Console.WriteLine("  /caveman-stats               - Show token/dollar savings (use: reset)");
            Console.WriteLine("  /caveman-wiki <dir>          - Generate project wiki");
            Console.WriteLine("  /caveman-investigate <dir>   - Map directory symbols & structure");
            Console.WriteLine("  /caveman-build <desc> | <files> - Plan surgical changes");
            Console.WriteLine("  /caveman-crew-review [diff|EOF] - Cavecrew diff analysis");
            Console.WriteLine("  /caveman-safety <msg>        - Check security/destructive patterns");
            Console.WriteLine("  /router                      - Route content to best compressor (paste, EOF)");
            Console.WriteLine("  /router-demo                 - Demo content router on built-in samples");
            Console.WriteLine("  /output-shape <level>        - Preview verbosity steering (0-4)");
            Console.WriteLine("  /waste                       - Analyze token waste in content (paste, EOF)");
            Console.WriteLine("  /summarizer-demo             - Demo: TF-IDF summarization (Italian)");
            Console.WriteLine("  /summarizer                  - Summarize your own text (paste, EOF to end)");
            Console.WriteLine("  /textrank-demo               - Demo TextRank graph-based summary (Italian)");
            Console.WriteLine("  /textrank                    - TextRank summarizer (paste, EOF to end)");
            Console.WriteLine("  /textrank-chat               - TextRank on a full chat (paste, EOF)");
            Console.WriteLine("  /help                        - Show this menu");
            Console.WriteLine("  /exit                        - Exit");
            Console.WriteLine();

            while (true)
            {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("caveman> ");
                Console.ResetColor();
                var raw = Console.ReadLine()?.Trim();
                if (raw == null) break;
                var cmd = raw.ToLowerInvariant();

                switch (cmd)
                {
                    case "/exit":
                    case "/quit":
                        Console.WriteLine("Goodbye.");
                        return;

                    case "/help":
                        Console.WriteLine("Commands:");
                        Console.WriteLine("  /caveman-compress <dir>     - Compress context files in directory");
                        Console.WriteLine("  /caveman-commit [diff|EOF]  - Generate commit (paste diff then EOF)");
                        Console.WriteLine("  /caveman-review [diff|EOF]  - Review diff (paste diff then EOF)");
                        Console.WriteLine("  /caveman-stats               - Show compression statistics");
                        Console.WriteLine("  /caveman-stats reset         - Reset session statistics");
                        Console.WriteLine("  /caveman-wiki <dir>          - Generate project wiki");
                        Console.WriteLine("  /caveman-investigate <dir>   - Investigate files & symbols");
                        Console.WriteLine("  /caveman-build <desc> | <files> - Plan surgical changes");
                        Console.WriteLine("  /caveman-crew-review [diff|EOF] - Cavecrew diff analysis");
                        Console.WriteLine("  /caveman-safety <msg>       - Check message safety level");
                        Console.WriteLine("  /summarizer-demo            - Demo TF-IDF summarization (Italian)");
                        Console.WriteLine("  /summarizer                 - Summarize your own text (paste, EOF to end)");
                        Console.WriteLine("  /textrank-demo              - Demo TextRank graph-based summary (Italian)");
                        Console.WriteLine("  /textrank                   - TextRank summarizer (paste, EOF to end)");
                        Console.WriteLine("  /textrank-chat              - TextRank on a full chat: summarizes only long discourses (paste, EOF)");
                        Console.WriteLine("  /help                       - Show this menu");
                        Console.WriteLine("  /exit                       - Exit");
                        Console.WriteLine();
                        Console.WriteLine("Tips:");
                        Console.WriteLine("  - For multiline input (diffs), omit the argument and type EOF on its own line to finish");
                        Console.WriteLine("  - Enclose paths with spaces in double quotes");
                        Console.WriteLine("  - Use /caveman-stats reset to clear session counters");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-compress "):
                        {
                            var dir = raw.Substring("/caveman-compress ".Length).Trim().Trim('"');
                            await RunCompressCommandAsync(contextCompressor, dir, stats);
                            break;
                        }

                    case "/caveman-compress":
                        Console.WriteLine("Usage: /caveman-compress <directory_path>");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-commit "):
                        {
                            var diff = raw.Substring("/caveman-commit ".Length);
                            RunCommitCommand(commitGen, diff);
                            break;
                        }

                    case "/caveman-commit":
                        Console.WriteLine("Paste diff (end with EOF on new line):");
                        var diffLines = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(diffLines))
                            RunCommitCommand(commitGen, diffLines);
                        break;

                    case string c when c != null && c.StartsWith("/caveman-review "):
                        {
                            var diff = raw.Substring("/caveman-review ".Length);
                            RunReviewCommand(reviewer, diff);
                            break;
                        }

                    case "/caveman-review":
                        Console.WriteLine("Paste diff (end with EOF on new line):");
                        var reviewDiff = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(reviewDiff))
                            RunReviewCommand(reviewer, reviewDiff);
                        break;

                    case "/caveman-stats":
                        Console.WriteLine(stats.FormatFullReport());
                        break;

                    case "/caveman-stats reset":
                        stats.ResetSession();
                        Console.WriteLine("Session stats reset.");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-wiki "):
                        {
                            var wikiDir = raw.Substring("/caveman-wiki ".Length).Trim().Trim('"');
                            await RunWikiTestAsync(compressor, wikiDir);
                            break;
                        }

                    case "/caveman-wiki":
                        Console.WriteLine("Usage: /caveman-wiki <directory_path>");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-investigate "):
                        {
                            var invDir = raw.Substring("/caveman-investigate ".Length).Trim().Trim('"');
                            var invResult = await cavecrew.InvestigateAsync(invDir);
                            Console.WriteLine($"[{invResult.Agent}] {invResult.Summary}");
                            foreach (var d in invResult.Details)
                                Console.WriteLine(d);
                            break;
                        }

                    case "/caveman-investigate":
                        Console.WriteLine("Usage: /caveman-investigate <directory_path>");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-build "):
                        {
                            var rest = raw.Substring("/caveman-build ".Length);
                            var parts = rest.Split('|');
                            var desc = parts[0].Trim();
                            var files = parts.Length > 1
                                ? parts[1].Split(',').Select(f => f.Trim().Trim('"')).ToList()
                                : new List<string>();
                            if (files.Count == 0) { Console.WriteLine("Usage: /caveman-build <description> | <file1.cs,file2.cs>"); break; }
                            var buildResult = await cavecrew.BuildAsync(desc, files);
                            Console.WriteLine($"[{buildResult.Agent}] {buildResult.Summary}");
                            foreach (var d in buildResult.Details) Console.WriteLine(d);
                            break;
                        }

                    case "/caveman-build":
                        Console.WriteLine("Usage: /caveman-build <description> | <file1.cs,file2.cs>");
                        break;

                    case string c when c != null && c.StartsWith("/caveman-crew-review "):
                        {
                            var diffText = raw.Substring("/caveman-crew-review ".Length);
                            var crewReview = cavecrew.Review(diffText);
                            Console.WriteLine($"[{crewReview.Agent}] {crewReview.Summary}");
                            foreach (var d in crewReview.Details) Console.WriteLine(d);
                            break;
                        }

                    case "/caveman-crew-review":
                        Console.WriteLine("Paste diff (end with EOF):");
                        var crewDiff = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(crewDiff))
                        {
                            var crewReview = cavecrew.Review(crewDiff);
                            Console.WriteLine($"[{crewReview.Agent}] {crewReview.Summary}");
                            foreach (var d in crewReview.Details) Console.WriteLine(d);
                        }
                        break;

                    case string c when c != null && c.StartsWith("/caveman-safety "):
                        {
                            var msg = raw.Substring("/caveman-safety ".Length);
                            var verdict = safety.Check(msg);
                            Console.ForegroundColor = verdict.Level switch
                            {
                                SafetyLevel.Critical => ConsoleColor.Red,
                                SafetyLevel.Warning => ConsoleColor.Yellow,
                                _ => ConsoleColor.Green
                            };
                            Console.WriteLine($"Safety: {verdict.Level} | {verdict.Reason}");
                            Console.WriteLine($"Compress: {(verdict.ShouldCompress ? "YES" : "NO")}");
                            Console.ResetColor();
                            break;
                        }

                    case "/summarizer-demo":
                        await RunSummarizerDemoAsync();
                        break;

                    case "/summarizer":
                        Console.WriteLine("Paste your text (end with EOF on new line):");
                        var summaryText = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(summaryText))
                            await RunSummarizerAsync(summaryText);
                        break;

                    case "/textrank-demo":
                        await RunTextRankDemoAsync();
                        break;

                    case "/textrank":
                        Console.WriteLine("Paste your text (end with EOF on new line):");
                        var textrankText = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(textrankText))
                            RunTextRankAsync(textrankText);
                        break;

                    case "/textrank-chat":
                        Console.WriteLine("Paste the full chat context (end with EOF on new line):");
                        var chatText = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(chatText))
                            RunTextRankChatAsync(chatText);
                        break;

                    case "/router":
                        Console.WriteLine("Paste content to route (end with EOF on new line):");
                        var routeInput = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(routeInput))
                        {
                            var rr = await router.RouteAsync(routeInput);
                            Console.ForegroundColor = ConsoleColor.Cyan;
                            Console.WriteLine($"\nDetected: {rr.DetectedType} | Strategy: {rr.StrategyUsed}");
                            Console.WriteLine($"Tokens: {rr.TokensBefore} -> {rr.TokensAfter} ({rr.SavingsPercent:F1}% saved)");
                            Console.ResetColor();
                            Console.WriteLine("\n--- Compressed ---");
                            Console.WriteLine(rr.Compressed.Length > 800 ? rr.Compressed[..800] + "\n[...truncated]" : rr.Compressed);
                        }
                        break;

                    case "/router-demo":
                        await RunRouterDemoAsync(router);
                        break;

                    case var c when c != null && c.StartsWith("/output-shape"):
                    {
                        var parts = cmd.Split(' ', 2);
                        var lvl = parts.Length > 1 && int.TryParse(parts[1], out int lv) ? (VerbosityLevel)lv : VerbosityLevel.NoRestatement;
                        const string demoPrompt = "You are a helpful assistant. Answer questions clearly and accurately.";
                        var shaped = outputShaper.ShapeSystemPrompt(demoPrompt, lvl);
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"\nVerbosity level: {lvl}\n");
                        Console.ResetColor();
                        Console.WriteLine(shaped);
                        break;
                    }

                    case "/waste":
                        Console.WriteLine("Paste content to analyze (end with EOF on new line):");
                        var wasteInput = ReadMultilineInput();
                        if (!string.IsNullOrWhiteSpace(wasteInput))
                        {
                            var wa = wasteAnalyzer.Analyze(wasteInput);
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"\nWaste analysis:");
                            Console.WriteLine($"  HTML noise:   {wa.HtmlNoiseTokens,4} tokens");
                            Console.WriteLine($"  Base64 blobs: {wa.Base64Tokens,4} tokens");
                            Console.WriteLine($"  Whitespace:   {wa.WhitespaceTokens,4} tokens");
                            Console.WriteLine($"  JSON bloat:   {wa.JsonBloatTokens,4} tokens");
                            Console.WriteLine($"  Total waste:  {wa.TotalWasteTokens,4} tokens");
                            Console.ResetColor();
                        }
                        break;

                    case null:
                        break;

                    default:
                        Console.WriteLine($"Unknown command: {cmd}. Type /help for commands.");
                        break;
                }

                Console.WriteLine();
            }
        }

        static Task RunSummarizerDemoAsync()
        {
            var text = DemoText;
            RunSummarizerAsync(text);
            return Task.CompletedTask;
        }

        static Task RunTextRankDemoAsync()
        {
            var text = DemoText;
            RunTextRankAsync(text);
            return Task.CompletedTask;
        }

        static void RunTextRankAsync(string text)
        {
            var textRank = new CavemanTextRank();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("=== TEXT RANK (Graph-Based Summary) ===");
            Console.ResetColor();
            Console.WriteLine();

            var iso3 = new CavemanLanguageDetector().Detect(text);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Lingua rilevata: {iso3}");
            Console.ResetColor();

            PrintOriginalText(text);

            var sentenceCounts = new[] { 2, 3, 5 };
            foreach (var count in sentenceCounts)
            {
                var summary = textRank.RankAndSummarize(text, count, iso3);
                var originalWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var summaryWords = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var reduction = 100.0 * (1.0 - (double)summaryWords / originalWords);

                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"--- TextRank in {count} frasi ({summaryWords} parole, -{reduction:F0}%) ---");
                Console.ResetColor();
                Console.WriteLine(summary);
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('=', 65));
            Console.ResetColor();
            Console.WriteLine();
        }

        static void RunTextRankChatAsync(string text)
        {
            var textRank = new CavemanTextRank();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("=== TEXT RANK CHAT (riassume solo i discorsi lunghi) ===");
            Console.ResetColor();
            Console.WriteLine();

            // Parse roles/turns when the input is a structured conversation, and report metrics.
            var options = new ChatSummarizeOptions { ParseConversation = true };
            var result = textRank.RankAndSummarizeChatDetailed(text, options);

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Formato rilevato: {result.Format}");
            Console.WriteLine($"Caratteri: {text.Length} -> {result.Text.Length}");
            Console.WriteLine($"Token (GPT approx): {result.OriginalTokens} -> {result.CompressedTokens} (-{result.EfficiencyPercentage:F0}%)");
            Console.WriteLine($"Blocchi: {result.Blocks} | riassunti {result.SummarizedBlocks} | verbatim {result.KeptVerbatimBlocks} | duplicati rimossi {result.DuplicatesRemoved}");
            Console.ResetColor();
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.Magenta;
            Console.WriteLine("--- Risultato (discorsi compressi, parole chiave/risultati intatti) ---");
            Console.ResetColor();
            Console.WriteLine(result.Text);
            Console.WriteLine();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('=', 65));
            Console.ResetColor();
            Console.WriteLine();
        }

        static void PrintOriginalText(string text)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine();
            Console.WriteLine(new string('─', 65));
            Console.WriteLine("TESTO ORIGINALE:");
            Console.WriteLine(new string('─', 65));
            Console.ResetColor();
            Console.WriteLine(text.Trim());
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('─', 65));
            Console.ResetColor();
            Console.WriteLine();
        }

        static string DemoText => "Il ladro di ombre\n\nNel piccolo villaggio di Valchiara, situato ai piedi di una montagna perennemente innevata, viveva un uomo di nome Elia. A differenza degli altri abitanti, Elia non faceva il boscaiolo o il pastore, ma aveva un mestiere del tutto particolare: era un collezionista di ombre.\n\nFin da quando era ragazzo, Elia aveva scoperto di possedere un dono straordinario. Grazie a una piccola lanterna di ottone e a un pizzico di polvere di stelle, riusciva a staccare l'ombra dalle persone e dagli oggetti, conservandola in barattoli di vetro. Non rubava le ombre per fare del male, ma per preservare i ricordi felici. Nei suoi scaffali, allineati nella sua piccola casa di legno, custodiva l'ombra del primo sorriso di un bambino, l'ombra del gatto del sindaco che amava dormire al sole, e persino l'ombra del primo albero piantato nel paese.\n\nGli abitanti del villaggio, tuttavia, non capivano questa sua passione e lo evitavano, considerandolo uno stravagante stregone. L'unico a fargli visita era Leo, un bambino curioso e coraggioso di dieci anni. Leo andava spesso a trovare Elia, affascinato dai riflessi argentati e bluastri chiusi nei barattoli.\n\nUna gelida notte d'inverno, un vento ululante spazzò via la neve e spense tutti i lampioni del villaggio, lasciando Valchiara nel buio più totale. Gli abitanti, spaventati e incapaci di orientarsi, si chiusero in casa. Il freddo e il gelo stavano persino iniziando a bloccare i meccanismi della centrale elettrica del paese.\n\nSenza perdersi d'animo, Elia prese la sua borsa di tela e i suoi barattoli più preziosi. Insieme al piccolo Leo, uscì nella tormenta. Raggiunse la piazza principale e, aprendo i barattoli, liberò le ombre che aveva conservato nel corso degli anni: l'ombra del sole di mezzogiorno, l'ombra del fuoco scoppiettante del camino, l'ombra della gioia e del calore.\n\nImmediatamente, la piazza si illuminò di una luce calda e avvolgente. Le ombre danzavano sui muri delle case, portando con sé un tepore magico che sciolse il ghiaccio e ridiede coraggio e speranza a tutti. Gli abitanti, svegliati da quel bagliore dorato, uscirono dalle loro abitazioni e rimasero a bocca aperta.\n\nCapirono finalmente che Elia non era un pericolo, ma un custode di tesori preziosi. Da quella notte in poi, il villaggio non fu mai più avvolto dal buio e dal gelo, ed Elia divenne il cittadino più rispettato e amato da tutti.";

        static Task RunSummarizerAsync(string text)
        {
            var summarizer = new CavemanSummarizer();

            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("=== RIASSUNTO (Summarizer Demo) ===");
            Console.ResetColor();

            var iso3 = new CavemanLanguageDetector().Detect(text);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Lingua rilevata: {iso3}");
            Console.ResetColor();

            PrintOriginalText(text);

            var sentenceCounts = new[] { 2, 3, 5 };
            foreach (var count in sentenceCounts)
            {
                var summary = summarizer.CondenseText(text, count, iso3);
                var originalWords = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var summaryWords = summary.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                var reduction = 100.0 * (1.0 - (double)summaryWords / originalWords);

                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"--- Riassunto in {count} frasi ({summaryWords} parole, -{reduction:F0}%) ---");
                Console.ResetColor();
                Console.WriteLine(summary);
                Console.WriteLine();
            }

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(new string('=', 65));
            Console.ResetColor();

            var splitter = new CavemanTextSplitter();
            var detector = new CavemanSentenceDetector();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("=== ANALISI (Tokenizzazione + Frasi) ===");
            Console.ResetColor();

            var tokens = splitter.ParseText(text);
            var words = tokens.Count(t => t.Category == CavemanTokenCategory.Word);
            var punct = tokens.Count(t => t.Category == CavemanTokenCategory.Punctuation);
            var numbers = tokens.Count(t => t.Category == CavemanTokenCategory.Number);
            Console.WriteLine($"Token: {tokens.Length} totali | {words} parole | {punct} punteggiatura | {numbers} numeri");

            var sentences = detector.SplitText(text, iso3);
            Console.WriteLine($"Frasi: {sentences.Length} trovate");
            Console.ResetColor();
            Console.WriteLine();
            return Task.CompletedTask;
        }

        /// <summary>
        /// Interactive test runner for the CavemanWiki functionality.
        /// </summary>
        static async Task RunWikiTestAsync(CavemanCompressionService compressor, string? inputPath = null)
        {
            Console.WriteLine();
            if (string.IsNullOrEmpty(inputPath))
            {
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("📁 Enter the path to the project folder you want to scan:");
                Console.ForegroundColor = ConsoleColor.Gray;
                Console.Write("> ");
                inputPath = Console.ReadLine()?.Trim();
                Console.ResetColor();
            }

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

        static string ReadMultilineInput()
        {
            var lines = new List<string>();
            while (true)
            {
                var line = Console.ReadLine();
                if (line?.Trim().Equals("EOF", StringComparison.OrdinalIgnoreCase) == true)
                    break;
                lines.Add(line ?? "");
            }
            return string.Join("\n", lines);
        }

        static async Task RunCompressCommandAsync(CavemanContextCompressor compressor, string dir, CavemanStatsTracker stats)
        {
            Console.WriteLine($"Compressing context files in: {dir}");
            var results = await compressor.CompressDirectoryAsync(dir);

            if (results.Count == 0)
            {
                Console.WriteLine("No context files found (CLAUDE.md, TODO, README.md, etc).");
                return;
            }

            foreach (var r in results)
            {
                stats.TrackResult(new CompressionResult
                {
                    CompressedText = r.CompressedContent,
                    OriginalTokens = r.OriginalTokens,
                    CompressedTokens = r.CompressedTokens,
                    ErrorMessage = r.ErrorMessage
                });

                Console.ForegroundColor = r.HasError ? ConsoleColor.Red : ConsoleColor.Green;
                Console.WriteLine($"{Path.GetFileName(r.FilePath)}: {r.OriginalTokens} → {r.CompressedTokens} tokens ({r.SavingsPercent:F1}%)");
                Console.ResetColor();
                if (!r.HasError)
                {
                    Console.WriteLine($"  Compressed: {r.CompressedContent[..Math.Min(r.CompressedContent.Length, 120)]}");
                    Console.WriteLine();
                }
            }

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(stats.FormatSessionReport());
            Console.ResetColor();
        }

        static void RunCommitCommand(CavemanCommitGenerator generator, string diff)
        {
            var commit = generator.GenerateFromDiff(diff);
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Commit ({commit.SubjectLength} chars):");
            Console.ResetColor();
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"  {commit.FullMessage}");
            Console.ResetColor();
            Console.WriteLine($"  Type: {commit.Type} | Scope: {commit.Scope ?? "(none)"}");
        }

        static void RunReviewCommand(CavemanReviewService reviewer, string diff)
        {
            var review = reviewer.ReviewDiff(diff);
            Console.WriteLine($"Files: {review.ChangedFiles} | +{review.Additions} / -{review.Deletions} | Issues: {review.TotalIssues}");
            Console.WriteLine();

            foreach (var comment in review.Comments)
            {
                var color = comment.Severity switch
                {
                    "critical" => ConsoleColor.Red,
                    "bug" => ConsoleColor.Red,
                    "warning" => ConsoleColor.Yellow,
                    "perf" => ConsoleColor.Magenta,
                    _ => ConsoleColor.Cyan
                };
                Console.ForegroundColor = color;
                Console.WriteLine($"  {comment}");
                Console.ResetColor();
            }

            if (review.TotalIssues == 0)
                Console.WriteLine("  No issues found.");
        }

        // ==================== ROUTER DEMO ====================

        static async Task RunRouterDemoAsync(CavemanContentRouter router)
        {
            var tok = new ModelTokenizer();
            var demos = new (string Label, string Content)[]
            {
                ("Prose EN", "I would really like to know if you could kindly provide me with some information regarding the best and most affordable restaurants in London near Victoria Station for my family visit this weekend."),
                ("JSON array", @"[{""id"":1,""user"":""alice"",""action"":""login"",""status"":""ok""},{""id"":2,""user"":""bob"",""action"":""upload"",""status"":""ok""},{""id"":3,""user"":""carol"",""action"":""delete"",""status"":""failed""}]"),
                ("Git diff", "diff --git a/api.cs b/api.cs\n--- a/api.cs\n+++ b/api.cs\n@@ -1,8 +1,8 @@\n public class Api\n {\n     // context\n     // context\n-    public void OldEndpoint() {}\n+    public void NewEndpoint() {}\n     // context\n     // context\n }"),
                ("HTML", "<html><head><style>body{color:red}</style></head><body><p>Main content here, important information.</p><script>alert(1)</script></body></html>"),
            };
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== CONTENT ROUTER DEMO ===");
            Console.ResetColor();
            Console.WriteLine($"{"Type",-12} {"Orig",5} {"After",5} {"Saving%",8}  Strategy");
            Console.WriteLine(new string('-', 60));
            foreach (var (label, content) in demos)
            {
                var rr = await router.RouteAsync(content);
                int orig = tok.CountTokens(content, LlmModel.Gpt4);
                int after = tok.CountTokens(rr.Compressed, LlmModel.Gpt4);
                double pct = orig > 0 ? (orig - after) * 100.0 / orig : 0;
                Console.ForegroundColor = pct > 30 ? ConsoleColor.Green : ConsoleColor.White;
                Console.WriteLine($"{label,-12} {orig,5} {after,5} {pct,7:F1}%  {rr.StrategyUsed}");
            }
            Console.ResetColor();
        }

        // ==================== BENCHMARK ====================

        static async Task RunBenchmarkAsync()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            var svc = new CavemanCompressionService(new ModelTokenizer());
            var router = CavemanContentRouter.FromProfile(CompressionProfile.Balanced);
            var tok = new ModelTokenizer();

            var samples = new (string Label, string Text)[]
            {
                ("Prose EN", "I would really like to know if you could kindly provide me with some detailed information regarding the best and most affordable restaurants that are conveniently located in central London, specifically near the Victoria Station area, as I am planning to visit with my family this coming weekend and want to make the best possible choice for everyone."),
                ("Prose IT", "Buongiorno, vorrei gentilmente sapere se fosse possibile ricevere alcune informazioni riguardo ai migliori ristoranti economici che si trovano a Roma, preferibilmente vicino alla stazione Termini, in quanto ho intenzione di visitare la citta con la mia famiglia nel prossimo weekend e vorrei fare la scelta migliore possibile per tutti."),
                ("Prose DE", "Ich wuerde wirklich gerne wissen, ob Sie mir freundlicherweise einige detaillierte Informationen ueber die besten und guenstigsten Restaurants geben koennten, die sich in der Naehe des Berliner Hauptbahnhofs befinden, da ich plane, dieses Wochenende mit meiner Familie dorthin zu fahren."),
                ("Prose FR", "Je souhaiterais vraiment savoir si vous pourriez me donner quelques informations detaillees sur les meilleurs restaurants abordables situes pres de la gare du Nord a Paris, car je prevois de visiter la ville avec ma famille ce week-end."),
                ("Prose ES", "Me gustaria mucho saber si podria proporcionarme alguna informacion detallada sobre los mejores restaurantes asequibles que se encuentran cerca de la estacion de Atocha en Madrid."),
                ("JSON array 5r", @"[{""id"":1,""name"":""Alice Smith"",""email"":""alice@example.com"",""role"":""admin"",""status"":""active""},{""id"":2,""name"":""Bob Jones"",""email"":""bob@example.com"",""role"":""user"",""status"":""active""},{""id"":3,""name"":""Carol White"",""email"":""carol@example.com"",""role"":""user"",""status"":""inactive""},{""id"":4,""name"":""Dave Brown"",""email"":""dave@example.com"",""role"":""user"",""status"":""active""},{""id"":5,""name"":""Eve Black"",""email"":""eve@example.com"",""role"":""mod"",""status"":""active""}]"),
                ("Git diff", "diff --git a/src/App.cs b/src/App.cs\nindex abc..def 100644\n--- a/src/App.cs\n+++ b/src/App.cs\n@@ -1,15 +1,15 @@\n public class App\n {\n     // old comment 1\n     // old comment 2\n     // padding context line\n     // more padding context\n     // even more padding\n     // extra padding line\n-    public void OldMethod() { Console.WriteLine(\"old\"); }\n+    public void NewMethod() { Console.WriteLine(\"new\"); }\n     // after context 1\n     // after context 2\n     // after padding 1\n     // after padding 2\n     // final line\n }"),
                ("Build log", "INFO 2026-01-01 10:00:01 Build started\nINFO 2026-01-01 10:00:02 Restoring packages\nINFO 2026-01-01 10:00:03 Restored 42 packages in 1.2s\nINFO 2026-01-01 10:00:04 Compiling src/App.cs\nINFO 2026-01-01 10:00:05 Compiling src/Service.cs\nINFO 2026-01-01 10:00:06 Compiling src/Repository.cs\nINFO 2026-01-01 10:00:07 Compiling src/Models.cs\nERROR 2026-01-01 10:00:08 Build FAILED: NullReferenceException\n   at App.Run() in App.cs:line 42\n   at Program.Main() in Program.cs:line 10\nINFO 2026-01-01 10:00:09 Build output: 0 warnings, 1 error"),
                ("Markdown table", "| Name | Dept | Salary | City | Status | Notes |\n|------|------|--------|------|--------|-------|\n| Alice | Engineering | 85000 | Remote | Active | Senior |\n| Bob | Marketing | 72000 | NYC | Active | Junior |\n| Carol | Engineering | 91000 | Remote | Active | Lead |\n| Dave | Sales | 68000 | LA | Inactive | Part-time |\n| Eve | Engineering | 88000 | Remote | Active | Senior |"),
                ("HTML page", "<html><head><title>Report</title><style>body{font-family:Arial;font-size:12px}</style></head><body><div class='container'><h1>Monthly Report</h1><p>This month we achieved record sales across all regions. Total revenue reached 2.4 million dollars, exceeding our quarterly target by 18 percent.</p><p>Key highlights include expansion into three new markets and a 24 percent increase in customer retention.</p><script>analytics.track('report_view')</script></div></body></html>"),
                ("C# code", "// Auth service\nusing System;\nusing System.Threading.Tasks;\n// Handles user login\npublic class AuthService\n{\n    // Dependencies\n    private readonly IUserRepository _repo;\n    private readonly ILogger _logger;\n    // Init\n    public AuthService(IUserRepository repo, ILogger logger)\n    {\n        _repo = repo;   // store repo\n        _logger = logger; // store logger\n    }\n    // Authenticate user - returns true if valid\n    public async Task<bool> AuthenticateAsync(string username, string password)\n    {\n        // Log the attempt\n        _logger.LogInfo($\"Auth attempt for {username}\");\n        var user = await _repo.FindByUsernameAsync(username);\n        if (user == null) return false; // not found\n        return user.VerifyPassword(password); // check hash\n    }\n}"),
            };

            Console.WriteLine("=== CAVEMAN 1.3.0 — BENCHMARK TOKEN SAVINGS ===");
            Console.WriteLine();
            Console.WriteLine("NLP COMPRESSION (CavemanCompressionService)");
            Console.WriteLine($"{"Content",-22} {"OrigTok",7} {"Light",6} {"Sem.",6} {"Aggr.",6}  {"Light%",7} {"Sem.%",7} {"Aggr.%",7}");
            Console.WriteLine(new string('-', 82));

            foreach (var (label, text) in samples)
            {
                int orig = tok.CountTokens(text, LlmModel.Gpt4);
                var rL = await svc.CompressAsync(text, CavemanCompressionLevel.Light);
                var rS = await svc.CompressAsync(text, CavemanCompressionLevel.Semantic);
                var rA = await svc.CompressAsync(text, CavemanCompressionLevel.Aggressive);
                int tL = tok.CountTokens(rL.CompressedText, LlmModel.Gpt4);
                int tS = tok.CountTokens(rS.CompressedText, LlmModel.Gpt4);
                int tA = tok.CountTokens(rA.CompressedText, LlmModel.Gpt4);
                double pL = orig > 0 ? (orig - tL) * 100.0 / orig : 0;
                double pS = orig > 0 ? (orig - tS) * 100.0 / orig : 0;
                double pA = orig > 0 ? (orig - tA) * 100.0 / orig : 0;
                Console.WriteLine($"{label,-22} {orig,7} {tL,6} {tS,6} {tA,6}  {pL,6:F1}% {pS,6:F1}% {pA,6:F1}%");
            }

            Console.WriteLine();
            Console.WriteLine("CONTENT ROUTER (CompressContentAsync — Balanced profile)");
            Console.WriteLine($"{"Content",-22} {"OrigTok",7} {"After",6} {"Savings%",9}  {"Strategy",-30}");
            Console.WriteLine(new string('-', 82));

            foreach (var (label, text) in samples)
            {
                int orig = tok.CountTokens(text, LlmModel.Gpt4);
                var rr = await router.RouteAsync(text);
                int after = tok.CountTokens(rr.Compressed, LlmModel.Gpt4);
                double pct = orig > 0 ? (orig - after) * 100.0 / orig : 0;
                Console.WriteLine($"{label,-22} {orig,7} {after,6} {pct,8:F1}%  {rr.StrategyUsed,-30}");
            }

            Console.WriteLine();
            Console.WriteLine("COMPRESSION PROFILES (JSON array 5 rows)");
            var jsonSample = samples.First(s => s.Label.StartsWith("JSON")).Text;
            int jsonOrig = tok.CountTokens(jsonSample, LlmModel.Gpt4);
            Console.WriteLine($"{"Profile",-14} {"MaxItems",9} {"ProseLevel",-12} {"Tokens",7} {"Savings%",9}");
            Console.WriteLine(new string('-', 55));
            foreach (var profile in Enum.GetValues<CompressionProfile>())
            {
                var r2 = CavemanContentRouter.FromProfile(profile);
                var rr2 = await r2.RouteAsync(jsonSample);
                int after2 = tok.CountTokens(rr2.Compressed, LlmModel.Gpt4);
                double pct2 = jsonOrig > 0 ? (jsonOrig - after2) * 100.0 / jsonOrig : 0;
                Console.WriteLine($"{profile,-14} {"--",9} {"--",-12} {after2,7} {pct2,8:F1}%");
            }
        }
    }
}