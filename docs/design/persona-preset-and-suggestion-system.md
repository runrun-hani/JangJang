# 설계 문서: 페르소나 프리셋 & API 추천 시스템

> 입력: [문제 명세서](persona-preset-system-problem.md) (`/think` 산출물)
> 기존 설계: [persona-system.md](persona-system.md)
> 선택한 접근법: **B. API 연속 추천 + 로컬 페르소나 프로필**
> 다음 단계: `/build`로 구현 시작

---

## 문제

자캐 사용자가 캐릭터의 대사를 설정하는 과정이 무겁다. 프리셋으로 빠른 시작을 제공하고, API 기반 추천으로 캐릭터다운 대사를 쉽게 채울 수 있어야 한다. 대사가 쌓일수록 페르소나 프로필이 진화하여 추천 품질이 향상되는 플라이휠 구조.

## 핵심 경험

> **사용자가 10분 안에 원하는 페르소나의 초기 버전을 구현할 수 있다.**

## 선택한 접근법: B. API 연속 추천 + 로컬 페르소나 프로필

- **편집 UI에서만** API 호출 (런타임은 기존 임베딩 파이프라인 그대로)
- 로컬 페르소나 프로필이 매 API 호출의 컨텍스트가 됨
- 대사 축적 → 프로필 풍부 → 추천 품질 향상 (플라이휠)
- 피드백 (수락/편집/거절) → 프로필에 반영

### 주요 결정 요약

| 결정 사항 | 값 |
|---|---|
| LLM 위치 | 무료 API (편집 UI 전용, 런타임 아님) |
| 런타임 대사 선택 | 기존 임베딩 파이프라인 (변경 없음) |
| 프리셋 내용 | 말투 + 성격 + 상태별 샘플 대사 세트 |
| 프리셋 선택 | 필수 |
| 범용 샘플 대사 | 선택 (0개 가능) |
| 상태 카테고리 | PetState 그대로 (Happy, Alert, Annoyed, Sleeping, WakeUp) |
| SeedLine 상태 태깅 | 필수 (어떤 상태에 속하는 대사인지) |
| 추천 API | Gemini Flash-Lite(1순위), Groq Qwen3 32B(폴백) — 무료, 신용카드 불필요 |
| API 설정 저장 | AppSettings (settings.json) |
| SingleFile 배포 | 유지 가능 (로컬 LLM 없으므로) |

---

## 아키텍처 개요

```
[편집 시간 — PersonaWindow]

프리셋 선택
    ↓
상태별 SeedLine 자동 채움 (프리셋에서)
    ↓
사용자가 "추천 받기" 클릭
    ↓
PersonaProfileBuilder
    ├─ 프리셋 기본 설명
    ├─ 전체 기존 대사 (상태별 그룹)
    ├─ 피드백 히스토리
    └─ 요청 상태 (PetState)
    ↓ (구성된 프롬프트)
IDialogueSuggestionService
    ↓ (API 호출)
추천 대사 3-5개 반환
    ↓
사용자: 수락 / 편집 / 거절
    ↓
수락/편집 → SeedLine으로 저장
피드백 기록 → 다음 추천에 반영


[런타임 — 기존 파이프라인, 변경 없음]

Dialogue.GetLine()
    ↓
PersonaDialogueProvider (4단계)
    ├─ ContextCollector
    ├─ ContextNarrator
    ├─ EmbeddingCandidateSelector
    └─ PassthroughOutputProcessor
    ↓
최종 대사
```

---

## 데이터 모델

### PersonaPreset (프리셋 정의, 앱 번들)

```
PersonaPreset
├── Id: string                          # "tsundere", "supportive" 등
├── DisplayName: string                 # "츤데레", "응원형" 등
├── ToneDescription: string             # "겉으로는 차갑지만 속으로는 걱정하는 말투"
├── PersonalityKeywords: List<string>   # ["도도한", "은근 다정한", "솔직하지 못한"]
├── SeedLines: List<PresetSeedLine>     # 상태별 샘플 대사 (15-20개)
```

```
PresetSeedLine
├── State: PetState
├── Text: string
├── SituationDescription: string?
```

프리셋은 앱에 JSON으로 번들. 초기 출시 시 3-5개 프리셋 제공.

