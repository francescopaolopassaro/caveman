/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor)
 * * DESCRIPTION:
 * This system implements NLP-based "Prompt Contraction" logic.
 * The core objective is to drastically reduce the token count sent to LLMs 
 * (such as Gemma 3, Llama 3, or GPT-4) by selectively stripping low-semantic 
 * value grammatical elements (Stopwords, Determiners, Conjunctions).
 * * THE "CAVEMAN" PRINCIPLE:
 * The guiding philosophy is to transform complex natural language into an essential, 
 * high-density format that preserves the original intent. 
 * (e.g., "I would like to order a pepperoni pizza" -> "Order pepperoni pizza").
 * * BENEFITS:
 * 1. Reduced Inference Latency: Faster response times from local or cloud models.
 * 2. API Cost Optimization: Significant savings for token-based billing.
 * 3. Context Window Efficiency: Allows more information to fit within the model's memory.
 * * TECHNOLOGY STACK:
 * - Core: Catalyst NLP (Universal Dependencies Standard)
 * - Methodology: POS (Part-of-Speech) filtering and Lemmatization.
 * * AUTHOR: [Passaro Francesco Paolo]
 * DATE: April 2026
 *---------------------------------------------------------------------------------------*/
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.IO.Compression;

namespace caveman.SemanticKernel.Plugin
{

    public class TokenOptimizerPlugin
    {
        private readonly CavemanCompressionService _compressionService;

        public TokenOptimizerPlugin(CavemanCompressionService compressionService)
        {
            _compressionService = compressionService;
        }

        [KernelFunction, Description("Ottimizza il prompt riducendo i token in base al livello richiesto (0-3).")]
        public async Task<string> OptimizePrompt(
            [Description("Il testo da comprimere")] string input,
            [Description("Livello: 0=None, 1=Light, 2=Semantic, 3=Aggressive")] int level = 2)
        {
            return await _compressionService.CompressAsync(input, (CavemanCompressionLevel)level);
        }
    }

    //ESEMPIO DI ENUM PER LA COMPRESSSIONE (da adattare al tuo codice)
    // Inizializzazione
//var compressionService = new CavemanCompressionService();
//        var builder = Kernel.CreateBuilder();

//// Aggiungi Ollama (tramite connettore OpenAI compatibile)
//builder.AddOpenAIChatCompletion("gemma3", "http://localhost:11434/v1", "ollama");

//// Registra il plugin
//builder.Plugins.AddFromObject(new TokenOptimizerPlugin(compressionService));

//var kernel = builder.Build();

//    // ESEMPIO DI FLUSSO
//    string userPrompt = "Vorrei sapere se gentilmente potresti trovarmi dei ristoranti economici a Milano vicino alla stazione.";

//    // 1. Esegui la compressione (Livello 2 - Semantic)
//    var optimizedPrompt = await kernel.InvokeAsync<string>(
//        "TokenOptimizerPlugin", "OptimizePrompt",
//        new() { ["input"] = userPrompt, ["level"] = 2 }
//    );

//    // Risultato atteso: "trovare ristoranti economici Milano stazione"

//    // 2. Invia 
//    var result = await kernel.InvokePromptAsync(optimizedPrompt);
//    Console.WriteLine(result);


}
