// -----------------------------------------------------------------------------
// <copyright file="Program.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Developer script: compiles worddata/*.yaml into the embedded runtime artifacts (brotli per-language blobs + a small detection index).</summary>
// -----------------------------------------------------------------------------
using System.IO.Compression;
using System.Text;
using caveman.core;

// Compiles the human-editable worddata/*.yaml sources into the artifacts that are
// actually embedded in caveman.core:
//   - worddata/<iso3>.yaml.br : the YAML blob, brotli-compressed (full data,
//                               loaded + decompressed only for the detected language).
//   - worddata/_index.br      : tab-separated "iso3 iso1 name fw...", brotli-compressed,
//                               read once for fast language detection.
//
// Re-run this whenever a worddata/*.yaml changes, then rebuild caveman.core.

var worddataDir = Path.Combine(@"C:\Sorgenti\Personal\caveman", "worddata");
var yamlFiles = Directory.GetFiles(worddataDir, "*.yaml").OrderBy(f => f).ToList();
Console.WriteLine($"Compiling {yamlFiles.Count} language files...");

var index = new StringBuilder();
long rawTotal = 0, brTotal = 0;

foreach (var yaml in yamlFiles)
{
    var iso3 = Path.GetFileNameWithoutExtension(yaml);
    var bytes = File.ReadAllBytes(yaml);
    rawTotal += bytes.Length;

    // per-language blob: brotli(yaml bytes)
    var brPath = yaml + ".br";
    using (var outFile = File.Create(brPath))
    using (var brotli = new BrotliStream(outFile, CompressionLevel.Optimal))
        brotli.Write(bytes, 0, bytes.Length);
    brTotal += new FileInfo(brPath).Length;

    // index entry: parse just to extract iso1/name/function_words
    WordDataFile data;
    using (var fs = File.OpenRead(yaml))
        data = FunctionWordProvider.ParseYaml(fs);

    var fw = string.Join('\t', data.function_words.Select(w => w.Replace('\t', ' ')));
    index.Append(iso3).Append('\t')
         .Append(data.iso1).Append('\t')
         .Append(data.name.Replace('\t', ' ')).Append('\t')
         .Append(fw).Append('\n');
}

// detection index: brotli(text)
var indexPath = Path.Combine(worddataDir, "_index.br");
using (var outFile = File.Create(indexPath))
using (var brotli = new BrotliStream(outFile, CompressionLevel.Optimal))
{
    var idxBytes = Encoding.UTF8.GetBytes(index.ToString());
    brotli.Write(idxBytes, 0, idxBytes.Length);
}

Console.WriteLine($"Per-language blobs: {rawTotal / 1_048_576.0:F1} MB raw -> {brTotal / 1_048_576.0:F1} MB brotli");
Console.WriteLine($"Index: {new FileInfo(indexPath).Length / 1024.0:F0} KB ({yamlFiles.Count} languages)");
Console.WriteLine("Done. Rebuild caveman.core to embed the artifacts.");
