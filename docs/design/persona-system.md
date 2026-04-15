# 설계 문서: 자캐 페르소나 시스템 (Focus-Me 자캐 브랜치)

> 이 문서는 Focus-Me의 상위 버전인 **자캐(Jakae) 페르소나 시스템**의 아키텍처 설계 문서다.
> 입력: [문제 명세서](../../../.claude/plans/temporal-munching-snail.md) (`/think` 산출물)
> 다음 단계: `/build`로 구현 시작

---

## 문제

기존 Focus-Me("치와와")는 하드코딩된 대사로 중립적 알림만 제공. 자캐 버전은 사용자가 애정하는 가상 대상(최애/OC)의 페르소나를 부여하여 **"내 머릿속에만 살던 그 사람이, 오늘 나와 함께 일한다"**는 경험을 구현한다. 자캐 브랜치 = 치와와의 완전 상위 집합, 페르소나 기능은 옵션.

## 핵심 경험 (나침반)

> **"내 머릿속에만 살던 그 사람이, 오늘 나와 함께 일한다."**

모든 설계 결정은 이 한 줄을 기준으로 한다. 갈림길에서 "이 경험에 가까운 쪽"을 고른다.

## 선택한 접근법

**B (Provider + 4단계 파이프라인) + C의 선택적 편입**

- `IDialogueProvider` 인터페이스로 기존 대사 로직(`DefaultDialogueProvider`)과 페르소나 로직(`PersonaDialogueProvider`)을 병치
- 파이프라인 4단계: `ContextCollector → CandidateSelector → OutputProcessor → 출력`
- (가) MVP: `OutputProcessor`는 pass-through. (나) 확장: `LlmVariationOutputProcessor`로 교체만 하면 됨
- **C의 편입**: 씨앗 대사 입력 시 "상황 설명" 필드는 **선택 사항**. 없으면 대사 본문 벡터만으로 매칭(C 모드), 있으면 설명 벡터 우선(B 모드)
- 자캐 전용 코드는 `Core/Persona/`, `Views/Persona/`, `ViewModels/Persona/`로 **격리** → 브랜치 충돌 최소화

### 주요 결정 요약

| 결정 사항 | 값 |
|---|---|
| 상황 매칭 방식 | 임베딩 기반 (bge-m3, ~500MB) |
| 이미지 요구 | 초상화 1장 필수 (치와와 5단 체계와 별도) |
| 치와와 모드 공존 | 페르소나는 옵션, 자캐 = 치와와 완전 상위 집합 |
| 페르소나 개수 | 단일 (한 번에 하나, 디렉토리 구조로 저장하여 확장 여지 보존) |

## 브랜치 정책

| 항목 | 결정 |
|---|---|
| master | = 치와와. 분기하지 않음 |
| 자캐 브랜치 | master에서 분기. 이름은 **`jakae`** 확정 |
| master → jakae | 주기적 자동 리베이스 (격리 폴더 덕분에 충돌 최소) |
| jakae → master | **수동 cherry-pick만**. 공통화 가치 확실한 것만 |
| 공통 파일 수정 원칙 | 자캐 로직을 공통 파일에 **넣지 않음**. 최소 1-2줄 위임만 허용 |
| 릴리즈 태그 | master: `v1.x.x` 계승 / jakae: `v0.1.x-jakae` 시작, 안정화 후 `v1.x.x-jakae` |

## 아키텍처 개요

```
PetViewModel.OnStateUpdated()
        ↓
Dialogue.GetLine(state, annoyance, todaySeconds)
        ↓
[현재 활성 IDialogueProvider]
        ↓
 ┌────────────────────┴────────────────────┐
 ↓                                         ↓
DefaultDialogueProvider          PersonaDialogueProvider
(기존 하드코딩 배열)              (4단계 파이프라인)
                                           ↓
                              ContextCollector
                                           ↓  (DialogueContext)
                              ContextNarrator (C의 핵심)
                                           ↓  (상황 서술 문장)
                              CandidateSelector (EmbeddingCandidateSelector)
                                           ↓  (상위 N개 후보)
                              OutputProcessor (Passthrough / LlmVariation)
                                           ↓
                                       최종 대사
```

