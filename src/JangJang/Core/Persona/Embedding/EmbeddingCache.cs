using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace JangJang.Core.Persona.Embedding;

/// <summary>
/// 씨앗 대사 임베딩을 binary 파일에 저장/로드.
/// 텍스트 해시 기반 무효화: 씨앗 대사 텍스트가 바뀌면 해당 항목만 재계산.
///
/// 파일 포맷 (little-endian):
///   [int32: magic = 0x4A4B4543 ("CEKJ")]
///   [int32: version = 1]
///   [int32: dimension]
///   [int32: count]
///   For each entry:
///     [int64: textHash]
///     [float[dimension]: embedding vector]
///
/// 호환성이 깨지면 magic/version 체크 실패로 캐시 무시 + 전체 재계산.
/// </summary>
public static class EmbeddingCache
{
    private const int Magic = 0x4A4B4543; // "CEKJ" (Cache Embedding KangJang)
    private const int CurrentVersion = 1;

    /// <summary>
    /// 씨앗 대사의 매칭용 텍스트(설명 또는 본문)로부터 안정적인 해시 생성.
    /// MD5 첫 8바이트를 long으로 사용 (보안용 아님, 단순 무결성 체크).
    /// </summary>
    public static long ComputeTextHash(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var bytes = Encoding.UTF8.GetBytes(text);
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(bytes);
        return BitConverter.ToInt64(hash, 0);
    }

    /// <summary>
    /// 캐시에서 임베딩을 로드한다. 파일이 없거나 손상되면 빈 dictionary 반환.
    /// 키는 ComputeTextHash 결과, 값은 임베딩 벡터.
    /// </summary>
    public static Dictionary<long, float[]> Load(string cachePath, int expectedDimension)
    {
        var result = new Dictionary<long, float[]>();
        if (!File.Exists(cachePath)) return result;

        try
        {
            using var fs = File.OpenRead(cachePath);
            using var br = new BinaryReader(fs);

            var magic = br.ReadInt32();
            if (magic != Magic) return result; // 다른 포맷, 무시

            var version = br.ReadInt32();
            if (version != CurrentVersion) return result; // 버전 불일치, 무시

            var dimension = br.ReadInt32();
            if (dimension != expectedDimension) return result; // 차원 불일치, 무시

            var count = br.ReadInt32();
            if (count < 0 || count > 100_000) return result; // sanity

            for (int i = 0; i < count; i++)
            {
                var textHash = br.ReadInt64();
                var vec = new float[dimension];
                for (int d = 0; d < dimension; d++)
                {
                    vec[d] = br.ReadSingle();
                }
                result[textHash] = vec;
            }
        }
        catch
        {
            // 손상된 파일. 빈 dict 반환 → 호출자는 전체 재계산
            return new Dictionary<long, float[]>();
        }
        return result;
    }

    /// <summary>
    /// 임베딩 dictionary를 캐시 파일에 저장한다. 디렉토리가 없으면 생성.
    /// 실패해도 조용히 무시 (다음 실행 시 재계산되면 됨).
    ///
    /// 쓰기 시작 전에 모든 엔트리의 차원을 먼저 검증한다 — 중간에 throw되어
    /// 파일이 부분 기록된 상태로 남는 것을 방지. 잘못된 엔트리는 그냥 스킵.
    /// </summary>
    public static void Save(string cachePath, int dimension, IReadOnlyDictionary<long, float[]> entries)
    {
        try
        {
            // 1. 사전 검증: 차원이 맞는 엔트리만 추려냄. 쓰기 시작 전에 끝내야 함.
            var valid = new List<KeyValuePair<long, float[]>>(entries.Count);
            foreach (var kv in entries)
            {
                if (kv.Value != null && kv.Value.Length == dimension)
                    valid.Add(kv);
                // 차원 불일치 엔트리는 조용히 스킵 (호출자가 해시 미스로 재계산)
            }

            // 2. 파일 I/O
            var dir = Path.GetDirectoryName(cachePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            using var fs = File.Create(cachePath);
            using var bw = new BinaryWriter(fs);

            bw.Write(Magic);
            bw.Write(CurrentVersion);
            bw.Write(dimension);
            bw.Write(valid.Count);

            foreach (var kv in valid)
            {
                bw.Write(kv.Key);
                for (int d = 0; d < dimension; d++)
                {
                    bw.Write(kv.Value[d]);
                }
            }
        }
        catch
        {
            // 캐시 저장 실패는 치명적이지 않음. 다음 실행 시 재계산.
        }
    }
}