**프리셋 예시 (츤데레):**
```json
{
  "id": "tsundere",
  "displayName": "츤데레",
  "toneDescription": "겉으로는 차갑고 퉁명스럽지만, 사실은 상대를 걱정하고 있다. 칭찬을 직접적으로 하지 못하고 돌려 말한다.",
  "personalityKeywords": ["도도한", "은근 다정한", "솔직하지 못한", "퉁명스러운"],
  "seedLines": [
    { "state": "Happy", "text": "뭐야, 오늘은 좀 하네.", "situationDescription": "열심히 작업 중일 때" },
    { "state": "Happy", "text": "...계속 그렇게 해.", "situationDescription": "집중이 잘 될 때" },
    { "state": "Happy", "text": "딴짓 안 하니까 봐줄 만하네.", "situationDescription": null },
    { "state": "Alert", "text": "어디 눈 돌리는 거야.", "situationDescription": "잠깐 한눈팔 때" },
    { "state": "Alert", "text": "...집중.", "situationDescription": null },
    { "state": "Annoyed", "text": "하...진짜 안 할 거야?", "situationDescription": "작업을 오래 안 할 때" },
    { "state": "Annoyed", "text": "나 화난다?", "situationDescription": null },
    { "state": "Sleeping", "text": "...", "situationDescription": null },
    { "state": "WakeUp", "text": "왔어? ...빨리 시작해.", "situationDescription": "돌아왔을 때" }
  ]
}
```

### SeedLine 확장

기존:
```
SeedLine { Text, SituationDescription?, CreatedAt }
```

확장:
```
SeedLine
├── Text: string
├── SituationDescription: string?
├── State: PetState                     # [신규] 어떤 상태의 대사인지
├── Source: SeedLineSource              # [신규] 출처 추적
├── CreatedAt: DateTime
```

```
enum SeedLineSource
{
    Preset,         # 프리셋에서 자동 생성
    UserWritten,    # 사용자가 직접 작성
    AiSuggested,    # API 추천 수락 (원본 그대로)
    AiEdited        # API 추천 후 사용자 편집
}
```

**하위 호환성**: 기존 persona.json에 State/Source가 없는 SeedLine은 로드 시 `State = Happy`, `Source = UserWritten`으로 기본값 적용.

### PersonaData 확장

기존:
```
PersonaData { Name, PortraitFileName, ToneHint?, SeedLines }
```

확장:
```
PersonaData
├── Name: string
├── PortraitFileName: string
├── PresetId: string?                   # [신규] 선택한 프리셋 ID
├── CustomToneDescription: string?      # [신규] 사용자 정의 말투 설명 (프리셋 위에 덮어쓰기)
├── CustomPersonalityNotes: string?     # [신규] 사용자가 추가한 성격 메모
├── SeedLines: List<SeedLine>
```

`ToneHint`는 `CustomToneDescription`으로 대체 (마이그레이션: `ToneHint` → `CustomToneDescription` 복사).

### DialogueFeedback (피드백 기록)

```
DialogueFeedback
├── OriginalText: string                # API가 제안한 원본
├── EditedText: string?                 # 사용자가 편집한 경우
├── Type: FeedbackType                  # Accepted, Edited, Rejected
├── State: PetState                     # 어떤 상태에서의 추천이었는지
├── Timestamp: DateTime
```

```
enum FeedbackType { Accepted, Edited, Rejected }
```

**저장**: `%AppData%/JangJang/Personas/current/feedback.json`에 누적 저장. 최근 100건만 유지 (오래된 것은 자동 삭제).

---

## API 통합

### 인터페이스

```
IDialogueSuggestionService
├── SuggestAsync(state, context, count) → List<SuggestedLine>
```

```
SuggestionContext
├── PresetDescription: string           # 프리셋 기본 설명
├── PersonalityKeywords: List<string>
├── CustomNotes: string?                # 사용자 추가 메모
├── ExistingLines: Dictionary<PetState, List<string>>  # 상태별 기존 대사
├── RecentFeedback: List<DialogueFeedback>             # 최근 피드백
├── TargetState: PetState               # 추천 요청 상태
```

### 프롬프트 구성 전략

API 호출 시 프롬프트는 다음 요소를 조합:

1. **시스템 프롬프트**: "당신은 캐릭터 대사 작가입니다. 주어진 캐릭터 프로필에 맞는 짧은 대사를 생성합니다."
2. **캐릭터 프로필**: 프리셋 설명 + 성격 키워드 + 사용자 메모
3. **기존 대사 예시**: 해당 상태의 기존 대사 + 다른 상태의 대사 (스타일 참조용)
4. **상태 설명**: "현재 상태: Happy — 사용자가 열심히 작업에 집중하고 있는 상황"
5. **피드백 반영**: "이전에 거절된 스타일: [거절된 대사 패턴]", "선호하는 스타일: [수락된 대사 패턴]"
6. **요청**: "{count}개의 대사를 생성하세요. 각 대사는 한 줄, 20자 이내."

