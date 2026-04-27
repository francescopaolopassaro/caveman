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

### 1. Base Package
Install the core library and the model manager:

dotnet add package Catalyst
dotnet add package Mosaik.Core


### 2. Language Models
Install the packages for the languages you intend to support:
dotnet add package Catalyst.Models.English
dotnet add package Catalyst.Models.Italian

Alternatively, run the PowerShell script Install-CatalystModels.ps1 (it automatically updates all libraries in the project).

### 3. Quick Start
var compressor = new CavemanCompressionService();

string input = "Buongiorno, vorrei sapere se fosse possibile ricevere informazioni sui ristoranti a Roma.";

// Compresses the text by removing stopwords while maintaining meaning
string compressed = await compressor.CompressAsync(input, Language.Italian, CompressionLevel.Semantic);

Console.WriteLine(compressed); 
// Output: "Buongiorno sapere possibile ricevere informazioni ristoranti Roma"

### 5. 📊 NLP Compression Levels

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


🤝 Contributing
Pull requests are welcome! For major changes, please open an issue first to discuss what you would like to change.

Caveman License Agreement v1.0
Copyright (c) 2026 Francesco Paolo Passaro

Permission is hereby granted to use, copy, and modify this software ("Caveman") exclusively for Open Source and NON-Commercial purposes, under the following conditions:

Attribution: The original author's name, Francesco Paolo Passaro, and references to the "Caveman Compression" project must be retained in every copy or substantial portion of the software.

Non-Commercial Use: Use of the software, its derivatives, or the results produced by it for profit, sale, or integration into paid commercial products is strictly prohibited without prior written agreement.

Prohibition of Public Redistribution: The software may not be uploaded to public repositories, mirrors, or distributed to third parties outside the original context without the express written consent of the author.

Open Source "As-Is": The software is provided "as is", without warranty of any kind. The author is not responsible for any damages arising from the use of the software.

Any violation of the points above will result in the immediate revocation of the license to use.
For authorization requests regarding disclosure or commercial use, contact: passaroweb@gmail.com
