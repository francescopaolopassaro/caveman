# 🦴 Caveman — Prompt Compressor for LLMs

<img width="1197" height="766" alt="caveman_splash" src="https://github.com/user-attachments/assets/4b534140-c519-423f-b918-e705565a039f" />

**Caveman** is a self-contained C# library that drastically reduces the number of tokens in your LLM prompts (Gemma 3, Llama, GPT-4, …). It strips grammatical "noise" (articles, prepositions, conjunctions, auxiliaries) and normalises inflected words to their base form, keeping the semantic payload intact.

> "Why use many tokens when few tokens do trick?" — A caveman (and your wallet).

It is inspired by the token-saving idea behind the Caveman plugin for Claude, but it is an independent implementation written from scratch — **no porting and no runtime NLP-model dependency**.

---

## ✨ Highlights

- **Up to 70% token reduction** — slash API costs and speed up local inference.
- **50+ languages out of the box** — language data is embedded in the assembly; nothing to download at runtime.
- **No heavy NLP runtime** — pure lookup + heuristics; zero ML model dependencies.
- **Five compression levels** — `Light`, `Semantic`, `Aggressive`, `Statistical` (TF-IDF), `Syntactic` (rule-based grammatical-glue pruning, POS-gated hedge-clause elision).
- **POS lookup for 54/55 languages** — `FunctionWordProvider.GetPosTags`/`GetPosTag`: a frequency-baseline Universal POS tagger generated offline from the same Universal Dependencies treebanks as the lemma data, no runtime model.
- **Content-aware routing (v1.3.0)** — auto-detects JSON arrays, diffs, logs, HTML, code, tables and applies the best algorithm for each type.
- **JSON SmartCrusher** — lossless CSV/markdown compaction or BM25 row-drop with reversible CCR markers.
- **Output shaping** — inject verbosity-steering instructions into system prompts to prevent preamble/restatement generation.
- **Compression profiles** — `Light`, `Balanced`, `Agent`, `Aggressive` presets for one-line setup.
- **Batch & custom filters** — `CompressBatchAsync()` and user-defined POS-style filters.
- **Semantic Kernel plugins** + developer tools (commit/review/stats/safety/wiki).

---

## 🛠️ Installation

```bash
dotnet add package Caveman
```

That's it — all language data ships inside the package. There are **no models to install**.

> 📖 **Looking for a usage example of a specific method?** See
> [`docs/EXAMPLES.md`](docs/EXAMPLES.md) — a runnable snippet for every public API.
> Upgrading from 1.2.0 (Semantic Kernel plugins)? See [`docs/MIGRATION.md`](docs/MIGRATION.md).

### Quick start

```csharp
using caveman.core;

var compressor = new CavemanCompressionService();
string input = "I would like to know if it is possible to receive information about cheap restaurants in Rome.";

var result = await compressor.CompressAsync(input, CavemanCompressionLevel.Semantic);

Console.WriteLine($"Compressed: {result.CompressedText}");
Console.WriteLine($"Efficiency: {result.EfficiencyPercentage:F1}%");
Console.WriteLine($"🌿 Energy saved: {result.EstimatedEnergySavedMWh:F3} mWh");
```

The input language is detected automatically; you can also call `ApplyCompression(text, iso3, level)` to force a specific language (ISO 639-3 code).

---

## 🌐 Language detection (standalone)

You don't need to compress anything to use Caveman's language detector — it works on its own across all 50+ supported languages, with no model download:

```csharp
var caveman = new CavemanCompressionService();

string iso3 = caveman.DetectLanguage("Vorrei un tavolo per due persone, per favore.");
// -> "ita"

// or get confidence scores per language (ISO 639-3 -> ratio of matched stop words)
var scores = caveman.DetectLanguageScores("Where is the nearest train station?");
// -> { "eng": 0.42, ... }
```

The detector is also usable directly via `CavemanLanguageDetector` if you don't want the compression service:

```csharp
var detector = new CavemanLanguageDetector();
string iso3 = detector.Detect("Ich hätte gerne einen Kaffee.");   // -> "deu"
```

Detection is backed by a tiny embedded stop-word index, so it stays fast even though it scores every supported language.

