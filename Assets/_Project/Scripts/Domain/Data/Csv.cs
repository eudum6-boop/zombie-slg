using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZombieSLG.Domain
{
    /// <summary>
    /// Small RFC 4180 CSV reader for the data tables: quoted fields (with commas, newlines and ""),
    /// CRLF/LF, UTF-8 BOM, blank lines and '#' comment lines. Keeps source line numbers for error messages.
    /// </summary>
    public sealed class CsvTable
    {
        private readonly Dictionary<string, int> _columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly List<CsvRecord> _records = new List<CsvRecord>();

        public string Name { get; }
        public IReadOnlyList<CsvRecord> Records => _records;
        public int Count => _records.Count;

        private CsvTable(string name) { Name = name; }

        public bool HasColumn(string column) => _columns.ContainsKey(column);

        internal int ColumnIndex(string column) => _columns.TryGetValue(column, out int i) ? i : -1;

        public static CsvTable Parse(string name, string text, List<string> errors)
        {
            var table = new CsvTable(name);
            if (text == null) { errors.Add($"{name}: 파일 내용이 없습니다"); return table; }

            bool headerRead = false;
            foreach (var (line, cells) in ReadRows(text))
            {
                if (cells.Length == 0 || (cells.Length == 1 && cells[0].Trim().Length == 0)) continue;
                if (cells[0].TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;

                if (!headerRead)
                {
                    for (int i = 0; i < cells.Length; i++)
                    {
                        string col = cells[i].Trim();
                        if (col.Length == 0) continue;
                        if (table._columns.ContainsKey(col)) errors.Add($"{name}:{line} 열 이름 '{col}'이 중복됩니다");
                        else table._columns[col] = i;
                    }
                    headerRead = true;
                    continue;
                }
                table._records.Add(new CsvRecord(table, cells, line));
            }

            if (!headerRead) errors.Add($"{name}: 머리글(열 이름) 줄이 없습니다");
            return table;
        }

        /// <summary>Yields (starting line number, cells) for each logical row.</summary>
        private static IEnumerable<(int line, string[] cells)> ReadRows(string text)
        {
            int i = 0;
            if (text.Length > 0 && text[0] == '﻿') i = 1;

            var cells = new List<string>();
            var field = new StringBuilder();
            int line = 1, rowStart = 1;
            bool inQuotes = false, any = false;

            for (; i < text.Length; i++)
            {
                char c = text[i];
                if (inQuotes)
                {
                    if (c == '"')
                    {
                        if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                        else inQuotes = false;
                    }
                    else
                    {
                        if (c == '\n') line++;
                        if (c != '\r') field.Append(c);
                    }
                    continue;
                }

                switch (c)
                {
                    case '"':
                        inQuotes = true;
                        any = true;
                        break;
                    case ',':
                        cells.Add(field.ToString());
                        field.Clear();
                        any = true;
                        break;
                    case '\r':
                        break;
                    case '\n':
                        cells.Add(field.ToString());
                        field.Clear();
                        yield return (rowStart, cells.ToArray());
                        cells.Clear();
                        any = false;
                        line++;
                        rowStart = line;
                        break;
                    default:
                        field.Append(c);
                        any = true;
                        break;
                }
            }

            if (any || field.Length > 0 || cells.Count > 0)
            {
                cells.Add(field.ToString());
                yield return (rowStart, cells.ToArray());
            }
        }
    }

    /// <summary>One data row with typed, error-reporting accessors.</summary>
    public readonly struct CsvRecord
    {
        private readonly CsvTable _table;
        private readonly string[] _cells;

        public int Line { get; }

        internal CsvRecord(CsvTable table, string[] cells, int line)
        {
            _table = table;
            _cells = cells;
            Line = line;
        }

        public string Where(string column) => $"{_table.Name}:{Line} [{column}]";

        public string Str(string column)
        {
            int i = _table.ColumnIndex(column);
            return i >= 0 && i < _cells.Length ? _cells[i].Trim() : "";
        }

        public double Num(string column, List<string> errors, double? min = null)
        {
            string raw = Str(column);
            if (raw.Length == 0)
            {
                if (!_table.HasColumn(column)) errors.Add($"{_table.Name}: '{column}' 열이 없습니다");
                else errors.Add($"{Where(column)} 값이 비어 있습니다");
                return 0;
            }
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
            {
                errors.Add($"{Where(column)} '{raw}'은(는) 숫자가 아닙니다");
                return 0;
            }
            if (min.HasValue && v < min.Value) errors.Add($"{Where(column)} {raw} — {min.Value} 이상이어야 합니다");
            return v;
        }

        public int Int(string column, List<string> errors, int? min = null)
        {
            double v = Num(column, errors, min);
            if (Math.Abs(v - Math.Round(v)) > 1e-9) errors.Add($"{Where(column)} {v} — 정수여야 합니다");
            return (int)Math.Round(v);
        }
    }
}
