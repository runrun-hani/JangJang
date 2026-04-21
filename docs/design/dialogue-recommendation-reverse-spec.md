# 대사 추천 시스템 역기획서

> 작성일: 2026-04-21
> 대상 브랜치: `jakae`
> 범위: 현재 구현된 대사 선택·추천 파이프라인의 동작 방식(코드 기반 역추론)

---

## 0. TL;DR

"대사 추천"은 실제로 **두 개의 독립된 흐름**이 하나의 데이터 모델을 공유하는 구조다.

| 축 | 시점 | 목적 | 핵심 엔트리 |
|---|---|---|---|
| **A. 런타임 대사 선택** | 앱 구동 중 매 틱 | 펫 말풍선·토스트에 띄울 한 줄을 고른다 | [Dialogue.GetLine](src/JangJang/Core/Dialogue.cs:25) |
| **B. 편집 시 AI 대사 추천** | 페르소나 편집창에서 사용자 버튼 클릭 | LLM이 씨앗 대사 후보를 생성 | [ApiDialogueSuggestionService.SuggestAsync](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:145) |

- **A**는 설정에 따라 3가지 Provider 중 하나로 결정, 실패 시 치와와 폴백으로 내려감.
- **B**는 프롬프트 조립 → Gemini(기본)/OpenAI 호환 API 호출 → 피드백 누적 루프.
- 두 축을 연결하는 것은 `PersonaData.SeedLines` 한 곳. **B**가 만든 대사가 **A**의 입력 풀이 된다.

---

## 1. 아키텍처 개요

```
                   ┌──────────────────────────────┐
                   │        AppSettings           │
                   │  PersonaEnabled              │
                   │  EmbeddingMatchingEnabled    │
                   │  ActivePersonaId             │
                   │  SuggestionApiKeyProtected   │
                   └────────────┬─────────────────┘
                                │
          ┌─────────────────────┴──────────────────────┐
          │                                            │
   ┌──────▼──────────┐                       ┌─────────▼────────┐
   │  [A. 런타임]     │                       │  [B. 편집 시]     │
   │  Dialogue       │                       │  PersonaWindow   │
   │  .GetLine()     │                       │  → Suggest 버튼  │
   └──────┬──────────┘                       └─────────┬────────┘
          │                                            │
   ┌──────▼───────────────────┐              ┌─────────▼───────────────┐
   │ IDialogueProvider (택1)  │              │ ApiDialogueSuggestion   │
   │  • Default (치와와)      │              │ Service                 │
   │  • PersonaRandom         │              │  ├─ PromptBuilder       │
   │  • Persona (4단계)       │              │  ├─ Gemini/OpenAI call  │
   └──────┬───────────────────┘              │  └─ ParseTextToLines    │
          │ 빈 문자열이면 치와와 폴백         └─────────┬───────────────┘
          ▼                                            │
    말풍선/토스트                            ┌─────────▼────────────────┐
                                            │ 사용자 Accept/Edit/Reject│
                                            │ → FeedbackStore.Append   │
                                            │ → 다음 호출 프롬프트 주입│
                                            └──────────────────────────┘
```

---

## Part A — 런타임 대사 선택

### A.1 호출 진입점

[Dialogue.GetLine(PetState, double, int)](src/JangJang/Core/Dialogue.cs:25)은 내부 `_current` Provider로 위임만 한다. 기존 호출 시그니처를 보존하여 호출 측은 변경 없음.

**호출처 2곳**
- [PetViewModel.OnStateUpdated](src/JangJang/ViewModels/PetViewModel.cs:119): ActivityMonitor의 1초 틱에 연결
  - 상태 전환 시 **즉시** 새 대사
  - 그 외에는 `_dialogueCooldown`이 0 이하로 떨어질 때마다 갱신 (기본 25초, `DialogueIntervalSecondsClamped` 5~120 범위)
- [NotificationManager.GetAnnoyedLine](src/JangJang/Core/NotificationManager.cs:72): 토스트 알림 생성 시 Annoyed 상태 대사를 요청. 빈 문자열/예외면 `FallbackMessage`.

**폴백 1단**: Provider가 빈 문자열을 반환하면 `Dialogue.GetLine`이 `_default`(치와와)로 즉시 재호출 ([Dialogue.cs:37](src/JangJang/Core/Dialogue.cs:37)). 무음 대신 치와와 대사가 잠깐 보이는 것을 허용.