**핵심**: 대사가 쌓일수록 3번(기존 대사 예시)과 5번(피드백)이 풍부해짐 → 추천 품질 자연 향상.

### API 설정 — 무료 API 활용

**전략**: 무료 API를 기본 제공하여 사용자 비용 부담 제거.

| 순위 | 서비스 | 무료 한도 | 한국어 품질 | 신용카드 |
|------|--------|----------|-----------|---------|
| 1순위 | **Gemini Flash-Lite** | 1,000건/일, 250K TPM | 최상 | 불필요 |
| 폴백 | **Groq (Qwen3 32B)** | 14,400건/일, 6K TPM | 우수 | 불필요 |
| 대체 | **Cerebras** | ~1M 토큰/일 | 양호 | 불필요 |

모든 서비스가 **OpenAI 호환 REST API** 형식 → HTTP 클라이언트 하나로 `base_url`만 교체.

```
AppSettings (추가 필드)
├── SuggestionApiProvider: string       # "gemini" (기본값)
├── SuggestionApiKey: string?           # API 키 (무료 계정에서 발급)
├── SuggestionApiBaseUrl: string?       # 커스텀 base URL (선택, 고급 사용자용)
├── SuggestionApiModel: string?         # 모델 (기본: gemini-2.5-flash-lite)
```

**사용자 설정 플로우:**
1. PersonaWindow에서 "AI 추천 받기" 첫 클릭 시 API 키 미설정 감지
2. "무료 API 키 발급 안내" 다이얼로그 표시
   - Gemini: "Google AI Studio(aistudio.google.com)에서 API 키를 무료로 발급받으세요"
   - [AI Studio 열기] 버튼 + [키 입력] 필드
3. 키 입력 후 저장 → 이후 자동 사용
4. API 키 없어도 프리셋 + 수동 편집은 정상 작동

**프로바이더 전환**: SettingsWindow에서 프로바이더/키/모델을 변경할 수 있음 (고급 설정).
OpenAI 호환 API라면 어떤 서비스든 `base_url`만 바꿔서 사용 가능.

---

## UI 변경

### PersonaWindow 리워크

**현재**: 이름 + 초상화 + 플랫한 SeedLine 리스트
**변경 후**: 3단계 가이드 + 상태별 대사 관리

```
┌─────────────────────────────────────────────┐
│  Step 1: 프리셋 선택                          │
│  ┌──────┐ ┌──────┐ ┌──────┐ ┌──────┐       │
│  │츤데레 │ │응원형 │ │차분한 │ │사용자│       │
│  │      │ │      │ │      │ │정의  │       │
│  └──────┘ └──────┘ └──────┘ └──────┘       │
│                                              │
│  Step 2: 기본 정보                            │
│  이름: [____________]                        │
│  초상화: [선택] [미리보기]                      │
│  추가 성격 메모: [____________] (선택)         │
│                                              │
│  Step 3: 상태별 대사                          │
│  [Happy] [Alert] [Annoyed] [Sleeping] [WakeUp]│
│  ┌─────────────────────────────────────────┐ │
│  │ • "뭐야, 오늘은 좀 하네."      [편집][삭제]│ │
│  │ • "...계속 그렇게 해."         [편집][삭제]│ │
│  │ • "딴짓 안 하니까 봐줄 만하네." [편집][삭제]│ │
│  │                                         │ │
│  │ [+ 직접 추가]  [AI 추천 받기 ✨]          │ │
│  └─────────────────────────────────────────┘ │
│                                              │
│  [저장]  [취소]                               │
└─────────────────────────────────────────────┘
```

**"AI 추천 받기" 클릭 시:**
```
┌─────────────────────────────────────────────┐
│  추천 대사 (Happy 상태)              [재생성] │
│                                              │
│  1. "흠, 오늘은 의외로 괜찮은데."              │
│     [수락] [편집 후 수락] [거절]              │
│                                              │
│  2. "...뭐, 나쁘진 않아."                     │
│     [수락] [편집 후 수락] [거절]              │
│                                              │
│  3. "계속 이러면... 인정해줄 수도 있어."        │
│     [수락] [편집 후 수락] [거절]              │
│                                              │
│  API 키 없음? → 아래 안내 패널 표시                │
│                                                    │
│  ┌──────────────────────────────────────────────┐  │
│  │ AI 추천을 사용하려면 무료 API 키가 필요합니다.   │  │
│  │ Google AI Studio에서 30초만에 발급받을 수 있어요.│  │
│  │                                              │  │
│  │ [AI Studio 열기]  API 키: [________] [저장]   │  │
│  └──────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────┘
```

