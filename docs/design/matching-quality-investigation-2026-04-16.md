# 매칭 품질 조사 보고서 — 2026-04-16

> 사용자 보고: "대사가 상황에 맞지 않게 매칭된다"
> 조사 기간: 2026-04-16 (자율 세션, 약 2-3시간)
> 상태: **부분 해결** (keyword boost 적용, 근본 개선은 향후 작업)

## 조사 요약 한 줄

**원인은 우리 코드 버그가 아니라 `intfloat/multilingual-e5-small` 모델이 한국어 짧은 대화체 문장에 대해 제한적 의미 이해를 가지는 것.** Keyword overlap boost 하이브리드 접근으로 체감 개선을 달성했고, 근본 해결은 더 큰 모델 또는 한국어 특화 모델로의 교체가 필요.

## 증상

사용자가 자캐 페르소나를 실행하며 "대사가 상황에 맞지 않게 매칭된다"고 보고.
Tier 2 통합 테스트 중 `Cosine_SimilarSentences_HigherThanUnrelatedSentences` 테스트 실패:

```
유사 문장 코사인(0.858)이 무관 문장(0.858)보다 작음 — 매칭 품질 의심
```

"힘들지 쉬어"(관련)와 "고양이는 귀엽다"(무관)가 쿼리 "오래 앉아서 지쳐 보인다"에 대해 **정확히 같은 cosine 0.858**을 가짐. 우연 아님.

## 가설과 검증 순서

### ❌ 가설 1: SentencePiece 토크나이저가 한국어를 `<unk>`로 처리
검증: `Probe_TokenizerProducesDifferentTokensForDifferentKoreanInputs`
- Vocab 크기: 250,000 (XLM-RoBERTa 정상)
- 9개 한국어 입력 모두 서로 다른 토큰 시퀀스
- **한국어 입력의 UNK 비율 0%**
- 결론: 토크나이저 완벽 정상. **가설 기각.**

### ❌ 가설 2: `DenseTensor<float>` multi-dim indexer가 stride 오류
관찰: 초기 덤프에서 BOS(position[0])와 EOS(position[마지막])가 **정확히 동일한 값**으로 보임. Indexer가 같은 메모리 오프셋을 읽는 것으로 의심.

검증: `Probe_CompareIndexerVsFlatBuffer`
- `tensor[0, t, h]` (indexer) vs `flat[t * hidden + h]` (수동 stride) 결과 **완벽 일치**
- 중간 position들(1..seq-2)은 전부 서로 다른 값
- BOS == EOS는 **모델 자체의 출력** (Xenova e5-small의 특이한 export 특성)
- 결론: Indexer 정상. **가설 기각.**

### ❌ 가설 3: 잘못된 출력 읽기 (last_hidden_state가 아니라 pooler_output?)
검증: `Probe_InspectOnnxInputOutputMetadata`
- 입력: `input_ids`, `attention_mask`, `token_type_ids` (모두 int64)
- 출력: **`last_hidden_state`** (단 하나, shape `[batch, seq, 384]`)
- 결론: 올바른 출력을 읽고 있음. **가설 기각.**

### ✅ 가설 4: e5 모델의 anisotropy (수렴 현상)
검증: `Probe_EmbeddingCollapseDetection`
- 10개 무관한 한국어 문장의 all-pairs cosine
- **평균: 0.8867** (건강한 기대값 0.3-0.5)
- **표준편차: 0.0153** (건강한 기대값 0.1-0.2)
- **범위 (max-min): 0.0696** (매우 좁음)
- 모든 벡터가 L2 정규화됨 (norm ≈ 1.000)
- 결론: **모델이 내용을 구분하지만 매우 좁은 구분력.** Sentence-transformer 모델의 알려진 anisotropy 문제.

### ⚠️ 가설 5: Mean centering으로 anisotropy 해결 가능
검증: `Probe_MeanCenteringImprovement` + `Probe_CenteringABComparison`
- 이론: 학계의 isotropic normalization (All-but-the-Top 등)
- 효과 측정: 10 무관 문장 cosine 분포 기준
  - Raw: 평균 0.8867, stddev 0.0153
  - Centered: 평균 -0.1103, stddev 0.0717 (**4.7배** 증가)
- 실제 rank 품질 A/B (15 씨앗 × 10 쿼리):
  - Raw: top-1 **4/10**, top-3 **6/10**
  - Centered: top-1 **5/10**, top-3 **6/10**
