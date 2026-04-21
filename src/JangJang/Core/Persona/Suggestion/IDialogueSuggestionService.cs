using JangJang.Core;

namespace JangJang.Core.Persona.Suggestion;

/// <summary>
/// 대사 추천 서비스 인터페이스. 편집 UI에서만 사용된다 (런타임 아님).
/// </summary>
public interface IDialogueSuggestionService
{
    /// <summary>
    /// 지정된 상태에 대해 대사 추천을 생성한다.
    /// </summary>
    /// <param name="context">프리셋/기존대사/피드백 등 컨텍스트</param>
    /// <param name="count">생성할 추천 개수</param>
    /// <returns>추천 대사 목록</returns>
    Task<List<SuggestedLine>> SuggestAsync(SuggestionContext context, int count = 3);
}
