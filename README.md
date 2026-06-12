# 🦴 Caveman — Prompt Compressor for LLMs

<img width="1197" height="766" alt="caveman_splash" src="https://github.com/user-attachments/assets/4b534140-c519-423f-b918-e705565a039f" />

**Caveman** is a self-contained C# library that drastically reduces the number of tokens in your LLM prompts (Gemma 3, Llama, GPT-4, …). It strips grammatical "noise" (articles, prepositions, conjunctions, auxiliaries) and normalises inflected words to their base form, keeping the semantic payload intact.

> "Why use many tokens when few tokens do trick?" — A caveman (and your wallet).

It is inspired by the token-saving idea behind the Caveman plugin for Claude, but it is an independent implementation written from scratch — **no porting and no runtime NLP-model dependency**.

---

## ✨ Highlights

- **Up to 70% token reduction** — slash API costs and speed up local inference.
- **50+ languages out of the box** — language data is embedded in the assembly; nothing to download at runtime.
- **No heavy NLP runtime** — pure lookup + heuristics over per-language word data. The only package dependency is `Microsoft.SemanticKernel` (for the optional plugins).
- **Three compression levels** — `Light`, `Semantic`, `Aggressive`.
- **Fast language detection** — a streaming parser reads only the stop-word section of each language to identify the input.
- **Batch & custom filters** — `CompressBatchAsync()` and user-defined POS-style filters.
- **Semantic Kernel plugins** + a suite of developer services (commit/review/stats/safety/wiki).

---

## 🛠️ Installation

```bash
dotnet add package Caveman
```

That's it — all language data ships inside the package. There are **no models to install**.

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

## 📊 Compression levels

| Level | Applied logic | What is kept | Typical savings |
| :--- | :--- | :--- | :--- |
| **Light** | Stop-word removal | Everything except function words & punctuation | ~25–30% |
| **Semantic** | Content selection + lemmatization | Content words, normalised to their base form | ~50% |
| **Aggressive** | Lemmatization + generic/descriptive pruning | Core nouns/verbs in base form | ~70% |

### Example

| State | Prompt | Size |
| :--- | :--- | :--- |
| **Original** | "I would like to know if it is possible to have a margherita pizza immediately." | 100% |
| **Light** | "like know possible have margherita pizza immediately" | ~70% |
| **Semantic** | "know possible have margherita pizza immediately" | ~55% |
| **Aggressive** | "know possible margherita pizza" | ~40% |

---

## 🌍 How it works

Caveman does **not** load any NLP model at runtime. Each language is described by a `worddata/<iso3>.yaml` source file with four sections:

- **`function_words`** — stop words, used both for compression and for language detection.
- **`lemmas`** — `inflected form → base form` map (e.g. `studying → study`, `gatti → gatto`).
- **`verbs`** — `base verb → [conjugated forms]`; folded into the lemma map at load time so every conjugation collapses to its base.
- **`proper_nouns`** — a name gazetteer; capitalized tokens in it are kept verbatim (so names like `Termini` or `München` are never compressed).

For shipping, these YAML sources are compiled (by `scripts/compile-worddata`) into compact embedded artifacts and a custom streaming parser keeps loading fast:

1. **Detection** reads a tiny brotli-compressed index (`_index.br`) holding only the stop words of every language, and scores the input by stop-word frequency — the large per-language data is never touched.
2. **Compression** then loads the one detected language from its brotli blob (`<iso3>.yaml.br`), decompresses + parses it once, caches it, and applies the selected level.

This keeps the assembly small (~13 MB instead of ~68 MB of raw text) while loading only the language actually used.

Function words are dropped by their **surface form** before lemmatization, so a noisy lemma can never reinject a stop word.

### Language data & provenance

The `lemmas` and `verbs` data are generated from the **[Universal Dependencies](https://universaldependencies.org/)** treebanks via `scripts/import-ud-lemmas`. Languages with little inflection (Chinese, Vietnamese, Thai, …) intentionally carry few or no lemma entries. See **NOTICE** for per-language attribution.

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

### Console demos

The console app includes two demo commands that run both algorithms on a fixed Italian story ("Il ladro di ombre") plus a free-text mode:

```
/summarizer-demo     TF-IDF demo on the built-in story
/summarizer          Paste your own text for TF-IDF summarization
/textrank-demo       TextRank demo on the built-in story
/textrank            Paste your own text for TextRank summarization
```

---

## 🌿 Sustainability

Every token processed by an LLM has an energy cost. Caveman exposes a built-in estimator:

- **Energy saved**: ~0.005 mWh (5 µWh) per saved token.
- **CO₂ avoided**: ~0.4 mg per mWh saved.

Compressing a prompt from 1000 → 400 tokens saves ~3 mWh and avoids ~1.2 mg CO₂. At scale, that adds up.

---

## 🔌 Semantic Kernel integration

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
