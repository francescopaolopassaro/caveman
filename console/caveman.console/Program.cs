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
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Catalyst;
using caveman.core;
using Mosaik.Core;

namespace caveman
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("**********************************************************");
            Console.WriteLine("*  Caveman v.01 - di Passaro Francesco Paolo          *");
            Console.WriteLine("*  Porting ispirato a Caveman Compression                *");
            Console.WriteLine("**********************************************************");
            Console.WriteLine("Licenza: Open Source Non Commerciale. Divieto di ridistribuzione senza consenso.");
            Console.WriteLine();
            Console.WriteLine(@"

               /       \

              |  -   -  |
              | (o) (o) |
              |    V    |
             / \  ---  / \
            /   |     |   \      ______
           /    |     |    \    /      \

          |     |     |     |  |  CLAVA |
          |     |     |     |   \______/
          |     |_____|     |     ||
          |     |     |     |     ||
          |    /       \    |_____||
          |   /         \   |-----'
          |  |           |  |
          |  |           |  |
          |__|           |__|
           ||             ||
           ||             ||
          /_ \           /_ \
         ");
            // Inizializziamo il servizio
            // Nota: Catalyst caricherà i modelli al primo utilizzo (circa 50-100MB)
            var compressor = new CavemanCompressionService();

            var testCases = new List<(string Lang, string Text)>
            {
                ("Italiano", "Buongiorno, vorrei gentilmente sapere se fosse possibile ricevere alcune informazioni riguardo ai migliori ristoranti economici che si trovano a Roma, preferibilmente vicino alla stazione Termini."),
                ("Inglese", "Hello there, I would really like to know if you could kindly provide me with some information regarding the best cheap restaurants located in London, specifically near Victoria Station.")
            };

            Console.WriteLine("=== TEST DI COMPRESSIONE PROMPT  ===\n");

            foreach (var test in testCases)
            {
                Console.WriteLine($"--- LINGUA: {test.Lang} ---");
                Console.WriteLine($"Originale ({test.Text.Length} ch): \"{test.Text}\"\n");

                foreach (CavemanCompressionLevel level in Enum.GetValues(typeof(CavemanCompressionLevel)))
                {
                    var startTime = DateTime.Now;
                    string result = await compressor.CompressAsync(test.Text, level);
                    var duration = DateTime.Now - startTime;

                    double saving = CalculateSaving(test.Text, result);

                    Console.ForegroundColor = GetColorForLevel(level);
                    Console.WriteLine($"[{level}] (Risparmio: {saving:F1}%)");
                    Console.ResetColor();
                    Console.WriteLine($"> {result}");
                    Console.WriteLine($"Dettagli: {result.Length} caratteri | Elaborazione: {duration.TotalMilliseconds:F0}ms");
                    Console.WriteLine();
                }
                Console.WriteLine(new string('=', 50) + "\n");
            }
        }

        static double CalculateSaving(string original, string compressed)
        {
            if (original.Length == 0) return 0;
            return (double)(original.Length - compressed.Length) / original.Length * 100;
        }

        static ConsoleColor GetColorForLevel(CavemanCompressionLevel level) => level switch
        {
            CavemanCompressionLevel.None => ConsoleColor.White,
            CavemanCompressionLevel.Light => ConsoleColor.Green,
            CavemanCompressionLevel.Semantic => ConsoleColor.Yellow,
            CavemanCompressionLevel.Aggressive => ConsoleColor.Red,
            _ => ConsoleColor.Gray
        };
    }



   
  
}
