/*---------------------------------------------------------------------------------------
 * PROJECT: Caveman (NLP Prompt Compressor) - Semantic Kernel Plugin
 * DESCRIPTION:
 * Plugin per Semantic Kernel che espone la logica di compressione Caveman come funzione 
 * invocabile dall'IA. Ottimizza i prompt riducendo i token mantenendo il significato.
 * Include graceful degradation per input non supportati dal modello NLP.
 * 
 * AUTHOR: [Francesco Paolo Passaro]
 * DATE: May 2026
 *---------------------------------------------------------------------------------------*/


using System.ComponentModel;
using Microsoft.SemanticKernel;
using caveman.core.entities;

namespace caveman.core.SemanticKernel.Plugin
{
    /// <summary>
    /// Plugin Semantic Kernel per l'ottimizzazione token di prompt testuali.
    /// Gestisce automaticamente fallback sicuri per input non processabili (emoji, lingue non supportate, ecc.).
    /// </summary>
    public class TokenOptimizerPlugin
    {
        private readonly CavemanCompressionService _compressionService;

        /// <summary>
        /// Inizializza il plugin con il servizio di compressione fornito.
        /// </summary>
        /// <param name="compressionService">Istanza di CavemanCompressionService.</param>
        public TokenOptimizerPlugin(CavemanCompressionService compressionService)
        {
            _compressionService = compressionService ?? throw new ArgumentNullException(nameof(compressionService));
        }

        /// <summary>
        /// Comprime il testo in input riducendo i token secondo il livello specificato.
        /// Se l'input non è supportato dal motore NLP (es. solo emoji/simboli), restituisce 
        /// il testo originale senza errori per non interrompere il flusso dell'agente AI.
        /// </summary>
        /// <param name="input">Testo originale da ottimizzare.</param>
        /// <param name="level">Livello di compressione: 0=Nessuno, 1=Light, 2=Semantico, 3=Aggressivo.</param>
        /// <returns>Risultato della compressione con metriche dettagliate.</returns>
        [KernelFunction("OptimizePrompt")]
        [Description("Optimizes the prompt by reducing tokens based on the required level (0-3). " +
                     "Returns CompressionResult with compressed text and green metrics. " +
                     "Gracefully falls back to original text if language/model is unsupported.")]
        public async Task<CompressionResult> OptimizePrompt(
            [Description("The raw text to compress")] string input,
            [Description("Level: 0=None, 1=Light, 2=Semantic (default), 3=Aggressive")] int level = 2)
        {
            if (input is null)
                throw new ArgumentNullException(nameof(input));

            int safeLevel = Math.Clamp(level, 0, 3);

            try
            {
                // Tentativo di compressione standard
                return await _compressionService.CompressAsync(input, (CavemanCompressionLevel)safeLevel);
            }
            catch (NotSupportedException)
            {
                //  Graceful degradation: il modello NLP non supporta questa lingua/tipo di input
                // Restituiamo il testo originale per non rompere il flusso dell'AI
                return new CompressionResult
                {
                    CompressedText = input,
                    OriginalTokens = 0,
                    CompressedTokens = 0
                };
            }
            catch (Exception)
            {
                // Fallback ultimo per qualsiasi altro errore imprevisto del servizio
                return new CompressionResult
                {
                    CompressedText = input,
                    OriginalTokens = 0,
                    CompressedTokens = 0
                };
            }
        }
    }
}