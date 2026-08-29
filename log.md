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

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 5v5 FPS 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **클러치/포지셔닝만 최소 구현**(`PlayerCombatStats`), 나머지 3개는 아직 미구현
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 임시 관전 카메라만 존재
- 개발 우선순위는 원래 ①시뮬레이션 코어 → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 5v5 AI 배틀 — 현재 상태 (2026-08-27 기준)
대상 씬: `Assets/Scenes/AIBattle5v5.unity`. 스크립트: `Assets/Scripts/Battle/`

### 계층 구조
- **`AIBrain`** — 상위 결정 레이어. 적 탐색(`FindNearestVisibleEnemy`/`HasLineOfSight`), 정찰↔교전 상태 전환, 교전 중 "정지사격 우선" 원칙(확률 기반으로 정지/스트레이프 자세를 주기적으로 전환), 피격 시 반응 결정(`HandleDamaged`)까지 총괄
- **`MovementStepSelector`** — "어떻게 움직이는가"만 담당하는 하위 레이어. 좌우 스트레이프(+가끔 페이크 전환), 임의 지점 이동(`TickTowards`, 정찰/엄폐/후퇴 공용), 정지↔이동 전환 블렌딩(뚝 끊기지 않게 서서히 감속/가속). `AIBrain`이 없으면 런타임에 자동 부착(프리팹에 직접 안 붙어 있어도 Play 시 정상 동작)
- **`CombatReactionEvaluator`** — static 유틸(PK/오목의 스코어러 컨벤션과 동일). 피격 시 반격/엄폐 이탈/완전 후퇴/역공격 4종에 대해 체력·공격자 특정 여부·엄폐물 유무·클러치·포지셔닝으로 점수 계산, 상위 2개 후보 중 점수 비례 확률로 선택(예측 불가능성 확보)
- **`PlayerCombatStats`** — 클러치/포지셔닝(0~1) 필드만 있는 최소 스탯 컴포넌트. `AIBrain`이 없으면 자동 부착
- **`PlayerHealth`** — `OnDeath`/`OnDeathWithAttacker`(즉사) 외에 **`OnDamaged`**(비치명타 피격) 이벤트 추가됨. `AIBrain.HandleDamaged`가 이걸 구독해 반응 결정 트리거
- **`WeaponController`** — 부위별(머리/몸통) 누적 데미지, 이동 속도 기반 탄퍼짐(스프레드) — `PlayerMovement.GetCurrentSpread()`가 계산
- **`MatchManager`** — 5v5 스폰, 라운드제(3선승 아님, 팀 전멸 시 라운드 종료 + 자동 다음 라운드), 킬피드/배너 이벤트
- **`ArenaGenerator`** — 절차적 엄폐물 배치 + NavMesh 런타임 베이크. 엄폐물 프리팹(`Cover.prefab`)에 **`Cover` 태그**를 새로 등록해 적용함(엄폐물 탐색용)

### 이번 세션에 새로 확인/변경된 것
- 이전 세션 "다음 세션 확인" 항목 전부 확인 완료: `HumanoidBattleAnimator`(절차적 뼈대 애니메이션)·`BattleHUD`(OnGUI UI) 둘 다 완성 상태였음, stoppingDistance 버그 수정도 정상 반영 확인
- 교전 중 무빙이 단조롭다는 피드백 → 좌우 스트레이프 1차 구현 → 이후 "AI 전투 행동 결정 레이어" 요청으로 위 계층 구조 전체 리팩터링

### 다음 세션 확인/할 일
- **Unity 에디터에서 Play 테스트 필요** — 이번 세션 변경사항(정지사격 오가는 자세 전환, 피격 반응 4종, 스트레이프)은 코드 작성만 하고 실제 플레이 검증은 못 함
- `attackerKnown` 판정이 `detectRadius`(40) 이내 라인오브사이트로만 되어 있어서, 그보다 먼 거리에서 저격당하면 무조건 "위치 특정 안 됨"으로 처리됨(엄폐 선호 쪽으로 치우침) — 체감상 이상하면 조정 필요
- `nearbyEnemyCount`가 1v1 프로토타입 단순화로 항상 1 고정 — 5v5 전체 난전 상황에서 근처 교전 인원을 실제로 세도록 확장 필요
- `HumanoidBattleAnimator`의 다리 애니메이션이 이동 속도만 보고 전진 걷기 사이클을 재생 — 스트레이프/후퇴처럼 옆·뒤로 이동할 때도 전진 걷기처럼 보일 수 있음(방향 인지 안 됨)
- 밸런스 튜닝값(`AIBrain`의 `plantChance`, 반응 지속시간, `MovementStepSelector`의 스트레이프 반경/주기 등) 전부 임시값 — Play해보고 체감 튜닝 필요
- 아직 미구현: 에임/반응속도/팀워크 스탯, 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI), 부위별 데미지 실제 수치 차등(현재 head=50/body=25로 이미 다르지만 밸런스 미검증)

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
