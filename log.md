# 프로젝트 로그

## 개요
- FPS Manager: FM(풋볼매니저)류 팀 운영 루프 + AI가 대신 싸우는 5v5 FPS 이스포츠 매니지먼트 게임
- 프로젝트 루트(작업 디렉토리): `E:\Git\Fps Manager`
- Unity 6000.5.8f1, URP(Universal Render Pipeline)
- 버전관리: Git/GitHub (`github.com/ARHIENE/FPS-Manager`, public)
- 기획 원본: Notion "게임 제작기 > 기획(스펙 문서)" 페이지 참고

## Git 브랜치 전략 (1VS1 Game과 동일)
- `master`: 실제 작업 브랜치. 프로젝트 전체 파일 포함, **README.md 없음**
- `main`: GitHub 기본 브랜치. **README.md만 관리**(프로젝트 전체 파일 없음), 저장소 메인 페이지 노출용
- 두 브랜치는 공통 조상이 없는 별개 히스토리
- **SAVE 명령 시 main의 README.md도 그날 작업 반영해 최신화할 것** (전역 CLAUDE.md 규칙)

## SAVE 시 Notion 연동
- Notion "게임 제작기"(https://app.notion.com/p/334c4a0ecd3180668dbdc7d6d1aed848) 하위 "개발 일지" 페이지(https://app.notion.com/p/334c4a0ecd3181778dcaf0e6a8d57040) 아래 날짜별 하위 페이지 생성
- 형식: 제목은 날짜만(`YYYY-MM-DD`), 이모티콘은 페이지 아이콘으로만(오늘 작업 난이도/분위기 표현), 내용은 `1. 대제목` + 아래 들여쓴 상세설명
- 상세 규칙은 전역 CLAUDE.md SAVE 절차 7번 참고
- **ML-Agents 전용 페이지**(https://app.notion.com/p/3cbc4a0ecd3180428949e9aac23767a9): SAVE 시 이 세션에서 ML-Agents 작업이 있었다면 위 개발일지와 별도로 이 페이지에 진행 상황 정리. 형식은 개발일지와 동일(이모티콘/날짜/`1. 대제목` 구조)하되 내용은 진행률(현재 스텝/목표 스텝), 성능 체감(평균 보상 추이, 관찰된 행동 변화), 이번 세션에 바뀐 보상/관측/설정 위주로 정리
- **스크린샷 첨부**: unity-cli(`E:\Git\_tools\unity-cli`, 로컬 패치본)로 Unity 에디터 Game/Scene 뷰 스크린샷 캡처 가능(`capture_screenshot` 툴, `.unity/capture/`에 저장, git에는 안 올림) — 개발일지/ML-Agents 페이지 작성 시 관련 스크린샷을 같이 캡처해서 첨부할 것
- **PlayMCP(카카오톡) 알림**: 사용자에게 카카오톡 메시지 발송 가능(`mcp__claude_ai_PlayMCP__KakaotalkChat-MemoChat`, 최대 200자). 의미있는 작업(멀티스텝/코드 수정/빌드/학습 등) 완료 시 완료 내용+문제 발생 여부 요약해서 발송할 것(전역 CLAUDE.md 규칙, 2026-08-30 세션 5에 추가)

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 5v5 FPS 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **클러치/포지셔닝만 최소 구현**(`PlayerCombatStats`), 나머지 3개는 아직 미구현
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 임시 관전 카메라만 존재
- 개발 우선순위는 원래 ①시뮬레이션 코어 → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 5v5 AI 배틀 — 현재 상태 (2026-08-30 기준, 세션 5 종료 시점)
대상 씬: `Assets/Scenes/AIBattle5v5.unity`. 스크립트: `Assets/Scripts/Battle/`

세션 5 상세 작업 내역은 `changelog.md`(2026-08-30 (3) 항목) 참고. 요약:
- **unity-cli 브릿지 "좀비 소켓" 원인 규명 + 수정 완료, 커밋함.** 도메인 리로드 재바인딩 실패(`AddressAlreadyInUse`)가 원인이었고 `SO_REUSEADDR`+재시도 백오프로 해결, 실측 검증(11.8초 만에 정상 재바인딩)까지 마침.
- **다만 별개 문제 남음**: Play 모드로 무거운 시뮬레이션이 돌 때 브릿지가 자주 응답 없음 상태가 됨(원인 미확정, 리소스 경합 추정). 세션 내내 Unity 강제 재시작을 4번 함 — taskkill 후 재실행하면 100~150초 내 안정적으로 복구는 됨.
- **combat_v5 학습 완주, 그러나 구조적 결함 발견**: 4,000,000 스텝 완주(`MLAgentsTraining/results/combat_v5/CombatAgent.onnx`), 순간 명중률 여전히 ~9~10%(목표 45~50% 미달). 원인: `AIPlayer_TeamA/B.prefab`의 Agent `MaxStep = 3000` — 교전이 안 풀리면 무조건 `stepPenalty×3000 = -1.5`로 수렴하는 구조라, 보상 스케일만 조정해서는 근본적으로 못 고침(아래 "다음 세션 할 일" 1번 참고). 프리팹엔 미적용, `BehaviorType`은 2(Inference Only)로 원복, 기존 모델 유지.
- **팔 자세 — 이번 세션도 손 못 댐.** 학습 중엔 재컴파일이 트레이너 연결을 끊기 때문. 방향(라이플 없이 팔을 앞으로 뻗는 자세, 실측 기반 회전 상수 재조정)은 그대로 유효.
- **PlayMCP(카카오톡) 연동 정상 작동 확인.** "의미있는 작업 완료 시 카톡 알림" 규칙을 전역 CLAUDE.md에 추가함.

### 다음 세션 할 일 (우선순위 순)

**1. combat_v5 MaxStep 타임아웃 구조 문제 — 최우선**
- `AIPlayer_TeamA/B.prefab`의 Agent `MaxStep`(현재 3000)이 문제. 교전이 안 풀리면(원인 불문) 항상 `stepPenalty(-0.0005)×MaxStep`로 수렴 — 게다가 이 타임아웃 페널티(-1.5)가 그냥 죽는 것(대략 -1~-1.2)보다 나빠서, "죽는 게 오히려 덜 나쁜 선택"으로 학습될 잠재적 역유인 문제도 있음.
- (a) 타임아웃 시 처리 방식(명시적 페널티 축소/조정 or MaxStep 값 자체 조정) 재설계 (b) "애초에 왜 교전이 안 풀리는지"(탐지 범위/LOS 로직/스폰 위치/NavMesh 이동) 근본 조사 — 세션 5에서 시도한 미세 보상 조정(missPenalty, explorationRewardScale)만으로는 3번 다 재발했음, 구조적으로 접근할 것.
- 현재 결과물(combat_v5 onnx)은 이 결함을 안은 채 나온 것이므로 프로덕션에 쓰지 말고, 위 문제부터 고치고 v6로 재도전.

**2. unity-cli 브릿지 — Play 모드 중 응답 없음 현상 조사**
- 도메인 리로드 케이스는 해결됐으니, 남은 건 "무거운 시뮬레이션 중 간헐적 응답 없음". 재현 조건(부하 크기? 특정 명령?)을 좁혀서 진짜 버그인지 그냥 리소스 경합인지 판단.
- Play 모드 진입 전에 `mlagents-learn`을 먼저 띄워야 한다는 점, 좀비 python 워커가 포트 5004를 점유할 수 있다는 점은 계속 유효.

**3. 팔 자세 실측 튜닝 (방향 확정됨: 라이플 없이, 팔을 앞으로 뻗는 자세) — 학습이 안 도는 동안에만 가능**
- `HumanoidBattleAnimator.ApplyAimAndWeaponPose()`의 `upperArmR/L`/`forearmR/L`/`handR/L` 회전 상수 재작업. 리그의 실제 로컬 축 방향을 스크린샷으로 실측하며 값 조정(추측으로 넣지 말 것).
- 스파인/체스트/헤드 피치(조준 상하) 코드는 그대로 유지.

**4. 다리 애니메이션 클로즈업 검증**
- 캐릭터에 카메라 근접시켜 Walk/Run 블렌드가 실제로 자연스러운지 육안 확인. 부자연스러우면 블렌드 트리 반경(현재 Walk=4.2, Run=6.3)이나 클립 매핑 재조정.

**5. 기존 백로그(아직 미해결)**
- 앉기/뛰기/옆걸음/회피 홉을 `AIBrain`(핸드튜닝) 판단 로직에 연동할지 결정 필요(지금은 기능만 있고 아무도 안 씀)
- 밸런스 튜닝값(엄폐 활용 확률, 정찰 순찰 확률 등) 전부 임시값 — Play해보고 체감 튜닝 필요
- 아직 미구현: 에임/반응속도/팀워크 스탯, 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI)

## unity-cli (Claude Code의 Unity 에디터 직접 조작)
- **로컬 패치본 사용 중**: 공식 `akiojin/unity-cli-bridge` 패키지는 Unity 6000.5의 `GetInstanceID()` deprecation 때문에 컴파일 안 됨(업스트림 [이슈 #231](https://github.com/akiojin/unity-cli/issues/231) 미해결) → `E:\Git\_tools\unity-cli`에 클론해서 6군데 `GetInstanceID()`→`GetEntityId().GetHashCode()`로 직접 패치(커밋 완료), `Packages/manifest.json`에서 `file:` 경로로 참조
- **포트 16400 사용** (기본값 6400은 이 PC에서 무관한 다른 프로그램과 충돌해서 `ProjectSettings/UnityCliBridgeSettings.asset`로 변경) — CLI 사용 시 `UNITY_CLI_PORT=16400` 환경변수 필요
- **CLI 바이너리**: `~/.local/bin/unity-cli.exe`, 사용 시 `UNITY_PROJECT_ROOT`도 프로젝트 경로로 설정
- **"좀비 소켓"(도메인 리로드 후 포트 재바인딩 실패) 버그는 세션 5에서 원인 규명 + 수정 + 검증 + 커밋 완료.** `StartTcpListener()`에 `SO_REUSEADDR` 소켓 옵션 + 무제한 재시도(0.5초→최대 5초 백오프) 추가. 실측으로 도메인 리로드 후 11.8초 만에 에러 없이 재바인딩되는 것 확인.
- **남은 문제**: Play 모드로 무거운 시뮬레이션(ML-Agents 학습 등)이 돌 때 브릿지가 간헐적으로 응답 없음 상태가 됨 — 위와는 별개 증상, 원인 미확정(리소스 경합 추정). 재현 조건을 좁혀서 조사 필요(다음 세션 할 일 2번 참고). 이 경우 taskkill로 Unity 프로세스 강제 종료 후 재실행하면 100~150초 내 안정적으로 복구됨.
- **주의**: 사용자가 자리에 없을 때는 에디터 창에 강제로 포커스를 주거나 입력을 주입하지 말 것(자동화로 우회 불가능한 조작이 필요하면 taskkill 후 재시작으로 처리). 사용자가 있을 때는 필요시 직접 클릭해달라고 요청 가능.

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
- unity-cli 도입 후로는 Claude Code가 Play 진입/콘솔 확인/프리팹 편집/스크린샷까지 직접 할 수 있게 됨 — 다만 브릿지 안정성 이슈(위 참고) 때문에 여전히 사용자 확인이 필요할 때가 있었음
