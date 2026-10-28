using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// 첫 행이 헤더이고 한 행이 레코드 하나인 테이블형 CSV 시트입니다.
/// </summary>
/// <remarks>
/// 규약:
/// <list type="bullet">
/// <item>첫 번째 비어 있지 않은 행이 헤더입니다. 헤더 이름은 대소문자를 구분하지 않습니다.</item>
/// <item>헤더가 <c>#</c>으로 시작하는 열은 기획 메모 열이라 읽지 않습니다.</item>
/// <item>첫 칸이 <c>#</c>으로 시작하는 행은 주석 행, 모든 칸이 빈 행은 빈 행이라 건너뜁니다.</item>
/// <item>파일은 UTF-8이어야 합니다. 엑셀에서는 "CSV UTF-8(쉼표로 분리)"로 저장합니다.</item>
/// </list>
/// 콤마·따옴표·줄바꿈이 든 셀은 RFC 4180 규칙(큰따옴표로 감싸고 따옴표는 두 번)으로 읽습니다.
/// </remarks>
internal sealed class CsvTable
{
    private readonly Dictionary<string, int> m_columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private readonly List<CsvRow> m_rows = new List<CsvRow>();

    public string FilePath { get; }
    public string SheetName => Path.GetFileNameWithoutExtension(FilePath);
    public IReadOnlyList<CsvRow> Rows => m_rows;

    private CsvTable(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>파일을 읽어 테이블로 만듭니다. 형식이 잘못되면 <see cref="TableImportException"/>을 던집니다.</summary>
    public static CsvTable Load(string filePath)
    {
        byte[] bytes = File.ReadAllBytes(filePath);
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw new TableImportException(filePath, 0, null,
                "UTF-8이 아닙니다. 엑셀에서 \"CSV UTF-8(쉼표로 분리)\"로 다시 저장하세요.");
        }

        if (text.Length > 0 && text[0] == '﻿')
            text = text.Substring(1);

        CsvTable table = new CsvTable(filePath);
        table.Parse(text);
        return table;
    }

    /// <summary>열이 헤더에 있는지 확인합니다.</summary>
    public bool HasColumn(string column) => m_columns.ContainsKey(column);

    /// <summary>필수 열이 모두 있는지 확인합니다. 빠진 열이 있으면 예외를 던집니다.</summary>
    public void RequireColumns(params string[] columns)
    {
        List<string> missing = new List<string>();
        foreach (string column in columns)
        {
            if (!HasColumn(column))
                missing.Add(column);
        }

        if (missing.Count > 0)
            throw new TableImportException(FilePath, 1, null, $"필수 열이 없습니다: {string.Join(", ", missing)}");
    }

    internal bool TryGetColumnIndex(string column, out int index) => m_columns.TryGetValue(column, out index);

    private void Parse(string text)
    {
        bool hasHeader = false;
        foreach ((int line, List<string> cells) in ReadRecords(text))
        {
            if (IsBlank(cells))
                continue;

            if (!hasHeader)
            {
                ReadHeader(line, cells);
                hasHeader = true;
                continue;
            }

            if (cells[0].TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            m_rows.Add(new CsvRow(this, line, cells));
        }

        if (!hasHeader)
            throw new TableImportException(FilePath, 0, null, "헤더 행이 없습니다.");
    }

    private void ReadHeader(int line, List<string> cells)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            string name = cells[i].Trim();
            if (name.Length == 0 || name.StartsWith("#", StringComparison.Ordinal))
                continue;

            if (m_columns.ContainsKey(name))
                throw new TableImportException(FilePath, line, name, "헤더에 같은 열 이름이 두 번 있습니다.");

            m_columns.Add(name, i);
        }
    }

    private static bool IsBlank(List<string> cells)
    {
        foreach (string cell in cells)
        {
            if (!string.IsNullOrWhiteSpace(cell))
                return false;
        }

        return true;
    }

    /// <summary>RFC 4180 레코드를 시작 줄 번호(1부터)와 함께 돌려줍니다.</summary>
    private IEnumerable<(int line, List<string> cells)> ReadRecords(string text)
    {
        List<string> cells = new List<string>();
        StringBuilder cell = new StringBuilder();
        bool inQuotes = false;
        int line = 1;
        int recordLine = 1;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    if (c == '\n')
                        line++;
                    if (c != '\r')
                        cell.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    cells.Add(cell.ToString());
                    cell.Clear();
                    yield return (recordLine, cells);
                    cells = new List<string>();
                    line++;
                    recordLine = line;
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (inQuotes)
            throw new TableImportException(FilePath, recordLine, null, "큰따옴표가 닫히지 않았습니다.");

        if (cell.Length > 0 || cells.Count > 0)
        {
            cells.Add(cell.ToString());
            yield return (recordLine, cells);
        }
    }
}

