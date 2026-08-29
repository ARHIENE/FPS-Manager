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

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 5v5 FPS 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **클러치/포지셔닝만 최소 구현**(`PlayerCombatStats`), 나머지 3개는 아직 미구현
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 임시 관전 카메라만 존재
- 개발 우선순위는 원래 ①시뮬레이션 코어 → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 5v5 AI 배틀 — 현재 상태 (2026-08-30 기준, 세션 3 종료 시점)
대상 씬: `Assets/Scenes/AIBattle5v5.unity`. 스크립트: `Assets/Scripts/Battle/`

세션 3 상세 작업 내역은 `changelog.md`(2026-08-30 항목) 참고. 요약:
- **다리(로코모션) 완료**: `BattleLocomotion.controller`(Idle + Walk/Run 8방향 블렌드 트리) 3개 프리팹에 연결, `HumanoidBattleAnimator.ApplyLegAnimatorParams()`로 교체 완료. Play 모드에서 매치 정상 진행 확인(클로즈업 검증은 미완료).
- **팔 — 아직 "만세" 자세 그대로.** 라이플 메시가 프로젝트에 없어 IK 계획은 폐기(사용자 확인), "회전 상수만 재튜닝해서 팔을 앞으로 뻗게" 방향으로 확정했으나, 실측 시도 중 unity-cli 브릿지가 30분 넘게 멈춰서 코드 변경 없이 중단.
- **unity-cli 브릿지 상태 나쁨**: 도메인 리로드 후 재연결이 세션 초반엔 3분대였다가 세션 후반엔 30분+ 로 악화. `SO_REUSEADDR` 수정 1줄을 `E:\Git\_tools\unity-cli`에 추가했지만 uncommitted 상태이고 재컴파일 안 돼서 검증 못함.

### 다음 세션 할 일 (우선순위 순)

**1. unity-cli 브릿지 복구 먼저 확인**
- 세션 시작 시 `unity-cli.exe system ping`으로 상태 확인. 응답 없으면 Unity 에디터를 사용자가 직접 한 번 포커스하거나(재컴파일 유도), 그래도 안 되면 에디터 완전 재시작(좀비 소켓 정리)부터 할 것.
- 브릿지 복구되면 `E:\Git\_tools\unity-cli`의 uncommitted `SO_REUSEADDR` 수정이 실제로 재연결 속도를 개선하는지 확인.

**2. 팔 자세 실측 튜닝 (방향 확정됨: 라이플 없이, 팔을 앞으로 뻗는 자세)**
- `HumanoidBattleAnimator.ApplyAimAndWeaponPose()`의 `upperArmR/L`/`forearmR/L`/`handR/L` 회전 상수 재작업. 리그의 실제 로컬 축 방향을 스크린샷으로 실측하며 값 조정(추측으로 넣지 말 것 — 이게 원래 "만세" 버그 원인).
- 스파인/체스트/헤드 피치(조준 상하) 코드는 그대로 유지.

**3. 다리 애니메이션 클로즈업 검증**
- 캐릭터에 카메라 근접시켜 Walk/Run 블렌드가 실제로 자연스러운지 육안 확인. 부자연스러우면 블렌드 트리 반경(현재 Walk=4.2, Run=6.3)이나 클립 매핑 재조정.

**4. 재학습 (combat_v4)**
- 팔 자세 해결 후, `MLAgentsTraining/venv` 활성화 후 `mlagents-learn trainer_config.yaml --run-id=combat_v4` 실행 (Unity Editor Play 모드로 연결).
- **주의**: `AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab`의 `BehaviorParameters.m_BehaviorType`이 현재 `2`(Inference Only, 고정 onnx 모델 사용)로 설정되어 있음 — 학습을 시작하려면 이걸 `0`(Default, 외부 트레이너 연결)으로 바꿔야 함. 학습 끝나면 다시 `2`로 바꾸고 `m_Model`을 새 onnx로 교체.
- 목표: 라운드 종료 로그(`[MatchManager] 누적 명중률.../헤드샷 비율...`) 기준 명중률 45~50%, 헤드샷 비율 30% 근접. 미달 시 보상 스케일/하이퍼파라미터 추가 조정 후 `combat_v5`로 반복.

