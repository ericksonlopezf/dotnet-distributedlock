// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.DistributedLock.Showcase.Infrastructure;

/// <summary>
/// Provides formatted console output for the Showcase application.
/// </summary>
public static class ConsoleUi
{
    public static void PrintBanner()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("""
        ================================================================================
           EricksonLopez.DistributedLock — OFFICIAL SHOWCASE & REFERENCE IMPLEMENTATION
           High-Performance Distributed Mutual Exclusion & Cluster Coordination Tier 0
        ================================================================================
        """);
        Console.ResetColor();
    }

    public static void PrintHeader(string title, string? subtitle = null)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"\n>>> [{title.ToUpperInvariant()}]");
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    {subtitle}");
        }
        Console.ResetColor();
    }

    public static void PrintSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"  ✔ {message}");
        Console.ResetColor();
    }

    public static void PrintInfo(string message)
    {
        Console.ForegroundColor = ConsoleColor.Gray;
        Console.WriteLine($"  ℹ {message}");
        Console.ResetColor();
    }

    public static void PrintWarning(string message)
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"  ⚠ {message}");
        Console.ResetColor();
    }

    public static void PrintError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"  ✖ {message}");
        Console.ResetColor();
    }

    public static void PrintMetric(string name, object value, string? unit = null)
    {
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.Write($"  📊 {name}: ");
        Console.ForegroundColor = ConsoleColor.White;
        Console.Write(value);
        if (!string.IsNullOrEmpty(unit))
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write($" {unit}");
        }
        Console.WriteLine();
        Console.ResetColor();
    }
}
