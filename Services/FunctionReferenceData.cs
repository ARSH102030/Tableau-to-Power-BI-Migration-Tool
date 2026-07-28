using System.Collections.Generic;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public static class FunctionReferenceData
    {
        public static List<FunctionReference> GetAll() => new()
        {
            // ── Aggregation ──────────────────────────────────────────────────────
            new() { Category = "Aggregation", TableauFunction = "SUM([field])",          DaxEquivalent = "SUM('Table'[field])",              Notes = "Direct equivalent" },
            new() { Category = "Aggregation", TableauFunction = "AVG([field])",          DaxEquivalent = "AVERAGE('Table'[field])",          Notes = "Renamed to AVERAGE in DAX" },
            new() { Category = "Aggregation", TableauFunction = "COUNT([field])",        DaxEquivalent = "COUNTA('Table'[field])",           Notes = "Use COUNT for numeric-only columns" },
            new() { Category = "Aggregation", TableauFunction = "COUNTD([field])",       DaxEquivalent = "DISTINCTCOUNT('Table'[field])",    Notes = "Direct equivalent" },
            new() { Category = "Aggregation", TableauFunction = "MIN([field])",          DaxEquivalent = "MIN('Table'[field])",              Notes = "Direct equivalent" },
            new() { Category = "Aggregation", TableauFunction = "MAX([field])",          DaxEquivalent = "MAX('Table'[field])",              Notes = "Direct equivalent" },
            new() { Category = "Aggregation", TableauFunction = "MEDIAN([field])",       DaxEquivalent = "MEDIAN('Table'[field])",           Notes = "Direct equivalent" },
            new() { Category = "Aggregation", TableauFunction = "STDEV([field])",        DaxEquivalent = "STDEV.S('Table'[field])",          Notes = "Sample std dev" },
            new() { Category = "Aggregation", TableauFunction = "STDEVP([field])",       DaxEquivalent = "STDEV.P('Table'[field])",          Notes = "Population std dev" },
            new() { Category = "Aggregation", TableauFunction = "VAR([field])",          DaxEquivalent = "VAR.S('Table'[field])",            Notes = "Sample variance" },
            new() { Category = "Aggregation", TableauFunction = "VARP([field])",         DaxEquivalent = "VAR.P('Table'[field])",            Notes = "Population variance" },
            new() { Category = "Aggregation", TableauFunction = "ATTR([field])",         DaxEquivalent = "SELECTEDVALUE('Table'[field])",    Notes = "Returns value if unique in context" },
            new() { Category = "Aggregation", TableauFunction = "PERCENTILE([f], 0.9)",  DaxEquivalent = "PERCENTILEX.INC(Table, Table[f], 0.9)", Notes = "Requires iterator form in DAX" },

            // ── Logical ──────────────────────────────────────────────────────────
            new() { Category = "Logical", TableauFunction = "IF c THEN a ELSE b END",   DaxEquivalent = "IF(c, a, b)",                     Notes = "IF is a statement in Tableau, function in DAX" },
            new() { Category = "Logical", TableauFunction = "IIF(c, a, b)",             DaxEquivalent = "IF(c, a, b)",                     Notes = "Direct equivalent" },
            new() { Category = "Logical", TableauFunction = "CASE f WHEN v THEN r END", DaxEquivalent = "SWITCH(f, v, r)",                 Notes = "SWITCH() for multi-branch" },
            new() { Category = "Logical", TableauFunction = "AND",                      DaxEquivalent = "&&",                              Notes = "Keyword → operator" },
            new() { Category = "Logical", TableauFunction = "OR",                       DaxEquivalent = "||",                              Notes = "Keyword → operator" },
            new() { Category = "Logical", TableauFunction = "NOT([x])",                 DaxEquivalent = "NOT([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Logical", TableauFunction = "ISNULL([x])",              DaxEquivalent = "ISBLANK([x])",                    Notes = "DAX uses BLANK() not NULL" },
            new() { Category = "Logical", TableauFunction = "IFNULL([x], [y])",         DaxEquivalent = "IF(ISBLANK([x]), [y], [x])",      Notes = "No direct IFNULL in DAX" },
            new() { Category = "Logical", TableauFunction = "ZN([x])",                  DaxEquivalent = "IF(ISBLANK([x]), 0, [x])",        Notes = "Zero if null" },
            new() { Category = "Logical", TableauFunction = "NULL",                     DaxEquivalent = "BLANK()",                         Notes = "Tableau NULL = DAX BLANK()" },
            new() { Category = "Logical", TableauFunction = "TRUE",                     DaxEquivalent = "TRUE()",                          Notes = "DAX requires function call syntax" },
            new() { Category = "Logical", TableauFunction = "FALSE",                    DaxEquivalent = "FALSE()",                         Notes = "DAX requires function call syntax" },

            // ── String ───────────────────────────────────────────────────────────
            new() { Category = "String", TableauFunction = "LEN([x])",                  DaxEquivalent = "LEN([x])",                        Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "LEFT([x], n)",              DaxEquivalent = "LEFT([x], n)",                    Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "RIGHT([x], n)",             DaxEquivalent = "RIGHT([x], n)",                   Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "MID([x], start, len)",      DaxEquivalent = "MID([x], start, len)",            Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "UPPER([x])",                DaxEquivalent = "UPPER([x])",                      Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "LOWER([x])",                DaxEquivalent = "LOWER([x])",                      Notes = "Direct equivalent" },
            new() { Category = "String", TableauFunction = "TRIM([x])",                 DaxEquivalent = "TRIM([x])",                       Notes = "Both remove leading/trailing spaces" },
            new() { Category = "String", TableauFunction = "LTRIM([x])",                DaxEquivalent = "TRIM([x]) — approximate",         Notes = "DAX TRIM removes both sides; no LTRIM" },
            new() { Category = "String", TableauFunction = "RTRIM([x])",                DaxEquivalent = "TRIM([x]) — approximate",         Notes = "DAX TRIM removes both sides; no RTRIM" },
            new() { Category = "String", TableauFunction = "CONTAINS([x], \"str\")",    DaxEquivalent = "CONTAINSSTRING([x], \"str\")",    Notes = "Case-insensitive in DAX" },
            new() { Category = "String", TableauFunction = "STARTSWITH([x], \"str\")",  DaxEquivalent = "LEFT([x], LEN(\"str\")) = \"str\"", Notes = "No STARTSWITH in DAX — use LEFT" },
            new() { Category = "String", TableauFunction = "ENDSWITH([x], \"str\")",    DaxEquivalent = "RIGHT([x], LEN(\"str\")) = \"str\"", Notes = "No ENDSWITH in DAX — use RIGHT" },
            new() { Category = "String", TableauFunction = "FIND([x], \"str\")",        DaxEquivalent = "SEARCH(\"str\", [x])",            Notes = "Args reversed; SEARCH errors if not found" },
            new() { Category = "String", TableauFunction = "REPLACE([x], \"a\", \"b\")", DaxEquivalent = "SUBSTITUTE([x], \"a\", \"b\")", Notes = "Renamed in DAX" },
            new() { Category = "String", TableauFunction = "SPACE(n)",                  DaxEquivalent = "REPT(\" \", n)",                  Notes = "No SPACE() in DAX" },
            new() { Category = "String", TableauFunction = "ASCII([x])",                DaxEquivalent = "UNICODE([x])",                    Notes = "Returns Unicode code point" },
            new() { Category = "String", TableauFunction = "CHAR(n)",                   DaxEquivalent = "UNICHAR(n)",                      Notes = "Returns Unicode character" },
            new() { Category = "String", TableauFunction = "STR([x])",                  DaxEquivalent = "FORMAT([x], \"General\")",        Notes = "Use FORMAT for type conversion" },
            new() { Category = "String", TableauFunction = "SPLIT([x], delim, n)",      DaxEquivalent = "No equivalent — use Power Query", Notes = "Text.Split in Power Query M" },
            new() { Category = "String", TableauFunction = "REGEXP_MATCH([x], pat)",    DaxEquivalent = "No equivalent — use Power Query", Notes = "Text.RegularExpression in Power Query M" },
            new() { Category = "String", TableauFunction = "REGEXP_REPLACE([x], p, r)", DaxEquivalent = "No equivalent — use Power Query", Notes = "Text.RegularExpression in Power Query M" },
            new() { Category = "String", TableauFunction = "REGEXP_EXTRACT([x], pat)",  DaxEquivalent = "No equivalent — use Power Query", Notes = "Text.RegularExpression in Power Query M" },

            // ── Date ─────────────────────────────────────────────────────────────
            new() { Category = "Date", TableauFunction = "TODAY()",                     DaxEquivalent = "TODAY()",                         Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "NOW()",                       DaxEquivalent = "NOW()",                           Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DAY([date])",                 DaxEquivalent = "DAY([date])",                     Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "MONTH([date])",               DaxEquivalent = "MONTH([date])",                   Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "YEAR([date])",                DaxEquivalent = "YEAR([date])",                    Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "HOUR([dt])",                  DaxEquivalent = "HOUR([dt])",                      Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "MINUTE([dt])",                DaxEquivalent = "MINUTE([dt])",                    Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "SECOND([dt])",                DaxEquivalent = "SECOND([dt])",                    Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATEPART('year', [d])",       DaxEquivalent = "YEAR([d])",                       Notes = "Use scalar date functions" },
            new() { Category = "Date", TableauFunction = "DATEPART('month', [d])",      DaxEquivalent = "MONTH([d])",                      Notes = "Use scalar date functions" },
            new() { Category = "Date", TableauFunction = "DATEPART('quarter', [d])",    DaxEquivalent = "INT((MONTH([d])-1)/3)+1",         Notes = "No native QUARTER scalar in DAX" },
            new() { Category = "Date", TableauFunction = "DATEPART('week', [d])",       DaxEquivalent = "WEEKNUM([d])",                    Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATEPART('weekday', [d])",    DaxEquivalent = "WEEKDAY([d])",                    Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATEDIFF('day', s, e)",       DaxEquivalent = "DATEDIFF(s, e, DAY)",             Notes = "Argument order same; interval is enum in DAX" },
            new() { Category = "Date", TableauFunction = "DATEDIFF('month', s, e)",     DaxEquivalent = "DATEDIFF(s, e, MONTH)",           Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATEDIFF('year', s, e)",      DaxEquivalent = "DATEDIFF(s, e, YEAR)",            Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATEADD('day', n, d)",        DaxEquivalent = "d + n",                           Notes = "DAX dates support +/- for days" },
            new() { Category = "Date", TableauFunction = "DATEADD('month', n, d)",      DaxEquivalent = "EDATE(d, n)",                     Notes = "EDATE adds months" },
            new() { Category = "Date", TableauFunction = "DATEADD('year', n, d)",       DaxEquivalent = "EDATE(d, n*12)",                  Notes = "EDATE with months*12" },
            new() { Category = "Date", TableauFunction = "MAKEDATE(y, m, d)",           DaxEquivalent = "DATE(y, m, d)",                   Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "MAKETIME(h, m, s)",           DaxEquivalent = "TIME(h, m, s)",                   Notes = "Direct equivalent" },
            new() { Category = "Date", TableauFunction = "DATETRUNC('month', [d])",     DaxEquivalent = "EOMONTH([d], -1) + 1",           Notes = "First day of month" },
            new() { Category = "Date", TableauFunction = "DATETRUNC('year', [d])",      DaxEquivalent = "DATE(YEAR([d]), 1, 1)",           Notes = "First day of year" },
            new() { Category = "Date", TableauFunction = "DATENAME('month', [d])",      DaxEquivalent = "FORMAT([d], \"MMMM\")",           Notes = "Returns month name as text" },
            new() { Category = "Date", TableauFunction = "DATENAME('weekday', [d])",    DaxEquivalent = "FORMAT([d], \"dddd\")",           Notes = "Returns day name as text" },

            // ── Math ─────────────────────────────────────────────────────────────
            new() { Category = "Math", TableauFunction = "ABS([x])",                    DaxEquivalent = "ABS([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "CEILING([x])",                DaxEquivalent = "CEILING([x], 1)",                 Notes = "DAX requires significance parameter" },
            new() { Category = "Math", TableauFunction = "FLOOR([x])",                  DaxEquivalent = "FLOOR([x], 1)",                   Notes = "DAX requires significance parameter" },
            new() { Category = "Math", TableauFunction = "ROUND([x], n)",               DaxEquivalent = "ROUND([x], n)",                   Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "SQRT([x])",                   DaxEquivalent = "SQRT([x])",                       Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "POWER([x], n)",               DaxEquivalent = "POWER([x], n)",                   Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "[x] ^ n",                     DaxEquivalent = "POWER([x], n)",                   Notes = "^ operator → POWER() in DAX" },
            new() { Category = "Math", TableauFunction = "EXP([x])",                    DaxEquivalent = "EXP([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "LN([x])",                     DaxEquivalent = "LN([x])",                         Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "LOG([x], base)",              DaxEquivalent = "LOG([x], base)",                  Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "SIGN([x])",                   DaxEquivalent = "IF([x]>0,1,IF([x]<0,-1,0))",    Notes = "No SIGN() in DAX; use nested IF" },
            new() { Category = "Math", TableauFunction = "COT([x])",                    DaxEquivalent = "1/TAN([x])",                      Notes = "No COT() in DAX" },
            new() { Category = "Math", TableauFunction = "DIV(a, b)",                   DaxEquivalent = "QUOTIENT(a, b)",                  Notes = "Integer division" },
            new() { Category = "Math", TableauFunction = "[x] % [y]",                   DaxEquivalent = "MOD([x], [y])",                   Notes = "% operator → MOD() in DAX" },
            new() { Category = "Math", TableauFunction = "PI()",                        DaxEquivalent = "PI()",                            Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "SIN([x])",                    DaxEquivalent = "SIN([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "COS([x])",                    DaxEquivalent = "COS([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "TAN([x])",                    DaxEquivalent = "TAN([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "ASIN([x])",                   DaxEquivalent = "ASIN([x])",                       Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "ACOS([x])",                   DaxEquivalent = "ACOS([x])",                       Notes = "Direct equivalent" },
            new() { Category = "Math", TableauFunction = "ATAN([x])",                   DaxEquivalent = "ATAN([x])",                       Notes = "Direct equivalent" },

            // ── Type Conversion ──────────────────────────────────────────────────
            new() { Category = "Type Conversion", TableauFunction = "INT([x])",         DaxEquivalent = "INT([x])",                        Notes = "Direct equivalent" },
            new() { Category = "Type Conversion", TableauFunction = "FLOAT([x])",       DaxEquivalent = "VALUE([x])",                      Notes = "No FLOAT(); use VALUE() or multiplication" },
            new() { Category = "Type Conversion", TableauFunction = "STR([x])",         DaxEquivalent = "FORMAT([x], \"General\")",        Notes = "Use FORMAT for type conversion" },
            new() { Category = "Type Conversion", TableauFunction = "DATE([x])",        DaxEquivalent = "DATE(YEAR([x]), MONTH([x]), DAY([x]))", Notes = "Or DATEVALUE([x]) for string-to-date" },
            new() { Category = "Type Conversion", TableauFunction = "DATETIME([x])",    DaxEquivalent = "DATEVALUE([x])",                  Notes = "For string-to-datetime" },
            new() { Category = "Type Conversion", TableauFunction = "BOOL([x])",        DaxEquivalent = "IF([x], TRUE(), FALSE())",        Notes = "No BOOL() in DAX; use IF" },

            // ── LOD ──────────────────────────────────────────────────────────────
            new() { Category = "LOD", TableauFunction = "{ FIXED [Dim] : SUM([f]) }",   DaxEquivalent = "CALCULATE(SUM(T[f]), ALLEXCEPT(T, T[Dim]))", Notes = "Remove row context; compute at Dim level" },
            new() { Category = "LOD", TableauFunction = "{ FIXED : SUM([f]) }",         DaxEquivalent = "CALCULATE(SUM(T[f]), ALL())",     Notes = "Grand total — ignore all filters" },
            new() { Category = "LOD", TableauFunction = "{ INCLUDE [Dim] : SUM([f]) }", DaxEquivalent = "CALCULATE(SUM(T[f]), VALUES(T[Dim]))", Notes = "Add dimension to context" },
            new() { Category = "LOD", TableauFunction = "{ EXCLUDE [Dim] : SUM([f]) }", DaxEquivalent = "CALCULATE(SUM(T[f]), ALL(T[Dim]))", Notes = "Remove dimension from context" },

            // ── Table Calculations (no equivalent) ───────────────────────────────
            new() { Category = "Table Calc", TableauFunction = "LOOKUP(expr, offset)",  DaxEquivalent = "No equivalent — manual conversion", Notes = "Use EARLIER(), OFFSET in DAX 2023+" },
            new() { Category = "Table Calc", TableauFunction = "WINDOW_SUM(expr)",      DaxEquivalent = "No equivalent — manual conversion", Notes = "Use SUMX over filtered table" },
            new() { Category = "Table Calc", TableauFunction = "RUNNING_SUM(expr)",     DaxEquivalent = "No equivalent — manual conversion", Notes = "Use CALCULATE + date filters or FILTER" },
            new() { Category = "Table Calc", TableauFunction = "INDEX()",               DaxEquivalent = "RANKX() or ROWNUMBER()",           Notes = "ROWNUMBER() available in DAX 2023+" },
            new() { Category = "Table Calc", TableauFunction = "RANK(expr)",            DaxEquivalent = "RANKX(Table, expr)",              Notes = "Similar — verify sort order" },
            new() { Category = "Table Calc", TableauFunction = "FIRST()",               DaxEquivalent = "No equivalent — manual conversion", Notes = "Context-dependent; consider MIN()" },
            new() { Category = "Table Calc", TableauFunction = "LAST()",                DaxEquivalent = "No equivalent — manual conversion", Notes = "Context-dependent; consider MAX()" },
        };
    }
}