### A.2 Provider 결정 트리 (앱 시작 시 1회)

[App.TryInitializePersonaProvider](src/JangJang/App.xaml.cs:52)가 설정을 읽어 **한 번만** 결정한다. `EmbeddingMatchingEnabled` 토글은 재시작 후 반영.

```
PersonaEnabled == false
  → DefaultDialogueProvider (치와와)

PersonaEnabled == true
  ├ ActivePersonaId 없음 / persona == null / SeedLines 0개
  │    → DefaultDialogueProvider
  │
  ├ EmbeddingMatchingEnabled == true && 모델 설치됨
  │    → PersonaDialogueProvider.Create() 시도
  │        ├ 성공: 임베딩 4단계 파이프라인
  │        └ 실패(모델 손상/토크나이저 미스매치):
  │             → PersonaRandomDialogueProvider로 폴백
  │
  └ 그 외(임베딩 off / 모델 없음)
       → PersonaRandomDialogueProvider
```

### A.3 Provider별 동작

#### A.3.a DefaultDialogueProvider — 치와와 하드코딩

[DefaultDialogueProvider](src/JangJang/Core/Persona/DefaultDialogueProvider.cs)는 상태별 고정 문자열 배열에서 균등 랜덤.

- `HappyLines = {"열심히 일하는 중!", "집중!", "좋아", "굿", "이 느낌", "오"}`
- `AlertLines = {"...뭐 하는 거야?", "어디 갔어?", "야", "딴 짓 하지?", "돌아와"}`
- `Annoyed`는 `annoyance < 0.5`로 Mild/Furious 분기
- Sleeping/WakeUp는 각각 작은 풀

#### A.3.b PersonaRandomDialogueProvider — 임베딩 없는 페르소나

[PersonaRandomDialogueProvider](src/JangJang/Core/Persona/PersonaRandomDialogueProvider.cs:16)는:

1. `persona.SeedLines`에서 `line.State == context.State`로 필터
2. 후보 0개면 **빈 문자열 반환** → 치와와 폴백
3. 후보가 있으면 `PassthroughOutputProcessor`에 그대로 넘김 (A.3.c-4와 동일 가중치 + 최근 사용 방지)

ONNX·토크나이저를 로드하지 않으므로 앱 시작 비용이 낮다.

#### A.3.c PersonaDialogueProvider — 4단계 임베딩 파이프라인

[PersonaDialogueProvider.GetLine](src/JangJang/Core/Persona/PersonaDialogueProvider.cs:98)이 오케스트레이션. 어느 단계든 예외 또는 후보 0이면 **빈 문자열** 반환 → 치와와 폴백.

**1단계 — ContextCollector** ([DefaultContextCollector](src/JangJang/Core/Persona/Pipeline/DefaultContextCollector.cs))

입력 DialogueContext(State, Annoyance, TodaySeconds)에 `ActivityMonitor`로부터 `SessionSeconds`, `IdleSessionSeconds`, 현재 `Timestamp`를 덧붙인다.

**2단계 — ContextNarrator** ([ContextNarrator.Narrate](src/JangJang/Core/Persona/Pipeline/ContextNarrator.cs:14), 규칙 기반·LLM 없음)

DialogueContext를 4개 구(句)로 조합한 한국어 문장으로 변환. 이 문장이 쿼리 임베딩의 입력.

| 측면 | 규칙 |
|---|---|
| 상태 표현 | Happy → "열심히 작업에 집중하고 있다" / Alert → "잠깐 작업에서 한눈을 팔았다" / Annoyed(<0.5) → "작업을 내려놓고 다른 일을 하고 있다" / Annoyed(≥0.5) → "작업을 한참 동안 안 하고 있어 보고 있기 답답하다" / Sleeping → "아직 작업을 시작하지 않았거나 자리를 비운 상태이다" / WakeUp → "막 작업을 다시 시작하려는 참이다" |
| 세션 작업시간 | 3h+ "이번 세션에 X시간 넘게 일했다" / 1h+ "이번 세션에 약 X시간 일했다" / 30m+ "막 어느 정도 작업을 한 참이다" / 나머지 "이제 막 작업을 시작했다" |
| 오늘 누적 | 4h+ "오늘 이미 많은 작업을 했다" / 2h+ "오늘 어느 정도 작업을 했다" / 그 외 생략 |
| 시간대 | 05–09 이른 아침 / 09–12 오전 / 12–14 점심 / 14–18 오후 / 18–22 저녁 / 그 외 밤늦은 시간 |

