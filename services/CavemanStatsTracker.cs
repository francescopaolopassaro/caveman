// -----------------------------------------------------------------------------
// <copyright file="CavemanStatsTracker.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Tracks token and cost savings across sessions, with persistence.</summary>
// -----------------------------------------------------------------------------
using System.Text.Json;
using caveman.core.entities;

namespace caveman.core.services;

public class CavemanStatsTracker
{
    private readonly string _statsFilePath;
    private readonly double _costPerToken;
    private long _sessionOriginalTokens;
    private long _sessionCompressedTokens;
    private long _lifetimeOriginalTokens;
    private long _lifetimeCompressedTokens;
    private int _sessionRequests;

    public CavemanStatsTracker(string? statsFilePath = null, double costPerToken = 0.00001)
    {
        _statsFilePath = statsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Caveman", "stats.json");
        _costPerToken = costPerToken;
        Load();
    }

    public long SessionOriginalTokens => _sessionOriginalTokens;
    public long SessionCompressedTokens => _sessionCompressedTokens;
    public long SessionTokensSaved => _sessionOriginalTokens - _sessionCompressedTokens;
    public double SessionSavingsPercent => _sessionOriginalTokens == 0 ? 0 : (1.0 - (double)_sessionCompressedTokens / _sessionOriginalTokens) * 100;
    public double SessionCostSaved => SessionTokensSaved * _costPerToken;
    public int SessionRequests => _sessionRequests;

    public long LifetimeOriginalTokens => _lifetimeOriginalTokens;
    public long LifetimeCompressedTokens => _lifetimeCompressedTokens;
    public long LifetimeTokensSaved => _lifetimeOriginalTokens - _lifetimeCompressedTokens;
    public double LifetimeSavingsPercent => _lifetimeOriginalTokens == 0 ? 0 : (1.0 - (double)_lifetimeCompressedTokens / _lifetimeOriginalTokens) * 100;
    public double LifetimeCostSaved => LifetimeTokensSaved * _costPerToken;

    public void TrackResult(CompressionResult result, bool persist = true)
    {
        if (result.OriginalTokens <= 0 || result.HasError)
            return;

        _sessionOriginalTokens += result.OriginalTokens;
        _sessionCompressedTokens += result.CompressedTokens;
        _lifetimeOriginalTokens += result.OriginalTokens;
        _lifetimeCompressedTokens += result.CompressedTokens;
        _sessionRequests++;

        if (persist)
            Save();
    }

    public void TrackBatch(CompressionResult[] results, bool persist = true)
    {
        foreach (var r in results)
            TrackResult(r, false);

        if (persist)
            Save();
    }

    public void ResetSession()
    {
        _sessionOriginalTokens = 0;
        _sessionCompressedTokens = 0;
        _sessionRequests = 0;
    }

    public string FormatSessionReport()
    {
        return $"Session: {SessionTokensSaved} tokens saved ({SessionSavingsPercent:F1}%) | " +
               $"{SessionRequests} requests | ${SessionCostSaved:F4} saved";
    }

    public string FormatLifetimeReport()
    {
        return $"Lifetime: {LifetimeTokensSaved} tokens saved ({LifetimeSavingsPercent:F1}%) | " +
               $"${LifetimeCostSaved:F4} saved";
    }

    public string FormatFullReport()
    {
        return $"""
                ┌─ Caveman Stats ──────────────────────────────
                │ Session
                │   Requests : {SessionRequests,8}
                │   Tokens   : {SessionOriginalTokens,8} → {SessionCompressedTokens,8}
                │   Saved    : {SessionTokensSaved,8} ({SessionSavingsPercent,5:F1}%)
                │   Cost     : ${SessionCostSaved,8:F4}
                │ Lifetime
                │   Tokens   : {LifetimeOriginalTokens,8} → {LifetimeCompressedTokens,8}
                │   Saved    : {LifetimeTokensSaved,8} ({LifetimeSavingsPercent,5:F1}%)
                │   Cost     : ${LifetimeCostSaved,8:F4}
                │ Rate      : ${_costPerToken,8:F6}/token
                └─────────────────────────────────────────────
                """;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_statsFilePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var data = new StatsData
            {
                LifetimeOriginalTokens = _lifetimeOriginalTokens,
                LifetimeCompressedTokens = _lifetimeCompressedTokens
            };

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_statsFilePath, json);
        }
        catch
        {
        }
    }

    public void Load()
    {
        try
        {
            if (!File.Exists(_statsFilePath))
                return;

            var json = File.ReadAllText(_statsFilePath);
            var data = JsonSerializer.Deserialize<StatsData>(json);
            if (data != null)
            {
                _lifetimeOriginalTokens = data.LifetimeOriginalTokens;
                _lifetimeCompressedTokens = data.LifetimeCompressedTokens;
            }
        }
        catch
        {
        }
    }

    private class StatsData
    {
        public long LifetimeOriginalTokens { get; set; }
        public long LifetimeCompressedTokens { get; set; }
    }
}
