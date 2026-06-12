# Changelog

All notable changes to **Caveman** are documented in this file.

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