예시 출력: `"열심히 작업에 집중하고 있다. 이번 세션에 약 1시간 일했다. 오전 시간이다."`

**3단계 — EmbeddingCandidateSelector** ([EmbeddingCandidateSelector](src/JangJang/Core/Persona/Pipeline/EmbeddingCandidateSelector.cs), 하이브리드 매칭)

모델: `multilingual-e5-small` ONNX, 384차원, mean-pooled + L2-정규화 ([OnnxEmbeddingService](src/JangJang/Core/Persona/Embedding/OnnxEmbeddingService.cs)).

**e5 접두 규약** (매우 중요):
- 쿼리(narration)는 `"query: "` 접두
- 씨앗(SeedLine 매칭 텍스트)는 `"passage: "` 접두
- 위반 시 매칭 품질 크게 하락

**씨앗별 매칭 텍스트 결정** ([GetMatchingText](src/JangJang/Core/Persona/Pipeline/EmbeddingCandidateSelector.cs:92)):
- `SituationDescription`이 있으면 그것을 임베딩 ("B 모드")
- 없으면 대사 본문 자체를 임베딩 ("C 모드")
- 한 페르소나 안에서 대사별 혼용 가능

**캐시**: [EmbeddingCache](src/JangJang/Core/Persona/Embedding/EmbeddingCache.cs)가 텍스트 해시를 키로 임베딩을 영속화. 앱 시작 시 로드 → 누락된 씨앗만 계산 → 저장. 런타임에 새 씨앗이 추가돼도 다음 Select 호출에서 보충.

**후보 풀링**:
1. `SeedLines.Where(l => l.State == context.State)`로 상태 필터
2. 해당 상태 대사가 0개면 **전체 풀**로 폴백 (빈 결과 방지)

**스코어링 공식** (2026-04-16 매칭 품질 조사 반영):
```
finalScore = cosine(queryVec, seedVec) + 0.05 * Jaccard(queryTokens, seedTokens)
```

- `KeywordBoostWeight = 0.05f` ([EmbeddingCandidateSelector.cs:34](src/JangJang/Core/Persona/Pipeline/EmbeddingCandidateSelector.cs:34))
- 토큰화는 공백·구두점 분리 + 소문자화 (한국어 형태소 분석 없음)
- 근거: e5-small이 한국어 짧은 대화체에서 anisotropy 심함 (무관 문장 all-pairs cosine 평균 0.887, stddev 0.015) → pure cosine 변별력 부족. Jaccard boost로 top-1 +50%, top-3 +33% 실측 개선.

**출력**: 스코어 내림차순 **전체 후보** (topN=`int.MaxValue`, [PersonaDialogueProvider.cs:25](src/JangJang/Core/Persona/PersonaDialogueProvider.cs:25)). 선정 단계에서 컷오프하지 않는 이유 → 다양성 확보는 다음 단계 OutputProcessor에서.

**4단계 — PassthroughOutputProcessor** ([PassthroughOutputProcessor](src/JangJang/Core/Persona/Pipeline/PassthroughOutputProcessor.cs), 가중치 랜덤 + 반복 방지)

1. **기본 가중치**: `weights[i] = max(0.1, 1.0 - i * 0.2)`
   - 인덱스 0 = 1.0, 1 = 0.8, 2 = 0.6, 3 = 0.4, 4 = 0.2, 그 이후 모두 0.1
2. **최근 사용 페널티**: 최근 5개 큐(`_recentTexts`)에 포함된 대사는 가중치 × 0.2 (80% 감소, 완전 0 아님)
3. **룰렛 선택**: 가중치 누적합 기반 랜덤 픽. 합이 0이면 `candidates[0]` 폴백
4. 선택된 대사 Text를 큐에 push (크기 5 유지)

### A.4 디버그/진단 경로

