# 변경 이력

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
