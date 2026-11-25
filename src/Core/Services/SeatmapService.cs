using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AiClassAssistant.Core.Configuration;
using AiClassAssistant.Core.Models;

namespace AiClassAssistant.Core.Services;

/// <summary>座位表：读写 data/seats/seatmap.json（兼容旧格式）、换座检测、导出 Markdown。</summary>
public sealed class SeatmapService
{
    private readonly AppConfig _config;
    private readonly object _gate = new();
    private readonly List<Seat> _seats = new();
    private readonly JsonObject _extra = new();   // 旧格式中的未知键，保存时原样回写

    public SeatmapService(AppConfig config)
    {
        _config = config;
        Load();
    }

    public string FilePath => Path.Combine(_config.Dir("seats"), "seatmap.json");

    public IReadOnlyList<Seat> Seats
    {
        get { lock (_gate) { return _seats.ToList(); } }
    }

    /// <summary>读取座位表；支持 {seats:[...]}、顶层数组、{"r1c2":"张三"} 三种旧格式。</summary>
    public void Load()
    {
        string file = FilePath;
        if (!File.Exists(file))
        {
            return;
        }

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(file, Encoding.UTF8));
            JsonArray? array = null;
            if (root is JsonObject obj && obj["seats"] is JsonArray seatsArray)
            {
                array = seatsArray;
            }
            else if (root is JsonArray topArray)
            {
                array = topArray;
            }

            var loaded = new List<Seat>();
            if (array is not null)
            {
                foreach (JsonNode? node in array)
                {
                    Seat? seat = ParseArrayItem(node);
                    if (seat is not null)
                    {
                        loaded.Add(seat);
                    }
                }
            }
            else if (root is JsonObject mapping)
            {
                foreach ((string key, JsonNode? value) in mapping)
                {
                    if (!TryParseSeatCode(key, out int row, out int col))
                    {
                        continue;
                    }

                    loaded.Add(new Seat
                    {
                        Id = $"r{row}c{col}",
                        Row = row,
                        Col = col,
                        Name = value?.ToString() ?? ""
                    });
                }
            }

