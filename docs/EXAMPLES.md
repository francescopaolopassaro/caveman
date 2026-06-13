# Caveman — Examples for every public API

Runnable C# snippets covering all public methods, grouped by area. Unless noted, types
live in `caveman.core`, `caveman.core.entities` or `caveman.core.services`; the Semantic
Kernel plugins are in the separate **`Caveman.SemanticKernel`** package.

```csharp
using caveman.core;
using caveman.core.entities;
using caveman.core.services;
```

---

## 1. Prompt compression — `CavemanCompressionService`

```csharp
var svc = new CavemanCompressionService();

// CompressAsync(text, level) — auto-detects language.
CompressionResult r = await svc.CompressAsync(
    "Vorrei sapere se è possibile avere informazioni sui ristoranti a Roma.",
    CavemanCompressionLevel.Semantic);
Console.WriteLine($"{r.CompressedText}  ({r.EfficiencyPercentage:F1}% saved)");

// CompressAsync(text, level, customFilter, ct) — keep only content words & proper nouns.
var filter = new CompressionFilter
{
    KeepOnly = new HashSet<string> { "CONTENT", "PROPN" },   // FUNC, PUNCT, NUM, PROPN, CONTENT
    Remove   = new HashSet<string> { "PUNCT" },
    CustomPredicate = token => token.Length > 2
};
CompressionResult custom = await svc.CompressAsync("the big red car", CavemanCompressionLevel.None, filter);

// CompressBatchAsync(texts, level[, filter, ct]) — order preserved.
CompressionResult[] many = await svc.CompressBatchAsync(
    new[] { "Tell me about Rome.", "How to reach the Colosseum." },
    CavemanCompressionLevel.Aggressive);

// ApplyCompression(text, iso3, level[, filter]) — synchronous, explicit language.
CompressionResult forced = svc.ApplyCompression("I gatti corrono veloci", "ita", CavemanCompressionLevel.Aggressive);

// DetectLanguage / DetectLanguageScores
string iso3 = svc.DetectLanguage("Ich hätte gerne einen Kaffee.");          // "deu"
IReadOnlyDictionary<string, double> scores = svc.DetectLanguageScores("Where is the station?");

// Free cached per-language data (e.g. before shutdown).
svc.ReleaseMemory();
```

`CompressionResult` members: `CompressedText`, `OriginalTokens`, `CompressedTokens`,
`SavedTokens`, `EfficiencyPercentage`, `EstimatedEnergySavedMWh`, `EstimatedCO2SavedMg`,
`GptOriginalTokens`/`GptCompressedTokens`/`GptSavedTokens`, `ErrorMessage`, `HasError`.

### Language detection standalone — `CavemanLanguageDetector`

```csharp
var detector = new CavemanLanguageDetector();
string lang = detector.Detect("Vorrei un caffè");                 // "ita"
IReadOnlyDictionary<string, double> s = detector.DetectWithScores("good morning");
```

---

## 2. Tokenization & sentences

```csharp
// CavemanTextSplitter
var splitter = new CavemanTextSplitter();
CavemanToken[] tokens = splitter.ParseText("Ciao, mondo! 🎉 a@b.com https://x.io");
string[] words = splitter.ExtractWords("Caveman is 10/10!");      // ["Caveman", "is"]
string roundtrip = splitter.CombineTokens(tokens);                // original text
// Each CavemanToken: Value, StartIndex, EndIndex, Category, Length, IsWord, IsPunctuation, IsWhitespace.

// CavemanSentenceDetector
var sentences = new CavemanSentenceDetector();
CavemanSentence[] sents = sentences.SplitText("Dr. Rossi vive a Roma. Oggi piove.", "ita");
CavemanSentence[] fromTokens = sentences.SplitTokens(tokens, "ita");
string[] phrases = sentences.ExtractPhrases("Uno. Due. Tre.", "ita");
// CavemanSentence: Text, Tokens, WordCount.
```

---

## 3. Summarization