- **파일 플래그**: `%AppData%/JangJang/persona-debug.flag` 존재 시 각 호출마다 `persona-debug.log`에 스냅샷(narration, top 후보, 최종 대사) 기록 ([PersonaDialogueProvider.cs:145](src/JangJang/Core/Persona/PersonaDialogueProvider.cs:145))
- **런타임 이벤트**: `PersonaDialogueProvider.OnDebugEntry` static 이벤트로 UI 모니터링 창에 [DebugEntry](src/JangJang/Core/Persona/Pipeline/DebugEntry.cs) 전달. `AppSettings.DebugMode=true`일 때 사용.

---

## Part B — 편집 시 AI 대사 추천

### B.1 서비스 팩토리

[ApiDialogueSuggestionService.FromSettings](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:32)가 `SuggestionApiKeyDecrypted`가 비어 있지 않을 때만 인스턴스를 반환. 키가 없으면 null → 편집 UI가 추천 버튼을 비활성화할 수 있음.

**보안**: 키는 DPAPI(`ProtectedData.Protect`, CurrentUser 범위)로 암호화되어 `SuggestionApiKeyProtected` 필드에 base64로 저장. 레거시 평문 `SuggestionApiKey`는 로드 후 자동으로 암호화 필드로 마이그레이션 ([AppSettings.Save](src/JangJang/Core/AppSettings.cs:124)).

### B.2 SuggestionContext 조립

호출 측(편집 UI)이 [SuggestionContext](src/JangJang/Core/Persona/Suggestion/SuggestionContext.cs)에 다음을 채워 전달:

| 필드 | 출처 |
|---|---|
| `PresetDescription` | 선택된 [PersonaPreset](src/JangJang/Core/Persona/Preset/PersonaPreset.cs)의 말투 설명 |
| `PersonalityKeywords` | 프리셋의 성격 키워드 |
| `CustomToneDescription` | `PersonaData.CustomToneDescription` — 프리셋 위에 덮어씀 |
| `CustomNotes` | `PersonaData.CustomPersonalityNotes` |
| `ExistingLines` | 페르소나의 상태별 씨앗 대사 Dictionary |
| `RecentFeedback` | [FeedbackStore.Load(personaId)](src/JangJang/Core/Persona/Feedback/FeedbackStore.cs:23) 결과 (최대 100건) |
| `TargetState` | 추천 대상 PetState |

### B.3 프롬프트 조립 ([PromptBuilder](src/JangJang/Core/Persona/Suggestion/PromptBuilder.cs))

**시스템 프롬프트** (고정):
```
당신은 캐릭터 대사 작가입니다. 주어진 캐릭터 프로필에 맞는 짧은 대사를 생성합니다. 대사만 출력하세요.
```

**유저 프롬프트** (4개 섹션):

1. **`## 캐릭터 프로필`**
   - `PresetDescription`
   - `성격 키워드: ...` (존재 시)
   - `사용자 정의 말투: {CustomToneDescription}` — 프리셋 설명과 **다를 때만** 포함 (중복 방지, [PromptBuilder.cs:36](src/JangJang/Core/Persona/Suggestion/PromptBuilder.cs:36))
   - `추가 메모: {CustomNotes}` (존재 시)

2. **`## 기존 대사 예시 (이 캐릭터의 말투 참고)`**
   - 각 상태별 최대 5개씩 발췌 → 말투 일관성 유도

3. **`## 사용자 선호`** (TargetState에 해당하는 피드백만, 각 유형 최근 3개)
   - `좋아한 대사: "...", "..."` (Accepted)
   - `수정한 대사: "원본" → "편집본"` (Edited)
   - `거절한 대사: "...", "..."` (Rejected)
   - 세 카테고리 모두 비면 섹션 생략

4. **`## 요청`**
   - 상태 서술: 예) Happy → "사용자가 열심히 작업에 집중하고 있는 상황" ([PromptBuilder.StateDescriptions](src/JangJang/Core/Persona/Suggestion/PromptBuilder.cs:12))
   - `위 캐릭터가 이 상태에서 할 법한 대사를 {count}개 생성하세요.`
   - `각 대사는 한 줄, 20자 이내. 번호 없이 대사만 한 줄씩 출력하세요.`
   - `기존 대사와 동일하거나 거의 같은 대사는 생성하지 마세요. 새로운 표현을 사용하세요.`

### B.4 API 호출 — 두 가지 경로

서비스는 프로바이더별로 다른 형식을 씀 ([ApiDialogueSuggestionService.SuggestAsync](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:145)):