## 저장 구조

```
%AppData%/JangJang/
├── settings.json              (기존, PersonaEnabled 필드 추가)
├── WorkLog.json               (기존)
├── Models/
│   └── bge-m3-Q4_K_M.gguf     (최초 1회 사용자 배치, 이후 불변)
└── Personas/
    └── current/
        ├── persona.json       (이름, 말투, 씨앗 대사 풀)
        ├── portrait.png       (초상화 1장)
        └── embeddings.bin     (씨앗 대사 임베딩 캐시)
```

단일 페르소나지만 `current/` 디렉토리 구조로 저장 → 나중 다중 전환 시 해당 폴더 이름만 스위칭하면 됨.

### 모델 파일 배포 및 위치

**배포 방식** — 릴리즈 페이지에 두 아티팩트 병치:
- `JangJang-jakae.exe` — 프로그램 본체. 업데이트 대상
- `bge-m3-Q4_K_M.gguf` — 임베딩 모델. **최초 1회만 다운로드, 이후 불변**

**사용자 설치 순서 (최초)**:
1. 사용자는 릴리즈 페이지에서 두 파일을 모두 다운로드
2. 프로그램 실행 → 자캐 모드 활성화 시 "모델 파일을 선택해주세요" 파일 피커 표시
3. 사용자가 받은 `.gguf` 파일을 선택 → 앱이 `%AppData%/JangJang/Models/`로 자동 복사
4. 이후 해당 위치에서 로드

**이후 업데이트**: 프로그램 본체(.exe)만 다운로드. 모델 파일은 이미 AppData에 존재하므로 건드리지 않음. 사용자는 모델을 재다운로드할 필요 없음.

**파일 탐색 순서** (앱 시작 시):
1. `%AppData%/JangJang/Models/bge-m3-Q4_K_M.gguf` (표준 위치)
2. 프로그램 본체와 같은 폴더의 `bge-m3-Q4_K_M.gguf` (포터블/개발 환경 폴백)
3. 둘 다 없으면 → "모델 파일이 없습니다" UI + [릴리즈 페이지 열기] 버튼 + [파일 선택] 버튼

**인앱 다운로더 없음**: 앱에 네트워크 다운로드 로직을 구현하지 않는다. 배포는 GitHub Release 등 외부 플랫폼에 의존. 앱은 파일 존재 여부만 체크.

## 변경 파일 목록

### 공통 파일 (최소 수정, master 호환)

| 파일 | 변경 내용 |
|---|---|
| `src/JangJang/Core/Dialogue.cs` | 하드코딩 로직을 `DefaultDialogueProvider`로 이관. `GetLine()`은 현재 Provider에 위임 |
| `src/JangJang/Core/AppSettings.cs` | `PersonaEnabled: bool` (기본 false) 추가 |
| `src/JangJang/App.xaml.cs` | 시작 시 Provider 초기화 |
| `src/JangJang/JangJang.csproj` | `LLamaSharp`, `LLamaSharp.Backend.Cpu` NuGet 추가 (자캐 브랜치에만) |

### 자캐 전용 신규 파일 (격리 폴더)

**파이프라인 계약 & Provider**
- `src/JangJang/Core/Persona/IDialogueProvider.cs`
- `src/JangJang/Core/Persona/DefaultDialogueProvider.cs` — 기존 로직 이관
- `src/JangJang/Core/Persona/PersonaDialogueProvider.cs` — 오케스트레이션
- `src/JangJang/Core/Persona/DialogueContext.cs` — 상황 스냅샷