            lock (_gate)
            {
                _seats.Clear();
                _seats.AddRange(loaded);
                _extra.Clear();
                if (root is JsonObject meta)
                {
                    foreach ((string key, JsonNode? value) in meta)
                    {
                        if (key is "seats" or "version" or "updatedAt")
                        {
                            continue;
                        }

                        if (value is not null)
                        {
                            _extra[key] = value.DeepClone();
                        }
                    }
                }
            }
        }
        catch
        {
            // 损坏的座位表按空表处理，不删除用户文件。
        }
    }

    /// <summary>保存当前座位表；失败静默降级。</summary>
    public void Save()
    {
        try
        {
            var array = new JsonArray();
            JsonObject root;
            lock (_gate)
            {
                root = (JsonObject)_extra.DeepClone();
                root["version"] = 1;
                root["updatedAt"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
                if (_seats.Count > 0)
                {
                    root["rows"] = _seats.Max(s => s.Row);
                    root["cols"] = _seats.Max(s => s.Col);
                }

                foreach (Seat seat in _seats)
                {
                    array.Add(new JsonObject
                    {
                        ["id"] = seat.Id,
                        ["row"] = seat.Row,
                        ["col"] = seat.Col,
                        ["name"] = seat.Name,
                        ["faceId"] = seat.FaceId
                    });
                }
            }

            root["seats"] = array;
            string? parent = Path.GetDirectoryName(FilePath);
            if (!string.IsNullOrEmpty(parent))
            {
                Directory.CreateDirectory(parent);
            }

            File.WriteAllText(FilePath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        }
        catch
        {
            // 只读/无权限：保持内存态。
        }
    }

    /// <summary>
    /// 用摄像头观察（faceId + "r{行}c{列}"）更新座位表；返回是否发生变化。
    /// </summary>
    public bool UpdateFromObservations(IEnumerable<(string faceId, string seat)> observations)
    {
        if (observations is null)
        {
            return false;
        }

        bool changed = false;
        lock (_gate)
        {
            foreach ((string faceId, string seat) in observations)
            {
                if (!TryParseSeatCode(seat, out int row, out int col))
                {
                    continue;
                }

                Seat? target = _seats.FirstOrDefault(s => s.Row == row && s.Col == col);
                if (target is null)
                {
                    target = new Seat { Id = $"r{row}c{col}", Row = row, Col = col };
                    _seats.Add(target);
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(faceId) && target.FaceId != faceId)
                {
                    target.FaceId = faceId;
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Save();
        }

        return changed;
    }

    /// <summary>设置某座位学生姓名（OCR/手动录入）。</summary>
    public void SetName(int row, int col, string name)
    {
        lock (_gate)
        {
            Seat? seat = _seats.FirstOrDefault(s => s.Row == row && s.Col == col);
            if (seat is null)
            {
                seat = new Seat { Id = $"r{row}c{col}", Row = row, Col = col };
                _seats.Add(seat);
            }

            seat.Name = name ?? "";
        }

        Save();
    }

    public Seat? Get(int row, int col)
    {
        lock (_gate)
        {
            return _seats.FirstOrDefault(s => s.Row == row && s.Col == col);
        }
    }

    /// <summary>导出为 Markdown 表格（6x8 网格，按已有座位的行列范围扩张）。</summary>
    public string ExportMarkdown()
    {
        string dir = _config.Dir("seats");
        string file = Path.Combine(dir, "seatmap.md");
        try
        {
            List<Seat> snapshot;
            lock (_gate)
            {
                snapshot = _seats.ToList();
            }

            int maxRow = snapshot.Count == 0 ? 6 : Math.Max(6, snapshot.Max(s => s.Row));
            int maxCol = snapshot.Count == 0 ? 8 : Math.Max(8, snapshot.Max(s => s.Col));

            var sb = new StringBuilder();
            sb.AppendLine("# 座位表");
            sb.AppendLine();
            sb.AppendLine($"更新时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("| 排/列 | " + string.Join(" | ", Enumerable.Range(1, maxCol).Select(c => $"列{c}")) + " |");
            sb.AppendLine("| --- | " + string.Join(" | ", Enumerable.Range(1, maxCol).Select(_ => "---")) + " |");
            for (int r = 1; r <= maxRow; r++)
            {
                var cells = new List<string>();
                for (int c = 1; c <= maxCol; c++)
                {
                    Seat? seat = snapshot.FirstOrDefault(s => s.Row == r && s.Col == c);
                    cells.Add(string.IsNullOrWhiteSpace(seat?.Name) ? "—" : seat!.Name);
                }

                sb.AppendLine($"| 排{r} | " + string.Join(" | ", cells) + " |");
            }

            File.WriteAllText(file, sb.ToString(), Encoding.UTF8);
        }
        catch
        {
            // 忽略。
        }

        return file;
    }

    private static Seat? ParseArrayItem(JsonNode? node)
    {
        try
        {
            if (node is JsonObject obj)
            {
                int row = GetInt(obj, "row");
                int col = GetInt(obj, "col");
                string code = obj["id"]?.ToString() ?? "";
                if ((row <= 0 || col <= 0) && TryParseSeatCode(code, out row, out col))
                {
                    // 从 id 解析。
                }

                if (row <= 0 || col <= 0)
                {
                    return null;
                }

                return new Seat
                {
                    Id = string.IsNullOrWhiteSpace(code) ? $"r{row}c{col}" : code,
                    Row = row,
                    Col = col,
                    Name = obj["name"]?.ToString() ?? "",
                    FaceId = obj["faceId"]?.ToString() ?? ""
                };
            }

            if (node is JsonArray arr && arr.Count >= 2)
            {
                int row = int.TryParse(arr[0]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int r) ? r : 0;
                int col = int.TryParse(arr[1]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 0;
                if (row <= 0 || col <= 0)
                {
                    return null;
                }

                return new Seat
                {
                    Id = $"r{row}c{col}",
                    Row = row,
                    Col = col,
                    Name = arr.Count >= 3 ? arr[2]?.ToString() ?? "" : ""
                };
            }

            if (node is not null && TryParseSeatCode(node.ToString(), out int sr, out int sc))
            {
                return new Seat { Id = $"r{sr}c{sc}", Row = sr, Col = sc };
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static int GetInt(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out JsonNode? node) || node is null)
        {
            return 0;
        }

        return int.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : 0;
    }

    /// <summary>解析 r1c2 / 1-2 / 1,2 / 1排2列 等座位编码。</summary>
    internal static bool TryParseSeatCode(string? code, out int row, out int col)
    {
        row = 0;
        col = 0;
        string text = (code ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }

        Match match = Regex.Match(text, @"r?(\d+)\s*(?:c|[-_,排列]|\s)\s*(\d+)", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            return false;
        }

        row = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        col = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
        return row > 0 && col > 0;
    }
}