```csharp
string text = /* a long article */;

// TF-IDF summarizer
var tfidf = new CavemanSummarizer();
string s1 = tfidf.CondenseText(text, sentenceCount: 3);            // auto-detect language
string s2 = tfidf.CondenseText(text, sentenceCount: 3, "ita");
string s3 = tfidf.CondenseText(text, ratio: 0.3f);                 // 30% of sentences
string s4 = tfidf.CondenseText(text, ratio: 0.3f, "ita");
// Summarize then compress in one call:
CompressionResult chained = await tfidf.CompressWithSummaryAsync(text, CavemanCompressionLevel.Semantic, summarySentenceCount: 3);

// TextRank summarizer
var tr = new CavemanTextRank();
string t1 = tr.RankAndSummarize(text, sentenceCount: 3);
string t2 = tr.RankAndSummarize(text, sentenceCount: 3, "ita");
string t3 = tr.RankAndSummarize(text, ratio: 0.3f);
string t4 = tr.RankAndSummarize(text, ratio: 0.3f, "ita");
```

---

## 4. Cleaning markdown / JSON / HTML — `CavemanConversationToText`

```csharp
string md = "## Titolo\n```json\n{ \"k\": 1 }\n```\n<div><span>Ciao</span></div>";

// Default: strips JSON & code, extracts HTML inner text.
string clean = CavemanConversationToText.ExtractTextFromMarkdown(md);   // "Titolo\nCiao"

// With options: keep JSON and code verbatim.
string kept = CavemanConversationToText.ExtractTextFromMarkdown(md, new MarkdownExtractOptions
{
    KeepJson = true,
    KeepCode = true,
    ExtractHtml = true
});
```

---

## 5. Parsing a conversation — `CavemanConversationParser`

```csharp
var parser = new CavemanConversationParser();

// Parse(raw) — always returns a conversation (fallback = single Unknown message).
CavemanConversation conv = parser.Parse("""
[ { "role": "user", "content": "Ciao" }, { "role": "assistant", "content": "Salve" } ]
""");
Console.WriteLine(conv.Format);          // "openai-json" | "chatml" | "gemma" | "llama-inst" | "transcript" | "plain"
Console.WriteLine(conv.IsStructured);    // true

// TryParse(raw, out conv) — false when no structured format recognized.
if (parser.TryParse("User: hi\nAssistant: hello", out var t))
    Console.WriteLine(t.Messages.Count);

// Re-serialize a conversation.
string messagesJson = conv.ToMessagesJson(indented: true);   // [{ "role": "...", "content": "..." }]
string transcript   = conv.ToTranscript();                   // "User: ...\n\nAssistant: ..."
// CavemanMessage: Role, RawRole, Content, RoleLabel.
```

Recognized formats: OpenAI/Anthropic JSON (incl. content-block arrays, `{ "messages": [...] }`,
and `tool_calls` → `[tool_call: name(args)]`), ChatML, Gemma, Llama/Mistral, labeled transcripts.

---

## 6. Summarizing a whole conversation — `CavemanTextRank`

```csharp
var tr = new CavemanTextRank();
string conversation = /* markdown/JSON/HTML chat context, or a parsed-format transcript */;

// Simplest (flat, backward compatible):
string condensed = tr.RankAndSummarizeChat(conversation);

// Async + cancellation:
string condensedAsync = await tr.RankAndSummarizeChatAsync(conversation, options: null, ct: CancellationToken.None);

// Rich result with metrics, cost, structured output, trace:
ChatSummarizeResult res = tr.RankAndSummarizeChatDetailed(conversation, new ChatSummarizeOptions
{
    ParseConversation = true,
    KeepLastTurnsVerbatim = 4,
    CollectTrace = true
});
ChatSummarizeResult resAsync = await tr.RankAndSummarizeChatDetailedAsync(conversation);

Console.WriteLine($"{res.OriginalTokens} → {res.CompressedTokens} tokens ({res.EfficiencyPercentage:F0}%)");
Console.WriteLine($"Saved ≈ ${res.EstimatedSavedUsd:F4} / €{res.EstimatedSavedEur:F4}");
foreach (var b in res.Trace)
    Console.WriteLine($"  [{b.Index}] {b.Role} {b.Action} ({b.OriginalChars}→{b.FinalChars})");
string reusableMessages = res.Conversation.ToMessagesJson();
```

### `ChatSummarizeOptions`

```csharp
var opts = new ChatSummarizeOptions
{
    // Cleaning
    AlreadyClean = false, KeepJson = false, KeepCode = false, ExtractHtml = true,
    // Discourse detection thresholds
    MinDiscourseWords = 50, MinDiscourseSentences = 3, MinFunctionWordRatio = 0.20,
    SummaryRatio = 0.4f, MinSummarySentences = 2, MaxSummarySentences = 8,
    // Conversation structure
    ParseConversation = true, KeepLastTurnsVerbatim = 4, ShowRolePrefixes = true,
    Deduplicate = true, KeepSystemVerbatim = true, RespectSafety = true,
    // Fact pinning
    MustKeepPatterns = { @"CODICE-\d+" }, KeepNumbersAndDates = true,
    // Extra compression
    CompressKeptText = true, CompressionLevel = CavemanCompressionLevel.Aggressive,
    // Budget, model, cost, trace
    MaxTokens = 4000, TokenModel = LlmModel.Gpt4,
    UsdPer1KTokens = 0.03m, UsdToEurRate = 0.92m, CollectTrace = true,
    Iso3 = null
};

// Presets:
var p1 = ChatSummarizeOptions.Faithful();
var p2 = ChatSummarizeOptions.AgentMemory(maxTokens: 4000, model: LlmModel.Gpt4);
var p3 = ChatSummarizeOptions.CodingChat();
var p4 = ChatSummarizeOptions.Aggressive();
```

---

## 7. Token counting & cost

```csharp
// ModelTokenizer : ITokenCounter
var tk = new ModelTokenizer();
int n = tk.CountTokens("hello world", LlmModel.Gpt4);
(int gpt4, int gpt35, int llama3, int gemma3, int claude3) all = tk.CountAllModels("hello world");
string name = tk.ModelName(LlmModel.Claude3);     // "claude-3"

// Inject a custom counter (e.g. a real tiktoken implementation).
ITokenCounter myCounter = tk;
var trCustom = new CavemanTextRank(new FunctionWordProvider(), myCounter);

// Cost helpers (indicative; override prices as needed).
decimal price = CavemanCostEstimator.DefaultUsdPer1KTokens(LlmModel.Gpt4);   // 0.03
decimal usd = CavemanCostEstimator.Usd(tokens: 1200, usdPer1K: price);
decimal eur = CavemanCostEstimator.Eur(tokens: 1200, usdPer1K: price, usdToEur: 0.92m);
```

---

## 8. AI-agent toolkit

### Rolling working memory — `CavemanContextWindow`

```csharp
var window = new CavemanContextWindow(maxTokens: 4000, model: LlmModel.Gpt4)
{
    KeepLastTurns = 6,
    SessionId = "user-42",
    DeduplicateOnAppend = true
};

window.Append(CavemanRole.System, "You are a helpful assistant.");
window.Append(CavemanRole.User, "…");
window.Append(new CavemanMessage(CavemanRole.Assistant, "…"));

int tokens = window.TokenCount;          // auto-compacts on Append when over budget
                                         // (already-compacted turns are kept as-is, never re-summarized)
int turns  = window.MessageCount;
string transcript = window.Render();
string json = window.ToMessagesJson();
CavemanConversation snap = window.Snapshot();
window.Clear();
```

### Memory distillation — `CavemanMemoryExtractor`

```csharp
MemoryNote note = new CavemanMemoryExtractor().Extract(conversation, maxSentences: 5, maxKeywords: 10);
Console.WriteLine(note.Summary);                       // short recap
Console.WriteLine(string.Join(", ", note.Keywords));   // key terms / names
Console.WriteLine(note.Iso3);
Console.WriteLine(note.ToString());                    // "summary\nKey: a, b, c"
```

### Query-focused context — `CavemanRelevanceFilter`

```csharp
var rel = new CavemanRelevanceFilter();
string focused = rel.Focus(largeContext, query: "How do I reset the password?", topK: 5);
List<RelevanceHit> ranked = rel.Rank(largeContext, "carbonara recipe");   // Text, Score, Index
double score = rel.Score("La capitale d'Italia è Roma.", "capitale", "ita");
```

### Long-term memory store — `CavemanMemoryStore`

```csharp
var mem = new CavemanMemoryStore();
mem.Remember(note);
mem.Remember(new MemoryNote { Summary = "The deadline is Friday.", Keywords = { "deadline" } });
IReadOnlyList<MemoryNote> recalled = mem.Recall("when is the deadline?", topK: 3);
int count = mem.Count;
string saved = mem.Save(indented: true);
var mem2 = new CavemanMemoryStore();
mem2.Load(saved);
mem.Clear();
```

---

## 9. Persistence

```csharp
// Round-trip a window to JSON.
string state = window.Save();
CavemanContextWindow restored = CavemanContextWindow.Load(state);
ConversationState snapshot = window.ToState();
CavemanContextWindow fromState = CavemanContextWindow.FromState(snapshot);

// ConversationState helpers.
string json = snapshot.ToJson(indented: true);
ConversationState? back = ConversationState.FromJson(json);
string hash = ConversationState.Fingerprint("some turn content");

// Pluggable stores (IConversationStore).
IConversationStore fileStore = new FileConversationStore(@"C:\agent-state");
await window.SaveAsync(fileStore);                                  // uses window.SessionId
CavemanContextWindow? loaded = await CavemanContextWindow.LoadAsync(fileStore, "user-42");
await fileStore.DeleteAsync("user-42");

IConversationStore memStore = new InMemoryConversationStore();
await memStore.SaveAsync("s1", snapshot);
ConversationState? s = await memStore.LoadAsync("s1");
```

---

## 10. Developer services

```csharp
// Compress project context files (CLAUDE.md, README.md, TODO, …)
var ctxCompressor = new CavemanContextCompressor();
ContextCompressResult one = await ctxCompressor.CompressFileAsync(@"C:\proj\CLAUDE.md");
List<ContextCompressResult> all = await ctxCompressor.CompressDirectoryAsync(@"C:\proj");
string bundle = await ctxCompressor.GenerateCompressedContextAsync(@"C:\proj");

// Conventional commit from a diff
CommitSuggestion commit = new CavemanCommitGenerator().GenerateFromDiff(diffText);
Console.WriteLine(commit.FullMessage);   // also: Type, Scope, Subject, SubjectLength

// One-line PR review from a diff
var review = new CavemanReviewService().ReviewDiff(diffText);
Console.WriteLine($"{review.ChangedFiles} files, {review.TotalIssues} issues");
foreach (var c in review.Comments) Console.WriteLine(c);

// Token/cost stats (persists to %LOCALAPPDATA%/Caveman)
var stats = new CavemanStatsTracker();
stats.TrackResult(new CompressionResult { OriginalTokens = 100, CompressedTokens = 40 });
Console.WriteLine(stats.FormatFullReport());
Console.WriteLine(stats.FormatSessionReport());
stats.ResetSession();

// Safety guard
var guard = new CavemanSafetyGuard();
SafetyVerdict verdict = guard.Check("rm -rf / on production");
bool ok = guard.ShouldCompress("normal text");

// Cavecrew micro-agents
var crew = new CavecrewService();
var inv  = await crew.InvestigateAsync(@"C:\proj");
var built = await crew.BuildAsync("add caching layer", new List<string> { "Service.cs" });
var crewReview = crew.Review(diffText);   // Agent, Summary, Details

// Project wiki (AI-optimized markdown)
string wiki = await new CavemanWiki().GenerateAsync(
    projectFolderPath: @"C:\proj",
    maxFileSizeBytes: 100 * 1024,
    compressionLevel: CavemanCompressionLevel.Semantic,
    includeContents: true);
```

---

## 11. Semantic Kernel plugins — `Caveman.SemanticKernel` package

```csharp
// dotnet add package Caveman.SemanticKernel
using caveman.core.SemanticKernel.Plugin;
using Microsoft.SemanticKernel;

var builder = Kernel.CreateBuilder();
builder.Plugins.AddFromType<CavemanConversationPlugin>();   // summarize_conversation, fit_to_budget, extract_memory, focus_conversation, estimate_tokens
builder.Plugins.AddFromObject(new TokenOptimizerPlugin(new CavemanCompressionService())); // OptimizePrompt, estimate_tokens
builder.Plugins.AddFromType<CavemanWikiPlugin>();           // generate_project_wiki, get_project_summary, detect_project_type
builder.Plugins.AddFromType<CavemanServicesPlugin>();       // generate_commit, review_diff, check_safety, get_stats, …
var kernel = builder.Build();

// Direct (no kernel) calls also work:
var convPlugin = new CavemanConversationPlugin();
string summary = convPlugin.SummarizeConversation(conversation, parseRoles: true, keepLastTurns: 4);
string fitted  = convPlugin.FitToBudget(conversation, maxTokens: 4000, model: "gpt-4", keepLastTurns: 4);
string memory  = convPlugin.ExtractMemory(conversation, maxSentences: 5, maxKeywords: 10);
string focused = convPlugin.FocusConversation(largeContext, "reset password", topK: 5);
string tokens  = convPlugin.EstimateTokens(text, "gpt-4");
```

---

© 2026 Passaro Francesco Paolo — Digitalsolutions.it. Licensed under the Caveman License
(MIT + mandatory attribution).
