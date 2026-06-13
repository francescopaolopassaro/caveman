# Changelog

All notable changes to **Caveman** are documented in this file.

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
- **Examples** — `docs/EXAMPLES.md` documents every public method with a runnable snippet.

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
- Persistence + `DeduplicateOnAppend` cover idempotent reload and exact-duplicate
  turns, but the context window still re-summarizes already-compacted turns on each
  compaction (incremental "skip already-compacted" not yet wired).
- The chat path is synchronous (no `CancellationToken`); the budget loop is best-effort.

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
