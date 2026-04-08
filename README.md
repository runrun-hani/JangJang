# JangJang (장장) v1.0

프리랜서가 딴짓하면 화내는 데스크톱 펫.

지정한 프로그램(기본: Clip Studio Paint)에서 작업하지 않으면 점점 화를 내며, 오래 방치하면 빨개지고 커집니다.

## 기능

- **작업 감시** — 지정 프로그램의 포커스 상태를 감지 (타블렛/펜 입력 호환)
- **상태 변화** — Happy / Alert / Annoyed / Sleeping 4단계
- **화나면 커지기** — 분노도에 비례하여 펫이 점점 커짐 (ON/OFF 가능, 최대 5배)
- **색조 오버레이** — 화날수록 빨개짐 (PNG 투명 영역 보존)
- **흔들림 애니메이션** — Annoyed 상태에서 진폭 흔들림
- **세션 타이머** — 실제 작업 시간을 실시간 표시
- **작업 로그** — 일별 작업 시간 자동 기록 (worklog.json)
- **NoRest 모드** — 휴식 시간을 작업 시간에서 자동 차감
- **커스텀 이미지** — 상태별 5종 이미지 교체 가능 (기본/Happy/Alert/Annoyed/Sleeping/WakeUp)
- **유휴 시간 설정** — 엄격(5분) / 보통(10분) / 느긋(15분) / 사용자 지정
- **대상 프로그램 변경** — 실행 중인 프로그램 목록에서 선택 가능
- **자동 시작** — Windows 시작 시 자동 실행 옵션
- **Toast 알림** — 상태 변화 시 Windows 알림
- **시스템 트레이** — 트레이 아이콘으로 상태 확인, 더블클릭으로 복원
- **상태별 대사** — 랜덤 대사 출력 ("마감 언제인데", "딴짓?", "!!!" 등)

## 스크린샷

| 작업 중 | 유휴 | 화남 |
|---------|------|------|
| 😊 노란색 | 😐 주황색 | 🤬 빨간색 + 흔들림 + 커짐 |

## 설치 및 실행

### 빌드된 exe 사용
[Releases](https://github.com/runrun-hani/JangJang/releases)에서 최신 버전을 다운로드하세요. .NET 런타임 설치가 필요 없습니다.

### 소스에서 빌드
```bash
dotnet publish src/JangJang/JangJang.csproj -c Release -o publish
```

## 설정

우클릭 > 설정에서 변경할 수 있습니다.

| 항목 | 설명 |
|------|------|
| 감시 대상 프로그램 | 프로세스 이름 직접 입력 또는 목록에서 선택 |
| 유휴 시간 | 엄격(5분) / 보통(10분) / 느긋(15분) / 사용자 지정 |
| 펫 이미지 | PNG/JPG/GIF/BMP 지원, 투명 PNG 권장 (상태별 설정 가능) |
| 펫 크기 | 0.5x ~ 5.0x |
| 화나면 커지기 | ON/OFF, 최대 배율 설정 |
| NoRest 모드 | 휴식 시간 자동 차감 ON/OFF |
| 자동 시작 | Windows 시작 시 자동 실행 ON/OFF |

설정은 `%APPDATA%/JangJang/settings.json`에 저장됩니다.

## 기술 스택

- C# / .NET 7 / WPF
- Win32 P/Invoke (GetForegroundWindow, GetLastInputInfo)
- CommunityToolkit.Mvvm
- 단일 exe (Self-Contained, win-x64)

## 라이선스

MIT
