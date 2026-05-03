#🦴 Caveman: Prompt Compressor for LLMs

<img width="1197" height="766" alt="caveman_splash" src="https://github.com/user-attachments/assets/4b534140-c519-423f-b918-e705565a039f" />
It is the version that is inspired by the token saving algorithm of Caveman plugin for Claude, but it was conceived without doing any porting from the original, it is a code born from scratch


**Caveman** is a C# library built on **Catalyst** that drastically reduces the number of tokens in your LLM prompts (such as Gemma 3, Llama, or GPT-4). It utilizes Natural Language Processing (NLP) techniques to remove grammatical "noise" (articles, prepositions, conjunctions) while keeping the semantic value intact.

> "Why use many tokens when few tokens do trick?" — A caveman (and your wallet).

## 🚀 Features
- **Up to 70% Token Reduction**: Slash API costs and speed up local inference.
- **Multilingual**: Support for over 50 languages (English, Italian, French, etc.) via Catalyst models.
- **Compression Levels**: Choose between `Light`, `Semantic`, or `Aggressive` (Lemmatization).
- **LLM Integration with Semantic Kernel**: Optimized for next-gen models that perfectly understand contracted language.

---

## 🛠️ Installation
dotnet add package Caveman --version 1.0.2

###  Quick Start
var compressor = new CavemanCompressionService();
string input = "I would like to know if it is possible to receive information about cheap restaurants in Rome.";

// Compresses the text and calculates energy savings
var result = await compressor.CompressAsync(input, CavemanCompressionLevel.Semantic);

Console.WriteLine($"Compressed: {result.CompressedText}");
Console.WriteLine($"Efficiency: {result.EfficiencyPercentage:F1}%");
Console.WriteLine($"🌿 Energy Saved: {result.EstimatedEnergySavedMWh:F3} mWh");

### 🌿 Sustainability: Why it matters
Every token generated or processed by an LLM has an environmental cost. Caveman v1.1 introduces a built-in estimator based on industry averages:

Energy Consumption: Estimated at 5 mWh per token.

Carbon Footprint: Estimated at 0.4 mg of CO2 per mWh.

By compressing a prompt from 1000 to 400 tokens, you save approximately 3 mWh of energy. On a scale of millions of requests, Caveman helps build a more sustainable AI ecosystem.

### 📊 NLP Compression Levels

| Level | Applied Logic | Removed POS Tags (Filters) | Savings |
| :--- | :--- | :--- | :--- |
| **Light** | *Stopword Removal* | `DET`, `ADP`, `CCONJ`, `SCONJ`, `PRON`, `PUNCT` | **~25-30%** |
| **Semantic** | *Key Content Selection* | Keeps only `NOUN`, `VERB`, `ADJ`, `PROPN`, `ADV` | **~50%** |
| **Aggressive** | *Lemmatization* | Keeps only `NOUN`, `VERB`, `PROPN` (base form) | **~70%** |

### 🔍 Technical Tag Detail (Catalyst Mapping)

| POS Tag | Category | Examples (ENG/ITA) | Treatment |
| :--- | :--- | :--- | :--- |
| **DET** | Determiners | the, a / il, lo | **Removed** (from Light) |
| **ADP** | Prepositions | of, at / di, a | **Removed** (from Light) |
| **CCONJ** | Coord. Conjunctions | and, or / e, o | **Removed** (from Light) |
| **SCONJ** | Subord. Conjunctions | that, if / che, se | **Removed** (from Light) |
| **PRON** | Pronouns | I, you / io, tu | **Removed** (from Light) |
| **NOUN** | Nouns | house, pizza / casa, pizza | **Always Kept** |
| **VERB** | Verbs | eat, runs / mangiare, corre | **Always Kept** |
| **ADV** | Adverbs | not, quickly / non, molto | **Kept in Semantic** |

### 💡 Transformation Example

| State | Prompt Text | Tokens / Characters |
| :--- | :--- | :--- |
| **Original** | "I would like to know if it is possible to have a margherita pizza immediately." | 100% (78 ch) |
| **Light** | "like know possible have margherita pizza immediately" | ~70% (54 ch) |
| **Semantic** | "know possible have margherita pizza immediately" | ~55% (48 ch) |
| **Aggressive**| "know possible have margherita pizza" | **~40% (38 ch)** |

### 💡 This is a new feature introduced in version 1.0.2 : Caveman.Wiki

## Purpose
Automatically generate AI-friendly markdown documentation for any software project, 
semantically compressing content to optimize context for LLM prompts.

## How It Works
1. **Project Analysis**: Automatically detects project type (C#, Python, Node.js, etc.) 
   by scanning configuration files (.csproj, requirements.txt, package.json, etc.)

2. **File Scanning**: Recursively traverses the folder, applying intelligent filters 
   to exclude binary files, build folders, and external dependencies.

3. **Dependency Extraction**: Parses project files to extract packages and versions, 
   organizing them by source (NuGet, PyPI, npm, etc.)

4. **Content Compression**: For files >2KB, uses `CavemanCompressionService` with 
   `Semantic` level to reduce token count while preserving meaning.

5. **Markdown Output**: Generates a structured document with:
   - Project metadata in YAML format
   - Organized dependency list
   - Tree view of file structure
   - File contents with syntax highlighting
   - Statistical summary

## Benefits for AI
✅ Complete context in readable format  
✅ Token-optimized via semantic compression  
✅ Predictable structure for automatic parsing  
✅ Machine-readable metadata for RAG systems  

## Example Usage
// Basic usage
var wiki = new CavemanWiki();
string context = await wiki.GenerateAsync(@"C:\Users\Dev\MyAwesomeProject");
await File.WriteAllTextAsync("AI_CONTEXT.md", context);

// Advanced usage with custom parameters
string context = await wiki.GenerateAsync(
    projectFolderPath: @"..\MyProject",
    maxFileSizeBytes: 50 * 1024,  // 50KB max per file
    compressionLevel: CavemanCompressionLevel.Aggressive  // More aggressive compression
);

// Integration with AI prompt system
var prompt = $@"
You are an expert assistant for the project described below.

<project_context>
{context}
</project_context>

Answer questions based SOLELY on this context.
";
🤝 Contributing

Pull requests are welcome! For major changes, please open an issue first to discuss what you would like to change.

Pull requests are welcome! For major changes, please open an issue first to discuss what you would like to change.
