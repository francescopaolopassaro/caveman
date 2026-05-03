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
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/
using caveman.core.entities;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Threading.Tasks;

namespace caveman.core.SemanticKernel.Plugin
{
    public class TokenOptimizerPlugin
    {
        private readonly CavemanCompressionService _compressionService;

        public TokenOptimizerPlugin(CavemanCompressionService compressionService)
        {
            _compressionService = compressionService;
        }

        [KernelFunction, Description("Optimizes the prompt by reducing tokens based on the required level (0-3).")]
        public async Task<CompressionResult> OptimizePrompt(
            [Description("The raw text to compress")] string input,
            [Description("Level: 0=None, 1=Light, 2=Semantic, 3=Aggressive")] int level = 2)
        {
            // Now returns the full CompressionResult object instead of just a string
            return await _compressionService.CompressAsync(input, (CavemanCompressionLevel)level);
        }
    }
}