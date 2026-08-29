# 변경 이력

## 2026-08-29 (2) — unity-cli 브릿지 안정화 + ML-Agents 보상 버그 수정 (애니메이션 교체는 계획만 세우고 중단)
- **unity-cli 브릿지 "Play 모드 진입 후 멈춤" 버그 원인 규명 + 수정**: 도메인 리로드 직후 포트 재바인딩이 실패(`AddressAlreadyInUse`)했을 때 재시도 로직이 전혀 없어서 그대로 영구 Error 상태로 남는 게 근본 원인이었음(`UnityCliBridgeHost.cs`, Editor-prev.log에서 실제 재현 로그로 확인). `StartTcpListener()`에 논블로킹 재시도(0.5초→최대 5초로 백오프, 무제한 재시도) 추가, `StopTcpListener()`의 리스너 종료 대기도 1초→3초로 연장. 기존 GetInstanceID 패치와 동일하게 `E:\Git\_tools\unity-cli`에 uncommitted 로컬 패치로 적용. **주의: 세션 중 스크립트를 여러 개 연속 수정하며 도메인 리로드가 계속 겹쳐 발생, 마지막에 브릿지가 Error 상태로 남은 채 세션 종료 — 재시도 로직 자체는 로그로 동작 확인했으나(성공적으로 재바인딩했다가 다음 리로드로 다시 끊기는 걸 반복) 최종 수정본(무제한 재시도 버전)이 실제로 안정적으로 복구되는지는 다음 세션에서 재확인 필요**.
- **ML-Agents v3에서 진단됐던 버그 2개 실제 수정** (`Assets/Scripts/Battle/CombatMLAgent.cs` + `AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab`):
  - reward hacking: `aimRewardScale`(0.003→0.0003), `preciseAimBonus`(0.002→0.0002), `coverRewardScale`(0.0006→0.0001), `distanceRewardScale`(0.0008→0.0002)로 전부 10배가량 축소 — 가만히 조준만 하고 있어도 쌓이던 보상이 실제 명중/킬/헤드샷 보상보다 항상 작아지도록
  - 피치(상하) 조준 방향 관측 누락: `aimPivot` 로컬 기준 수직 방향 성분(`localAimDir.y`)을 새 관측으로 추가해 "위/아래 어느 쪽으로 고쳐야 하는지" 명시적 신호 제공 — 관측 개수 16→17, 두 프리팹의 `BehaviorParameters.VectorObservationSize`도 같이 17로 수정
  - 헤드샷 비율 계측 신규 추가(`WeaponController.TotalHeadshots`/`HeadshotPercent`), 라운드 종료 로그(`MatchManager`)에 명중률과 같이 출력 — 목표(명중률 45~50%, 헤드샷 30%) 달성 여부를 콘솔 로그만으로 판단 가능하게 함
- **"총을 오른손에 들고 만세하는" 시각 버그 원인 조사 + 해결 계획 수립**: `HumanoidBattleAnimator`가 이 리그의 실제 로컬 축 방향 확인 없이 오일러 값을 감으로 넣은 게 원인. Kevin Iglesias 애셋 팩(`Assets/Asset/Kevin Iglesias 1/`)에는 로코모션(Idle/Walk/Run/Sprint/Turn/Jump) 클립만 있고 총기 파지 애니메이션이 없어 클립으로 직접 대체 불가 — 다리/이동은 실제 Humanoid 클립(Animator Controller + 8방향 블렌드 트리)으로, 팔은 `com.unity.animation.rigging` 패키지의 Two Bone IK로 라이플 그립 포인트(GripPointR/L)에 고정하는 방식으로 계획만 세우고 **구현은 다음 세션으로 이관**(상세 계획은 아래 log.md "다음 세션 할 일" 참고).
- 사용자 지시로 이번 세션엔 재학습(`combat_v4`)은 시작하지 않고 코드/애셋 수정만 진행 후 세션 종료.

## 2026-08-29 — 5v5 AI 전투 디테일 개선 + unity-cli 도입 + ML-Agents 실험
- **AI 전투 디테일 수정** (`AIBrain`/`MovementStepSelector`/`WeaponController`/`HumanoidBattleAnimator`/`PlayerMovement`)
  - 스트레이프 중 시야가 순간 끊겨도 곧장 이동 방향을 보지 않고 잠깐 마지막 위치를 계속 조준(`targetMemoryDuration`) — "쐈다가 홱 딴 데 보는" 버그 수정
  - 교전 자세를 정지사격/스트레이프 2종 → 정지사격/스트레이프/엄폐 이동 3종으로 확장, 이미 도착한 엄폐물은 재선택 제외(가만히 서있는 버그 수정)
  - 정찰 목적지를 엄폐물 사이 순찰로 다양화, 스트레이프에 전진/후퇴 변주 추가
  - 앉기/뛰기/옆걸음/회피 홉을 실제 기능으로 구현(`PlayerMovement.SetCrouching/SetSprinting`, `MovementStepSelector.TriggerEvadeHop`) — 다만 AIBrain에는 아직 연동 안 함(판단 로직 없음)
  - 총알 트레이서가 사수 사망/라운드 전환 시 안 지워지고 영구히 남는 버그 수정(`WeaponController.TracerFader` — 트레이서 자신이 스스로 페이드/삭제)
  - 명중률 집계 추가(`WeaponController.TotalShotsFired/TotalHits/AccuracyPercent`), 라운드 종료 시 `MatchManager`가 콘솔에 로그
