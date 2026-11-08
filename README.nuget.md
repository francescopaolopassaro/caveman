# Caveman

NLP prompt compressor for LLMs. It reduces the tokens of prompts, chats and tool output without a model, without
a network call and without embeddings: it uses language data (function words, lemmas, proper nouns) that ships
inside the package, for 50+ languages.

Version 1.5.1 targets .NET 10.

## Install

    dotnet add package Caveman

## Quick start

    using caveman.core;

    var compressor = new CavemanCompressionService();
    var result = await compressor.CompressAsync("I would like to know if it is possible to get pizza near Rome.",
                                                  CavemanCompressionLevel.Semantic);
    Console.WriteLine(result.CompressedText);

## Compression levels

| Level | What it does |
| :--- | :--- |
| Light | Removes function words |
| Semantic | Keeps content words and lemmatizes; output is compact keyword text, not grammatical prose |
| Aggressive | Lemmatization and generic-term pruning |
| Statistical | TF-IDF word scoring blended with global IDF tables |
| Syntactic | Rule-based pruning that keeps grammatical glue next to surviving words |

Numbers, dates, identifiers, IP addresses, versions and similar values are kept verbatim at every level.
JSON, code, SQL, XML, HTML and YAML never go through the word filters: they are routed to structure-aware
compressors.

## What is new in 1.5.1

| Feature | Summary |
| :--- | :--- |
| Keep dictionaries | Per-language negations and query words (54 languages) are never removed; Italian and English ship more than 1500 each |
| Numbers and dates | Words next to a number or a date are kept, so limits such as more than 65 or after 2022 survive |
| Markup tags | XML/HTML-style tags are protected tokens and survive compression verbatim |
| Smaller package | About 68 MB instead of 149 MB: very rare terms are no longer listed in the frequency tables |

## What was new in 1.5.0

| Feature | Summary |
| :--- | :--- |
| CavemanSemanticCache | Answers a paraphrased query from a previous answer, in every supported language, without embeddings |
| Structured content in chats | JSON, XML, HTML, Markdown, SQL and code are compressed (still valid) instead of stripped |
| Tool calls preserved | Tool calls and tool results in agentic chats are never removed or altered |
| Strict APIs | ToStrictAlternation and ToConverseMessagesJson build valid payloads for Amazon Bedrock Converse (Nova, Claude) |
| XML | New content type, detected and compressed |
| More models | Token counting for Gemini, Qwen, DeepSeek, Grok, GLM, Spark, Mistral, Nova, Kimi, Ernie, Cohere Command |
| .NET 10 | The package targets net10.0 |

## SemanticCache

    var cache = new CavemanSemanticCache();
    cache.Set("How do I create a new configuration file for the database?", "Use the init command.");

    if (cache.TryGet("How to create database configuration file?", out var hit))
        Console.WriteLine(hit.Response);      // served from the cache

How it works: the query is normalized (language detection, lowercase, no punctuation, function words removed,
lemmas) and compared with the Jaccard index of the token sets. A hit needs a similarity of at least 0.65.
Numbers, file names, paths, identifiers and quoted strings are protected tokens that must be identical, so a
request about Program.cs never matches a request about Startup.cs.

| Option | Default | Meaning |
| :--- | :--- | :--- |
| SimilarityThreshold | 0.65 | Minimum Jaccard similarity for a hit |
| MaxEntries | 1000 | Entries kept; least recently used is evicted |
| Ttl | none | Entries expire after this time |
| Language | auto | Force a language (ISO 639-1 or 639-3) |
| UseLemmas | true | Collapse inflected forms |
| MinTokenLength | 3 | Shorter plain words are ignored |

The cache is thread-safe and exposes Stats (Hits, Misses, HitRate, CharsSaved).

## Compressing a chat

    var textRank = new CavemanTextRank();
    var result = textRank.RankAndSummarizeChatDetailed(messagesJson, new ChatSummarizeOptions
    {
        ParseConversation = true,
        KeepLastTurnsVerbatim = 4,
        KeepSystemVerbatim = true,
        MaxTokens = 4000
    });
    string messages = result.Conversation.ToMessagesJson();

| Content in a message | Treatment |
| :--- | :--- |
| Prose | Summarized and compressed |
| JSON | Minified, still valid |
| XML | Comments and indentation removed, still well-formed |
| HTML | Comments and whitespace between tags removed |
| Markdown | Tables and blank lines compacted |
| Code, SQL | Comments and blank lines removed |
| Tool calls and results | Never removed, never altered |

Recognized tool formats: OpenAI-compatible (OpenAI, DeepSeek, Qwen, Grok, GLM, Spark, Mistral, Kimi),
Anthropic tool_use and tool_result, Bedrock Converse toolUse and toolResult, Gemini functionCall and
functionResponse, Cohere tool_results, and the text markers used by Qwen, DeepSeek, Mistral, Llama and GLM.

## Strict chat APIs (Amazon Bedrock Converse, Nova)

    var result = textRank.RankAndSummarizeChatDetailed(messagesJson, new ChatSummarizeOptions { ParseConversation = true });
    string converse = result.Conversation.ToConverseMessagesJson(out string? system);
    // pass system in the system field and converse as the messages

| Provider error | Fix |
| :--- | :--- |
| First message must be a user message | ToStrictAlternation or ToConverseMessagesJson |
| Roles must alternate | Same helpers: they merge consecutive turns |
| Text content is empty | Same helpers: no empty turn is emitted |
| Unknown role system or tool | Take the system prompt out with the out parameter |

## Other services

| Service | Purpose |
| :--- | :--- |
| CavemanContentRouter | Detects the content type and picks the right compressor |
| CavemanTextRank | Sentence ranking, summarization, chat compaction |
| CavemanLanguageDetector | Language detection from function words, 50+ languages |
| CavemanContextWindow | Incremental conversation compaction under a token budget |
| CavemanCodeCompressor, CavemanSqlCompressor, CavemanJsonCrusher | Structure-aware compressors |
| CavemanCacheAligner | Finds volatile tokens that break provider prompt caching |
| CavemanSimHash | Near-duplicate detection |
| ModelTokenizer | Token estimates for the main model families |

## Family

| Package | Purpose |
| :--- | :--- |
| Caveman | The compressor (this package) |
| Caveman.SemanticKernel | Semantic Kernel plugins |
| Caveman.Mcp | MCP server for Claude Code, Cursor and similar tools |
| Caveman.PrivacyGuard | PII detection and masking |

## License

MIT with mandatory attribution to Passaro Francesco Paolo, Digitalsolutions.it. See the LICENSE file in the package.
