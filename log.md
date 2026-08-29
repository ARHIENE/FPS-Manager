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

## 5v5 AI 배틀 — 현재 상태 (2026-08-29 기준, 세션 2)
대상 씬: `Assets/Scenes/AIBattle5v5.unity`. 스크립트: `Assets/Scripts/Battle/`

### 이번 세션에 완료한 것
1. **unity-cli 브릿지 안정화 코드 수정** — `E:\Git\_tools\unity-cli\UnityCliBridge\Packages\unity-cli-bridge\Editor\Core\UnityCliBridgeHost.cs`에 재시도 로직 추가(무제한 재시도, 0.5초→최대 5초 백오프). uncommitted 로컬 패치(기존 GetInstanceID 패치와 동일 파일 위치, git status로 diff 확인 가능). **다음 세션 최우선 확인 사항: Editor.log에서 `[unity-cli-bridge] Status changed to: Connected`(또는 최소 `Disconnected`로 안정)가 나오는지, `unity-cli.exe system ping`이 정상 응답하는지 먼저 확인할 것** — 이번 세션 종료 시점엔 스크립트를 연속으로 여러 번 고치며 도메인 리로드가 겹쳐 발생해서 Error 상태로 끝남(재시도 자체는 로그로 동작 확인됨).
2. **ML-Agents v3 보상/관측 버그 수정 완료** (`CombatMLAgent.cs` + `AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab` 둘 다 반영):
   - `aimRewardScale` 0.003→0.0003, `preciseAimBonus` 0.002→0.0002, `coverRewardScale` 0.0006→0.0001, `distanceRewardScale` 0.0008→0.0002 (reward hacking 방지)
   - `CollectObservations()`에 `aimPivot` 로컬 기준 수직 방향 성분(`localAimDir.y`) 관측 추가 — 관측 개수 16→17, 두 프리팹의 `BehaviorParameters.VectorObservationSize`도 17로 수정 완료
   - `WeaponController.TotalHeadshots`/`HeadshotPercent` 신규 추가, `MatchManager` 라운드 종료 로그에 헤드샷 비율도 같이 출력
3. **"만세" 자세 버그 원인 파악 + 해결 계획 수립(구현은 다음 세션)** — 아래 "다음 세션 할 일 1" 참고

### 다음 세션 할 일 (우선순위 순)

**1. 캐릭터 애니메이션 교체 (계획 확정, 구현 전 단계에서 중단됨)**

원인: `HumanoidBattleAnimator.ApplyAimAndWeaponPose()`가 `upperArmR/L`에 `Quaternion.Euler(60f,-25f,-15f)` 같은 값을 이 리그의 실제 로컬 축 방향 확인 없이 넣고 있어서 팔이 위로 들림(리깅 시 로컬 축이 일반적인 가정과 다르게 잡혀 있음). Kevin Iglesias 애셋 팩(`Assets/Asset/Kevin Iglesias 1/Human Animations/`)에는 Idle/Walk/Run/Sprint/Turn/Jump 계열 로코모션 클립만 있고 총기 파지/조준 애니메이션은 없음 — 클립으로 직접 대체 불가능.

확정된 해결 방향:
- **다리/이동**: `Assets/Animation/Battle/` 폴더 신설, `BattleLocomotion.controller`(Animator Controller) 생성 — Idle 상태 + Walk/Run 2D Freeform Directional 블렌드 트리(파라미터 `MoveX`/`MoveZ`), 소스 클립은 `Assets/Asset/Kevin Iglesias 1/Human Animations/Animations/Male/Movement/Walk|Run/`의 8방향 클립(RootMotion 아닌 일반 버전, Apply Root Motion 끔 — 위치는 지금처럼 NavMeshAgent가 담당). 프리팹(`AIPlayer.prefab`/`AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab`)의 `HumanDummy` 자식에 이미 있는 `Animator` 컴포넌트(현재 `m_Controller` 비어있음, `fileID: 0`)에 연결. `HumanoidBattleAnimator.ApplyLegAnimation()`(본 직접 회전) 삭제하고 대신 `agent.velocity`를 로컬 좌표로 변환해 `MoveX`/`MoveZ` 파라미터로 넘기는 짧은 메서드로 교체.
- **팔(라이플 파지)**: `com.unity.animation.rigging` 패키지 추가(`Packages/manifest.json`, 아직 미설치). 라이플 프리팹(`TacticalRifle`, `B-hand.R/B-handProp.R` 하위)에 그립 포인트 빈 오브젝트 2개(`GripPointR`: 트리거 근처, `GripPointL`: 전방 손잡이) 추가 — 위치는 unity-cli 스크린샷으로 눈으로 보면서 조정. `HumanDummy` 하위에 `Rig Builder` + `Rig` + 좌/우 `Two Bone IK Constraint`(Root=upperArm, Mid=forearm, Tip=hand, Target=해당 GripPoint) 추가. `ApplyAimAndWeaponPose()`에서 `upperArmR/L`/`forearmR/L`/`handR/L` 직접 회전 코드 제거(IK가 대체), 스파인/체스트/헤드 피치(조준 상하)는 기존처럼 유지.
- 리그는 이미 Humanoid로 세팅되어 있고 Kevin Iglesias 애셋의 뼈 이름 규칙(`B-hips`/`B-shoulder.L/R` 등)과 프리팹이 정확히 일치함 — 리타겟팅 문제 없음.
- 검증: Play 모드 진입 후 unity-cli 스크린샷으로 양손이 자연스럽게 라이플을 쥐는지, 다리가 실제 클립으로 걷는지 육안 확인. 그립 포인트 위치는 반복 조정 필요할 가능성 높음.
- 이 작업은 unity-cli 브릿지가 안정적으로 붙어 있어야 스크린샷으로 눈으로 보면서 반복 조정 가능 — **위 "1번" 브릿지 확인을 먼저 하고 시작할 것**.