- **엄폐물 배치를 180도 대칭으로 복원** (`ArenaGenerator`, 맵 52x52/엄폐물 24개/스폰 ±24) — 이전 배틀로얄 전환 커밋에 묻혀 있던 걸 재적용
- **unity-cli 도입**: akiojin/unity-cli-bridge를 Unity 6000.5용으로 로컬 패치(`GetInstanceID`→`GetEntityId`, `E:\Git\_tools\unity-cli`)해서 `file:` 패키지로 연결 — Claude Code가 Unity 에디터를 직접 조작(Play 진입/콘솔 읽기/프리팹 편집/스크린샷)할 수 있게 됨. 포트 6400이 무관한 다른 프로그램과 충돌하는 문제가 있어 `ProjectSettings/UnityCliBridgeSettings.asset`로 16400 포트로 변경
- **ML-Agents 도입(실험 진행 중)**: Python 3.10 + mlagents 1.1.0 설치(`MLAgentsTraining/venv`), `CombatMLAgent.cs` 신설 — AIBrain을 대체하는 학습형 전투 에이전트(AIBrain은 코드 유지, 붙으면 자동 비활성화). 3차례 학습(v1~v3) 진행하며 MaxStep 누락, onnxscript 의존성 문제, 보상 설계(reward hacking), 피치 조준 관측 누락 등 여러 문제를 발견/일부 수정 — 아직 명중률 목표(40%+) 미달성, 다음 세션에 보상 재조정 후 재학습 필요

## 2026-08-26 — 프로젝트 생성
- FPS Manager: Unity 신규 프로젝트 생성
- Unity 6000.5.8f1, URP(Universal Render Pipeline) 3D 템플릿으로 시작
- 버전관리: 원래 Plastic SCM으로 초기화되어 있었으나 Git으로 전환 (`github.com/ARHIENE/FPS-Manager`, public 예정)
- 상태: Unity 3D(URP) 기본 템플릿 그대로인 초기 상태(SampleScene 외 커스텀 스크립트/씬 없음), 게임 기획/구체적 기능 미정
- 같은 날 Notion 연동 설정: SAVE 시 Notion "게임 제작기 > 개발 일지"에 날짜별 페이지로 작업 내용을 기록하는 규칙 도입(전역 CLAUDE.md 반영)

## 2026-08-27 — 5v5 AI 배틀 마일스톤 1 + AI 전투 행동 결정 레이어
- 1VS1 Game의 FPS 미니게임 스크립트를 Photon 제거 + NavMeshAgent 기반으로 포팅해 5v5 AI 배틀 마일스톤 1 구현
  (`PlayerMovement`/`WeaponController`/`PlayerHealth`/`AIBrain`/`MatchManager`/`ArenaGenerator`, `AIBattle5v5` 씬, 팀별 캐릭터 프리팹)
- 헤드샷 즉사(1VS1 원본) 로직 제거 → 부위(머리/몸통)별 누적 데미지로 변경
- `NavMeshAgent.stoppingDistance`를 정찰/교전 상태별로 분리해 정찰 중 멈춰버리는 버그 수정
- 사용자가 Unity 에디터 + Unity AI로 `HumanoidBattleAnimator`(절차적 뼈대 애니메이션), `BattleHUD`(OnGUI 스코어보드/킬피드/라운드 배너), 캐릭터 모델(Kevin Iglesias Human Character Dummy)/머티리얼 직접 추가
- "교전 중 무빙이 단조롭다"는 피드백에 따라 좌우 스트레이프 + 페이크(주크) 무빙 1차 구현
- 이어서 "AI 전투 행동 결정 레이어" 요청으로 대규모 리팩터링 진행:
  - `MovementStepSelector` 신설 — "어떻게 움직이는가" 전담, 스트레이프 로직 이관, 정지사격↔이동 전환을 블렌딩으로 부드럽게 처리
  - `CombatReactionEvaluator` 신설 — 피격 시 반격/엄폐 이탈/완전 후퇴/역공격 4종에 대한 Utility AI 스코어링, 상위 2개 후보 중 점수 비례 확률로 선택(예측 불가능성 확보)
  - `PlayerCombatStats` 신설 — 클러치/포지셔닝(0~1) 최소 스탯만 구현(나머지 스탯은 추후 매니저 메타 루프에서 확장)
  - `PlayerHealth`에 `OnDamaged`(비치명타 피격) 이벤트 추가
  - `Cover` 태그 신규 등록(`TagManager.asset`) + `Cover.prefab`에 적용 — 엄폐물 탐색용
  - `AIBrain`을 "교전 중 정지사격 우선(확률 기반 planted/strafe 전환) + 피격 반응 총괄"하는 상위 결정 레이어로 재구성(클래스명은 기존 프리팹 참조 유지를 위해 `AIBrain` 그대로 사용, 요청서의 `UtilityAIBrain`으로 개명하지 않음)
- 참고: `MovementStepSelector`/`PlayerCombatStats`는 `AIBrain.Awake()`에서 없으면 런타임에 자동 부착됨 — 프리팹에 직접 붙어있지 않아도 Play 시 정상 동작(단, 인스펙터에서 개별 수치를 프리팹에 고정하고 싶다면 에디터에서 수동으로 부착 필요)