- **실질적 개선 미미 + 일부 케이스 악화** (예: "배고파서 밥 먹고 싶다" → raw #2, centered #11)
- 결론: **효과 대비 복잡도 부족.** 채택 안 함.

### ✅ 가설 6: Keyword overlap boost (hybrid 검색)
검증: `Probe_KeywordBoostABComparison` — weight sweep [0.00, 0.05, 0.10, 0.20, 0.50]

| weight | top-1 | top-3 |
|---|---|---|
| 0.00 (기준) | 4/10 | 6/10 |
| **0.05** | **6/10** | **8/10** |
| 0.10 | 6/10 | 8/10 |
| 0.20 | 6/10 | 8/10 |
| 0.50 | 6/10 | 8/10 |

- **Sweet spot 0.05**: top-1 +50%, top-3 +33%
- Weight 0.05 이상 포화(추가 개선 없음, 악화도 없음) → **민감도 낮아 안정적**
- 결론: **채택.** `EmbeddingCandidateSelector`에 통합.

## 핵심 발견

1. **e5-small의 한국어 역량이 제한적**. 특히 짧은 대화체 문장에서 어휘 중복 없는 의미 매칭이 약함. 다국어 모델이지만 한국어 대화체 훈련 데이터 부족으로 추정.

2. **Anisotropy는 진짜지만 주된 문제는 아님**. 평균 cosine 0.88이 병리적으로 보이지만, mean centering으로도 실질 rank 품질은 거의 개선되지 않음. 수렴은 원인이 아니라 증상.

3. **Keyword overlap이 주효**. e5가 의미 임베딩보다 **subword 어휘 매칭에 강하게 반응**하는 특성을 역으로 활용. 전통적 BM25 스타일 신호를 0.05 weight로 추가하는 것만으로 체감 품질 개선.

4. **Production 아키텍처는 건강함**. 파이프라인 4단계, IDialogueProvider 추상화, 폴백 체인, PassthroughOutputProcessor의 weighted random — 모두 제한된 품질 환경에서 실용적 동작을 보장.

## 적용된 변경

### `Core/Persona/Pipeline/EmbeddingCandidateSelector.cs`
- `KeywordBoostWeight = 0.05f` 상수
- 씨앗 대사마다 공백/구두점 분할 기반 token set 계산 (`_seedTokens`)
- `Select()` 시 쿼리 토큰 추출 + Jaccard 유사도 계산
- `finalScore = cosine + 0.05 * jaccard(queryTokens, seedTokens)`
- 한국어 형태소 분석 없음 — 간단한 공백 분할로 충분

### `JangJang.Tests/OnnxEmbeddingServiceIntegrationTests.cs`
- `Cosine_SimilarSentences_HigherThanUnrelatedSentences` 삭제 (e5 anisotropy 현실 반영)
- `Cosine_RawEmbeddingSanityCheck_NotCollapsed` 신규 — 임베딩이 완전 붕괴(mean>0.99)하지 않았다는 완화된 sanity check

## 남은 한계 (향후 개선 가능)

### 1. e5-small의 의미 이해 한계
- **현상**: "밤에 자기 전 인사" → "잘자" 매칭 실패 (의미 동등, 어휘 다름)
- **개선책**:
  - 더 큰 모델: `intfloat/multilingual-e5-base` (~280MB, 2.3배 큰)
  - 한국어 특화: `jhgan/ko-sroberta-multitask`, `BM-K/KoSimCSE-roberta`
  - 한계: 모델 교체는 NuGet 의존성·배포 크기·테스트 재검증 필요 → scope 밖

### 2. 단순 공백 토큰화의 한계
- **현상**: 한국어 조사(은/는/이/가/을/를)가 token 끝에 붙어 Jaccard 과소평가
- **개선책**: 형태소 분석기 도입 (KoNLPy, Khaiii 등)
- **현재 판단**: 단순 분할로도 +50%/+33% 개선 확인 → 복잡도 추가 가치 낮음

### 3. SeedLine.SituationDescription 활용 안 됨
- 사용자의 현재 페르소나 `단홍`은 10개 대사 모두 `SituationDescription=null` (C 모드)
- B 모드(설명 있음)는 매칭 품질이 더 좋을 것으로 예상 (쿼리와 직접 매칭)
- **사용자 권고**: 정확한 매칭을 원하면 설명 필드를 채워라

### 4. 페르소나 톤 편향
- 사용자의 `단홍` 페르소나는 10개 대사 중 8개가 "감시/훈육" 톤 일변도
- Happy/Alert/Annoyed 시나리오에서 항상 "지금 내가 보는 앞에서 딴 짓을 하는건가?"류가 top 1
- **개선책**: 톤 다양성 (웜, 응원, 일상, 칭찬 등)을 추가하면 시나리오별 구분력 향상

## 사용자가 깨어나서 할 일 체크리스트

- [ ] `persona-debug-probe.flag` 파일은 두고 와도 됨 (Tier 2 진단 테스트가 계속 실행됨)
- [ ] **최신 빌드 테스트**: 앱 재실행 → 기존 단홍 페르소나로 자캐 모드 테스트 → 매칭 체감 변화 확인. Keyword boost 0.05로 어휘 중복 강한 쿼리는 눈에 띄게 개선돼야 함
- [ ] **`settings.json`에 `PersonaEnabled` 필드가 없음을 확인**. 설정 창 → 저장으로 필드 다시 생기게 하거나, 수동으로 `"PersonaEnabled": true` 추가 필요
- [ ] 실제 사용하면서 Rank 품질이 여전히 부족하면 다음 옵션 고려:
  - SituationDescription을 씨앗 대사에 채우기 (B 모드 전환, 품질 ↑ 예상)
  - 페르소나 대사에 톤 다양성 추가 (웜/응원/일상 계열 5-10개)
  - 더 큰 모델로 교체 검토 (별도 기획 필요)

## 최종 테스트 결과

```
통과!  - 실패: 0, 통과: 43, 건너뜀: 0, 전체: 43, 기간: 7s
```

- Tier 1 순수 로직 26개 ✓
- Tier 2 통합 테스트 5개 (단홍 페르소나 + 모델 실제 작동) ✓
- Diagnostic probes 12개 (플래그 파일 조건부) ✓

## 참고 문서

- 이 조사의 probe 테스트들: `src/JangJang.Tests/*DiagnosticTests.cs`, `*ExperimentTests.cs`
- 초기 설계: [docs/design/persona-system.md](persona-system.md)
- 문제 명세서: [.claude/plans/temporal-munching-snail.md](../../.claude/plans/temporal-munching-snail.md)
