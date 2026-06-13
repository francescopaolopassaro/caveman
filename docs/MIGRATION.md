# Migration guide

## Upgrading to 1.2.1 — the package split

In **1.2.1** the Semantic Kernel plugins moved out of the core `Caveman` package into a new,
optional **`Caveman.SemanticKernel`** package. The core no longer depends on
`Microsoft.SemanticKernel`.

### If you only use compression / summarization / the agent APIs
Nothing to do — you get a lighter `Caveman` package automatically (no Semantic Kernel
dependency).

### If you use the Semantic Kernel plugins
The plugin types (`TokenOptimizerPlugin`, `CavemanServicesPlugin`, `CavemanWikiPlugin`,
`CavemanConversationPlugin`) are no longer in the `Caveman` package. Add the new package:

```bash
dotnet add package Caveman.SemanticKernel
```

It pulls in `Caveman` automatically. **No namespace or code changes are required** — the
plugin classes keep the same namespace (`caveman.core.SemanticKernel.Plugin`):

```csharp
using caveman.core.SemanticKernel.Plugin;   // unchanged
using Microsoft.SemanticKernel;

var builder = Kernel.CreateBuilder();
builder.Plugins.AddFromType<CavemanConversationPlugin>();
var kernel = builder.Build();
```

## New in 1.2.1 (all additive / backward compatible)

Existing calls keep working unchanged. New capabilities you can opt into:

- **Conversation summarization** — `CavemanTextRank.RankAndSummarizeChat` /
  `RankAndSummarizeChatDetailed` (+ async), role/turn parsing, recency window, token budget,
  dedup, must-keep pinning, safety-aware skipping, presets (`ChatSummarizeOptions.AgentMemory()`…).
- **Agent toolkit** — `CavemanContextWindow`, `CavemanMemoryExtractor`, `CavemanRelevanceFilter`,
  `CavemanMemoryStore`, persistence (`IConversationStore`, `FileConversationStore`).
- **Cost estimate** in USD and EUR on `ChatSummarizeResult`.
- **DI seams** — `ITokenCounter`, `ICompressionService`, `ILanguageDetector`, `IConversationParser`,
  `ISummarizer`, `IConversationStore`. Prefer the fluent builders to the long constructors:

```csharp
var textRank = CavemanTextRank.CreateBuilder()
    .WithTokenCounter(myCounter)
    .Build();

var window = CavemanContextWindow.CreateBuilder()
    .WithMaxTokens(4000)
    .WithKeepLastTurns(6)
    .Build();
```

See [`EXAMPLES.md`](EXAMPLES.md) for a snippet of every public method.