**파이프라인 4단계**
- `src/JangJang/Core/Persona/Pipeline/IContextCollector.cs` + `DefaultContextCollector.cs`
- `src/JangJang/Core/Persona/Pipeline/ContextNarrator.cs` — 상황을 한국어 문장으로 서술 (C 기능의 핵심)
- `src/JangJang/Core/Persona/Pipeline/ICandidateSelector.cs` + `EmbeddingCandidateSelector.cs`
- `src/JangJang/Core/Persona/Pipeline/IOutputProcessor.cs` + `PassthroughOutputProcessor.cs` ((가) MVP 구현)
- (나 확장 시) `LlmVariationOutputProcessor.cs`

**임베딩 서비스**
- `src/JangJang/Core/Persona/Embedding/BgeM3Service.cs` — LLamaSharp 래퍼
- `src/JangJang/Core/Persona/Embedding/EmbeddingCache.cs` — 씨앗 대사 벡터 영속화

**페르소나 데이터**
- `src/JangJang/Core/Persona/PersonaData.cs`
- `src/JangJang/Core/Persona/SeedLine.cs` — `{ Text, SituationDescription?, CreatedAt }`
- `src/JangJang/Core/Persona/PersonaStore.cs`

**UI**
- `src/JangJang/Views/Persona/PersonaWindow.xaml(.cs)`
- `src/JangJang/Views/Persona/SeedLineEditControl.xaml(.cs)`
- `src/JangJang/ViewModels/Persona/PersonaViewModel.cs`

## 구현 단계 (순서)

### Step 0 — 브랜치 준비
1. master에서 `jakae` 브랜치 생성
2. 자캐 전용 디렉토리 스켈레톤 생성 (`Core/Persona/`, `Core/Persona/Pipeline/`, `Core/Persona/Embedding/`, `Views/Persona/`, `ViewModels/Persona/`)

### Step 1 — 추상화 계층 도입 (동작 변화 0)
1. `IDialogueProvider` 정의
2. `DefaultDialogueProvider` 생성, 기존 `Dialogue.cs` 로직 복사
3. `Dialogue.GetLine()`을 정적 `DefaultProvider`에 위임
4. **회귀 확인**: 자캐 브랜치 + `PersonaEnabled=false` → 치와와 동작 100% 동일

### Step 2 — 데이터 모델 & 저장소
1. `PersonaData`, `SeedLine`, `PersonaStore` 구현
2. 샘플 `persona.json` 수동 작성 → 로드/저장 round-trip 확인

### Step 3 — 임베딩 서비스
1. **프로토타입 검증** (최우선): 빈 .NET 7 콘솔 앱 + LLamaSharp + bge-m3 로드 + SingleFile 빌드 → 실행 확인
   - 통과 시 → 2-A 방식 확정 (SingleFile 유지 + `IncludeNativeLibrariesForSelfExtract`)
   - 실패 시 → 2-B 방식으로 피봇 (자캐 브랜치만 폴더 배포)
2. LLamaSharp NuGet 추가 (`LLamaSharp`, `LLamaSharp.Backend.Cpu`)
3. `JangJang.csproj`에 `<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>` 추가 (2-A 경로)
4. bge-m3-Q4_K_M.gguf 개발용 확보 (Hugging Face에서 직접 다운로드, 개발 중 임시 위치 지정)
5. `BgeM3Service` — 로드/임베딩/코사인 유사도. 모델 경로 탐색 로직 (AppData/Models → exe 폴더 → 실패 시 UI 요청)
6. `EmbeddingCache` — 씨앗 대사 hash 기반 무효화
7. 스탠드얼론 테스트: 5개 대사로 매칭 검증

### Step 4 — 파이프라인 구현
1. `DialogueContext`, `DefaultContextCollector`
2. `ContextNarrator` — 규칙 기반 상황 서술 문장 생성 (예: "집중 3시간째, 점심 무렵, 약간 짜증난 상태")
3. `EmbeddingCandidateSelector` — 설명 있음/없음 자동 모드 전환, 최근 N개 대사 가중치 감소
4. `PassthroughOutputProcessor` — 후보 중 가중치 랜덤
5. `PersonaDialogueProvider` — 4단계 오케스트레이션