**Gemini 네이티브** (기본, `_provider == "gemini"`):
- URL: `{baseUrl}/models/{model}:generateContent?key={apiKey}` (Authorization 헤더 없음)
- Body: `{ systemInstruction: {...}, contents: [...], generationConfig: { temperature: 0.8, maxOutputTokens: 200 } }`
- 기본 모델: `gemini-2.5-flash-lite`
- 응답 경로: `candidates[0].content.parts[0].text`

**OpenAI 호환** (Groq/Cerebras/기타):
- URL: `{baseUrl}/chat/completions`
- 헤더: `Authorization: Bearer {apiKey}`
- Body: `{ model, messages: [{role:"system"}, {role:"user"}], max_tokens: 200, temperature: 0.8 }`
- 응답 경로: `choices[0].message.content`

공용 HttpClient(`_http`, 타임아웃 30초) 재사용.

### B.5 모델 자동 발견 ([DiscoverAvailableModelsAsync](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:55))

Gemini 전용. 사용자에게 "이 키로 쓸 수 있는 무료 텍스트 모델"을 제시하기 위한 유틸.

1. `GET {baseUrl}/models?key=...` → 전체 모델 목록
2. **이름 필터**: `supportedGenerationMethods`에 `generateContent` 포함 AND 이름에 `tts/image/vision/embedding/robotics/nano/aqa/bisheng` 미포함 AND `pro` 미포함(유료 모델 제외)
3. **실제 호출 테스트**: 각 후보에 `"Hi"` 1토큰 호출 ([TestModelAsync](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:125))
4. 성공한 것만 반환. 이름에 `flash`+`lite` 둘 다 포함 → `Recommended=true`로 상단 정렬.

### B.6 응답 파싱 ([ParseTextToLines](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:297))

LLM이 번호/따옴표를 붙이는 경향에 대한 방어:
- `\n` 기준 분리 + trim
- 줄 시작의 `1.`, `2)`, `12.`, `- `, `* ` 패턴 제거 ([CleanLine](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:313))
- 양끝 `"..."` / `"..."` 제거
- 빈 줄 제거 → `List<SuggestedLine>`

### B.7 피드백 루프 ([DialogueFeedback](src/JangJang/Core/Persona/Feedback/DialogueFeedback.cs) + [FeedbackStore](src/JangJang/Core/Persona/Feedback/FeedbackStore.cs))

사용자가 편집 UI에서 추천된 `SuggestedLine`을 처리하면 한 건의 피드백이 누적된다.

| 유형 | 사용자 액션 | 저장 필드 |
|---|---|---|
| Accepted | 추천 그대로 씨앗 대사로 추가 | OriginalText, State |
| Edited | 편집하여 추가 | OriginalText + EditedText, State |
| Rejected | 버림 | OriginalText, State |

**저장**: `%AppData%/JangJang/Personas/{PersonaId}/feedback.json` — 페르소나별 독립. 최대 100건, 초과 시 오래된 것부터 잘라냄 ([FeedbackStore.Save](src/JangJang/Core/Persona/Feedback/FeedbackStore.cs:41)).

**재주입**: 다음 `SuggestAsync` 호출 시 `RecentFeedback`로 프롬프트 `## 사용자 선호` 섹션에 들어감. **TargetState 일치분만 사용**하므로 상태별 학습 루프.

### B.8 연결 테스트 ([TestConnectionAsync](src/JangJang/Core/Persona/Suggestion/ApiDialogueSuggestionService.cs:157))

설정창 "API 테스트" 버튼용. `"Hi"` 10토큰 호출 → 성공 시 null, 실패 시 `{statusCode}: {body}` 또는 예외 메시지 반환.

---

## 3. 설정 플래그 요약

[AppSettings](src/JangJang/Core/AppSettings.cs) 기준.