/// <summary>테이블 시트의 한 행입니다. 값 읽기 실패는 파일·줄·열이 담긴 예외로 알립니다.</summary>
internal sealed class CsvRow
{
    private readonly CsvTable m_table;
    private readonly List<string> m_cells;

    public int Line { get; }

    internal CsvRow(CsvTable table, int line, List<string> cells)
    {
        m_table = table;
        Line = line;
        m_cells = cells;
    }

    /// <summary>셀 문자열을 앞뒤 공백을 잘라 돌려줍니다. 열이 없거나 칸이 모자라면 빈 문자열입니다.</summary>
    public string GetString(string column)
    {
        if (!m_table.TryGetColumnIndex(column, out int index) || index >= m_cells.Count)
            return string.Empty;

        return m_cells[index].Trim();
    }

    public int GetInt(string column)
    {
        string value = GetRequired(column);
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            throw Error(column, $"정수가 아닙니다: \"{value}\"");

        return result;
    }

    public float GetFloat(string column)
    {
        string value = GetRequired(column);
        if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float result))
            throw Error(column, $"실수가 아닙니다: \"{value}\"");

        return result;
    }

    public bool GetBool(string column)
    {
        string value = GetRequired(column);
        if (value == "1" || value.Equals("TRUE", StringComparison.OrdinalIgnoreCase))
            return true;
        if (value == "0" || value.Equals("FALSE", StringComparison.OrdinalIgnoreCase))
            return false;

        throw Error(column, $"TRUE/FALSE가 아닙니다: \"{value}\"");
    }

    public T GetEnum<T>(string column) where T : struct, Enum
    {
        string value = GetRequired(column);
        if (!Enum.TryParse(value, true, out T result) || !Enum.IsDefined(typeof(T), result))
            throw Error(column, $"{typeof(T).Name}에 없는 값입니다: \"{value}\" (가능한 값: {string.Join(", ", Enum.GetNames(typeof(T)))})");

        return result;
    }

    /// <summary>이 행 위치를 담은 가져오기 예외를 만듭니다.</summary>
    public TableImportException Error(string column, string message)
    {
        return new TableImportException(m_table.FilePath, Line, column, message);
    }

    private string GetRequired(string column)
    {
        string value = GetString(column);
        if (value.Length == 0)
            throw Error(column, "값이 비어 있습니다.");

        return value;
    }
}

/// <summary>시트 가져오기 실패입니다. 메시지에 파일·줄·열이 붙습니다.</summary>
internal sealed class TableImportException : Exception
{
    public TableImportException(string filePath, int line, string column, string message)
        : base(Format(filePath, line, column, message))
    {
    }

    private static string Format(string filePath, int line, string column, string message)
    {
        StringBuilder builder = new StringBuilder(Path.GetFileName(filePath));
        if (line > 0)
            builder.Append($" {line}행");
        if (!string.IsNullOrEmpty(column))
            builder.Append($" [{column}]");

        return $"{builder}: {message}";
    }
}