---

## 📊 Benchmark — real token savings (v1.3.0)

All numbers are GPT-4 token counts measured on real inputs with `ModelTokenizer`.

### NLP Compression (`CavemanCompressionService`)

| Content type | Orig. tokens | Light | Semantic | Aggressive |
| :--- | ---: | :--- | :--- | :--- |
| Prose EN | 92 | −35.9% | −34.8% | −34.8% |
| Prose IT | 93 | −23.7% | −28.0% | **−51.6%** |
| Prose DE | 81 | −25.9% | −28.4% | −35.8% |
| Prose FR | 65 | −33.8% | −32.3% | −38.5% |
| Prose ES | 51 | −27.5% | −19.6% | −27.5% |
| JSON array | 256 | −66.8% | **−68.8%** | **−68.8%** |
| Git diff | 196 | −51.0% | −58.2% | −58.2% |
| Build log | 207 | −32.4% | −62.3% | −62.3% |
| Markdown table | 158 | −60.8% | **−64.6%** | **−64.6%** |
| HTML page | 192 | −45.3% | −49.0% | −50.0% |
| C# source code | 249 | −41.0% | −41.0% | −41.0% |

### Content Router (`CavemanContentRouter.FromProfile(Balanced)`)

The router auto-detects content type and picks the best algorithm:

| Content type | Orig. tokens | After | Savings | Strategy |
| :--- | ---: | ---: | :--- | :--- |
| Prose EN | 92 | 60 | −34.8% | NlpCompression |
| JSON array | 256 | 134 | **−47.7%** | JsonCrush:MarkdownTable |
| Git diff | 196 | 137 | −30.1% | DiffCompression |
| HTML page | 192 | 58 | **−69.8%** | HtmlExtract+NlpCompression |
| C# code | 249 | 184 | −26.1% | CodeCompression |

### NLP Compression levels

| Level | Applied logic | Typical savings |
| :--- | :--- | :--- |
| **Light** | Stop-word removal | ~25–35% |
| **Semantic** | Content words + lemmatization | ~30–69% |
| **Aggressive** | Lemmatization + generic-term pruning | ~35–70% |
| **Statistical** | TF-IDF word scoring instead of curated dictionaries — keeps words that are frequent in the prompt but rare against the language's standard-corpus reference | ~30–65% |
| **Syntactic** | Rule-based pruning: same content-word filtering as Aggressive, but a function word survives when it's grammatical glue directly touching a surviving word (e.g. a determiner in front of its noun), so the result reads as a terse but grammatical sentence | ~25–55% |

---

## 🌍 How it works

Caveman does **not** load any NLP model at runtime. Each language is described by a `worddata/<iso3>.yaml` source file with four sections, plus a handful of per-language supplementary resources:

- **`function_words`** — stop words, used both for compression and for language detection.
- **`lemmas`** — `inflected form → base form` map (e.g. `studying → study`, `gatti → gatto`).
- **`verbs`** — `base verb → [conjugated forms]`; folded into the lemma map at load time so every conjugation collapses to its base.
- **`proper_nouns`** — a name gazetteer; capitalized tokens in it are kept verbatim (so names like `Termini` or `München` are never compressed).

Supplementary per-language resources (each an independent brotli blob, loaded only if the feature using it runs):

| File | Contents | Coverage |
| :--- | :--- | :--- |
| `{iso3}.fw.yaml.br` | Hand-curated grammatical function words (articles, pronouns, prepositions, conjunctions, auxiliaries) | 7 curated languages |
| `{iso3}.excl.yaml.br` | Exclusive markers for language-detection disambiguation | 7 curated languages |
| `{iso3}.generic.yaml.br` | Generic/filler words pruned in `Aggressive`/`Syntactic` mode | 9 curated languages; every other language derives its own generic set algorithmically from verb-form richness instead (see `FunctionWordProvider.GetGenericWords`) |
| `{iso3}.pos.yaml.br` | Universal POS tag lookup (NOUN, VERB, ADJ, ADP, DET, …) — the most frequent tag Universal Dependencies observed per word form | 54 of 55 mappable languages |