### SettingsWindow 변경

기존 페르소나 섹션에 API 설정 추가:
```
자캐 페르소나 (실험적)
☑ 페르소나 모드 활성화
[자캐 페르소나 편집...]

AI 대사 추천 (무료)
프로바이더: [Gemini ▾]
API 키: [________________] [테스트]
  → Google AI Studio에서 무료 발급 [열기]
  → Groq, Cerebras 등 OpenAI 호환 API도 사용 가능 (고급)
```

---

## 저장 구조 변경

```
%AppData%/JangJang/
├── settings.json                   (+ SuggestionApiProvider, SuggestionApiKey, SuggestionApiModel)
├── Personas/
│   └── current/
│       ├── persona.json            (확장: PresetId, CustomToneDescription, CustomPersonalityNotes, SeedLine.State/Source)
│       ├── portrait.png
│       ├── embeddings.bin          (기존)
│       └── feedback.json           (신규: 피드백 기록)
└── Presets/                        (앱 번들에서 복사 또는 앱 내 리소스에서 직접 로드)
```

### 프리셋 배포

프리셋 JSON 파일은 **앱 리소스로 번들** (EmbeddedResource 또는 Content).
- 앱 업데이트 시 프리셋도 함께 업데이트 가능
- 사용자가 수정할 필요 없음

---

## 변경 파일 목록

### 기존 파일 수정

| 파일 | 변경 내용 |
|---|---|
| `Core/Persona/PersonaData.cs` | PresetId, CustomToneDescription, CustomPersonalityNotes 필드 추가. ToneHint 제거(마이그레이션) |
| `Core/Persona/SeedLine.cs` | State(PetState), Source(SeedLineSource) 필드 추가. 기본값 처리 |
| `Core/Persona/PersonaStore.cs` | 확장된 PersonaData 직렬화, 피드백 로드/저장, 하위 호환성 |
| `Core/AppSettings.cs` | SuggestionApiProvider, SuggestionApiKey, SuggestionApiModel 필드 추가 |
| `Views/Persona/PersonaWindow.xaml` | 3단계 가이드 UI, 상태 탭, 추천 패널로 전면 리워크 |
| `Views/Persona/PersonaWindow.xaml.cs` | 프리셋 로드, 상태별 대사 관리, 추천/피드백 로직 |
| `Views/SettingsWindow.xaml` | API 키 입력 UI 추가 |
| `Views/SettingsWindow.xaml.cs` | API 키 저장/로드 |

### 신규 파일

**프리셋**
| 파일 | 역할 |
|---|---|
| `Core/Persona/Preset/PersonaPreset.cs` | 프리셋 데이터 모델 |
| `Core/Persona/Preset/PresetSeedLine.cs` | 프리셋 내 샘플 대사 |
| `Core/Persona/Preset/PresetStore.cs` | 프리셋 로드 (앱 리소스에서) |
| `Resources/Presets/tsundere.json` | 프리셋: 츤데레 |
| `Resources/Presets/supportive.json` | 프리셋: 응원형 |
| `Resources/Presets/calm.json` | 프리셋: 차분한 |

**추천 서비스**
| 파일 | 역할 |
|---|---|
| `Core/Persona/Suggestion/IDialogueSuggestionService.cs` | 추천 서비스 인터페이스 |
| `Core/Persona/Suggestion/ApiDialogueSuggestionService.cs` | API 기반 구현 (HTTP 클라이언트) |
| `Core/Persona/Suggestion/SuggestionContext.cs` | API 호출 시 전달할 컨텍스트 |
| `Core/Persona/Suggestion/SuggestedLine.cs` | 추천 결과 데이터 모델 |
| `Core/Persona/Suggestion/PromptBuilder.cs` | 컨텍스트 → 프롬프트 조합 |

**피드백**
| 파일 | 역할 |
|---|---|
| `Core/Persona/Feedback/DialogueFeedback.cs` | 피드백 데이터 모델 |
| `Core/Persona/Feedback/FeedbackStore.cs` | 피드백 저장/로드 (feedback.json) |

