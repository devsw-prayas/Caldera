using System;
using System.Collections.Generic;

namespace Caldera
{
    // ── Name demanglers (best-effort, no external tooling) ────────────────────
    //
    // Two separate ABI implementations:
    //   • TryDemangleMsvcName  — MSVC /FA  (?add@@YA_K_K0@Z  → add(unsigned long long, unsigned long long))
    //   • TryDemangleItanium   — clang/g++ (_Z3addii          → add(int, int))

    internal static class AsmDemangler
    {
        // ── MSVC type tables ──────────────────────────────────────────────────

        private static readonly Dictionary<char, string> _msvcTypeMap = new()
        {
            ['X'] = "void",
            ['D'] = "char",
            ['C'] = "signed char",
            ['E'] = "unsigned char",
            ['F'] = "short",
            ['G'] = "unsigned short",
            ['H'] = "int",
            ['I'] = "unsigned int",
            ['J'] = "long",
            ['K'] = "unsigned long",
            ['M'] = "float",
            ['N'] = "double",
            ['O'] = "long double",
        };

        private static readonly Dictionary<char, string> _msvcUnderscoreMap = new()
        {
            ['J'] = "long long",
            ['K'] = "unsigned long long",
            ['W'] = "wchar_t",
            ['N'] = "__int128",
            ['F'] = "__int16",
            ['G'] = "unsigned __int16",
        };

        // ── Itanium type table ────────────────────────────────────────────────

        private static readonly Dictionary<char, string> _itaniumTypeMap = new()
        {
            ['v'] = "void",
            ['b'] = "bool",
            ['c'] = "char",
            ['a'] = "signed char",
            ['h'] = "unsigned char",
            ['s'] = "short",
            ['t'] = "unsigned short",
            ['i'] = "int",
            ['j'] = "unsigned int",
            ['l'] = "long",
            ['m'] = "unsigned long",
            ['x'] = "long long",
            ['y'] = "unsigned long long",
            ['f'] = "float",
            ['d'] = "double",
            ['e'] = "long double",
            ['z'] = "...",
        };

        // ── MSVC ──────────────────────────────────────────────────────────────

        internal static string? TryDemangleMsvcName(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith('?')) return null;
            try
            {
                var s = name[1..];

                // Template instantiation: ??$dot@M@@... → function name is "dot"
                if (s.StartsWith("?$"))
                {
                    s = s[2..];
                    int at = s.IndexOf('@');
                    if (at <= 0) return null;
                    return s[..at] + "(...)";
                }

                int atIdx = s.IndexOf('@');
                if (atIdx <= 0) return null;
                var funcName = s[..atIdx];
                s = s[(atIdx + 1)..];

                // Class scope: ?foo@Bar@@...
                string? className = null;
                if (s.Length > 0 && s[0] != '@' && s[0] != 'Y' && !char.IsDigit(s[0]))
                {
                    int at2 = s.IndexOf('@');
                    if (at2 > 0) { className = s[..at2]; s = s[(at2 + 1)..]; }
                }

                // Locate 'Y' (function type encoding start)
                int yi = s.IndexOf('Y');
                if (yi < 0) return null;
                s = s[(yi + 1)..];
                if (s.Length > 0) s = s[1..]; // skip calling-convention letter

                // Skip return type
                s = SkipMsvcType(s, new List<string>(), out _);

                // Parse parameter types
                var paramTypes = new List<string>();
                while (s.Length > 0 && s[0] != '@' && s[0] != 'Z')
                {
                    s = SkipMsvcType(s, paramTypes, out var t);
                    if (t == "void" || t == null) break;
                    paramTypes.Add(t);
                }

                var qualName = className != null ? $"{className}::{funcName}" : funcName;
                return qualName + "(" + string.Join(", ", paramTypes) + ")";
            }
            catch { return null; }
        }

        private static string SkipMsvcType(string s, List<string> paramHistory, out string? typeName)
        {
            typeName = null;
            if (s.Length == 0) return s;
            char c = s[0];

            if (c == 'P' || c == 'Q') // pointer
            {
                var s2 = s[1..];
                if (s2.Length > 0 && (s2[0] == 'A' || s2[0] == 'B')) s2 = s2[1..]; // cv
                s2 = SkipMsvcType(s2, paramHistory, out var inner);
                typeName = (inner ?? "?") + "*";
                return s2;
            }
            if (c == 'A') // reference
            {
                var s2 = s[1..];
                if (s2.Length > 0 && (s2[0] == 'A' || s2[0] == 'B')) s2 = s2[1..];
                s2 = SkipMsvcType(s2, paramHistory, out var inner);
                typeName = (inner ?? "?") + "&";
                return s2;
            }
            if (c == '_' && s.Length > 1)
            {
                typeName = _msvcUnderscoreMap.TryGetValue(s[1], out var t2) ? t2 : null;
                return s[2..];
            }
            // Back-reference digit: 0 = first param, 1 = second, etc.
            if (char.IsDigit(c))
            {
                int idx = c - '0';
                typeName = idx < paramHistory.Count ? paramHistory[idx] : null;
                return s[1..];
            }
            if (_msvcTypeMap.TryGetValue(c, out var t)) { typeName = t; return s[1..]; }
            return s[1..]; // unknown — skip
        }