For shipping, the main YAML sources are compiled (by `scripts/compile-worddata`) into compact embedded artifacts and a custom streaming parser keeps loading fast:

1. **Detection** reads a tiny brotli-compressed index (`_index.br`) holding only the stop words of every language, and scores the input by stop-word frequency — the large per-language data is never touched.
2. **Compression** then loads the one detected language from its brotli blob (`<iso3>.yaml.br`), decompresses + parses it once, caches it, and applies the selected level.

This keeps the assembly small (~12 MB instead of ~66 MB of raw text) while loading only the language actually used.

Function words are dropped by their **surface form** before lemmatization, so a noisy lemma can never reinject a stop word.

### Language data & provenance

The `lemmas`, `verbs` and `pos` data are all generated from the **[Universal Dependencies](https://universaldependencies.org/)** treebanks via `scripts/import-ud-lemmas` — a frequency-baseline POS tagger (most-frequent-tag-per-form lookup) alongside the lemma/verb extraction, no runtime model involved. Languages with little inflection (Chinese, Vietnamese, Thai, …) intentionally carry few or no lemma entries. Historical-stage treebanks that happen to share a modern language's UD name (`UD_Italian-Old`, `UD_Swedish-Old`, `UD_Icelandic-IcePaHC`, …) are excluded from every import, so archaic senses never outvote the modern one. See **NOTICE** for per-language attribution.

---

## 🚀 Batch compression & custom filters

**Batch** — compress many prompts in one call:

```csharp
string[] prompts =
{
    "I would like to know about cheap restaurants in Rome.",
    "Tell me how to get to the Colosseum from Termini station."
};

var results = await compressor.CompressBatchAsync(prompts, CavemanCompressionLevel.Semantic);
foreach (var r in results)
    Console.WriteLine($"{r.CompressedText}  (error: {r.ErrorMessage ?? "none"})");
```

**Custom filters** — override the default rules:

```csharp
var filter = new CompressionFilter
{
    KeepOnly = new HashSet<string> { "CONTENT", "PROPN" },        // keep content words & proper nouns
    CustomPredicate = token => token.Length > 2                    // skip very short tokens
};

var result = await compressor.CompressAsync(input, CavemanCompressionLevel.None, filter);
```

You can also blacklist categories with `Remove` (e.g. `"FUNC"`, `"PUNCT"`).

---

## 🧠 NLP pipeline — tokenize, detect sentences, summarize

Caveman now ships a language-agnostic NLP pipeline built entirely on its YAML word data — no external models, no API/LLM calls. Every component works across all 50+ supported languages.

### Tokenization (`CavemanTextSplitter`)

Splits text into tokens by Unicode category — Word, Number, Punctuation, Whitespace, Email, URL, Emoji, Newline — with no regex dependency. Handles Latin, CJK, Arabic, Devanagari, and any other script.

```csharp
var splitter = new CavemanTextSplitter();
var tokens = splitter.ParseText("Ciao, mondo! 🎉");

// 7 tokens: Ciao (Word) , (Punct)  (Space) mondo (Word) ! (Punct)  (Space) 🎉 (Emoji)

// Extract only words:
string[] words = splitter.ExtractWords("Caveman is 10/10!");  // ["Caveman", "is"]
```

### Sentence detection (`CavemanSentenceDetector`)

Splits text into sentences using punctuation + context + per-language abbreviation lists from the YAML data (so *Sig.*, *Dr.*, *e.g.* don't cause false splits).

```csharp
var detector = new CavemanSentenceDetector();
string[] sentences = detector.SplitText("Dr. Rossi abita a Roma. Oggi piove.", "ita");
// → "Dr. Rossi abita a Roma."  |  "Oggi piove."
```

### Summarization — two algorithms

Both algorithms accept a target sentence count or a compression ratio (0.0–1.0) and return the most important sentences from the original text.

| Algorithm | Strategy | Best for | Typical reduction |
| :--- | :--- | :--- | :--- |
| **TF-IDF + MMR** (`CavemanSummarizer`) | Rare-term weighting + position bias (first/last sentences boosted) + Maximum Marginal Relevance diversity | Factual/report text, news articles, documentation | 50–80% |
| **TextRank + MMR** (`CavemanTextRank`) | Sentence similarity graph → PageRank centrality + MMR diversity | Narrative/story text, blog posts, long-form content | 50–80% |

#### TF-IDF summarizer

```csharp
var summarizer = new CavemanSummarizer();

// By sentence count:
string summary = summarizer.CondenseText(longText, sentenceCount: 3, "ita");

// Or by ratio (30% of original):
string summary = summarizer.CondenseText(longText, ratio: 0.3f, "ita");

// Chain compression after summarization for even more savings:
var result = await summarizer.CompressWithSummaryAsync(longText, 3, "ita");
// result.Summary = condensed text
// result.CompressedText = summary with stop words removed, lemmatized
```

#### TextRank summarizer

```csharp
var textRank = new CavemanTextRank();

// By sentence count:
string summary = textRank.RankAndSummarize(longText, sentenceCount: 3, "ita");

// By ratio:
string summary = textRank.RankAndSummarize(longText, ratio: 0.3f, "ita");
```

#### When to use which

- **TF-IDF** picks sentences with rare, distinctive words — ideal for extracting key facts (e.g. *"Revenue grew 23% in Q3"*, *"The patient was diagnosed with X"*).
- **TextRank** picks sentences that are central in the narrative flow — ideal for preserving the storyline (e.g. *"Elia opened his jar of shadows and lit up the square"*).
- **Chained** (`CompressWithSummaryAsync`) first condenses to the essential sentences, then strips function words and normalises inflections — max token savings for LLM prompts.

### Summarizing a whole conversation (`RankAndSummarizeChat`)

When the input is a **full chatbot/LLM transcript** — a single big string mixing
prose, JSON tool output, markdown and HTML — you usually don't want to summarize
everything uniformly. `RankAndSummarizeChat` cleans the transcript, splits it into
blocks and runs TextRank **only** on the long natural-language passages, while
leaving short structured output (service results, keyword lists like
*"I.5 - Stemma, gonfalone, sigillo"*) and already-short passages **verbatim**.

```csharp
var textRank = new CavemanTextRank();

string conversation = /* the whole markdown/JSON/HTML chat context */;
string condensed = textRank.RankAndSummarizeChat(conversation);
```

A block is treated as a summarizable **discourse** only when it clears all three
heuristics — so a keyword list or a JSON-derived result is never mangled:

- a **word quota** (`MinDiscourseWords`),
- a minimum **stop-word density** (`MinFunctionWordRatio`) — prose has many
  function words, keyword/service lists almost none,
- a minimum **sentence count** (`MinDiscourseSentences`).

Tune any threshold via `ChatSummarizeOptions`:

```csharp
var options = new ChatSummarizeOptions
{
    MinDiscourseWords = 80,     // only compress longer passages
    SummaryRatio = 0.3f,        // keep ~30% of each discourse's sentences
    MaxSummarySentences = 5,
    Iso3 = "ita",               // skip auto-detection
    AlreadyClean = false        // run markdown/JSON/HTML extraction first
};

string condensed = textRank.RankAndSummarizeChat(conversation, options);
```

Markdown, JSON and HTML are stripped to plain text by
`CavemanConversationToText.ExtractTextFromMarkdown` (HTML **inner text is kept**,
entities decoded); call it yourself and pass `AlreadyClean = true` if your input
is already clean.

### Role/turn-aware conversations (multi-format)

Set `ParseConversation = true` and Caveman parses the *structure* of a conversation —
**OpenAI/Anthropic JSON** (incl. content-block arrays and `{ "messages": [...] }`),
**ChatML**, **Gemma**, **Llama/Mistral** (`[INST]`, `<<SYS>>`) and plain labeled
transcripts (`User:` / `Assistant:` / `Utente:` …) — then summarizes **per turn**:

```csharp
var options = new ChatSummarizeOptions
{
    ParseConversation    = true,   // detect roles/turns automatically
    KeepLastTurnsVerbatim = 4,     // recency window: keep the last 4 turns intact
    KeepSystemVerbatim    = true,  // never summarize the system prompt
    Deduplicate           = true,  // drop a system prompt repeated every turn
    RespectSafety         = true,  // keep security-critical turns verbatim
    KeepCode              = true,  // keep fenced code blocks (coding chats)
    MaxTokens             = 4000,  // hard budget: shrink → compress → drop oldest
    TokenModel            = LlmModel.Gpt4
};

var result = textRank.RankAndSummarizeChatDetailed(conversation, options);

Console.WriteLine($"{result.OriginalTokens} → {result.CompressedTokens} tokens " +
                  $"({result.EfficiencyPercentage:F0}% saved), format: {result.Format}");

// Re-feed the compacted conversation to your LLM API:
string messagesJson = result.Conversation.ToMessagesJson(indented: true);
```

`RankAndSummarizeChatDetailed` returns token metrics, per-block stats and the
compacted `CavemanConversation` (re-serializable with `ToMessagesJson()` /
`ToTranscript()`). When the budget can't be met by compression alone, the oldest
turns are dropped and replaced with a compact `[…]` marker.

---

## 🤖 AI-agent toolkit (forward-looking)

Three embedding-free primitives for building agents on top of Caveman:

```csharp
// 1) A rolling, token-budget-bounded working memory.
var window = new CavemanContextWindow(maxTokens: 4000) { KeepLastTurns = 6 };
window.Append(CavemanRole.User, "…");
window.Append(CavemanRole.Assistant, "…");
// It auto-compacts older turns whenever it would exceed the budget:
string contextForNextCall = window.ToMessagesJson();

// 2) Distill a durable memory (salient sentences + key terms) to carry across sessions.
MemoryNote memory = new CavemanMemoryExtractor().Extract(conversation, maxSentences: 5);
//   memory.Summary  -> short recap   |   memory.Keywords -> names/entities to remember

// 3) Query-focused context shaping: keep only what matters for the current question.
string focused = new CavemanRelevanceFilter()
    .Focus(largeContext, query: "How do I reset the password?", topK: 5);
```

These are also exposed to **Semantic Kernel** via `CavemanConversationPlugin`
(`summarize_conversation`, `fit_to_budget`, `extract_memory`, `focus_conversation`,
`estimate_tokens`), so the model can manage its own context window. The plugins ship in
the separate, optional **`Caveman.SemanticKernel`** package (`dotnet add package
Caveman.SemanticKernel`):

```csharp
var builder = Kernel.CreateBuilder();
builder.Plugins.AddFromType<CavemanConversationPlugin>();
var kernel = builder.Build();
```

### Persistence & long-term memory

Save/restore an agent's working memory, and keep a recallable long-term memory across
sessions — JSON, no external store required:

```csharp
// Persist a context window (atomic file store; pluggable backend via IConversationStore).
var store = new FileConversationStore(@"C:\agent-state");
window.SessionId = "user-42";
await window.SaveAsync(store);
var restored = await CavemanContextWindow.LoadAsync(store, "user-42");

// Long-term memory: remember distilled notes, recall the relevant ones for a query.
var memory = new CavemanMemoryStore();
memory.Remember(new CavemanMemoryExtractor().Extract(conversation));
var relevant = memory.Recall("what did we decide about the API?", topK: 3);
string json = memory.Save();   // persist anywhere
```

`CavemanContextWindow.DeduplicateOnAppend = true` makes appending an already-seen turn
idempotent (e.g. a system prompt re-sent every call).

### Accurate budgets & cost

Token counting is pluggable via **`ITokenCounter`** — inject a real BPE/tiktoken counter
for exact budgets: `new CavemanTextRank(new FunctionWordProvider(), myCounter)`.
`RankAndSummarizeChatDetailed` also reports the estimated money saved in **USD and EUR**
(`EstimatedSavedUsd` / `EstimatedSavedEur`; indicative prices, override via
`ChatSummarizeOptions.UsdPer1KTokens` / `UsdToEurRate`).

To never lose a critical figure, pin blocks with `MustKeepPatterns` (regex) or
`KeepNumbersAndDates = true` — matching blocks are kept verbatim.

### Console demos

The console app includes demo commands that run both algorithms on a fixed Italian story ("Il ladro di ombre"), a free-text mode, and a conversation mode:

```
/summarizer-demo     TF-IDF demo on the built-in story
/summarizer          Paste your own text for TF-IDF summarization
/textrank-demo       TextRank demo on the built-in story
/textrank            Paste your own text for TextRank summarization
/textrank-chat       Paste a full conversation; summarizes only the long discourses
```

---

## 🌿 Sustainability

Every token processed by an LLM has an energy cost. Caveman exposes a built-in estimator:

- **Energy saved**: ~0.005 mWh (5 µWh) per saved token.
- **CO₂ avoided**: ~0.4 mg per mWh saved.

Compressing a prompt from 1000 → 400 tokens saves ~3 mWh and avoids ~1.2 mg CO₂. At scale, that adds up.

---

## 🔌 Semantic Kernel integration

> The Semantic Kernel plugins live in the separate, optional **`Caveman.SemanticKernel`**
> package — the core `Caveman` package has no Semantic Kernel dependency.
> `dotnet add package Caveman.SemanticKernel`

```csharp
var builder = Kernel.CreateBuilder();
builder.Plugins.AddFromType<TokenOptimizerPlugin>();
var kernel = builder.Build();

var result = await kernel.InvokeAsync<CompressionResult>("TokenOptimizer", "OptimizePrompt", new()
{
    ["input"] = "I would like to know if it's possible to get pizza near Rome.",
    ["level"] = 2  // Semantic
});
```

- **`TokenOptimizerPlugin`** — prompt compression as a kernel function.
- **`CavemanWikiPlugin`** — on-demand, token-optimized project documentation (`generate_project_wiki`, `get_project_summary`, `detect_project_type`).
- **`CavemanServicesPlugin`** — exposes the developer services below.

---

## 🦴 Caveman services (developer toolkit)

| Service | What it does |
| :--- | :--- |
| `CavemanContextCompressor` | Compresses context files (CLAUDE.md, notes) into caveman-speak. |
| `CavemanCommitGenerator` | Conventional commit messages from a git diff, under 50 chars. |
| `CavemanReviewService` | Single-line PR review comments from a diff. |
| `CavemanStatsTracker` | Tracks token & cost savings across sessions (persists to `%LOCALAPPDATA%/Caveman`). |
| `CavemanSafetyGuard` | Auto-disables compression for security-critical/destructive content. |
| `CavecrewService` | Micro-agents: investigator / builder / reviewer. |
| `CavemanWiki` | AI-friendly, semantically compressed project documentation. |

```csharp
var wiki = new CavemanWiki();
string context = await wiki.GenerateAsync(@"C:\Dev\MyProject");
await File.WriteAllTextAsync("AI_CONTEXT.md", context);
```

---

## 📄 License & attribution

Caveman is released under the **Caveman License** — the MIT License **plus one mandatory condition**:

> **Any use of this library must clearly and visibly disclose that it uses
> "Caveman" by Passaro Francesco Paolo (Digitalsolutions.it).**

A disclosure such as the following, in your docs, an *About/credits* screen, or your repository, satisfies the requirement:

```
Powered by Caveman — © Passaro Francesco Paolo, Digitalsolutions.it (https://digitalsolutions.it)
```

See [`LICENSE`](LICENSE) for the full terms.

**Bundled language data** under `worddata/` is derived from the Universal Dependencies treebanks and is provided under their respective licenses (predominantly **CC BY-SA / CC BY**), *not* under the Caveman software license. See [`NOTICE`](NOTICE) for attribution and source treebanks.

---

## 🤝 Contributing

Pull requests are welcome. For major changes, please open an issue first to discuss what you would like to change.

To regenerate the language data from Universal Dependencies and recompile the
embedded artifacts:

```bash
# 1. import lemmas / verbs / proper nouns into worddata/*.yaml (the source)
dotnet run --project scripts/import-ud-lemmas -- --all     # all languages
dotnet run --project scripts/import-ud-lemmas -- ita fra   # specific languages

# 2. compile worddata/*.yaml -> worddata/*.yaml.br + worddata/_index.br (embedded)
dotnet run --project scripts/compile-worddata

# 3. rebuild the package so it embeds the fresh artifacts
dotnet pack caveman.core.csproj -c Release
```

© 2026 Passaro Francesco Paolo — Digitalsolutions.it
