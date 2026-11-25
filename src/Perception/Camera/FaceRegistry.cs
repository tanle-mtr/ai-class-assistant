using System.Text;
using System.Text.Json;
using AiClassAssistant.Core.Configuration;
using OpenCvSharp;
using Size = OpenCvSharp.Size;

namespace AiClassAssistant.Perception.Camera;

/// <summary>一次人脸观察记录。</summary>
public readonly record struct FaceObservation(string FaceId, string Name, string Seat);

/// <summary>
/// 人脸底库：Haar 检测 + 64 位 dHash 指纹建档 / 去重 / 座位记录。
///
/// 资源说明：OpenCvSharp NuGet 包不自带 haarcascade_frontalface_default.xml。
/// 本类会依次在程序目录、data/faces/、OPENCV_DATA 环境变量下查找；
/// 找不到时 _cascade 保持 null，DetectFaces 返回空列表（空实现），
/// 摄像头采集与就座率分析照常运行，绝不因缺资源崩溃。
/// </summary>
public sealed class FaceRegistry : IDisposable
{
    private const string CascadeFileName = "haarcascade_frontalface_default.xml";
    private const int HashThreshold = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _faceDir;
    private readonly string _indexPath;
    private readonly Dictionary<string, FaceRecord> _faces = new();
    private CascadeClassifier? _cascade;
    private bool _disposed;

    public FaceRegistry()
    {
        _faceDir = AppConfig.Instance.Dir("faces");
        _indexPath = Path.Combine(_faceDir, "faces_index.json");
        Load();
        TryLoadCascade();
    }

    /// <summary>是否成功加载 Haar 级联（未加载时人脸检测为空实现）。</summary>
    public bool FaceDetectionAvailable => _cascade is not null;

