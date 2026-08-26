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

## SAVE 시 Notion 연동 (2026-08-26 도입)
- Notion "게임 제작기"(https://app.notion.com/p/334c4a0ecd3180668dbdc7d6d1aed848) 하위 "개발 일지" 페이지(https://app.notion.com/p/334c4a0ecd3181778dcaf0e6a8d57040) 아래 날짜별 하위 페이지 생성
- 형식: 제목은 날짜만(`YYYY-MM-DD`), 이모티콘은 페이지 아이콘으로만(오늘 작업 난이도/분위기 표현), 내용은 `1. 대제목` + 아래 들여쓴 상세설명
- 상세 규칙은 전역 CLAUDE.md SAVE 절차 7번 참고

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 5v5 FPS 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **아직 미구현**, 현재는 전원 동일 성능
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 임시 관전 카메라만 존재
- 개발 우선순위(기획서 7번)는 원래 ①시뮬레이션 코어(비시각) → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **사용자 판단으로 순서를 바꿔 AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 5v5 AI 배틀 — 마일스톤 1 구현 완료 (2026-08-27)
1VS1 Game 프로젝트의 `MiniGame1`(1v1 FPS 미니게임) 스크립트/프리팹을 재료로 활용. 전부 Photon PUN2에 결합돼 있었는데(이 프로젝트는 온라인 대전이 아니라 AI끼리 싸우는 로컬 시뮬레이션이라 네트워킹 불필요), Photon 계층 제거하고 순수 MonoBehaviour + NavMeshAgent 기반으로 새로 작성.

### 대상 씬 / 핵심 스크립트
- 씬: `Assets/Scenes/AIBattle5v5.unity`
- `Assets/Scripts/Battle/`:
  - `PlayerMovement.cs`: NavMeshAgent 기반 이동, 이동속도 기반 명중률 스프레드 계산, 조준(AimAt)/이동방향 바라보기(FaceMoveDirection)
  - `WeaponController.cs`: 레이캐스트 히트스캔, 부위(머리/몸통) 판정. **헤드샷만 즉사하던 1VS1 원본 로직은 제거**하고 부위 상관없이 데미지가 들어가도록 변경(`headDamage`/`bodyDamage` 분리, 현재는 값 동일 — 부위별 차등은 추후 세팅 예정)
  - `PlayerHealth.cs`: `maxHealth`/`CurrentHealth` 체력제, `ApplyDamage()`로 누적 데미지 처리, 0 이하 시 `Kill()`
  - `AIBrain.cs`: Search(정찰)/Engage(교전) 2상태 FSM. `MatchManager.GetEnemies()`로 적 조회, 라인오브사이트 레이캐스트로 시야 확인 후 교전
  - `MatchManager.cs`: 팀별 스폰, 생존자 집계, 라운드 종료/재시작(R키 또는 자동), 킬피드 이벤트
  - `ArenaGenerator.cs`: 절차적 엄폐물 배치 + NavMesh 런타임 베이크
  - `SpectatorCamera.cs`: 자유비행 관전 카메라 + 숫자키 1~0으로 특정 플레이어 시점 스냅(임시 관전 수단, 기획서 5번의 정식 매크로/1인칭 듀얼뷰 아님)
  - `HumanoidBattleAnimator.cs`, `BattleHUD.cs`: **세션 중 사용자가 Unity 에디터 + Unity AI로 직접 추가**(애니메이션/HUD) — 상세 구현은 다음 세션에서 확인 필요
- 프리팹: `Assets/Prefabs/AIPlayer.prefab`(공용), `AIPlayer_TeamA.prefab`/`AIPlayer_TeamB.prefab`(팀별, **사용자가 Kevin Iglesias Human Character Dummy 에셋으로 캡슐 placeholder를 실제 캐릭터 모델로 교체**), `Cover.prefab`
- 머티리얼: `Assets/Materials/`에 AIPlayerBody/CoverMat/GroundMat/GunMat/TrimMat/WallMat — 대부분 사용자가 직접 세팅
- `ProjectSettings/TagManager.asset`에 `Head` 태그 추가(헤드샷 판정용)

### 버그 수정 이력
- **NavMeshAgent stoppingDistance 공용 사용 버그**: 교전용 정지거리(사거리 밖에서 멈춰 사격, ~16.8)를 정찰 이동에도 그대로 써서, 적이 안 보일 때 목적지에 못 미친 채 "도착" 판정 나 정지 → 재탐색 조건(목적지 근접) 영원히 미충족 → AI가 그 자리에 멈춰버리는 문제. `AIBrain.cs`에서 Engage/Search 상태별로 `agent.stoppingDistance`를 다르게 설정하도록 수정 완료

### 다음 세션 확인할 것
- 사용자가 세션 중 Unity 에디터에서 직접 진행한 부분(HumanoidBattleAnimator/BattleHUD 상세 로직, 팀별 프리팹의 실제 캐릭터 모델/애니메이터 연결 상태, 라운드제 세부 밸런스)이 어디까지 됐는지 먼저 확인
- 정지거리 버그 수정 후 실제 정찰 이동이 정상화됐는지 재확인
- 아직 미구현: 선수 스탯 시스템(에임/반응속도/클러치/포지셔닝/팀워크), 매크로 관전 뷰, 매니저 메타 루프(영입/훈련/UI), 부위별 차등 데미지 실제 수치 세팅

## Unity AI 활용 방침 (2026-08-27 확립)
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝하는 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
