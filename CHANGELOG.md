# Changelog

All notable changes to **Caveman** are documented in this file.

## [Unreleased]

### Added
- **`Statistical` compression level** — TF-IDF word scoring as an alternative to curated dictionaries: scores each word by frequency in the prompt vs. how many of the prompt's own sentences contain it, grounding "common" words against the language's curated function/generic-word lists as the standard-corpus reference. Keeps words at/above the adaptive median score; never empties a sentence that had content.
- **`Syntactic` compression level** — rule-based pruning: same content-word filtering as `Aggressive`, but a function word survives when it is grammatical glue directly touching a surviving word (e.g. a determiner right in front of its noun), so the result reads as a terse but grammatical sentence rather than a keyword bag.
- `FunctionWordProvider.GetGenericWords(iso3)` — generic/filler words (e.g. "want", "know", "time") pruned in `Aggressive`/`Syntactic` mode, loaded per-language from `{iso3}.generic.yaml.br` for 9 curated languages. For every other language (40+), derived algorithmically from that language's own verb data — the most richly inflected verb lemmas are ranked as the generic set — instead of guessing translations.
- `mcp compress`/`compress_batch` tools now accept `"statistical"` and `"syntactic"` as level values.
- **`FunctionWordProvider.GetPosTags(iso3)` / `GetPosTag(word, iso3)`** — a Universal POS lookup (NOUN, VERB, ADJ, ADP, DET, …) per language: the most frequent tag Universal Dependencies treebanks observed for each word form. A frequency-baseline tagger, not a model — a dictionary lookup generated offline by `scripts/import-ud-lemmas` from the same UD source already used for lemmas/verbs, covering 54 of 55 mappable languages (only Kannada has no UD treebank). `Syntactic` now uses it to safely elide a leading hedging/matrix clause ("I kindly ask you to…", "vorrei che tu…") in favour of the sentence's last verb — a first attempt at this without real POS evidence broke on verb/preposition homographs (Italian "entro" = "by/within" vs. "I enter") and was rolled back; with a POS tag per word it's safe, and is further restricted to only fire when no real content noun sits between the candidate verbs, so coordinated clauses ("I bought bread and ate cake") are never mistaken for a hedge clause and gutted.
- **`CavemanLogCompressor` now folds repeated/near-identical consecutive lines** (after normalising digits, hex addresses and paths to placeholders — the same normalisation the warning-dedup step already used) into one line plus a "(repeated Nx)" count, regardless of total log length. Previously line-dropping only kicked in above `MaxTotalLines` (80 by default), so a short but repetitive log (e.g. many identical passing-test lines) got zero benefit; folding now runs unconditionally, with severity-based line selection layered on top only if the folded log still exceeds the threshold. New `MinFoldRun` property (default 3) sets how many consecutive repeats are required before folding, so two incidental repeats of ordinary prose are left alone.
- **`CavemanCodeCompressor.Compress(code, skeletonize: true)`** — a new opt-in parameter (default `false`, existing default behaviour unchanged) that additionally replaces function/method bodies with a placeholder, keeping only signatures. Uses real brace-depth counting (not regex) to find each body's matching close, correctly handling arbitrary nesting and braces inside string/char literals; Python bodies are found by indentation instead. Only leaf `def`/method bodies are collapsed — a `class` or C-style type declaration is a container, never itself replaced — and bodies under ~40 characters or Python bodies with fewer than two statements are left alone since there's nothing meaningful to hide. Unlike the default comment-stripping pass (always a valid subset of the input), skeletonization is lossy by design, hence opt-in.
- **`CavemanJsonCrusher.Bm25Delta`** (default 1.0) — BM25+ instead of plain BM25 for lossy row-drop: a lower-bound term is added to every non-zero term match, so a genuinely relevant row that only mentions the query term once no longer scores near zero just because it's a long row. Set to 0 to recover plain BM25.
- **`CavemanSimHash`** — a 64-bit Charikar SimHash fingerprint utility (FNV-1a feature hashing, no external dependency) for near-duplicate text detection. `CavemanLogCompressor.FuzzyFold` (default `false`, opt-in) uses it to fold consecutive log lines that are near-duplicates — not just exact matches after digit/hex/path normalisation — e.g. templated lines that substitute a username or IP address. Off by default: exact-match folding is the safer, already-proven behaviour.
- **`CavemanTopicSegmenter`** — TextTiling-style topic segmentation (Hearst, 1997): groups sentences into blocks, scores vocabulary-similarity between adjacent blocks, and cuts where similarity dips sharply relative to the surrounding peaks. Pure term-frequency cosine similarity, no external dependency. `CavemanSummarizer.CondenseTextTopicAware(text, sentenceCount, iso3)` is a new, separate method (existing `CondenseText` overloads are unchanged) that segments first and allocates the sentence budget proportionally across topics, so a summary can't let one statistically dense topic dominate and starve the others entirely — falls back to `CondenseText` when segmentation finds no real topic structure.
- **`CavemanRetriever`** — BM25+ ranking over arbitrary text chunks (sentences, conversation turns, log lines, …), with `RetrieveWithFeedback` adding an RM3 pseudo-relevance-feedback pass: an initial BM25 ranking builds a relevance model from its own top results' vocabulary, expands the query with that model's top terms, and re-ranks — surfacing genuinely relevant chunks that don't literally contain the query's words (e.g. a query for "car" finding a chunk about "Tesla and Rivian battery range" via the "battery"/"range" vocabulary shared with the chunks that do say "car").
- **A comprehensibility test suite** (`CavemanComprehensibilityTests`) that treats "would a reader — human or AI — still get the point from the compressed text alone?" as a directly testable property: each of several realistic multi-language prompts is paired with the specific facts (names, places, key nouns) needed to answer a concrete question about it, and every compression level is checked for whether those facts survive.

