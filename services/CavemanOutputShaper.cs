// -----------------------------------------------------------------------------
// <copyright file="CavemanOutputShaper.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Reduces LLM output tokens by injecting verbosity-steering instructions at the tail of the system prompt.</summary>
// -----------------------------------------------------------------------------
using caveman.core.entities;

namespace caveman.core.services;

/// <summary>
/// Reduces LLM output tokens without touching the model's response: appends a
/// <see cref="VerbosityLevel"/>-appropriate instruction block to the tail of a system
/// prompt so the model skips ceremony, restatement, and rationale.
/// <para>
/// The steering text is byte-stable per level and tagged with a sentinel so it is
/// idempotent — calling <see cref="ShapeSystemPrompt"/> twice at the same level
/// does not double-inject the instructions.
/// </para>
/// </summary>
public sealed class CavemanOutputShaper
{
    private const string SentinelOpen = "<!-- caveman-verbosity-";
    private const string SentinelClose = " -->";

    // Cumulative instruction blocks per verbosity level
    private static readonly string L1Text =
        "Skip preamble and postamble. Do not say things like \"Sure!\", \"Of course!\", " +
        "\"Great question!\", \"Certainly!\", \"I'd be happy to...\", \"Let me...\", or similar. " +
        "Start with substance immediately.";

    private static readonly string L2Text =
        "Never restate or echo back code, file contents, diffs, or tool output shown in context. " +
        "Do not repeat what was provided. If you need to refer to it, use filename:line references.";

    private static readonly string L3Text =
        "Give conclusions and results only. Do not explain your reasoning unless explicitly asked.";

    private static readonly string L4Text =
        "Minimum token response. Sentence fragments are fine. " +
        "No preamble. No postamble. No restatement. No reasoning.";

    private static string BuildSteering(VerbosityLevel level) => level switch
    {
        VerbosityLevel.SkipCeremony    => L1Text,
        VerbosityLevel.NoRestatement   => L1Text + "\n" + L2Text,
        VerbosityLevel.ConclusionsOnly => L1Text + "\n" + L2Text + "\n" + L3Text,
        VerbosityLevel.MinimumTokens   => L4Text,
        _                              => string.Empty
    };

    /// <summary>
    /// Appends verbosity-steering instructions to <paramref name="systemPrompt"/> at the given
    /// <paramref name="level"/>. Idempotent: a second call at the same level is a no-op.
    /// Passing <see cref="VerbosityLevel.Off"/> returns the prompt unchanged.
    /// </summary>
    public string ShapeSystemPrompt(string systemPrompt, VerbosityLevel level = VerbosityLevel.NoRestatement)
    {
        if (level == VerbosityLevel.Off) return systemPrompt;

        var sentinel = SentinelOpen + (int)level + SentinelClose;
        if ((systemPrompt ?? string.Empty).Contains(sentinel, StringComparison.Ordinal))
            return systemPrompt!; // already shaped at this level

        // Remove any prior steering before re-injecting at the new level
        var clean = RemoveVerbositySteering(systemPrompt ?? string.Empty);
        var steering = BuildSteering(level);
        return clean.TrimEnd() + "\n\n" + sentinel + "\n" + steering;
    }

    /// <summary>Returns true when any verbosity-steering block is present in <paramref name="systemPrompt"/>.</summary>
    public bool HasVerbositySteering(string systemPrompt)
        => systemPrompt.Contains(SentinelOpen, StringComparison.Ordinal);

    /// <summary>Removes any verbosity-steering block previously injected by <see cref="ShapeSystemPrompt"/>.</summary>
    public string RemoveVerbositySteering(string systemPrompt)
    {
        if (string.IsNullOrEmpty(systemPrompt) || !systemPrompt.Contains(SentinelOpen, StringComparison.Ordinal))
            return systemPrompt;

        int start = systemPrompt.IndexOf(SentinelOpen, StringComparison.Ordinal);
        // Walk back past any leading whitespace/newlines before the sentinel
        int trimStart = start;
        while (trimStart > 0 && (systemPrompt[trimStart - 1] == '\n' || systemPrompt[trimStart - 1] == '\r' || systemPrompt[trimStart - 1] == ' '))
            trimStart--;

        return systemPrompt[..trimStart];
    }
}
