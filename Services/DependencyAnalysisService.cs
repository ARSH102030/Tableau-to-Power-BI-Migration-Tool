using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TableauToPbi.Models;

namespace TableauToPbi.Services
{
    public static class DependencyAnalysisService
    {
        // ─── Public entry point ─────────────────────────────────────────────

        public static List<FieldDependencyRow> Analyze(TableauWorkbook workbook)
        {
            // 1. Collect all calculated fields across all data sources
            var allCalcFields = workbook.DataSources
                .SelectMany(ds => ds.Fields
                    .Where(f => f.IsCalculated)
                    .Select(f => (ds.DisplayName, f)))
                .ToList();

            // Build name → field lookup (display name, case-insensitive)
            // Use TryAdd to silently keep the first occurrence when names clash across data sources
            var fieldLookup = new Dictionary<string, TableauField>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, f) in allCalcFields)
                fieldLookup.TryAdd(f.DisplayName, f);

            // 2. For each field: extract [Ref] tokens and match against known calc fields
            //    adjacency: fieldName -> set of calc field names it directly depends on
            var dependsOn = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (_, f) in allCalcFields)
                dependsOn[f.DisplayName] = ExtractCalcFieldRefs(f.Formula ?? "", fieldLookup);

            // 3. Reverse graph: usedBy[x] = set of fields whose formula references x
            var usedBy = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in dependsOn.Keys)
                usedBy[name] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (name, deps) in dependsOn)
                foreach (var dep in deps)
                    if (usedBy.ContainsKey(dep))
                        usedBy[dep].Add(name);

            // 4. Detect cycles using DFS
            var cyclePaths = DetectCycles(dependsOn);

            // 5. Compute max upstream depth (longest path from a leaf to this node)
            var depths = ComputeDepths(dependsOn);

            // 6. Build result rows
            var rows = new List<FieldDependencyRow>();
            foreach (var (dsName, f) in allCalcFields)
            {
                string name = f.DisplayName;
                var deps    = dependsOn.TryGetValue(name, out var d) ? d : new HashSet<string>();
                var uses    = usedBy.TryGetValue(name, out var u)    ? u : new HashSet<string>();
                int depth   = depths.TryGetValue(name, out var dv)   ? dv : 0;

                bool hasCycle   = cyclePaths.ContainsKey(name);
                string cyclePath = hasCycle ? cyclePaths[name] : "";

                // Classify
                string kind;
                string kindColor;
                if (depth == 0 && uses.Count == 0)
                { kind = "Standalone";  kindColor = "#888888"; }
                else if (depth == 0)
                { kind = "Base";        kindColor = "#0078D4"; }   // depends on nothing, used by others
                else if (uses.Count == 0)
                { kind = "Top-level";   kindColor = "#107C10"; }   // depends on others, nothing uses it
                else
                { kind = "Intermediate"; kindColor = "#C19A00"; }  // middle of a chain

                rows.Add(new FieldDependencyRow
                {
                    FieldName       = name,
                    DataSource      = dsName,
                    DependsOn       = deps.Count > 0 ? string.Join(", ", deps.OrderBy(x => x)) : "—",
                    DependsOnList   = deps.ToList(),
                    UsedBy          = uses.Count > 0 ? string.Join(", ", uses.OrderBy(x => x)) : "—",
                    UsedByList      = uses.ToList(),
                    DepthLevel      = depth,
                    DirectDepsCount = deps.Count,
                    UsedByCount     = uses.Count,
                    Kind            = kind,
                    KindColor       = kindColor,
                    HasCircular     = hasCycle,
                    CircularPath    = cyclePath,
                    Formula         = f.Formula ?? ""
                });
            }

            return rows.OrderByDescending(r => r.DepthLevel)
                       .ThenBy(r => r.FieldName)
                       .ToList();
        }

        // ─── Build full upstream chain (all ancestors) ──────────────────────

        public static string BuildUpstreamChain(
            string fieldName,
            List<FieldDependencyRow> allRows,
            int indent = 0)
        {
            var sb = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildUpstream(fieldName, BuildSafeLookup(allRows), sb, indent, visited);
            return sb.Length > 0 ? sb.ToString() : "(no upstream dependencies)";
        }

        private static void BuildUpstream(
            string name,
            Dictionary<string, FieldDependencyRow> lookup,
            StringBuilder sb, int indent,
            HashSet<string> visited)
        {
            if (!lookup.TryGetValue(name, out var row)) return;
            if (row.DependsOnList.Count == 0) return;

            foreach (var dep in row.DependsOnList.OrderBy(x => x))
            {
                sb.AppendLine(new string(' ', indent * 4) + dep);
                if (!visited.Contains(dep))
                {
                    visited.Add(dep);
                    BuildUpstream(dep, lookup, sb, indent + 1, visited);
                }
                else
                {
                    sb.AppendLine(new string(' ', (indent + 1) * 4) + "(circular ref)");
                }
            }
        }

        // ─── Build full downstream chain (all descendants) ──────────────────

        public static string BuildDownstreamChain(
            string fieldName,
            List<FieldDependencyRow> allRows)
        {
            var sb = new StringBuilder();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            BuildDownstream(fieldName, BuildSafeLookup(allRows), sb, 0, visited);
            return sb.Length > 0 ? sb.ToString() : "(nothing uses this field)";
        }

        /// <summary>Build a name->row dictionary, keeping the first occurrence on duplicates.</summary>
        private static Dictionary<string, FieldDependencyRow> BuildSafeLookup(List<FieldDependencyRow> rows)
        {
            var d = new Dictionary<string, FieldDependencyRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in rows)
                d.TryAdd(r.FieldName, r);
            return d;
        }

        private static void BuildDownstream(
            string name,
            Dictionary<string, FieldDependencyRow> lookup,
            StringBuilder sb, int indent,
            HashSet<string> visited)
        {
            if (!lookup.TryGetValue(name, out var row)) return;
            if (row.UsedByList.Count == 0) return;

            foreach (var user in row.UsedByList.OrderBy(x => x))
            {
                sb.AppendLine(new string(' ', indent * 4) + user);
                if (!visited.Contains(user))
                {
                    visited.Add(user);
                    BuildDownstream(user, lookup, sb, indent + 1, visited);
                }
                else
                {
                    sb.AppendLine(new string(' ', (indent + 1) * 4) + "(circular ref)");
                }
            }
        }

        // ─── Helpers ────────────────────────────────────────────────────────

        /// <summary>Extract [FieldName] tokens from a formula that match known calculated fields.</summary>
        private static HashSet<string> ExtractCalcFieldRefs(
            string formula,
            Dictionary<string, TableauField> knownFields)
        {
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Tableau references look like [Field Name] — including spaces
            foreach (Match m in Regex.Matches(formula, @"\[([^\]]+)\]"))
            {
                string candidate = m.Groups[1].Value.Trim();
                if (knownFields.ContainsKey(candidate))
                    refs.Add(knownFields[candidate].DisplayName); // normalise to display name
            }
            return refs;
        }

        /// <summary>Detect cycles using DFS. Returns fieldName -> "A -> B -> A" cycle path string.</summary>
        private static Dictionary<string, string> DetectCycles(
            Dictionary<string, HashSet<string>> graph)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // 0=unvisited, 1=in-stack, 2=done
            var color = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var k in graph.Keys) color[k] = 0;

            var stack = new List<string>();

            void Dfs(string node)
            {
                color[node] = 1;
                stack.Add(node);

                if (graph.TryGetValue(node, out var neighbors))
                {
                    foreach (var nb in neighbors)
                    {
                        if (!color.ContainsKey(nb)) continue;
                        if (color[nb] == 1)
                        {
                            // Found cycle
                            int idx = stack.IndexOf(nb);
                            var cycle = stack.Skip(idx).ToList();
                            cycle.Add(nb);
                            string path = string.Join(" -> ", cycle);
                            foreach (var n in cycle.Take(cycle.Count - 1))
                                result[n] = path;
                        }
                        else if (color[nb] == 0)
                        {
                            Dfs(nb);
                        }
                    }
                }

                stack.RemoveAt(stack.Count - 1);
                color[node] = 2;
            }

            foreach (var k in graph.Keys)
                if (color[k] == 0)
                    Dfs(k);

            return result;
        }

        /// <summary>Compute depth = longest path from any leaf (field with no deps) to each node.</summary>
        private static Dictionary<string, int> ComputeDepths(
            Dictionary<string, HashSet<string>> graph)
        {
            var memo = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            int GetDepth(string node, HashSet<string> visiting)
            {
                if (memo.TryGetValue(node, out int cached)) return cached;
                if (visiting.Contains(node)) return 0; // cycle guard

                visiting.Add(node);
                int max = 0;
                if (graph.TryGetValue(node, out var deps))
                    foreach (var dep in deps)
                        max = Math.Max(max, GetDepth(dep, visiting) + 1);
                visiting.Remove(node);

                memo[node] = max;
                return max;
            }

            foreach (var k in graph.Keys)
                GetDepth(k, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

            return memo;
        }
    }
}
