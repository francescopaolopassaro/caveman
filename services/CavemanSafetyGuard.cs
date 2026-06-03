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

    public SafetyVerdict Check(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return new SafetyVerdict { Level = SafetyLevel.Normal };

        foreach (var pattern in CriticalPatterns)
        {
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new SafetyVerdict
                {
                    Level = SafetyLevel.Critical,
                    Reason = $"Critical security pattern detected: '{pattern}'"
                };
            }
        }

        foreach (var pattern in DestructiveCommandPatterns)
        {
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new SafetyVerdict
                {
                    Level = SafetyLevel.Critical,
                    Reason = $"Destructive command pattern detected: '{pattern}'"
                };
            }
        }

        foreach (var pattern in WarningPatterns)
        {
            if (message.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                return new SafetyVerdict
                {
                    Level = SafetyLevel.Warning,
                    Reason = $"Warning pattern detected: '{pattern}'"
                };
            }
        }

        return new SafetyVerdict { Level = SafetyLevel.Normal };
    }

    public bool ShouldCompress(string message)
    {
        return Check(message).ShouldCompress;
    }
}