**2. 재학습 (combat_v4)**
- 1번(애니메이션) 완료 후, 위 보상/관측 수정이 반영된 상태로 `MLAgentsTraining/venv` 활성화 후 `mlagents-learn trainer_config.yaml --run-id=combat_v4` 실행 (Unity Editor Play 모드로 연결).
- **주의**: `AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab`의 `BehaviorParameters.m_BehaviorType`이 현재 `2`(Inference Only, 고정 onnx 모델 사용)로 설정되어 있음 — 학습을 시작하려면 이걸 `0`(Default, 외부 트레이너 연결)으로 바꿔야 함. 학습 끝나면 다시 `2`로 바꾸고 `m_Model`을 새 onnx로 교체.
- 목표: 라운드 종료 로그(`[MatchManager] 누적 명중률.../헤드샷 비율...`) 기준 명중률 45~50%, 헤드샷 비율 30% 근접. 미달 시 보상 스케일/하이퍼파라미터 추가 조정 후 `combat_v5`로 반복.
- 사용자 지시로 이번 세션엔 시작하지 않음 — 애니메이션 작업 마친 뒤 진행.

**3. 기존 백로그(아직 미해결)**
- 앉기/뛰기/옆걸음/회피 홉을 `AIBrain`(핸드튜닝) 판단 로직에 연동할지 결정 필요(지금은 기능만 있고 아무도 안 씀)
- 밸런스 튜닝값(엄폐 활용 확률, 정찰 순찰 확률 등) 전부 임시값 — Play해보고 체감 튜닝 필요
- 아직 미구현: 에임/반응속도/팀워크 스탯, 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI)

## unity-cli (Claude Code의 Unity 에디터 직접 조작)
- **로컬 패치본 사용 중**: 공식 `akiojin/unity-cli-bridge` 패키지는 Unity 6000.5의 `GetInstanceID()` deprecation 때문에 컴파일 안 됨(업스트림 [이슈 #231](https://github.com/akiojin/unity-cli/issues/231) 미해결) → `E:\Git\_tools\unity-cli`에 클론해서 7군데 `GetInstanceID()`→`GetEntityId().GetHashCode()`로 직접 패치, `Packages/manifest.json`에서 `file:` 경로로 참조
- **포트 16400 사용** (기본값 6400은 이 PC에서 무관한 다른 프로그램과 충돌해서 `ProjectSettings/UnityCliBridgeSettings.asset`로 변경) — CLI 사용 시 `UNITY_CLI_PORT=16400` 환경변수 필요
- **CLI 바이너리**: `~/.local/bin/unity-cli.exe`, 사용 시 `UNITY_PROJECT_ROOT`도 프로젝트 경로로 설정
- **"Play 모드 진입 후 자주 멈춤" 버그 — 2026-08-29 세션 2에 원인 규명 + 수정 코드 작성 완료(실제 안정 동작은 다음 세션에서 재확인 필요)**: 근본 원인은 `UnityCliBridgeHost.cs`의 `StartTcpListener()`가 도메인 리로드 직후 포트 재바인딩 실패(`AddressAlreadyInUse`) 시 재시도 없이 그냥 포기하고 영구 Error 상태로 남는 것. 논블로킹 재시도(백오프, 무제한 재시도)로 수정. 이전에 알려진 대응법(창 포커스 후 Enter, 프로세스 강제 종료)은 이제 불필요해야 하지만 다음 세션에서 실사용 검증 전까지는 백업으로 알아둘 것.

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
- unity-cli 도입 후로는 Claude Code가 Play 진입/콘솔 확인/프리팹 편집/스크린샷까지 직접 할 수 있게 됨 — 다만 브릿지 안정성 이슈(위 참고) 때문에 여전히 사용자 확인이 필요할 때가 있었음(2026-08-29 세션 2에 수정 시도)
