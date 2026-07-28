using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    /// <summary>
    /// Rule-based Tableau formula → DAX converter.
    /// Handles IF/THEN/ELSE, CASE/WHEN, LOD expressions, and 80+ function mappings.
    /// </summary>
    public class TableauToDaxConverter
    {
        // ─────────────────────────────────────────────────────────────────────
        //  Public entry point
        // ─────────────────────────────────────────────────────────────────────

        public ConversionResult Convert(string tableauFormula)
        {
            var result = new ConversionResult();

            if (string.IsNullOrWhiteSpace(tableauFormula))
            {
                result.DaxExpression = string.Empty;
                return result;
            }

            string formula = tableauFormula.Trim();

            // Step 1: Detect table calculations — flag immediately
            if (DetectTableCalculations(formula, result))
            {
                result.DaxExpression = $"/* TABLE CALC — Manual conversion required */\n{formula}";
                result.Status = ConversionStatus.ManualRequired;
                return result;
            }

            // Step 2: Convert LOD expressions { FIXED/INCLUDE/EXCLUDE ... : ... }
            formula = ConvertLodExpressions(formula, result);

            // Step 3: Convert CASE/WHEN/THEN/ELSE/END → SWITCH()
            formula = ConvertCaseWhen(formula, result);

            // Step 4: Convert IF/THEN/ELSEIF/ELSE/END → nested IF() — inside-out
            formula = ConvertIfThenElse(formula, result);

            // Step 5: Convert IIF(cond, true, false) → IF(cond, true, false)
            formula = Regex.Replace(formula, @"\bIIF\b", "IF", RegexOptions.IgnoreCase);

            // Step 6: Apply all function substitutions
            formula = ApplyFunctionMappings(formula, result);

            // Step 7: Convert logical operators
            formula = ConvertLogicalOperators(formula, result);

            // Step 8: NULL helpers
            formula = ConvertNullHelpers(formula, result);

            // Step 9: Type cast functions
            formula = ConvertTypeCasts(formula, result);

            // Step 10: Arithmetic operator cleanup
            formula = ConvertArithmeticOperators(formula, result);

            result.DaxExpression = formula;

            if (result.Status != ConversionStatus.ManualRequired)
                result.Status = result.Warnings.Count > 0
                    ? ConversionStatus.PartialConversion
                    : ConversionStatus.Success;

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 1 — Table calculation detection
        // ─────────────────────────────────────────────────────────────────────

        private static readonly string[] TableCalcFunctions = new[]
        {
            "LOOKUP", "WINDOW_SUM", "WINDOW_AVG", "WINDOW_MIN", "WINDOW_MAX",
            "WINDOW_COUNT", "WINDOW_COUNTD", "WINDOW_STDEV", "WINDOW_MEDIAN",
            "WINDOW_PERCENTILE", "RUNNING_SUM", "RUNNING_AVG", "RUNNING_MIN",
            "RUNNING_MAX", "RUNNING_COUNT", "INDEX", "FIRST", "LAST", "SIZE",
            "TOTAL", "PREVIOUS_VALUE", "RANK", "RANK_DENSE", "RANK_MODIFIED",
            "RANK_PERCENTILE", "RANK_UNIQUE", "SCRIPT_INT", "SCRIPT_REAL",
            "SCRIPT_STR", "SCRIPT_BOOL"
        };

        private static bool DetectTableCalculations(string formula, ConversionResult result)
        {
            foreach (var fn in TableCalcFunctions)
            {
                if (Regex.IsMatch(formula, $@"\b{Regex.Escape(fn)}\s*\(", RegexOptions.IgnoreCase))
                {
                    result.Warnings.Add($"'{fn}' is a Tableau Table Calculation with no direct DAX equivalent. " +
                        "Use RANKX, iterators (SUMX/AVERAGEX), or time-intelligence functions in DAX.");
                    return true;
                }
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 2 — LOD expressions
        // ─────────────────────────────────────────────────────────────────────

        private string ConvertLodExpressions(string formula, ConversionResult result)
        {
            // Match { FIXED [d1], [d2] : AGG([field]) }
            // Match { INCLUDE [d1] : AGG([field]) }
            // Match { EXCLUDE [d1] : AGG([field]) }

            int safety = 0;
            while (safety++ < 20)
            {
                int start = FindLodStart(formula);
                if (start < 0) break;

                int end = FindMatchingBrace(formula, start);
                if (end < 0) { result.Warnings.Add("Unmatched '{' in LOD expression."); break; }

                string lodBlock = formula.Substring(start + 1, end - start - 1).Trim();
                string converted = ConvertSingleLod(lodBlock, result);

                formula = formula.Substring(0, start) + converted + formula.Substring(end + 1);
            }

            return formula;
        }

        private static int FindLodStart(string formula)
        {
            // Find '{' that is followed by FIXED, INCLUDE, or EXCLUDE
            for (int i = 0; i < formula.Length; i++)
            {
                if (formula[i] == '{')
                {
                    // Look ahead (skip whitespace)
                    int j = i + 1;
                    while (j < formula.Length && char.IsWhiteSpace(formula[j])) j++;
                    string rest = formula.Substring(j).TrimStart();
                    if (Regex.IsMatch(rest, @"^(FIXED|INCLUDE|EXCLUDE)\b", RegexOptions.IgnoreCase))
                        return i;
                }
            }
            return -1;
        }

        private static int FindMatchingBrace(string formula, int openPos)
        {
            int depth = 0;
            for (int i = openPos; i < formula.Length; i++)
            {
                if (formula[i] == '{') depth++;
                else if (formula[i] == '}')
                {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private string ConvertSingleLod(string lodBody, ConversionResult result)
        {
            // Split on ':' — left is type + dimensions, right is aggregation
            int colonPos = FindTopLevelChar(lodBody, ':');
            if (colonPos < 0)
            {
                result.Warnings.Add($"Could not parse LOD expression: {{{lodBody}}}");
                return $"/* LOD: {{{lodBody}}} */";
            }

            string left = lodBody.Substring(0, colonPos).Trim();
            string right = lodBody.Substring(colonPos + 1).Trim();

            // Convert the aggregation on the right
            string aggConverted = ConvertAggregations(right);

            var match = Regex.Match(left, @"^(FIXED|INCLUDE|EXCLUDE)\s*(.*)", RegexOptions.IgnoreCase);
            if (!match.Success)
            {
                result.Warnings.Add($"Unknown LOD type in: {{{lodBody}}}");
                return $"/* LOD: {{{lodBody}}} */";
            }

            string lodType = match.Groups[1].Value.ToUpperInvariant();
            string dimList = match.Groups[2].Value.Trim().TrimEnd(',');

            string[] dims = string.IsNullOrWhiteSpace(dimList)
                ? Array.Empty<string>()
                : Regex.Split(dimList, @",(?![^(]*\))");

            result.Notes.Add($"LOD '{lodType}' converted. Verify table names in CALCULATE expression.");

            switch (lodType)
            {
                case "FIXED":
                    if (dims.Length == 0)
                        return $"CALCULATE({aggConverted}, ALL())";
                    else
                    {
                        string allExceptArgs = string.Join(", ", Array.ConvertAll(dims, d => $"'TableName'[{d.Trim().Trim('[', ']')}]"));
                        return $"CALCULATE({aggConverted}, ALLEXCEPT('TableName', {allExceptArgs}))";
                    }

                case "INCLUDE":
                    if (dims.Length == 0)
                        return $"CALCULATE({aggConverted})";
                    else
                    {
                        string valuesArgs = string.Join(", ", Array.ConvertAll(dims, d => $"VALUES('TableName'[{d.Trim().Trim('[', ']')}])"));
                        return $"CALCULATE({aggConverted}, {valuesArgs})";
                    }

                case "EXCLUDE":
                    if (dims.Length == 0)
                        return $"CALCULATE({aggConverted})";
                    else
                    {
                        string allArgs = string.Join(", ", Array.ConvertAll(dims, d => $"ALL('TableName'[{d.Trim().Trim('[', ']')}])"));
                        return $"CALCULATE({aggConverted}, {allArgs})";
                    }

                default:
                    return $"CALCULATE({aggConverted})";
            }
        }

        private static string ConvertAggregations(string expr)
        {
            // Convert Tableau aggregation syntax to DAX inside LOD
            expr = Regex.Replace(expr, @"\bSUM\s*\(\s*\[([^\]]+)\]\s*\)", "SUM('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bAVG\s*\(\s*\[([^\]]+)\]\s*\)", "AVERAGE('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bCOUNTD\s*\(\s*\[([^\]]+)\]\s*\)", "DISTINCTCOUNT('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bCOUNT\s*\(\s*\[([^\]]+)\]\s*\)", "COUNTA('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bMIN\s*\(\s*\[([^\]]+)\]\s*\)", "MIN('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bMAX\s*\(\s*\[([^\]]+)\]\s*\)", "MAX('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bMEDIAN\s*\(\s*\[([^\]]+)\]\s*\)", "MEDIAN('TableName'[$1])", RegexOptions.IgnoreCase);
            expr = Regex.Replace(expr, @"\bATTR\s*\(\s*\[([^\]]+)\]\s*\)", "SELECTEDVALUE('TableName'[$1])", RegexOptions.IgnoreCase);
            return expr;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 3 — CASE / WHEN / THEN / ELSE / END → SWITCH()
        // ─────────────────────────────────────────────────────────────────────

        private string ConvertCaseWhen(string formula, ConversionResult result)
        {
            int safety = 0;
            while (safety++ < 20)
            {
                int casePos = FindTopLevelKeyword(formula, "CASE", 0);
                if (casePos < 0) break;

                int endPos = FindTopLevelKeyword(formula, "END", casePos + 4);
                if (endPos < 0) { result.Warnings.Add("Unmatched CASE/END."); break; }

                string block = formula.Substring(casePos, endPos - casePos + 3);
                string converted = ParseCaseBlock(block, result);
                formula = formula.Substring(0, casePos) + converted + formula.Substring(endPos + 3);
            }
            return formula;
        }

        private string ParseCaseBlock(string block, ConversionResult result)
        {
            // CASE [expr] WHEN v1 THEN r1 WHEN v2 THEN r2 ELSE default END
            var m = Regex.Match(block, @"^\s*CASE\s+(.+?)\s+(?=WHEN)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!m.Success)
            {
                result.Warnings.Add($"Could not parse CASE block: {block}");
                return $"/* {block} */";
            }

            string switchExpr = m.Groups[1].Value.Trim();
            string remainder = block.Substring(m.Index + m.Length);

            // Remove trailing END
            remainder = Regex.Replace(remainder, @"\s*END\s*$", "", RegexOptions.IgnoreCase).Trim();

            // Optional ELSE
            string? elseValue = null;
            var elseMatch = Regex.Match(remainder, @"\bELSE\b(.+)$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (elseMatch.Success)
            {
                elseValue = elseMatch.Groups[1].Value.Trim();
                remainder = remainder.Substring(0, elseMatch.Index).Trim();
            }

            // Split WHEN ... THEN ... pairs
            var whenPairs = new List<(string When, string Then)>();
            var whenMatches = Regex.Matches(remainder, @"\bWHEN\b\s*(.+?)\s*\bTHEN\b\s*(.+?)(?=\bWHEN\b|\bELSE\b|$)",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            foreach (Match wm in whenMatches)
                whenPairs.Add((wm.Groups[1].Value.Trim(), wm.Groups[2].Value.Trim()));

            if (whenPairs.Count == 0)
            {
                result.Warnings.Add($"CASE block has no WHEN clauses: {block}");
                return $"/* {block} */";
            }

            var sb = new StringBuilder();
            sb.Append($"SWITCH({switchExpr}");
            foreach (var (w, t) in whenPairs)
                sb.Append($", {w}, {t}");
            if (elseValue != null)
                sb.Append($", {elseValue}");
            sb.Append(")");

            return sb.ToString();
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 4 — IF / THEN / ELSEIF / ELSE / END → nested IF()
        // ─────────────────────────────────────────────────────────────────────

        private string ConvertIfThenElse(string formula, ConversionResult result)
        {
            int safety = 0;
            while (safety++ < 30)
            {
                // Find the LAST IF keyword (processes inside-out for nesting)
                int ifPos = FindLastTopLevelKeyword(formula, "IF");
                if (ifPos < 0) break;

                // Find the matching END after this IF
                int endPos = FindTopLevelKeyword(formula, "END", ifPos + 2);
                if (endPos < 0) { result.Warnings.Add("Unmatched IF/END block."); break; }

                string block = formula.Substring(ifPos, endPos - ifPos + 3);
                string converted = ParseIfBlock(block, result);
                formula = formula.Substring(0, ifPos) + converted + formula.Substring(endPos + 3);
            }
            return formula;
        }

        private string ParseIfBlock(string block, ConversionResult result)
        {
            // Strip leading IF and trailing END
            string inner = block.Trim();
            if (inner.StartsWith("IF ", StringComparison.OrdinalIgnoreCase))
                inner = inner.Substring(3);
            else if (inner.StartsWith("IF\t", StringComparison.OrdinalIgnoreCase))
                inner = inner.Substring(3);

            // Remove trailing END
            inner = Regex.Replace(inner, @"\s*END\s*$", "", RegexOptions.IgnoreCase).Trim();

            // Split into: cond THEN value [ELSEIF cond THEN value]* [ELSE value]
            // We find top-level THEN, ELSEIF, ELSE keywords
            var segments = SplitOnTopLevelIfKeywords(inner);

            if (segments.Count == 0 || segments[0].Keyword != "THEN")
            {
                result.Warnings.Add($"Could not parse IF block: {block}");
                return $"/* IF block parse error: {block} */";
            }

            // Build list of (condition, value) pairs and optional else
            var branches = new List<(string Cond, string Val)>();
            string? elseValue = null;
            string currentCond = segments[0].Before.Trim();

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                if (seg.Keyword == "THEN")
                {
                    // Value is everything until next segment
                    string val = (i + 1 < segments.Count) ? segments[i + 1].Before.Trim() : seg.After.Trim();
                    branches.Add((currentCond, val));
                }
                else if (seg.Keyword == "ELSEIF")
                {
                    currentCond = seg.After.Trim();
                    // Next should be THEN
                }
                else if (seg.Keyword == "ELSE")
                {
                    elseValue = seg.After.Trim();
                    break;
                }
            }

            if (branches.Count == 0)
            {
                result.Warnings.Add($"IF block has no branches: {block}");
                return $"/* {block} */";
            }

            // Build nested IF(cond1, val1, IF(cond2, val2, ...))
            return BuildNestedIf(branches, elseValue, 0);
        }

        private static string BuildNestedIf(List<(string Cond, string Val)> branches, string? elseValue, int index)
        {
            if (index >= branches.Count)
                return elseValue ?? "BLANK()";

            string inner = BuildNestedIf(branches, elseValue, index + 1);
            return $"IF({branches[index].Cond}, {branches[index].Val}, {inner})";
        }

        private class IfSegment
        {
            public string Keyword { get; set; } = "";
            public string Before { get; set; } = "";  // text before keyword
            public string After { get; set; } = "";   // text after keyword (to next keyword)
        }

        private List<IfSegment> SplitOnTopLevelIfKeywords(string text)
        {
            var result = new List<IfSegment>();
            string[] keywords = { "ELSEIF", "ELSE", "THEN" };

            int pos = 0;
            while (pos < text.Length)
            {
                // Find the next keyword that appears at top level
                int bestPos = -1;
                string bestKw = "";

                foreach (var kw in keywords)
                {
                    int found = FindTopLevelKeyword(text, kw, pos);
                    if (found >= 0 && (bestPos < 0 || found < bestPos))
                    {
                        bestPos = found;
                        bestKw = kw;
                    }
                }

                if (bestPos < 0) break;

                string before = text.Substring(pos, bestPos - pos).Trim();
                int afterStart = bestPos + bestKw.Length;

                // Find the next keyword after this one for "after" text
                int nextPos = -1;
                string nextKw = "";
                foreach (var kw in keywords)
                {
                    int found = FindTopLevelKeyword(text, kw, afterStart);
                    if (found >= 0 && (nextPos < 0 || found < nextPos))
                    {
                        nextPos = found;
                        nextKw = kw;
                    }
                }

                string after = nextPos >= 0
                    ? text.Substring(afterStart, nextPos - afterStart).Trim()
                    : text.Substring(afterStart).Trim();

                result.Add(new IfSegment { Keyword = bestKw.ToUpperInvariant(), Before = before, After = after });
                pos = afterStart;

                // For ELSE, we're done
                if (bestKw.Equals("ELSE", StringComparison.OrdinalIgnoreCase)) break;
            }

            return result;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 5 — Function mappings
        // ─────────────────────────────────────────────────────────────────────

        private string ApplyFunctionMappings(string formula, ConversionResult result)
        {
            formula = ApplyAggregationFunctions(formula, result);
            formula = ApplyStringFunctions(formula, result);
            formula = ApplyDateFunctions(formula, result);
            formula = ApplyMathFunctions(formula, result);
            return formula;
        }

        private static string ApplyAggregationFunctions(string formula, ConversionResult result)
        {
            // SUM/AVG/COUNT/COUNTD/MIN/MAX work on columns — add note about table prefix
            formula = Regex.Replace(formula, @"\bAVG\s*\(", "AVERAGE(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bCOUNTD\s*\(", "DISTINCTCOUNT(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bSTDEVP\s*\(", "STDEV.P(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bSTDEV\s*\(", "STDEV.S(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bVARP\s*\(", "VAR.P(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bVAR\s*\(", "VAR.S(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bATTR\s*\(", "SELECTEDVALUE(", RegexOptions.IgnoreCase);

            // COUNT(expr) → COUNTA(expr)
            formula = Regex.Replace(formula, @"\bCOUNT\s*\((?!A\s*\()", "COUNTA(", RegexOptions.IgnoreCase);

            return formula;
        }

        private static string ApplyStringFunctions(string formula, ConversionResult result)
        {
            // Direct equivalents
            formula = Regex.Replace(formula, @"\bLEN\s*\(", "LEN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bLEFT\s*\(", "LEFT(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bRIGHT\s*\(", "RIGHT(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bMID\s*\(", "MID(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bUPPER\s*\(", "UPPER(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bLOWER\s*\(", "LOWER(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bTRIM\s*\(", "TRIM(", RegexOptions.IgnoreCase);

            // LTRIM/RTRIM — DAX has no direct equivalent, use TRIM (with note)
            if (Regex.IsMatch(formula, @"\bLTRIM\s*\(", RegexOptions.IgnoreCase))
            {
                formula = Regex.Replace(formula, @"\bLTRIM\s*\(", "TRIM( /* LTRIM: DAX TRIM removes both sides */ ", RegexOptions.IgnoreCase);
                result.Warnings.Add("LTRIM: DAX TRIM() removes both leading and trailing spaces. No direct LTRIM equivalent in DAX.");
            }
            if (Regex.IsMatch(formula, @"\bRTRIM\s*\(", RegexOptions.IgnoreCase))
            {
                formula = Regex.Replace(formula, @"\bRTRIM\s*\(", "TRIM( /* RTRIM: DAX TRIM removes both sides */ ", RegexOptions.IgnoreCase);
                result.Warnings.Add("RTRIM: DAX TRIM() removes both leading and trailing spaces. No direct RTRIM equivalent in DAX.");
            }

            // CONTAINS([field], "str") → CONTAINSSTRING([field], "str")
            formula = Regex.Replace(formula, @"\bCONTAINS\s*\(", "CONTAINSSTRING(", RegexOptions.IgnoreCase);

            // STARTSWITH([x], "str") → LEFT([x], LEN("str")) = "str"
            formula = Regex.Replace(formula,
                @"\bSTARTSWITH\s*\(\s*(.+?)\s*,\s*("".+?"")\s*\)",
                m => $"LEFT({m.Groups[1].Value}, LEN({m.Groups[2].Value})) = {m.Groups[2].Value}",
                RegexOptions.IgnoreCase);

            // ENDSWITH([x], "str") → RIGHT([x], LEN("str")) = "str"
            formula = Regex.Replace(formula,
                @"\bENDSWITH\s*\(\s*(.+?)\s*,\s*("".+?"")\s*\)",
                m => $"RIGHT({m.Groups[1].Value}, LEN({m.Groups[2].Value})) = {m.Groups[2].Value}",
                RegexOptions.IgnoreCase);

            // FIND([x], "str") → SEARCH("str", [x]) — note: SEARCH is 1-based, returns error if not found
            formula = Regex.Replace(formula,
                @"\bFIND\s*\(\s*(.+?)\s*,\s*("".+?"")\s*\)",
                m => $"SEARCH({m.Groups[2].Value}, {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // REPLACE([x], "old", "new") → SUBSTITUTE([x], "old", "new")
            formula = Regex.Replace(formula, @"\bREPLACE\s*\(", "SUBSTITUTE(", RegexOptions.IgnoreCase);

            // SPACE(n) → REPT(" ", n)
            formula = Regex.Replace(formula,
                @"\bSPACE\s*\(\s*(.+?)\s*\)",
                m => $"REPT(\" \", {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // ASCII([x]) → UNICODE([x])
            formula = Regex.Replace(formula, @"\bASCII\s*\(", "UNICODE(", RegexOptions.IgnoreCase);

            // CHAR(n) → UNICHAR(n)
            formula = Regex.Replace(formula, @"\bCHAR\s*\(", "UNICHAR(", RegexOptions.IgnoreCase);

            // SPLIT — no direct DAX equivalent
            if (Regex.IsMatch(formula, @"\bSPLIT\s*\(", RegexOptions.IgnoreCase))
            {
                result.Warnings.Add("SPLIT(): No direct DAX equivalent. Use Power Query (Text.Split) in the data model instead.");
                formula = Regex.Replace(formula, @"\bSPLIT\s*\(", "/* SPLIT - use Power Query */ SPLIT(", RegexOptions.IgnoreCase);
            }

            // REGEXP_* — no DAX equivalent
            foreach (var fn in new[] { "REGEXP_MATCH", "REGEXP_REPLACE", "REGEXP_EXTRACT", "REGEXP_EXTRACT_NTH" })
            {
                if (Regex.IsMatch(formula, $@"\b{fn}\s*\(", RegexOptions.IgnoreCase))
                {
                    result.Warnings.Add($"{fn}(): No DAX equivalent. Use Power Query for regex operations.");
                    formula = Regex.Replace(formula, $@"\b{fn}\s*\(",
                        $"/* {fn} - no DAX equivalent */ {fn}(", RegexOptions.IgnoreCase);
                }
            }

            // FINDNTH → no direct equivalent
            if (Regex.IsMatch(formula, @"\bFINDNTH\s*\(", RegexOptions.IgnoreCase))
            {
                result.Warnings.Add("FINDNTH(): No direct DAX equivalent.");
                formula = Regex.Replace(formula, @"\bFINDNTH\s*\(", "/* FINDNTH - no DAX equiv */ FINDNTH(", RegexOptions.IgnoreCase);
            }

            return formula;
        }

        private static string ApplyDateFunctions(string formula, ConversionResult result)
        {
            // Direct equivalents
            formula = Regex.Replace(formula, @"\bTODAY\s*\(\s*\)", "TODAY()", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bNOW\s*\(\s*\)", "NOW()", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bDAY\s*\(", "DAY(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bMONTH\s*\(", "MONTH(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bYEAR\s*\(", "YEAR(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bHOUR\s*\(", "HOUR(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bMINUTE\s*\(", "MINUTE(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bSECOND\s*\(", "SECOND(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bDATE\s*\(", "DATE(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bTIME\s*\(", "TIME(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bWEEKDAY\s*\(", "WEEKDAY(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bWEEKNUM\s*\(", "WEEKNUM(", RegexOptions.IgnoreCase);

            // MAKEDATE(y, m, d) → DATE(y, m, d)
            formula = Regex.Replace(formula, @"\bMAKEDATE\s*\(", "DATE(", RegexOptions.IgnoreCase);

            // MAKETIME(h, m, s) → TIME(h, m, s)
            formula = Regex.Replace(formula, @"\bMAKETIME\s*\(", "TIME(", RegexOptions.IgnoreCase);

            // MAKEDATETIME(date, time) → date + time (note)
            if (Regex.IsMatch(formula, @"\bMAKEDATETIME\s*\(", RegexOptions.IgnoreCase))
            {
                result.Notes.Add("MAKEDATETIME: Converted to date + time arithmetic. Verify result.");
                formula = Regex.Replace(formula,
                    @"\bMAKEDATETIME\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)",
                    m => $"({m.Groups[1].Value} + {m.Groups[2].Value})",
                    RegexOptions.IgnoreCase);
            }

            // DATEPART('part', [date]) → DAX scalar function
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'year'\s*,\s*(.+?)\s*\)", m => $"YEAR({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'month'\s*,\s*(.+?)\s*\)", m => $"MONTH({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'day'\s*,\s*(.+?)\s*\)", m => $"DAY({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'hour'\s*,\s*(.+?)\s*\)", m => $"HOUR({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'minute'\s*,\s*(.+?)\s*\)", m => $"MINUTE({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'second'\s*,\s*(.+?)\s*\)", m => $"SECOND({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'quarter'\s*,\s*(.+?)\s*\)",
                m => $"INT((MONTH({m.Groups[1].Value}) - 1) / 3) + 1",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'week'\s*,\s*(.+?)\s*\)", m => $"WEEKNUM({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEPART\s*\(\s*'weekday'\s*,\s*(.+?)\s*\)", m => $"WEEKDAY({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // DATENAME — same as DATEPART for most parts, returns string
            formula = Regex.Replace(formula,
                @"\bDATENAME\s*\(\s*'month'\s*,\s*(.+?)\s*\)",
                m => $"FORMAT({m.Groups[1].Value}, \"MMMM\")",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATENAME\s*\(\s*'weekday'\s*,\s*(.+?)\s*\)",
                m => $"FORMAT({m.Groups[1].Value}, \"dddd\")",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATENAME\s*\(\s*'year'\s*,\s*(.+?)\s*\)",
                m => $"FORMAT({m.Groups[1].Value}, \"yyyy\")",
                RegexOptions.IgnoreCase);

            // DATEDIFF('day', start, end) → DATEDIFF(start, end, DAY)
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'day'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, DAY)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'week'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, WEEK)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'month'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, MONTH)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'quarter'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, QUARTER)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'year'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, YEAR)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'hour'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, HOUR)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'minute'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, MINUTE)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEDIFF\s*\(\s*'second'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"DATEDIFF({m.Groups[1].Value}, {m.Groups[2].Value}, SECOND)",
                RegexOptions.IgnoreCase);

            // DATEADD('day', n, date) → date + n  (scalar, for months use EDATE)
            formula = Regex.Replace(formula,
                @"\bDATEADD\s*\(\s*'day'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"({m.Groups[2].Value} + {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEADD\s*\(\s*'month'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"EDATE({m.Groups[2].Value}, {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEADD\s*\(\s*'year'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"EDATE({m.Groups[2].Value}, {m.Groups[1].Value} * 12)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATEADD\s*\(\s*'quarter'\s*,\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"EDATE({m.Groups[2].Value}, {m.Groups[1].Value} * 3)",
                RegexOptions.IgnoreCase);

            // DATETRUNC — approximate
            formula = Regex.Replace(formula,
                @"\bDATETRUNC\s*\(\s*'month'\s*,\s*(.+?)\s*\)",
                m => $"EOMONTH({m.Groups[1].Value}, -1) + 1",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATETRUNC\s*\(\s*'year'\s*,\s*(.+?)\s*\)",
                m => $"DATE(YEAR({m.Groups[1].Value}), 1, 1)",
                RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula,
                @"\bDATETRUNC\s*\(\s*'quarter'\s*,\s*(.+?)\s*\)",
                m => $"DATE(YEAR({m.Groups[1].Value}), (INT((MONTH({m.Groups[1].Value})-1)/3)*3)+1, 1)",
                RegexOptions.IgnoreCase);

            // ISDATE
            if (Regex.IsMatch(formula, @"\bISDATE\s*\(", RegexOptions.IgnoreCase))
            {
                result.Notes.Add("ISDATE(): No direct DAX equivalent. Consider using IFERROR(DATEVALUE(...), FALSE) or data type checks.");
                formula = Regex.Replace(formula, @"\bISDATE\s*\(", "/* ISDATE - no direct DAX equiv */ ISDATE(", RegexOptions.IgnoreCase);
            }

            return formula;
        }

        private static string ApplyMathFunctions(string formula, ConversionResult result)
        {
            formula = Regex.Replace(formula, @"\bABS\s*\(", "ABS(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bSQRT\s*\(", "SQRT(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bPOWER\s*\(", "POWER(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bEXP\s*\(", "EXP(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bLN\s*\(", "LN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bLOG\s*\(", "LOG(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bROUND\s*\(", "ROUND(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bPI\s*\(\s*\)", "PI()", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bDEGREES\s*\(", "DEGREES(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bRADIANS\s*\(", "RADIANS(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bSIN\s*\(", "SIN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bCOS\s*\(", "COS(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bTAN\s*\(", "TAN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bASIN\s*\(", "ASIN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bACOS\s*\(", "ACOS(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bATAN\s*\(", "ATAN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bATAN2\s*\(", "ATAN2(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bMIN\s*\(", "MIN(", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bMAX\s*\(", "MAX(", RegexOptions.IgnoreCase);

            // CEILING([x]) → CEILING([x], 1)
            formula = Regex.Replace(formula,
                @"\bCEILING\s*\(\s*(.+?)\s*\)(?!\s*,)",
                m => $"CEILING({m.Groups[1].Value}, 1)",
                RegexOptions.IgnoreCase);

            // FLOOR([x]) → FLOOR([x], 1)
            formula = Regex.Replace(formula,
                @"\bFLOOR\s*\(\s*(.+?)\s*\)(?!\s*,)",
                m => $"FLOOR({m.Groups[1].Value}, 1)",
                RegexOptions.IgnoreCase);

            // SIGN([x]) → IF([x] > 0, 1, IF([x] < 0, -1, 0))
            formula = Regex.Replace(formula,
                @"\bSIGN\s*\(\s*(.+?)\s*\)",
                m => $"IF({m.Groups[1].Value} > 0, 1, IF({m.Groups[1].Value} < 0, -1, 0))",
                RegexOptions.IgnoreCase);

            // COT([x]) → 1/TAN([x])
            formula = Regex.Replace(formula,
                @"\bCOT\s*\(\s*(.+?)\s*\)",
                m => $"(1 / TAN({m.Groups[1].Value}))",
                RegexOptions.IgnoreCase);

            // DIV(a, b) → QUOTIENT(a, b)
            formula = Regex.Replace(formula, @"\bDIV\s*\(", "QUOTIENT(", RegexOptions.IgnoreCase);

            // % operator → MOD()
            formula = Regex.Replace(formula,
                @"(\[.+?\]|\d+)\s*%\s*(\[.+?\]|\d+)",
                m => $"MOD({m.Groups[1].Value}, {m.Groups[2].Value})",
                RegexOptions.IgnoreCase);

            // ^ (power) operator → POWER()
            formula = Regex.Replace(formula,
                @"(\[.+?\]|\d+(?:\.\d+)?)\s*\^\s*(\[.+?\]|\d+(?:\.\d+)?)",
                m => $"POWER({m.Groups[1].Value}, {m.Groups[2].Value})");

            // HEXBINX / HEXBINY — no DAX equivalent
            foreach (var fn in new[] { "HEXBINX", "HEXBINY" })
            {
                if (Regex.IsMatch(formula, $@"\b{fn}\s*\(", RegexOptions.IgnoreCase))
                {
                    result.Warnings.Add($"{fn}(): No DAX equivalent (Tableau-specific spatial function).");
                    formula = Regex.Replace(formula, $@"\b{fn}\s*\(",
                        $"/* {fn} - no DAX equiv */ {fn}(", RegexOptions.IgnoreCase);
                }
            }

            return formula;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 6 — Logical operators
        // ─────────────────────────────────────────────────────────────────────

        private static string ConvertLogicalOperators(string formula, ConversionResult result)
        {
            // AND keyword → && (but not inside strings)
            formula = Regex.Replace(formula, @"(?<![A-Za-z])\bAND\b(?![A-Za-z])", "&&", RegexOptions.IgnoreCase);

            // OR keyword → ||
            formula = Regex.Replace(formula, @"(?<![A-Za-z])\bOR\b(?![A-Za-z])", "||", RegexOptions.IgnoreCase);

            // NOT(x) stays as NOT(x) — DAX supports NOT()
            // NOT followed by [ — convert to NOT( ... )
            formula = Regex.Replace(formula, @"\bNOT\s*\(", "NOT(", RegexOptions.IgnoreCase);

            // TRUE / FALSE literals
            formula = Regex.Replace(formula, @"\bTRUE\b", "TRUE()", RegexOptions.IgnoreCase);
            formula = Regex.Replace(formula, @"\bFALSE\b", "FALSE()", RegexOptions.IgnoreCase);

            // Fix double-replacement of TRUE()/FALSE() back
            formula = Regex.Replace(formula, @"TRUE\s*\(\s*\)\s*\(\s*\)", "TRUE()");
            formula = Regex.Replace(formula, @"FALSE\s*\(\s*\)\s*\(\s*\)", "FALSE()");

            return formula;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 7 — NULL helpers
        // ─────────────────────────────────────────────────────────────────────

        private static string ConvertNullHelpers(string formula, ConversionResult result)
        {
            // ISNULL([x]) → ISBLANK([x])
            formula = Regex.Replace(formula, @"\bISNULL\s*\(", "ISBLANK(", RegexOptions.IgnoreCase);

            // IFNULL([x], [y]) → IF(ISBLANK([x]), [y], [x])
            formula = Regex.Replace(formula,
                @"\bIFNULL\s*\(\s*(.+?)\s*,\s*(.+?)\s*\)",
                m => $"IF(ISBLANK({m.Groups[1].Value}), {m.Groups[2].Value}, {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // ZN([x]) → IF(ISBLANK([x]), 0, [x])
            formula = Regex.Replace(formula,
                @"\bZN\s*\(\s*(.+?)\s*\)",
                m => $"IF(ISBLANK({m.Groups[1].Value}), 0, {m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // NULL literal → BLANK()
            formula = Regex.Replace(formula, @"\bNULL\b", "BLANK()", RegexOptions.IgnoreCase);

            return formula;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 8 — Type casts
        // ─────────────────────────────────────────────────────────────────────

        private static string ConvertTypeCasts(string formula, ConversionResult result)
        {
            // INT([x]) → INT([x])
            formula = Regex.Replace(formula, @"\bINT\s*\(", "INT(", RegexOptions.IgnoreCase);

            // FLOAT([x]) → CONVERT([x], CURRENCY) or just use [x] * 1.0 — use VALUE()
            formula = Regex.Replace(formula,
                @"\bFLOAT\s*\(\s*(.+?)\s*\)",
                m => $"VALUE({m.Groups[1].Value})",
                RegexOptions.IgnoreCase);

            // STR([x]) → FORMAT([x], "General")
            formula = Regex.Replace(formula,
                @"\bSTR\s*\(\s*(.+?)\s*\)",
                m => $"FORMAT({m.Groups[1].Value}, \"General\")",
                RegexOptions.IgnoreCase);

            // DATETIME([x]) → DATEVALUE([x])
            formula = Regex.Replace(formula, @"\bDATETIME\s*\(", "DATEVALUE(", RegexOptions.IgnoreCase);

            // BOOL([x]) → IF([x], TRUE(), FALSE())
            formula = Regex.Replace(formula,
                @"\bBOOL\s*\(\s*(.+?)\s*\)",
                m => $"IF({m.Groups[1].Value}, TRUE(), FALSE())",
                RegexOptions.IgnoreCase);

            return formula;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Step 9 — Arithmetic operators
        // ─────────────────────────────────────────────────────────────────────

        private static string ConvertArithmeticOperators(string formula, ConversionResult result)
        {
            // != → <> (DAX uses <> for inequality)
            formula = formula.Replace("!=", "<>");

            // // (double slash) for integer division in Tableau — use INT(a/b)
            formula = Regex.Replace(formula, @"(.+?)\s*\/\/\s*(.+)", m =>
                $"INT({m.Groups[1].Value.Trim()} / {m.Groups[2].Value.Trim()})");

            return formula;
        }

        // ─────────────────────────────────────────────────────────────────────
        //  Utility: context-aware keyword finder
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>Finds first occurrence of keyword at the top syntactic level (not inside strings/parens/brackets).</summary>
        private static int FindTopLevelKeyword(string text, string keyword, int start)
        {
            int depth = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;
            int i = start;

            while (i < text.Length)
            {
                char c = text[i];

                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (c == '\'') { inSingleQuote = true; i++; continue; }
                    if (c == '"') { inDoubleQuote = true; i++; continue; }
                    if (c == '(' || c == '{' || c == '[') { depth++; i++; continue; }
                    if (c == ')' || c == '}' || c == ']') { depth--; i++; continue; }

                    if (depth == 0 && i + keyword.Length <= text.Length)
                    {
                        if (string.Compare(text, i, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0)
                        {
                            bool beforeOk = i == 0 || !char.IsLetterOrDigit(text[i - 1]) && text[i - 1] != '_';
                            int afterIdx = i + keyword.Length;
                            bool afterOk = afterIdx >= text.Length || !char.IsLetterOrDigit(text[afterIdx]) && text[afterIdx] != '_';
                            if (beforeOk && afterOk) return i;
                        }
                    }
                }
                else if (inSingleQuote && c == '\'') { inSingleQuote = false; }
                else if (inDoubleQuote && c == '"') { inDoubleQuote = false; }

                i++;
            }
            return -1;
        }

        /// <summary>Finds last occurrence of keyword at top level (for inside-out IF processing).</summary>
        private static int FindLastTopLevelKeyword(string text, string keyword)
        {
            int last = -1;
            int pos = 0;
            while (true)
            {
                int found = FindTopLevelKeyword(text, keyword, pos);
                if (found < 0) break;
                last = found;
                pos = found + keyword.Length;
            }
            return last;
        }

        /// <summary>Finds first occurrence of a single char at top level (not inside strings/parens/brackets).</summary>
        private static int FindTopLevelChar(string text, char ch)
        {
            int depth = 0;
            bool inSingleQuote = false;
            bool inDoubleQuote = false;

            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!inSingleQuote && !inDoubleQuote)
                {
                    if (c == '\'') { inSingleQuote = true; continue; }
                    if (c == '"') { inDoubleQuote = true; continue; }
                    if (c == '(' || c == '{' || c == '[') depth++;
                    else if (c == ')' || c == '}' || c == ']') depth--;
                    else if (c == ch && depth == 0) return i;
                }
                else if (inSingleQuote && c == '\'') inSingleQuote = false;
                else if (inDoubleQuote && c == '"') inDoubleQuote = false;
            }
            return -1;
        }
    }
}