### Fixed
- **Cross-language generic-word contamination**: `Aggressive`/`Syntactic` used to prune generic words from a hardcoded list shared across a whole language *family* (e.g. Italian, Spanish, French and Portuguese all shared one "romance" bucket), so a Spanish-only filler word could be wrongly stripped from Italian text. Now loaded per-language.
- **Silent verb loss**: the `"are"` suffix in the Romance descriptive-word heuristic matched every Italian first-conjugation infinitive verb ("analizzare", "parlare", …) in addition to its intended "-are" adjectives ("regolare", "particolare"), so `Aggressive`/`Syntactic` compression could delete a sentence's main verb. The suffix was removed from the heuristic.
- **Silent loss of domain-specifying adjectives**: same failure mode, found by the new comprehensibility test suite — the `"al"`/`"ical"` suffixes in the English descriptive-word heuristic matched decorative adjectives ("occasional") but just as often matched a domain-specifying one that carries real information ("quarterly **financial** report" lost "financial" in `Aggressive`/`Syntactic`). Both suffixes were removed.
- **`Aggressive` could compress a content-bearing prompt to an empty string** (e.g. a one-word imperative like "Vai."/"Go." whose only word was itself curated as generic/filler). Added a safety floor: falls back to `Semantic`-level filtering instead of returning nothing.
- Italian curated function words were missing the accented copula "è" (only the unaccented conjunction "e" was present).
- `FunctionWordProvider` no longer hardcodes the 7 curated languages' function-word lists in code — loaded from `{iso3}.fw.yaml.br` embedded resources instead, matching how exclusive markers and generic words are already sourced.
- **Tokenizer split words apart in scripts that use combining marks** (Kannada, Hindi, Tamil, Thai, …): the word-matching regex only matched `\p{L}` (Letter), but these scripts attach vowel signs/virama as separate `\p{M}` (Mark) codepoints, so e.g. Kannada "ಪರೀಕ್ಷೆ" fragmented into "ಪರ", "ಕ", "ಷ". Fixed in both `CavemanCompressionService` and `CavemanLanguageDetector`.
- **"torta" (Italian "cake") was lemmatized to "torcere" ("to twist")**: the `import-ud-lemmas` pipeline mixed `UD_Italian-Old` (Dante-era 14th-century Italian, where "torta" is the archaic feminine participle of "torcere") into the modern-Italian corpus, letting an archaic sense outvote the modern one. `UD_Swedish-Old` had the same problem. Historical-stage treebanks (`-Old` suffix, which — unlike Ancient/Classical treebanks for other languages, e.g. `UD_Ancient_Greek-*` — share the modern language's UD name and so silently entered the modern corpus) are now excluded from every language's import.
- **The exclusion above only stops *new* contamination — the already-imported `ita`/`swe` lemma and verb data still carried entries learned from `UD_Italian-Old`/`UD_Swedish-Old` in earlier runs**, because the import pipeline merges by *union* (it adds what UD currently observes but never removes a form/lemma UD stops observing, to protect hand-curated entries from being dropped when a treebank is temporarily unavailable). Removing just the one bad "torta" entry left siblings like "torte" (also an archaic `torcere` participle) still mapped wrong. Fixed by clearing and fully rebuilding `ita`/`swe` lemmas + verbs from the now-`-Old`-free treebank set, instead of merging on top of the tainted data.
- A full-org treebank scan (463 repos) found `UD_Icelandic-IcePaHC` ("Parsed Historical Corpus") had the same silent-modern-prefix problem under a different naming convention; `isl` was rebuilt the same way. `UD_Romanian-Nonstandard` had it too, under a name with *no* historical hint at all — its README describes 17th–18th century biblical/folklore Romanian (a 1648 New Testament, texts from 1673/1743/1818) OCR-transliterated into the modern Latin alphabet; `ron` was rebuilt without it. (`UD_Romanian-MolDoRo`, Cyrillic-script historical Moldovan Romanian, was kept — a different script has no realistic collision risk against Latin-script modern forms.) A systematic audit for *further* undiscovered cases (cross-referencing every language's `{iso3}.pos.yaml.br` most-frequent-tag against its verb-derived lemma mappings) was attempted but produced ~95%+ false positives — ordinary noun/verb pairs of the same lexeme (e.g. English "accounts"→"account") are indistinguishable from true unrelated homonyms (like "torta"→"torcere") without semantic data the tagger doesn't have, so it was not applied.
- `import-ud-lemmas`: fixed a `Process` deadlock that could hang the import indefinitely — `git clone`'s progress output was redirected but never read until after `WaitForExit()`, so a large enough stderr write filled the OS pipe buffer and blocked the child process forever. Now reads stdout/stderr asynchronously, disables interactive credential prompts (`GIT_TERMINAL_PROMPT=0`), and enforces a 3-minute per-clone timeout. Also caps per-treebank file count (large multi-thousand-file treebanks like `UD_Portuguese-Bosque` are size-sampled instead of read in full, which was pathologically slow under Windows Defender's per-file scanning).

## [1.4.0](https://github.com/francescopaolopassaro/caveman/releases/tag/v1.4.0) - 2026-06-24

### Highlights

- **Two-pass language detection** — `CavemanLanguageDetector` now runs a second pass after the raw stop-word scoring. It checks for *exclusive markers*: words that appear in one curated language but in no other. A single exclusive-marker hit beats an ambiguous tie between languages that share common words (`per`, `a`, `in`, `via` etc.). This eliminates the most common false-positive category (e.g. short Italian sentences detected as English or Dutch).
- **`FunctionWordProvider.GetExclusiveMarkers(iso3)`** — new public method backed by embedded `.excl.yaml.br` files for 7 curated languages (`eng`, `ita`, `fra`, `deu`, `spa`, `por`, `nld`). Returns a `HashSet<string>` of words unique to that language; empty when no file is available.
- **Curated-preference tiebreak** — when the raw-scoring winner is a YAML-only language and a curated language scores ≥ 75% as high, the curated language is preferred. Improves reliability on short multilingual snippets.
- **`Caveman.Mcp` package** — new standalone NuGet (`Caveman.Mcp`) ships an MCP stdio server exposing five tools (`compress`, `detect_language`, `route_content`, `summarize`, `compress_batch`) for Claude Code, OpenCode, Cursor, Windsurf and any MCP-compatible agent. The core `Caveman` package stays dependency-free.

### Added
- `FunctionWordProvider.GetExclusiveMarkers(string iso3)` — loads `{iso3}.excl.yaml.br` from embedded resources.
- Embedded exclusive-marker files: `eng.excl.yaml.br`, `ita.excl.yaml.br`, `fra.excl.yaml.br`, `deu.excl.yaml.br`, `spa.excl.yaml.br`, `por.excl.yaml.br`, `nld.excl.yaml.br`.

### Changed
- `CavemanLanguageDetector.Detect()` — upgraded to two-pass algorithm (exclusive-marker boost + curated-preference tiebreak). Output is backward-compatible; detection accuracy improves on ambiguous/short texts.
- `caveman.core.csproj` — `mcp/` and `copilot/` subfolders excluded from compilation (separate packages).
- `PackageTags` updated: added `MCP`, `Claude`, `Copilot`.

### Fixed
- False-positive detections on texts containing words shared across curated languages (e.g. Italian short sentences previously returned `eng` or `nld`).

[1.4.0]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.4.0

## [1.3.0](https://github.com/francescopaolopassaro/caveman/releases/tag/v1.3.0) - 2026-06-23

A **content-aware compression pipeline** release
Every type of content an LLM reads now has a dedicated compressor; the new `CavemanContentRouter` detects the type and dispatches automatically. Everything is **fully additive**: existing APIs are unchanged.

### Highlights

* **`CavemanContentRouter`** — single entry point that detects content type (JSON array, log/stacktrace, search results, git diff, HTML, code, tabular, plain text) and applies the best algorithm. Includes a two-tier **skip-set + result cache** (30-min TTL) and a **circuit breaker** that falls back to passthrough after 3 consecutive failures. An **inflation guard** reverts to the original if the output ever exceeds the input token count. One-line setup via `CavemanContentRouter.FromProfile(CompressionProfile)`.
* **`CavemanJsonCrusher`** — JSON array compressor with two paths:
  * *Lossless*: renders uniform arrays as a markdown table (≤6 keys, ≤50 rows) or CSV (`#schema:` header + RFC 4180 rows) when savings ≥ 15%.
  * *Lossy*: BM25 row-drop with first-30%/last-15% anchors and anomaly detection; emits a `<<ccr:HASH,dropped=N/TOTAL>>` marker so the LLM can retrieve dropped rows via `CavemanCcrStore`.
* **`CavemanLogCompressor`** — severity scoring (ERROR/WARN/INFO/DEBUG), multi-language stack-frame detection, context windowing (±2 lines around errors), conservative warning deduplication (digits → N, hex → ADDR).
* **`CavemanSearchCompressor`** — parses grep/ripgrep output, groups by file, scores matches by query relevance + error signal, keeps first/last anchors per file; selects top-N files by aggregate score.
* **`CavemanDiffCompressor`** — preserves all `+`/`-` lines; trims context to `MaxContextLines` (default 2); drops pure-context hunks and respects `MaxHunksPerFile` / `MaxFiles`.
* **`CavemanHtmlExtractor`** — pure-regex HTML-to-text: removes scripts/styles, converts block elements to newlines, decodes HTML entities. No external deps.
* **`CavemanCodeCompressor`** — multi-language comment stripper (C#/Java/JS/TS/Go/Rust, Python, Ruby, SQL, Shell) + blank-line collapsing. Output is always a safe structural subset of the input.
* **`CavemanTabularCompressor`** — CSV and markdown-table compressor: drops empty/constant columns, samples rows by query relevance with first/last anchors.
* **`CavemanOutputShaper`** — appends verbosity-steering instructions to system prompts (`SkipCeremony` / `NoRestatement` / `ConclusionsOnly` / `MinimumTokens`). Byte-stable per level, idempotent, removable.
* **`CavemanCacheAligner`** — detects volatile tokens (UUIDs, ISO-8601 datetimes, JWTs, hex hashes) in system prompts that bust the LLM KV-cache.
* **`CavemanCcrStore`** — thread-safe in-memory store (5-min TTL) for dropped JSON rows; keyed by 12-char SHA-256 hex prefix.
* **`CavemanCompressionCache`** — two-tier O(1) cache: Tier 1 skip-set for non-compressible content, Tier 2 result cache with 30-min TTL and lazy eviction.
* **`CavemanWasteAnalyzer`** — estimates wasted tokens from HTML noise, base64 blobs, excessive whitespace, and large inline JSON. Non-destructive; use alongside a compressor.
* **`CavemanSharedContext`** — inter-agent compressed context store: `Put` compresses + stores, `Get` serves the compressed copy by default (tokens saved on every agent read), `Get(full:true)` returns the original.
* **`CavemanMessageDeduplicator`** — hash-based duplicate detection across conversation messages; flags re-reads (gap > 3 messages) vs. polling. `RemoveDuplicates` replaces duplicates with `[duplicate of message #N]`.
* **`CompressionProfile` enum** — four presets (`Light`, `Balanced`, `Agent`, `Aggressive`) that pre-configure the router, JSON crusher and prose level. Use `CavemanContentRouter.FromProfile(profile)` for zero-config setup.
* **`ICompressionService.CompressContentAsync`** — new method on the existing interface with a backward-compatible default implementation (passthrough). `CavemanCompressionService` overrides it to delegate to `CavemanContentRouter`.
* **`CavemanContentRouterBuilder`** — gains `WithProseLevel`... *[il testo si interrompe qui]*

[1.3.0]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.3.0

## [1.3.1](https://github.com/francescopaolopassaro/caveman/releases/tag/v1.3.1) - 2026-06-23

Patch release — republish of 1.3.0 with all new services correctly included in the assembly. No API changes.

### Fixed
- `CavemanContentRouter`, `CavemanJsonCrusher`, `CavemanLogCompressor`, `CavemanSearchCompressor`, `CavemanDiffCompressor`, `CavemanHtmlExtractor`, `CavemanCodeCompressor`, `CavemanTabularCompressor`, `CavemanWasteAnalyzer`, `CavemanCacheAligner`, `CavemanCcrStore`, `CavemanCompressionCache`, `CavemanSharedContext`, `CavemanMessageDeduplicator` were missing from the 1.3.0 package; all are present in 1.3.1.

[1.3.1]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.3.1

## [1.2.1] - 2026-06-13

A **special, agent-ready** release: Caveman now understands the *structure* of a
human↔chatbot conversation (roles, turns, multiple AI formats) and ships
forward-looking primitives for building AI agents — a rolling context window, a
memory distiller and a query-focused context filter — plus a dedicated Semantic
Kernel plugin. Everything is **additive and backward compatible**: the existing
`RankAndSummarizeChat(string)` behaves exactly as before unless you opt in.

### Highlights
- **Structured conversation model + multi-format parser** — `CavemanConversation`,
  `CavemanMessage`, `CavemanRole` and `CavemanConversationParser`, which detects and
  parses **OpenAI/Anthropic JSON** (including Anthropic content-block arrays and the
  `{ "messages": [...] }` wrapper), **ChatML** (`<|im_start|>`), **Gemma**
  (`<start_of_turn>`), **Llama/Mistral** (`[INST]`, `<<SYS>>`) and **plain labeled
  transcripts** (`User:` / `Assistant:` / `Utente:` …), with a safe fallback.
- **Role/turn-aware chat summarization** — `RankAndSummarizeChat` can parse roles,
  keep the **last N turns verbatim** (recency window), keep the **system prompt**
  verbatim, **deduplicate** repeated blocks, skip **safety-critical** blocks, keep
  **JSON/code** verbatim (`KeepJson`/`KeepCode`), optionally **caveman-compress** the
  kept text, and honor a hard **token budget** (shrink → compress → drop oldest, with
  a `[…]` truncation marker). All via additive `ChatSummarizeOptions`.
- **Metrics + structured output** — new `RankAndSummarizeChatDetailed` returns a
  `ChatSummarizeResult` with per-model token counts, per-block stats and the compacted
  `CavemanConversation` (re-serializable via `ToMessagesJson()` / `ToTranscript()`).
- **AI-agent primitives (forward-looking):**
  - `CavemanContextWindow` — a rolling, **token-budget-bounded working memory**:
    append turns; it auto-compacts older ones to always fit the model's window.
  - `CavemanMemoryExtractor` — distills a **durable memory** (salient sentences +
    key terms/entities) so an agent can forget the transcript but keep what matters.
  - `CavemanRelevanceFilter` — **query-focused, embedding-free** context shaping:
    keep only the blocks most relevant to the current question.
- **Semantic Kernel** — new `CavemanConversationPlugin` (`summarize_conversation`,
  `fit_to_budget`, `extract_memory`, `focus_conversation`, `estimate_tokens`) and an
  `estimate_tokens` function added to `TokenOptimizerPlugin`.
- **Package split** — the core `Caveman` package **no longer depends on
  Microsoft.SemanticKernel**. All Semantic Kernel plugins moved to a new, optional
  **`Caveman.SemanticKernel`** package (which references `Caveman`). Install
  `Caveman.SemanticKernel` only if you use the plugins; it pulls in the core
  automatically. Compression/summarization/agent APIs stay in the dependency-free core.
- **Pluggable token counting** — new `ITokenCounter` abstraction (`ModelTokenizer`
  implements it); inject your own (real BPE/tiktoken) into `CavemanTextRank` /
  `CavemanContextWindow` for accurate budgets.
- **Cost estimate (USD + EUR)** — `ChatSummarizeResult` now reports
  `EstimatedSavedUsd` / `EstimatedSavedEur` (indicative per-model prices via
  `CavemanCostEstimator`, overridable through `ChatSummarizeOptions.UsdPer1KTokens` /
  `UsdToEurRate`).
- **Better relevance & fact pinning** — `CavemanRelevanceFilter` now **lemmatizes**
  terms (so *passwords* matches *password*) and exposes a `Score` method;
  `ChatSummarizeOptions` gains `MustKeepPatterns` (regex) and `KeepNumbersAndDates`
  to keep factual blocks verbatim.
- **Parser fidelity** — `CavemanConversationParser` preserves OpenAI **tool/function
  calls** as `[tool_call: …]` notes and adds `[image]`/`[type]` placeholders for
  non-text content blocks instead of dropping them.
- **Persistence & agent memory** — `ConversationState`/`PersistedTurn` DTOs,
  `CavemanContextWindow.Save()/Load()` (+ `DeduplicateOnAppend` idempotency),
  `IConversationStore` with `FileConversationStore` (atomic writes) and
  `InMemoryConversationStore`, and a `CavemanMemoryStore` that recalls relevant
  `MemoryNote`s for a query (embedding-free) and round-trips to JSON.
- **Shared lemma map** — `FunctionWordProvider.GetLemmaMap(iso3)` centralizes the
  inflected→base form map used by the compressor, summarizer and relevance/recall.
- **Async chat API** — `RankAndSummarizeChatAsync` / `RankAndSummarizeChatDetailedAsync`
  with `CancellationToken` checkpoints in the block-processing and budget loops.
- **Config presets** — `ChatSummarizeOptions.Faithful()`, `.AgentMemory(maxTokens, model)`,
  `.CodingChat()`, `.Aggressive()`.
- **Observability** — opt-in `ChatSummarizeOptions.CollectTrace` populates
  `ChatSummarizeResult.Trace` (per-block action: summarized / compressed / kept /
  critical / dropped / deduplicated, with sizes).
- **Examples** — `docs/EXAMPLES.md` documents every public method with a runnable snippet;
  `docs/MIGRATION.md` covers the package split.
- **Fluent builders** — `CavemanTextRank.CreateBuilder()` and
  `CavemanContextWindow.CreateBuilder()` tame the constructors now that there are several
  injectable seams.
- **Incremental compaction** — `CavemanContextWindow` no longer re-summarizes turns it
  already compacted: a previously compacted turn is kept as-is on later compactions
  (still droppable under the budget), avoiding progressive quality loss. Exposed for
  advanced use via `ChatSummarizeOptions.VerbatimContentHashes`.
- **Hardened `CavemanSafetyGuard`** — word-boundary-aware matching replaces naive
  substring checks, eliminating false positives (e.g. *dos* in "Windows", *rce* in
  "commerce"/"source", *production* in "reproduction") while still catching standalone
  acronyms (DDoS, XSS, TLS) and command strings (`rm -rf`, `> /dev/sda`). A new
  constructor accepts extra critical/warning patterns: `new CavemanSafetyGuard(extraCritical, extraWarning)`.
- **Abstractions / DI seams** — `ISummarizer` (implemented by both `CavemanSummarizer`
  and `CavemanTextRank`, via a unified `Summarize(text, count|ratio, iso3?)`),
  `IConversationParser` (implemented by `CavemanConversationParser`) and
  `ICompressionService` (implemented by `CavemanCompressionService`). `CavemanTextRank`
  accepts an injectable parser and compression engine; `CavemanSummarizer` accepts an
  injectable compression engine; `CavemanContextWindow` forwards both an `ITokenCounter`
  and an `ICompressionService` to its internal pipeline. Plus `ILanguageDetector`
  (implemented by `CavemanLanguageDetector`), injectable into `CavemanCompressionService`
  and `CavemanTextRank`. The full DI seam set is now: `ITokenCounter`, `IConversationStore`,
  `ISummarizer`, `IConversationParser`, `ICompressionService`, `ILanguageDetector`.

### Added
- `CavemanConversation`, `CavemanMessage`, `CavemanRole`, `CavemanConversationParser`.
- `ChatSummarizeResult`, `CavemanTextRank.RankAndSummarizeChatDetailed`.
- `ChatSummarizeOptions`: `ParseConversation`, `KeepLastTurnsVerbatim`,
  `ShowRolePrefixes`, `Deduplicate`, `KeepSystemVerbatim`, `RespectSafety`,
  `KeepJson`, `KeepCode`, `ExtractHtml`, `CompressKeptText`, `CompressionLevel`,
  `MaxTokens`, `TokenModel`.
- `CavemanConversationToText.ExtractTextFromMarkdown(text, MarkdownExtractOptions)`.
- `CavemanContextWindow`, `CavemanMemoryExtractor` / `MemoryNote`,
  `CavemanRelevanceFilter` / `RelevanceHit`.
- `CavemanConversationPlugin`; `TokenOptimizerPlugin.estimate_tokens`.

### Known limitations (planned for a future release)
- The **default** token counter is heuristic; inject `ITokenCounter` for an exact
  provider tokenizer when budget precision matters.
- The token budget loop is best-effort (shrink → compress → drop oldest); it does not
  guarantee a globally optimal selection.

[1.2.1]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.2.1

## [1.2.0] - 2026-06-13

Caveman can now summarize **whole chatbot/LLM conversations** in one call,
intelligently compressing only the long natural-language passages while leaving
structured service output untouched.

### Highlights
- **Conversation summarizer** — `CavemanTextRank.RankAndSummarizeChat(text)` takes
  a full conversation context (markdown + JSON allowed), cleans it, splits it into
  blocks and applies TextRank **only** to the long "discourse" blocks. Short blocks
  (service results, keyword lists such as *"I.5 - Stemma, gonfalone, sigillo"*) and
  discourses already under the quota are preserved **verbatim**.
- **Discourse detection heuristic** — a block is treated as a summarizable discourse
  only when it clears all three thresholds: a **word quota**, a minimum
  **stop-word density** (prose vs. keyword/service lists) and a minimum
  **sentence count**. No model, no LLM call.
- **Tunable** — every threshold is exposed through `ChatSummarizeOptions`
  (`MinDiscourseWords`, `MinFunctionWordRatio`, `MinDiscourseSentences`,
  `SummaryRatio`, `MinSummarySentences`, `MaxSummarySentences`, `Iso3`, `AlreadyClean`).
- **HTML is now extracted, not dropped** — `CavemanConversationToText` pulls the
  inner text out of HTML (decoding entities like `&nbsp;`/`&amp;`, turning block
  tags into separators) instead of discarding the content.

### Added
- `CavemanTextRank.RankAndSummarizeChat(string conversation, ChatSummarizeOptions? options = null)`.
- `ChatSummarizeOptions` — thresholds that drive the conversation summarizer.
- `/textrank-chat` console command — summarizes a pasted conversation.

### Changed
- `CavemanConversationToText.ExtractTextFromMarkdown` extracts HTML inner text as
  plain text (entity decoding + exotic-space normalization) rather than removing it.

### Removed
- `/textrank-conversation` console command (superseded by `/textrank-chat`).

[1.2.0]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.2.0

## [1.1.0] - 2026-06-12

Caveman gains a full **NLP pipeline** — tokenization, sentence detection, and
two independent summarizers (TF-IDF + TextRank) — all language-agnostic and
backed by the same YAML word data, with zero external NLP dependencies.

### Highlights
- **Unicode-aware tokenizer** — `CavemanTextSplitter` splits text into tokens
  by Unicode category (Word, Number, Punctuation, Whitespace, Email, Url, Emoji,
  Newline) without a single regex, working correctly across Latin, CJK, Arabic,
  Devanagari, and every other script.
- **Abbreviation-aware sentence detector** — `CavemanSentenceDetector` uses a
  per-language abbreviation list (from the YAML data) to avoid false splits after
  common abbreviations (e.g. *Dr.*, *Sig.*, *e.g.*).
- **Two summarization algorithms** — choose the approach that fits your content:
  - **TF-IDF + position bias + MMR** (`CavemanSummarizer.CondenseText`) —
    weighs sentences by rare-term frequency, boosts first/last sentences, and
    diversifies with Maximum Marginal Relevance. Best for factual/report text.
  - **TextRank + MMR** (`CavemanTextRank.RankAndSummarize`) — builds a
    similarity graph between sentences and runs PageRank to extract the most
    central ones. Best for narrative/story text.
- **Interactive demo commands** — `/summarizer-demo`, `/summarizer`,
  `/textrank-demo`, `/textrank` in the console app.

### Added
- `CavemanTextSplitter` — `ParseText()`, `CombineTokens()`, `ExtractWords()`
- `CavemanSentenceDetector` — `SplitText()`, `SplitTokens()`, `ExtractPhrases()`
- `CavemanSummarizer` — `CondenseText(text, sentenceCount, iso3)` and
  `CondenseText(text, ratio, iso3)`, plus `CompressWithSummaryAsync()` that
  chains compression after summarization.
- `CavemanTextRank` — `RankAndSummarize(text, sentenceCount, iso3)` and
  `RankAndSummarize(text, ratio, iso3)`.
- Demo text ("Il ladro di ombre") embedded in the console app for immediate
  evaluation of both algorithms.

### Fixed
- `CavemanToken` constructor signature updated for consistency.
- Emoji regex syntax changed from `\u{...}` to surrogate-pair form for
  cross-runtime compatibility.
- Async warning in `CompressWithSummaryAsync` resolved.

[1.1.0]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.1.0

## [1.0.3] - 2026-06-03

Major release: Caveman is now a fully **self-contained** library with greatly
expanded multilingual data and a much smaller footprint.

### Highlights
- **Self-contained engine** — no NLP model and no `Catalyst` dependency at runtime.
  The only package dependency is `Microsoft.SemanticKernel` (for the optional plugins).
- **50+ languages, fully populated** — lemmas, verb forms and a proper-noun
  gazetteer derived from **Universal Dependencies**.
- **Names are preserved** — proper nouns (e.g. `Termini`, `Roma`, `München`) are
  kept verbatim instead of being compressed to a common word.
- **~13 MB assembly** instead of ~68 MB of raw data, with **faster language detection**.

### Added
- **Public language detection**, usable without compressing anything:
  - `CavemanCompressionService.DetectLanguage(text)` → ISO 639-3 code
  - `CavemanCompressionService.DetectLanguageScores(text)` → per-language confidence
  - `CavemanLanguageDetector` usable standalone
- **Proper-noun gazetteer** (`proper_nouns`) per language: capitalized tokens that
  are known names are never lemmatized — works mid-sentence, at sentence start, and
  even in German (where every noun is capitalized).
- **Verb-driven compression**: every conjugated form collapses to its base verb.
- Short-input language detection (one or two words) is now supported.
- XML documentation shipped with the package for IntelliSense.

### Changed
- Language data is compiled to **brotli artifacts** (`*.yaml.br`) plus a compact
  **detection index** (`_index.br`); detection reads only the small index, while
  compression decompresses just the one detected language (then caches it).
- Custom streaming word-data parser replaces the runtime YAML dependency
  (`YamlDotNet` removed from the runtime).
- `CompressAsync` / `CompressBatchAsync` no longer use fake-async; `null` and
  cancellation now surface as a faulted / cancelled task.
- Licensed under the **Caveman License** (MIT + a mandatory attribution clause:
  any use must disclose use of *Caveman* by Passaro Francesco Paolo — Digitalsolutions.it).
  Bundled language data is derived from **Universal Dependencies** and provided under
  their respective terms (predominantly CC BY-SA / CC BY). See `NOTICE`.

### Fixed
- Proper nouns are no longer lemmatized into common words.
- Stop words with noisy source lemmas no longer survive compression.

[1.0.3]: https://github.com/francescopaolopassaro/caveman/releases/tag/v1.0.3