### Step 5 — Provider 전환 & 설정 연결
1. `AppSettings.PersonaEnabled` 추가
2. `App.xaml.cs`: 시작 시 Provider 결정. PersonaEnabled=true & `current/` 존재 시 `PersonaDialogueProvider` 주입, 아니면 `DefaultDialogueProvider`
3. 런타임 전환은 재시작 요구 (안전)

### Step 6 — UI
1. `PersonaWindow` 3단계 가이드:
   - ① 초상화 파일 선택 + 페르소나 이름
   - ② 말투 프리셋 선택 (옵션 — 열린 질문 4번)
   - ③ 씨앗 대사 입력 (최소 5개 권장)
2. `SeedLineEditControl` — 대사 + 상황 설명(선택) 입력
3. `SettingsWindow`에 "자캐 페르소나 편집" 버튼 + "페르소나 모드 활성화" 체크박스 추가
4. 초상화 표시 — 기존 `PetViewModel` 이미지 시스템과 통합 (열린 질문 6번)

### Step 7 — 통합 검증
1. 페르소나 생성 → 저장 → 활성화 → 재시작 → 자캐 대사 확인
2. 페르소나 OFF → ON → OFF 전환 정상 동작
3. 씨앗 대사 추가/삭제 → 임베딩 캐시 정합성
4. 각 상태(Happy/Alert/Annoyed 등)에서 상황에 맞는 대사가 나오는지 수동 관찰

### Step 8 — 자기 검증 루프
MVP 완성. 개발자 본인이 자신의 자캐로 **2-4주 실사용**. 문제 명세서의 검증 질문 3개 점검.

## 재사용할 기존 코드

- `src/JangJang/Core/Dialogue.cs` → `DefaultDialogueProvider`로 로직 그대로 이관
- `src/JangJang/Core/ActivityMonitor.cs` — `SessionSeconds`, `IdleSessionSeconds`, `AnnoyanceLevel`, `IdleSeconds` → `DefaultContextCollector`에서 그대로 읽음
- `src/JangJang/Core/WorkLog.cs` — `TodaySeconds` → `DefaultContextCollector` 사용
- `src/JangJang/Core/PetState.cs` enum → `DialogueContext.State` 필드
- `src/JangJang/ViewModels/PetViewModel.cs:111` — `Dialogue.GetLine()` 호출 방식 변화 없음 (Provider 뒤에 숨음)
- `src/JangJang/Core/AppSettings.cs` JSON 직렬화 패턴 → `PersonaStore`에서 동일 패턴 재사용
- `src/JangJang/Views/SettingsWindow.xaml.cs` 이미지 선택 다이얼로그 패턴 → `PersonaWindow`에서 초상화 선택에 재사용

## 엣지 케이스 / 주의사항

1. **bge-m3 로드 실패** (네트워크 없음, 디스크 공간 부족 등) → `PersonaDialogueProvider` 생성 실패 → 자동으로 `DefaultDialogueProvider` 폴백. 사용자에게 토스트 알림
2. **씨앗 대사 풀이 비었을 때** → 후보 0개 → 치와와 기본 대사로 임시 폴백. UI에 "대사를 추가하세요" 경고
3. **상황 설명이 모두 비어있을 때 (C 모드)** → `EmbeddingCandidateSelector`가 자동으로 대사 본문 벡터 매칭으로 전환 (내부 플래그)
4. **임베딩 계산 지연**: bge-m3 CPU 추론 ~100-300ms 예상. 알림 한두 초 지연은 UX 영향 적음. 씨앗 대사 임베딩은 **캐시 필수** (앱 시작 시 재계산 금지)
5. **대사 반복감 방지** (전제 C의 핵심 리스크): `PassthroughOutputProcessor`에 최근 N개 큐 → 최근 출력 대사는 가중치 낮춤
6. **초상화 없음**: 치와와 기본 이미지 폴백
7. **모델 파일 부재**: 자캐 모드 활성화 시 모델 파일 탐색 → 없으면 "모델 파일이 필요해요. 릴리즈 페이지에서 받은 `bge-m3-Q4_K_M.gguf` 파일을 선택해주세요." UI 표시. 파일 선택 시 `%AppData%/JangJang/Models/`로 자동 복사. 인앱 다운로더 없음
8. **PersonaEnabled 런타임 전환**: 재시작 요구 (임베딩 서비스 초기화 비용 때문)
9. **씨앗 대사의 내용 위험**: 사용자가 쓰는 내용 그대로 노출되므로, 개인 정보/민감 내용이 들어갈 수 있음 → 로그나 오류 보고에 대사 내용을 포함시키지 않음