### 변경 없는 파일 (런타임 파이프라인)

- `Core/Dialogue.cs` — 변경 없음
- `Core/Persona/PersonaDialogueProvider.cs` — 변경 없음
- `Core/Persona/Pipeline/*` — 변경 없음
- `Core/Persona/Embedding/*` — 변경 없음
- `ViewModels/PetViewModel.cs` — 변경 없음
- `App.xaml.cs` — 변경 없음 (Provider 초기화 로직은 SeedLine에 State가 추가되어도 영향 없음)

---

## 구현 단계

### Step 1 — 프리셋 데이터 모델 + 스토어
1. `PersonaPreset`, `PresetSeedLine` 데이터 모델
2. `PresetStore` — 앱 리소스에서 프리셋 JSON 로드
3. 프리셋 JSON 3개 작성 (츤데레, 응원형, 차분한)
4. 단위 테스트: 프리셋 로드 round-trip

### Step 2 — SeedLine + PersonaData 확장
1. `SeedLine`에 `State`, `Source` 필드 추가
2. `SeedLineSource` enum
3. `PersonaData`에 `PresetId`, `CustomToneDescription`, `CustomPersonalityNotes` 추가
4. `PersonaStore` 하위 호환성 (기존 persona.json 마이그레이션)
5. 단위 테스트: 확장된 데이터 round-trip, 하위 호환성

### Step 3 — 피드백 데이터
1. `DialogueFeedback`, `FeedbackType`
2. `FeedbackStore` — feedback.json 로드/저장, 100건 제한
3. 단위 테스트: 피드백 round-trip

### Step 4 — API 추천 서비스
1. `IDialogueSuggestionService` 인터페이스
2. `SuggestionContext`, `SuggestedLine` 데이터 모델
3. `PromptBuilder` — 컨텍스트 → 프롬프트 문자열 조합
4. `ApiDialogueSuggestionService` — OpenAI 호환 HTTP 클라이언트 (base_url 교체로 Gemini/Groq/Cerebras 지원)
5. `AppSettings`에 API 관련 필드 추가 (Provider, Key, BaseUrl, Model)
6. 단위 테스트: PromptBuilder 스냅샷 테스트

### Step 5 — PersonaWindow UI 리워크
1. Step 1 UI: 프리셋 선택 (카드/버튼 그리드)
2. Step 2 UI: 이름 + 초상화 + 추가 메모
3. Step 3 UI: 상태별 탭 + 대사 리스트 + 편집/삭제
4. "AI 추천 받기" 버튼 → 추천 패널 (수락/편집/거절)
5. "직접 추가" 버튼 → 기존 수동 입력
6. 프리셋 선택 시 SeedLine 자동 채움
7. 저장 시 PersonaStore.Save() + EmbeddingCache 재구축

### Step 6 — SettingsWindow API 키 UI
1. 프로바이더 선택 ComboBox (Gemini 기본, Groq, 커스텀)
2. API 키 입력 필드 + "무료 발급" 링크 버튼
3. "테스트" 버튼 (간단한 API 호출로 키 유효성 확인)
4. AppSettings 저장/로드

### Step 7 — 통합 검증
1. 프리셋 선택 → SeedLine 채움 → 저장 → 런타임 매칭 작동
2. API 추천 → 수락 → SeedLine 추가 → 런타임 매칭에 반영
3. 피드백 누적 → 다음 추천 품질 변화 확인 (수동)
4. 기존 persona.json 하위 호환성
5. API 키 없을 때 추천 비활성화, 수동 편집만 가능

---

## 재사용할 기존 코드

| 파일 | 재사용 방식 |
|---|---|
| `PersonaStore.cs` JSON 직렬화 패턴 | FeedbackStore, PresetStore에서 동일 패턴 |
| `SettingsWindow.xaml.cs` ComboBox+이벤트 패턴 | PersonaWindow 프리셋 선택에 재사용 |
| `PersonaWindow.xaml.cs` ObservableCollection 패턴 | 상태별 SeedLine 리스트에 재사용 |
| `PersonaWindow.xaml.cs` 초상화 선택 로직 | 그대로 유지 |
| `EmbeddingCandidateSelector` 매칭 로직 | 런타임 변경 없음, SeedLine.State 필드는 매칭에 미사용 (ContextNarrator가 상태 정보를 이미 포함) |

---

## 엣지 케이스 / 주의사항

