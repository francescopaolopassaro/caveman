// -----------------------------------------------------------------------------
// <copyright file="CavemanSafetyGuard.cs" company="Digitalsolutions.it">
//   Caveman — NLP prompt compressor for LLMs.
//   Copyright (c) 2026 Passaro Francesco Paolo — Digitalsolutions.it.
//   Licensed under the Caveman License (MIT + mandatory attribution): any use
//   must disclose use of the Caveman library by Passaro Francesco Paolo
//   (Digitalsolutions.it). See the LICENSE file for full terms.
// </copyright>
// <summary>Auto-disables compression for security-critical or destructive content.</summary>
// -----------------------------------------------------------------------------
using System.Text.RegularExpressions;

namespace caveman.core.services;

public enum SafetyLevel
{
    Normal,
    Warning,
    Critical
}

public class SafetyVerdict
{
    public SafetyLevel Level { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool ShouldCompress => Level == SafetyLevel.Normal;
}

public class CavemanSafetyGuard
{
    private static readonly HashSet<string> CriticalPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "security", "vulnerability", "exploit", "cve-",
        "data breach", "leak", "exposed",
        "malware", "ransomware", "trojan",
        "authentication bypass", "unauthorized access",
        "remote code execution", "rce",
        "sql injection", "sqli", "xss", "csrf",
        "privilege escalation",
        "denial of service", "dos", "ddos",
        "buffer overflow", "overflow",
        "certificate expired", "tls", "ssl",
        "encryption key", "private key", "secret exposed",
        "firewall", "intrusion"
    };

    private static readonly HashSet<string> DestructiveCommandPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "rm -rf", "del /f", "format ", "mkfs",
        "dd if=", "> /dev/sda", "> /dev/sd",
        "drop database", "drop table", "truncate table",
        "delete from", "shutdown", "reboot",
        "chmod 777", "chown -r",
        "git push --force", "git reset --hard",
        ">|", ":!q", ":w!",
        "cargo clean", "npm cache clean",
        "docker rmi -f", "docker system prune -a"
    };

    private static readonly HashSet<string> WarningPatterns = new(StringComparer.OrdinalIgnoreCase)
    {
        "warning", "caution", "important",
        "do not", "never", "avoid",
        "deprecated", "removed", "obsolete",
        "experimental", "unstable", "untested",
        "backup", "back up",
        "permission denied", "access denied",
        "rate limit", "timeout",
        "irreversible", "irreversibile",
        "production", "prod", "deploy",
        "rollback", "migration"
    };

    // Pre-compiled, word-boundary-aware matchers. A boundary is asserted only on the side(s)
    // where the pattern starts/ends with an alphanumeric char, so acronyms like "dos"/"rce"
    // no longer match inside "dose"/"Windows"/"force"/"source", while command strings such as
    // "rm -rf", "> /dev/sda" or ">|" still match by content.
    private static readonly (string Pattern, Regex Rx)[] CriticalMatchers = Build(CriticalPatterns);
    private static readonly (string Pattern, Regex Rx)[] DestructiveMatchers = Build(DestructiveCommandPatterns);
    private static readonly (string Pattern, Regex Rx)[] WarningMatchers = Build(WarningPatterns);

    private readonly (string Pattern, Regex Rx)[] _extraCritical;
    private readonly (string Pattern, Regex Rx)[] _extraWarning;

    /// <summary>Creates a guard, optionally adding extra critical and/or warning patterns.</summary>
    public CavemanSafetyGuard(
        IEnumerable<string>? extraCriticalPatterns = null,
        IEnumerable<string>? extraWarningPatterns = null)
    {
        _extraCritical = Build(extraCriticalPatterns);
        _extraWarning = Build(extraWarningPatterns);
    }

    private static (string, Regex)[] Build(IEnumerable<string>? patterns) =>
        patterns is null
            ? Array.Empty<(string, Regex)>()
            : patterns.Where(p => !string.IsNullOrEmpty(p)).Select(p => (p, BuildMatcher(p))).ToArray();

    private static Regex BuildMatcher(string pattern)
    {
        var body = Regex.Escape(pattern);
        var left = char.IsLetterOrDigit(pattern[0]) ? @"(?<![A-Za-z0-9])" : string.Empty;
        var right = char.IsLetterOrDigit(pattern[^1]) ? @"(?![A-Za-z0-9])" : string.Empty;
        return new Regex(left + body + right, RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }

    public SafetyVerdict Check(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new SafetyVerdict { Level = SafetyLevel.Normal };

        if (FirstMatch(CriticalMatchers, message, out var p) || FirstMatch(_extraCritical, message, out p))
            return new SafetyVerdict { Level = SafetyLevel.Critical, Reason = $"Critical security pattern detected: '{p}'" };

        if (FirstMatch(DestructiveMatchers, message, out p))
            return new SafetyVerdict { Level = SafetyLevel.Critical, Reason = $"Destructive command pattern detected: '{p}'" };

        if (FirstMatch(WarningMatchers, message, out p) || FirstMatch(_extraWarning, message, out p))
            return new SafetyVerdict { Level = SafetyLevel.Warning, Reason = $"Warning pattern detected: '{p}'" };

        return new SafetyVerdict { Level = SafetyLevel.Normal };
    }

    private static bool FirstMatch((string Pattern, Regex Rx)[] matchers, string message, out string pattern)
    {
        foreach (var (p, rx) in matchers)
        {
            if (rx.IsMatch(message))
            {
                pattern = p;
                return true;
            }
        }
        pattern = string.Empty;
        return false;
    }

    public bool ShouldCompress(string message)
    {
        return Check(message).ShouldCompress;
    }
}