## 테스트 방법

### 수동 테스트 체크리스트
- [ ] **치와와 회귀**: 자캐 브랜치 + PersonaEnabled=false에서 master와 완전 동일 동작
- [ ] **페르소나 생성 → 활성화 → 대사 전환**: 기본 플로우
- [ ] **상황 매칭**: 각 PetState로 인위적 전환 시 대사가 상황에 맞게 바뀜
- [ ] **대사 반복감**: 같은 상태 10분 유지 중 연속 반복이 없음
- [ ] **상황 설명 있음/없음**: 두 모드 간 매칭 품질 비교
- [ ] **씨앗 대사 추가/삭제**: 임베딩 캐시 정합성

### 최소 단위 자동 테스트 (옵션)
- `PersonaStore` round-trip
- `BgeM3Service` 코사인 계산 sanity check
- `ContextNarrator` 규칙 기반 문장 생성 스냅샷

### 진짜 테스트 — 자기 검증 루프 (2-4주)
문제 명세서의 세 질문:
1. "AI 같다"는 순간이 있었는가? (한 번이라도 있었다면 치명적)
2. "비위 맞춰야 한다"는 감각이 들었는가?
3. 작업 완수에 실제로 도움이 됐는가?

## 결정된 사항 (기존 열린 질문 1-3)

1. **bge-m3 모델 배포 전략**: **별도 파일로 배포.** 릴리즈 페이지에 프로그램 본체와 모델 파일 두 아티팩트 병치. 사용자는 최초 1회 두 파일 모두 다운로드 후, 자캐 모드 활성화 시 파일 피커로 모델 위치 지정 → 앱이 `%AppData%/JangJang/Models/`로 복사. 이후 프로그램 업데이트는 본체만 받으면 됨. 인앱 다운로더 없음. (상세: "저장 구조 > 모델 파일 배포 및 위치" 섹션)
2. **LLamaSharp + win-x64 self-contained 호환성**: **검증 유예.** Step 3 초입에 최소 콘솔 프로토타입으로 실측. 통과 시 2-A(SingleFile 유지 + `IncludeNativeLibrariesForSelfExtract`), 실패 시 2-B(자캐 브랜치만 폴더 배포로 피봇).
3. **브랜치 이름**: **`jakae` 확정.**

## 열린 질문 (남아있음, `/build` 중 해결)

4. **말투 프리셋의 필요성**: (가) MVP에선 말투가 씨앗 대사에 녹아있음. "말투 설정" 필드가 추가 가치가 있는가? → **Step 6 시작 시 재평가**
5. **페르소나 생성 마법사 vs 일반 창**: 첫 실행 시 강제 마법사 vs 사용자 자율. → **Step 6에서 결정**
6. **페르소나 이미지 표시 방식**: 치와와의 상태별 이미지와 어떻게 병치/대체할 것인가. 초상화 1장만 있을 때 Happy/Alert/Annoyed를 어떻게 표현할지 (예: 초상화 + 상태별 프레임 효과, 또는 치와와 이미지를 그대로 쓰되 대사만 페르소나화 등). → **Step 6에서 결정**

---

**다음 단계**: `/build` 로 구현 시작.