    /// <summary>检测人脸框 [(x, y, w, h)]；资源缺失或检测失败时返回空列表。</summary>
    public List<Rect> DetectFaces(Mat frame)
    {
        CascadeClassifier? cascade = _cascade;
        if (cascade is null || frame is null || frame.Empty())
        {
            return new List<Rect>();
        }

        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            using var equalized = new Mat();
            Cv2.EqualizeHist(gray, equalized);
            Rect[] faces = cascade.DetectMultiScale(equalized, 1.1, 5, 0, new Size(40, 40));
            return faces.ToList();
        }
        catch
        {
            // 图像尺寸异常 / 原生库故障：本帧跳过
            return new List<Rect>();
        }
    }

    /// <summary>
    /// 处理一帧：检测 → dHash 匹配或建档 → 记录座位，返回观察结果。
    /// </summary>
    public List<FaceObservation> Observe(Mat frame, string seatLabel)
    {
        var results = new List<FaceObservation>();
        foreach (Rect rect in DetectFaces(frame))
        {
            try
            {
                using var crop = new Mat(frame, rect);
                string hash = ComputeDHash(crop);
                string faceId;
                string name;
                bool isNew;

                lock (_gate)
                {
                    string? matched = Match(hash);
                    isNew = matched is null;
                    faceId = matched ?? AllocateId();
                    if (!_faces.TryGetValue(faceId, out FaceRecord? record))
                    {
                        record = new FaceRecord { Name = $"学生{faceId.TrimStart('f')}", Hash = hash, File = $"{faceId}.jpg" };
                        _faces[faceId] = record;
                    }

                    if (isNew)
                    {
                        TrySaveCrop(crop, record.File);
                    }

                    if (!string.IsNullOrEmpty(seatLabel) && !record.Seats.Contains(seatLabel))
                    {
                        record.Seats.Add(seatLabel);
                    }

                    name = record.Name;
                    Save();
                }

                results.Add(new FaceObservation(faceId, name, seatLabel));
            }
            catch
            {
                // 单张人脸失败不影响其它人脸
            }
        }

        return results;
    }

    public void Rename(string faceId, string name)
    {
        lock (_gate)
        {
            if (_faces.TryGetValue(faceId, out FaceRecord? record))
            {
                record.Name = name;
                Save();
            }
        }
    }

    public Dictionary<string, string> ListFaces()
    {
        lock (_gate)
        {
            return _faces.ToDictionary(pair => pair.Key, pair => pair.Value.Name);
        }
    }

    private void TryLoadCascade()
    {
        string? envDir = Environment.GetEnvironmentVariable("OPENCV_DATA");
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, CascadeFileName),
            Path.Combine(AppContext.BaseDirectory, "haarcascades", CascadeFileName),
            Path.Combine(_faceDir, CascadeFileName)
        };
        if (!string.IsNullOrWhiteSpace(envDir))
        {
            candidates.Add(Path.Combine(envDir, CascadeFileName));
            candidates.Add(Path.Combine(envDir, "haarcascades", CascadeFileName));
        }

        foreach (string path in candidates)
        {
            try
            {
                if (!File.Exists(path))
                {
                    continue;
                }

                var cascade = new CascadeClassifier(path);
                if (cascade.Empty())
                {
                    cascade.Dispose();
                    continue;
                }

                _cascade = cascade;
                return;
            }
            catch
            {
                // 级联文件损坏或原生库不可用：继续尝试下一个路径
            }
        }

        // 无级联资源：保持空实现，所有检测调用安全返回空列表
        _cascade = null;
    }

    private static string ComputeDHash(Mat frame)
    {
        try
        {
            using var gray = new Mat();
            Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);
            using var small = new Mat();
            Cv2.Resize(gray, small, new Size(9, 8), 0, 0, InterpolationFlags.Area);
            var builder = new StringBuilder(64);
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    builder.Append(small.At<byte>(y, x) > small.At<byte>(y, x + 1) ? '1' : '0');
                }
            }

            return builder.ToString();
        }
        catch
        {
            return new string('0', 64);
        }
    }

    private static int Hamming(string a, string b)
    {
        int distance = 0;
        int length = Math.Min(a.Length, b.Length);
        for (int i = 0; i < length; i++)
        {
            if (a[i] != b[i])
            {
                distance++;
            }
        }

        return distance;
    }

    private string? Match(string hash)
    {
        string? bestId = null;
        int bestDistance = int.MaxValue;
        foreach ((string id, FaceRecord record) in _faces)
        {
            int distance = Hamming(hash, record.Hash);
            if (distance >= bestDistance)
            {
                continue;
            }

            bestId = id;
            bestDistance = distance;
        }

        return bestDistance <= HashThreshold ? bestId : null;
    }

    private string AllocateId()
    {
        for (int number = 1; number < 100000; number++)
        {
            string id = $"f{number:0000}";
            if (!_faces.ContainsKey(id))
            {
                return id;
            }
        }

        return $"f{Guid.NewGuid():N}";
    }

    private void TrySaveCrop(Mat crop, string fileName)
    {
        try
        {
            Cv2.ImWrite(Path.Combine(_faceDir, fileName), crop);
        }
        catch
        {
            // 只读目录时跳过图像保存，索引仍然可用
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_indexPath))
            {
                return;
            }

            Dictionary<string, FaceRecord>? loaded = JsonSerializer.Deserialize<Dictionary<string, FaceRecord>>(
                File.ReadAllText(_indexPath, Encoding.UTF8), JsonOptions);
            if (loaded is null)
            {
                return;
            }

            lock (_gate)
            {
                foreach ((string key, FaceRecord value) in loaded)
                {
                    _faces[key] = value;
                }
            }
        }
        catch
        {
            // 索引损坏时保持空底库，绝不阻断启动
        }
    }

    private void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(_faces, JsonOptions);
            File.WriteAllText(_indexPath, json, new UTF8Encoding(false));
        }
        catch
        {
            // 只读目录：保持内存态
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        CascadeClassifier? cascade = Interlocked.Exchange(ref _cascade, null);
        cascade?.Dispose();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private sealed class FaceRecord
    {
        public string Name { get; set; } = "";
        public string Hash { get; set; } = "";
        public string File { get; set; } = "";
        public List<string> Seats { get; set; } = new();
    }
}