1. **API 키 없음**: 추천 기능만 비활성화. 프리셋 + 수동 편집은 정상 작동. 첫 "AI 추천 받기" 클릭 시 무료 API 키 발급 안내 인라인 표시.
2. **API 호출 실패**: 타임아웃/에러 시 사용자에게 토스트 알림. 재시도 버튼. 기존 대사에는 영향 없음.
3. **프리셋 변경**: 이미 편집된 대사가 있을 때 다른 프리셋을 선택하면? → "기존 대사를 프리셋 대사로 교체할까요?" 확인 다이얼로그. Source=UserWritten/AiEdited인 대사는 보존 옵션 제공.
4. **하위 호환성**: 기존 persona.json에 State/Source 필드 없음 → 로드 시 기본값 적용 (State=Happy, Source=UserWritten). 저장 시 새 필드 포함.
5. **피드백 오버플로우**: feedback.json은 최근 100건만 유지. 오래된 피드백의 패턴은 프리셋 프로필에 이미 반영되어 있으므로 삭제 가능.
6. **API 키 보안**: settings.json에 평문 저장. 로컬 앱이므로 수용 가능하나, 로그/오류 보고에 키를 포함시키지 않음. 무료 API 키이므로 유출 시 비용 리스크 없음 (rate limit만 소진).
10. **API rate limit 초과**: 무료 한도 초과 시 (Gemini: 1,000건/일) 사용자에게 "오늘 추천 한도에 도달했습니다. 내일 다시 시도하거나, 다른 프로바이더를 설정하세요" 안내.
11. **프로바이더 전환**: OpenAI 호환 형식이므로 base_url + API 키만 교체. 프롬프트는 동일. 단, 모델마다 한국어 품질 차이가 있을 수 있음.
7. **빈 상태 탭**: 특정 PetState에 대사가 0개일 수 있음. 런타임에서는 해당 상태에 대사가 없으면 다른 상태 대사 중 가장 유사한 것으로 폴백 (기존 임베딩 매칭이 자연스럽게 처리).
8. **추천 대사 길이**: 프롬프트에서 "20자 이내" 제약을 명시하되, 모델이 초과할 수 있음. 클라이언트 측에서 잘라내지 않고 사용자에게 편집 기회 제공.
9. **API 비용**: Claude Haiku 기준 추천 1회(3개 대사) ≈ ~200 입력 토큰 + ~100 출력 토큰. 비용 극미미.

---

## 테스트 방법

### 단위 테스트
- PresetStore: 프리셋 JSON 로드 round-trip
- PersonaData 확장: 직렬화 round-trip, 하위 호환성
- FeedbackStore: 피드백 저장/로드, 100건 제한
- PromptBuilder: 컨텍스트 → 프롬프트 스냅샷 테스트

### 수동 테스트
- [ ] 프리셋 선택 → SeedLine 자동 채움 → 저장 → 런타임 대사 작동
- [ ] API 추천 → 수락 → SeedLine 추가 → 런타임에 반영
- [ ] API 추천 → 편집 후 수락 → 편집된 내용으로 저장
- [ ] API 추천 → 거절 → 피드백 기록 → 다음 추천에 반영
- [ ] API 키 없을 때 추천 버튼 비활성화
- [ ] 기존 persona.json 하위 호환성 (State/Source 없는 파일)
- [ ] 프리셋 변경 시 기존 대사 보존/교체 선택

### 검증 루프 (1-2주)
1. 프리셋만으로 10분 내 초기 설정이 가능한가?
2. API 추천이 캐릭터다운 대사를 생성하는가?
3. 대사 축적 후 추천 품질이 실제로 향상되는가?

---

## 열린 질문 (`/build` 중 해결)

1. **프리셋 추가 종류**: 초기 3개(츤데레, 응원형, 차분한) 외에 더 필요한가? → Step 1에서 결정
2. **"사용자 정의" 프리셋**: 빈 프리셋(프리셋 없이 시작)을 허용할 것인가, 프리셋 선택을 강제할 것인가? → Step 5에서 결정
3. **API 모델 선택**: 기본은 Gemini Flash-Lite 고정, SettingsWindow에서 프로바이더/모델 변경 가능 (고급). → Step 6에서 UI 확정
4. **프롬프트 튜닝**: 최적 프롬프트 템플릿은 실제 테스트를 거쳐야 확정됨 → Step 4 + 검증 루프에서 반복

---

**다음 단계**: `/build` Step 1부터 구현 시작.
