namespace JangJang.Core.Persona.Pipeline;

/// <summary>
/// (가) MVP 출력 가공기. 후보를 그대로 사용하며 LLM 변주는 하지 않는다.
///
/// 동작:
///   1. 빈 후보면 빈 문자열 반환
///   2. 최근 N개에 포함된 대사는 점수에서 패널티 (반복감 방지)
///   3. 상위 후보들 중 가중치 랜덤으로 한 줄 선택
///       - 후보 인덱스 0(최고 유사도)에 가장 높은 가중치
///       - 인덱스가 클수록 낮음 (단, 0은 아님 — 다양성 유지)
///   4. 선택된 대사를 최근 사용 큐에 기록
/// </summary>
public sealed class PassthroughOutputProcessor : IOutputProcessor
{
    private const int RecentMemorySize = 5;
    private readonly Random _rand = new();
    private readonly Queue<string> _recentTexts = new();

    public string Process(IReadOnlyList<SeedLine> candidates, DialogueContext context)
    {
        if (candidates == null || candidates.Count == 0)
            return string.Empty;

        // 1. 후보별 기본 가중치 (인덱스 0이 가장 높음, 점진 감소)
        //    예: [1.0, 0.7, 0.5, 0.3, 0.2]
        var weights = new double[candidates.Count];
        for (int i = 0; i < candidates.Count; i++)
        {
            weights[i] = Math.Max(0.1, 1.0 - i * 0.2);
        }

        // 2. 최근 사용 대사는 가중치 80% 감소 (완전 0은 아님 — 후보가 적을 때 안전)
        for (int i = 0; i < candidates.Count; i++)
        {
            if (_recentTexts.Contains(candidates[i].Text))
                weights[i] *= 0.2;
        }

        // 3. 가중치 합 기반 랜덤 선택
        double total = 0;
        for (int i = 0; i < weights.Length; i++) total += weights[i];
        if (total <= 0)
        {
            // 모두 0이면 첫 후보 폴백
            var first = candidates[0].Text;
            RecordUsed(first);
            return first;
        }

        var pick = _rand.NextDouble() * total;
        double cum = 0;
        SeedLine chosen = candidates[0];
        for (int i = 0; i < candidates.Count; i++)
        {
            cum += weights[i];
            if (pick <= cum)
            {
                chosen = candidates[i];
                break;
            }
        }

        RecordUsed(chosen.Text);
        return chosen.Text;
    }

    private void RecordUsed(string text)
    {
        _recentTexts.Enqueue(text);
        while (_recentTexts.Count > RecentMemorySize)
            _recentTexts.Dequeue();
    }
}
