// -----------------------------------------------------------------------------
// <copyright file="LanguageMapper.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Maps language codes between ISO 639-1 and ISO 639-3.</summary>
// -----------------------------------------------------------------------------
namespace caveman.core;

public static class LanguageMapper
{
    private static readonly Dictionary<string, string> Iso1ToIso3 = new(StringComparer.OrdinalIgnoreCase)
    {
        { "af", "afr" }, { "sq", "sqi" }, { "ar", "ara" }, { "hy", "hye" },
        { "eu", "eus" }, { "be", "bel" }, { "bn", "ben" }, { "bg", "bul" },
        { "ca", "cat" }, { "zh", "zho" }, { "hr", "hrv" }, { "cs", "ces" },
        { "da", "dan" }, { "nl", "nld" }, { "en", "eng" }, { "et", "est" },
        { "fi", "fin" }, { "fr", "fra" }, { "gl", "glg" }, { "de", "deu" },
        { "el", "ell" }, { "he", "heb" }, { "hi", "hin" }, { "hu", "hun" },
        { "is", "isl" }, { "id", "ind" }, { "ga", "gle" }, { "it", "ita" },
        { "ja", "jpn" }, { "kn", "kan" }, { "kk", "kaz" }, { "ko", "kor" },
        { "la", "lat" }, { "lv", "lav" }, { "lt", "lit" }, { "mk", "mkd" },
        { "ms", "msa" }, { "mr", "mar" }, { "nb", "nor" }, { "no", "nor" },
        { "fa", "fas" }, { "pl", "pol" }, { "pt", "por" }, { "ro", "ron" },
        { "ru", "rus" }, { "sr", "srp" }, { "sk", "slk" }, { "sl", "slv" },
        { "es", "spa" }, { "sv", "swe" }, { "ta", "tam" }, { "te", "tel" },
        { "th", "tha" }, { "tr", "tur" }, { "uk", "ukr" }, { "ur", "urd" },
        { "vi", "vie" }
    };

    public static string GetIso3(string code)
    {
        if (string.IsNullOrEmpty(code))
            return string.Empty;

        code = code.ToLowerInvariant();

        if (code.Length == 3 && Iso1ToIso3.ContainsValue(code))
            return code;

        return Iso1ToIso3.TryGetValue(code, out var iso3)
            ? iso3
            : string.Empty;
    }

    public static bool IsSupported(string code)
    {
        if (string.IsNullOrEmpty(code))
            return false;

        code = code.ToLowerInvariant();
        return code.Length == 3 ? Iso1ToIso3.ContainsValue(code) : Iso1ToIso3.ContainsKey(code);
    }

    public static IEnumerable<string> AllIso1Codes => Iso1ToIso3.Keys;
    public static IEnumerable<string> AllIso3Codes => Iso1ToIso3.Values;
}