| 플래그 | 기본값 | 영향 |
|---|---|---|
| `PersonaEnabled` | `false` | 페르소나 대사 풀 사용 여부 (A 전체 분기) |
| `EmbeddingMatchingEnabled` | `false` | true + 모델 설치 시 임베딩 파이프라인 사용. 재시작 후 반영 |
| `ActivePersonaId` | `null` | 현재 활성 페르소나 GUID. null이면 페르소나 Provider 비활성 |
| `RegisteredPersonaIds` | `[]` | 앱에 노출되는 페르소나 화이트리스트 |
| `DialogueIntervalSeconds` | `25` | 런타임 대사 교체 주기 초 (5~120 clamp) |
| `DebugMode` | `false` | 파이프라인 모니터링 창 활성화 |
| `SuggestionApiProvider` | `"gemini"` | AI 추천 API 프로바이더 |
| `SuggestionApiModel` | `null` → `gemini-2.5-flash-lite` | API 모델명 |
| `SuggestionApiBaseUrl` | `null` → Gemini 기본 | 고급 사용자용 커스텀 URL |
| `SuggestionApiKeyProtected` | `null` | DPAPI 암호화된 API 키 (base64) |

---

## 4. 핵심 데이터 구조

### [SeedLine](src/JangJang/Core/Persona/SeedLine.cs)
- `Text`: 대사 원문
- `SituationDescription?`: 상황 설명. **있으면 매칭 키**, 없으면 `Text` 자체가 매칭 키 (EmbeddingCandidateSelector의 B/C 모드 자동 전환)
- `State`: 소속 PetState (런타임 필터 기준, 하위 호환 기본값 Happy)
- `Source`: Preset/UserWritten/AiSuggested/AiEdited (B 추천 흐름 출처 추적)
- `CreatedAt`: 생성 시각

### [DialogueContext](src/JangJang/Core/Persona/DialogueContext.cs) (런타임 스냅샷)
- `State`, `Annoyance`, `TodaySeconds` (호출 시점 기본)
- `SessionSeconds`, `IdleSessionSeconds`, `Timestamp` (ContextCollector가 보강)

### [PersonaData](src/JangJang/Core/Persona/PersonaData.cs)
- `Id`, `Name`, `PortraitFileName`
- `PresetId`, `CustomToneDescription`, `CustomPersonalityNotes` — B 프롬프트 소스
- `SeedLines` — A 매칭 풀이자 B 예시 소스

### [DialogueFeedback](src/JangJang/Core/Persona/Feedback/DialogueFeedback.cs)
- `OriginalText`, `EditedText?`, `Type`, `State`, `Timestamp`

---

## 5. 주요 관찰 / 한계

### A(런타임) 관련
- **매칭 품질 한계**: e5-small 한국어 짧은 대화체 anisotropy. Jaccard 0.05 보정은 실질 개선이지만 근본 해결은 아님 ([matching-quality-investigation-2026-04-16.md](docs/design/matching-quality-investigation-2026-04-16.md))
- **Provider는 앱 시작 시 결정**: 설정 변경 후 즉시 반영되지 않음 (재시작 필요)
- **씨앗 캐시 키**: 텍스트 해시 → 대사 본문 또는 `SituationDescription`이 바뀌면 재계산
- **후보 0 → 빈 문자열 → 치와와 폴백**: 페르소나 모드여도 갑자기 치와와 대사가 뜰 수 있음 (의도된 안전장치, 사용자 체감 UX는 비일관)
- **토스트 알림 연동**: Annoyed 상태 토스트도 런타임 Provider를 그대로 재사용 (대사 풀 공용화)

### B(편집 시 추천) 관련
- **피드백 필터링 범위**: 프롬프트에 `TargetState`와 일치하는 피드백만 주입 → 다른 상태 학습은 독립적
- **CustomToneDescription 중복 회피**: PresetDescription과 동일 문자열이면 프롬프트에서 생략 ([PromptBuilder.cs:36](src/JangJang/Core/Persona/Suggestion/PromptBuilder.cs:36)) — 토큰 절약 + "덮어쓰기" 의미의 명확화
- **모델 자동 발견이 Gemini 전용**: OpenAI 호환 프로바이더는 수동으로 모델명을 입력해야 함
- **응답 파싱의 강건성 한계**: 번호·글머리·양끝 따옴표만 처리. 마크다운 볼드, 인라인 주석(`// ...`), 이모지 등은 그대로 통과

### 공통
- 두 흐름이 `PersonaData.SeedLines` 한 곳에서 만나며, B가 생성한 대사가 A의 매칭 풀이 된다. Source 필드로 "AI가 제안", "사용자가 편집" 등을 사후 추적 가능하지만 현재 A 쪽 스코어링에는 Source를 사용하지 않는다.