        // ── Itanium ABI (clang/g++ Linux or MinGW) ───────────────────────────

        internal static string? TryDemangleItanium(string mangled)
        {
            if (!mangled.StartsWith("_Z") && !mangled.StartsWith("__Z")) return null;
            try
            {
                var s = mangled.StartsWith("__Z") ? mangled[3..] : mangled[2..];
                string funcName;
                string remaining;

                if (s.StartsWith("N")) // nested name: _ZN3Foo3barEv
                {
                    s = s[1..];
                    var parts = new List<string>();
                    while (s.Length > 0 && s[0] != 'E')
                    {
                        if (s[0] == 'I') { s = SkipToE(s[1..]); continue; }
                        if (!char.IsDigit(s[0])) break;
                        var len = ParseLength(s, out var after);
                        if (len <= 0 || len > after.Length) break;
                        parts.Add(after[..len]);
                        s = after[len..];
                    }
                    funcName = string.Join("::", parts);
                    remaining = s.StartsWith("E") ? s[1..] : s;
                }
                else if (char.IsDigit(s[0])) // simple name: _Z3add
                {
                    var len = ParseLength(s, out var after);
                    if (len <= 0 || len > after.Length) return null;
                    funcName = after[..len];
                    remaining = after[len..];
                    if (remaining.StartsWith("I"))
                        remaining = SkipToE(remaining[1..]);
                }
                else return null;

                if (remaining.Length == 0 || remaining == "v")
                    return funcName + "()";

                var paramTypes = new List<string>();
                var sub = new List<string>();
                int i = 0;
                while (i < remaining.Length && remaining[i] != 'E')
                {
                    var (typeName, advance) = ParseItaniumType(remaining[i..], sub);
                    if (typeName == null || advance == 0) break;
                    if (typeName != "void") paramTypes.Add(typeName);
                    i += advance;
                }

                return funcName + "(" + string.Join(", ", paramTypes) + ")";
            }
            catch { return null; }
        }

        private static (string? type, int advance) ParseItaniumType(string s, List<string> sub)
        {
            if (s.Length == 0) return (null, 0);
            char c = s[0];

            // Qualifiers — consume and recurse
            if (c == 'K' || c == 'r' || c == 'V') // const, restrict, volatile
            {
                var (inner, adv) = ParseItaniumType(s[1..], sub);
                string qual = c == 'K' ? "const " : c == 'r' ? "__restrict__ " : "volatile ";
                return (inner != null ? qual + inner : null, adv + 1);
            }

            if (c == 'P') { var (inner, adv) = ParseItaniumType(s[1..], sub); return (inner != null ? inner + "*" : null, adv + 1); }
            if (c == 'R') { var (inner, adv) = ParseItaniumType(s[1..], sub); return (inner != null ? inner + "&" : null, adv + 1); }
            if (c == 'O') { var (inner, adv) = ParseItaniumType(s[1..], sub); return (inner != null ? inner + "&&" : null, adv + 1); }

            // Substitution S_ S0_ S1_ ...
            if (c == 'S')
            {
                if (s.Length > 1 && s[1] == '_') { var t = sub.Count > 0 ? sub[0] : "?"; return (t, 2); }
                if (s.Length > 2 && char.IsDigit(s[1]) && s[2] == '_') { int idx = (s[1] - '0') + 1; var t = idx < sub.Count ? sub[idx] : "?"; return (t, 3); }
                if (s.Length > 1 && s[1] == 't') return ("std", 2);
                return ("?", 1);
            }

            // Template args — skip entirely
            if (c == 'I') { var rest = SkipToE(s[1..]); return ("...", s.Length - rest.Length + 1); }

            // Builtin type
            if (_itaniumTypeMap.TryGetValue(c, out var bt))
            {
                if (bt != "void") sub.Add(bt);
                return (bt, 1);
            }

            // User-defined type (length-prefixed name)
            if (char.IsDigit(c))
            {
                var len = ParseLength(s, out var after);
                if (len > 0 && len <= after.Length)
                {
                    var name = after[..len];
                    sub.Add(name);
                    return (name, s.Length - after.Length + len);
                }
            }

            return (null, 1); // unknown — skip one char
        }

        private static string SkipToE(string s)
        {
            int depth = 0, i = 0;
            while (i < s.Length)
            {
                if (s[i] == 'I') depth++;
                else if (s[i] == 'E') { if (depth == 0) return s[(i + 1)..]; depth--; }
                i++;
            }
            return string.Empty;
        }

        private static int ParseLength(string s, out string remainder)
        {
            int i = 0;
            while (i < s.Length && char.IsDigit(s[i])) i++;
            if (i == 0) { remainder = s; return -1; }
            var len = int.Parse(s[..i]);
            remainder = s[i..];
            return len;
        }
    }
}