**5. 기존 백로그(아직 미해결)**
- 앉기/뛰기/옆걸음/회피 홉을 `AIBrain`(핸드튜닝) 판단 로직에 연동할지 결정 필요(지금은 기능만 있고 아무도 안 씀)
- 밸런스 튜닝값(엄폐 활용 확률, 정찰 순찰 확률 등) 전부 임시값 — Play해보고 체감 튜닝 필요
- 아직 미구현: 에임/반응속도/팀워크 스탯, 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI)

## unity-cli (Claude Code의 Unity 에디터 직접 조작)
- **로컬 패치본 사용 중**: 공식 `akiojin/unity-cli-bridge` 패키지는 Unity 6000.5의 `GetInstanceID()` deprecation 때문에 컴파일 안 됨(업스트림 [이슈 #231](https://github.com/akiojin/unity-cli/issues/231) 미해결) → `E:\Git\_tools\unity-cli`에 클론해서 7군데 `GetInstanceID()`→`GetEntityId().GetHashCode()`로 직접 패치, `Packages/manifest.json`에서 `file:` 경로로 참조
- **포트 16400 사용** (기본값 6400은 이 PC에서 무관한 다른 프로그램과 충돌해서 `ProjectSettings/UnityCliBridgeSettings.asset`로 변경) — CLI 사용 시 `UNITY_CLI_PORT=16400` 환경변수 필요
- **CLI 바이너리**: `~/.local/bin/unity-cli.exe`, 사용 시 `UNITY_PROJECT_ROOT`도 프로젝트 경로로 설정
- **"Play 모드 진입 후 자주 멈춤" / "도메인 리로드 후 브릿지 재연결 느림" 버그 — 여전히 미해결, 세션 3 내에서도 악화 추세.** 세션 2에서 `StartTcpListener()`에 무제한 재시도(백오프)를 추가해 영구 정지는 해결됐지만, 재연결 소요 시간이 세션 3 초반엔 3분대(재시도 40회+, 자연 회복)였다가 후반엔 **30분 넘게 복구 안 되는 상태**로 악화됨. `Get-NetTCPConnection -LocalPort 16400`으로 보면 그동안 Unity 프로세스 자신이 포트를 `Listen`+`CloseWait` 두 상태로 동시에 물고 있음(단순 TIME_WAIT보다 오래 걸림, 좀비 소켓). 세션 3 후반에 `StartTcpListener()`에 `SO_REUSEADDR` 소켓 옵션 추가 시도했으나 **uncommitted 상태이고, 브릿지가 죽어있어 재컴파일을 트리거할 방법이 없어서 실제 반영/검증을 못함** — 다음 세션에서 에디터 재시작 후 이 수정이 컴파일되는지, 재연결 속도가 개선되는지부터 확인할 것. 이 지연 동안에도 Unity 에디터 GUI 자체는 항상 정상 동작(죽는 건 CLI 자동화 채널뿐) — 급하면 메뉴를 에디터에서 직접 클릭해 우회 가능. **주의**: 재연결 시도 중 에디터 창에 강제로 포커스를 주려는 시도는 사용자가 데스크톱을 다른 용도로 쓰고 있을 수 있어 위험 — 자동화로 강제 포커스 전환/입력 주입 시도하지 말 것, 필요하면 사용자에게 직접 클릭해달라고 요청할 것.

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
- unity-cli 도입 후로는 Claude Code가 Play 진입/콘솔 확인/프리팹 편집/스크린샷까지 직접 할 수 있게 됨 — 다만 브릿지 안정성 이슈(위 참고) 때문에 여전히 사용자 확인이 필요할 때가 있었음(2026-08-29 세션 2에 수정 시도)
