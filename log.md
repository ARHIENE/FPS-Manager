# 프로젝트 로그

## 개요
- FPS Manager: FM(풋볼매니저)류 팀 운영 루프 + AI가 대신 싸우는 **배틀로얄(4인 팀 × 20팀, 총 80명) FPS** 이스포츠 매니지먼트 게임
- 프로젝트 루트(작업 디렉토리): `E:\Git\Fps Manager`
- Unity 6000.5.8f1, URP(Universal Render Pipeline)
- 버전관리: Git/GitHub (`github.com/ARHIENE/FPS-Manager`, public)
- 기획 원본: Notion "게임 제작기 > 기획(스펙 문서)" 페이지 참고 (2026-08-27 파트별 하위 페이지 9개로 분리됨, 아래 참고)

## Git 브랜치 전략 (1VS1 Game과 동일)
- `master`: 실제 작업 브랜치. 프로젝트 전체 파일 포함, **README.md 없음**
- `main`: GitHub 기본 브랜치. **README.md만 관리**(프로젝트 전체 파일 없음), 저장소 메인 페이지 노출용
- 두 브랜치는 공통 조상이 없는 별개 히스토리
- **SAVE 명령 시 main의 README.md도 그날 작업 반영해 최신화할 것** (전역 CLAUDE.md 규칙)

## SAVE 시 Notion 연동
- Notion "게임 제작기"(https://app.notion.com/p/334c4a0ecd3180668dbdc7d6d1aed848) 하위 "개발 일지" 페이지(https://app.notion.com/p/334c4a0ecd3181778dcaf0e6a8d57040) 아래 날짜별 하위 페이지 생성
- 형식: 제목은 날짜만(`YYYY-MM-DD`), 이모티콘은 페이지 아이콘으로만(오늘 작업 난이도/분위기 표현), 내용은 `1. 대제목` + 아래 들여쓴 상세설명
- 상세 규칙은 전역 CLAUDE.md SAVE 절차 7번 참고
- **(2026-08-28 신설, 전역 CLAUDE.md 규칙)** 스펙/요구사항이 바뀌면 Notion 기획 문서에서 옛 내용을 남겨두고 추가하지 말고, 옛 내용을 지우고 새 내용으로 덮어쓴다(추가가 아니라 교체)

## Notion 기획 문서 구조 (2026-08-27 파트 분리, 2026-08-28 배틀로얄 반영)
"기획(스펙 문서)" 페이지(https://app.notion.com/p/334c4a0ecd3180c4a796e5220302a0bd)는 목차 역할만 하고, 아래 9개 하위 페이지에 실제 내용이 있음: 1.컨셉 / 2.핵심 루프 / 3.선수 시스템 / 4.전술 시스템 / 5.경기 시뮬레이션 관전 방식 / 6.UI-UX 개요 / 7.개발 우선순위 / 8.미결정 사항 / **9.배틀로얄 모드(신규)**. 1/5/7번은 배틀로얄 전환에 맞춰 "5v5"/"2팀" 서술을 교체 완료.

## 기획 요약 (Notion 기획서 기준)
- 플레이어는 직접 조작하지 않음 — 선수 영입/훈련 후 AI가 배틀로얄 경기를 대신 치름
- 핵심 루프: 영입/스카우팅 → 훈련/성장 → 경기 시뮬레이션(관전) → 결과/성장 반영
- 선수 스탯(1차 범위): 에임/반응속도/클러치/포지셔닝/팀워크 — **클러치/포지셔닝만 최소 구현**(`PlayerCombatStats`), 나머지 3개는 아직 미구현
- 관전은 매크로(탑뷰) + FPS 1인칭 두 시점 모두 필요(기획서 5번) — 현재는 정식 아키텍처 아닌 관전 카메라(자유비행 + 팀/팀원 순환 빙의)만 존재
- 개발 우선순위는 원래 ①시뮬레이션 코어 → ②매크로 뷰 → ③FPS 1인칭 뷰였으나, **AI가 실제로 움직이며 싸우는 전투 자체를 먼저 구현**하기로 함(매니저 메타 루프·스탯 시스템은 이후 단계)

## 배틀로얄 AI 배틀 — 현재 상태 (2026-08-28 기준)
대상 씬: `Assets/Scenes/AIBattle5v5.unity` (이름은 5v5 시절 그대로지만 내용은 배틀로얄로 완전 전환됨 — 리네임 여부는 보류, 필요시 다음 세션에 판단). 스크립트: `Assets/Scripts/Battle/`

### 계층 구조
- **`TerrainGenerator`** — 펄린 노이즈 기반 랜덤 지형 생성(기본 300×300). `randomizeSeedOnStart`(기본 켜짐)로 Play할 때마다 다른 지형. 생성 직후 NavMesh 자동 베이크 → `MatchManager.BeginMatch()` 순서로 연동. 엄폐물(`SpawnObject.isCover` 체크)엔 Cover 태그 + NavMeshObstacle 자동 부착
- **`BattleRoyaleSpawner`** — 20개 팀 클러스터를 최소 거리 제약으로 랜덤 배치, 클러스터당 4명 분산, NavMesh 검증
- **`MatchManager`** — `List<BattleRoyaleTeam>`(N팀) 관리. 팀 전멸 시 `OnTeamEliminated`, 최후 1팀 생존 시 `OnMatchEnded`. 80명 규모 성능 대응으로 공간 분할 그리드(20m 셀, 0.15초 재구성) 보유, `GetNearbyEnemies`로 AI 탐지 후보 제한
- **`ZoneManager`** — 자기장(세이프존). 6단계 기본 단계표(대기/축소시간, 축소비율, 데미지 점증), `MatchManager.OnMatchStarted` 이벤트로 자동 시작. 반투명 실린더(현재 원)+흰 링(다음 원) 시각화
- **`AIBrain`** — 상위 결정 레이어. 그리드 기반 적 탐지(0.15초 스로틀), 정지사격 우선, 피격 반응 4종(`CombatReactionEvaluator`), **자기장 위급도(`zoneUrgency`) 계산**해서 임계치 근처 확률적으로 교전보다 자기장 이동 우선
- **`MovementStepSelector`** — 이동 전담(스트레이프/로밍/블렌딩), 자기장 위급 시 자기장 중심으로 이동
- **`CombatReactionEvaluator`** — static 유틸. 반격/엄폐 이탈/완전 후퇴/역공격 스코어링 + `zoneUrgency` 반영
- **`PlayerCombatStats`** — 클러치/포지셔닝만 구현
- **`PlayerHealth`** — `OnDeath`/`OnDeathWithAttacker`/`OnDamaged`. 자기장 데미지는 `attacker=null`로 호출됨
- **`TeamColorApplier`** — MaterialPropertyBlock으로 팀 색(20색 hue 균등 분배, 안 겹침) 캐릭터에 적용. `HumanoidBattleAnimator.SetTeamColor` 연동은 사용자가 직접 보강
- **`SpectatorCamera`** — Tab(팀 순환)+←→(팀원 순환) 2단계 빙의
- **`BattleHUD`** — "N/20 TEAMS LEFT" 요약, 자기장 단계/타이머 패널, 자기장 밖이면 경고, 킬피드(일반 처치 + 팀 탈락 + 자기장 사망)
- **`ArenaGenerator`** — 5v5 시절 유물, 현재 미사용(삭제 안 함)

### 이번 세션에 한 일 (2026-08-28, 상세는 changelog.md 참고)
1. (세션 초반) `ArenaGenerator` 엄폐물 180도 회전 대칭 배치로 변경 — 이후 미사용 처리됨
2. 배틀로얄 전환(4인×20팀) 전체 구현 — 위 계층 구조 항목들 신규/재작성
3. 지형 랜덤 시드, 팀별 캐릭터 색상 추가
4. 자기장(세이프존) 시스템 전체 구현
5. Notion 기획 문서 파트 분리(8→9개) + 배틀로얄 반영
6. Editor.log로 실제 Play 세션 확인(80명 스폰/전투/팀탈락 정상, 예외 0건) — 자기장은 세션 중엔 씬에 미부착이었으나, 세션 종료 시점에 사용자가 Unity AI로 `ZoneManager` 부착 + `ZoneWallMat` 연결 완료 확인됨(씬 파일 직접 확인)

### 다음 세션 확인/할 일
- **자기장 실제 Play 확인 최우선** — `ZoneManager` 부착은 완료됐으나 실제로 반투명 벽/흰 링이 잘 보이는지, PHASE 전환이 체감되는지, AI가 정말 위급할 때 자기장 안으로 이동하는지 아직 미확인
- 밸런스 튜닝 전부 임시값: `ZoneManager.phases`(대기/축소시간/데미지), `AIBrain.zoneUrgencyThreshold`/`zoneUrgencyBand`/`roamWanderRadius`, `BattleRoyaleSpawner.minClusterDistance`/`memberSpreadRadius`, `TerrainGenerator` 맵 크기(300×300) — 실제 플레이해보고 조정
- 캐릭터 프리팹(Kevin Iglesias 모델) 팀 색상이 실제로 잘 입혀지는지 셰이더 호환 확인
- 씬 이름 `AIBattle5v5`가 이제 내용과 안 맞음 — 리네임 여부 사용자 판단 필요
- `HandleDamaged`의 `nearbyEnemyCount`가 여전히 1로 고정(5v5 확장 시 교체 예정이었던 부분, 배틀로얄에서도 미반영) — 필요시 그리드 기반으로 실제 근처 인원 수 세도록 확장 고려
- 매크로 관전 뷰(탑뷰), 매니저 메타 루프(영입/훈련/스탯 확장)는 여전히 미착수

## Unity AI 활용 방침
- Unity AI(에디터 내 AI Assistant)는 실제 에디터 안에서 Play 눌러 확인하거나 인스펙터/비주얼 튜닝, 씬 컴포넌트 부착/배선 용도로 활용
- 스크립트 구조/아키텍처 변경이 필요한 이슈는 Claude Code로 가져와서 처리(두 AI가 따로 코드를 건드리면 구조가 어긋날 수 있어서)
- **(2026-08-28 신설, 전역 CLAUDE.md 규칙)** 새로운 기능을 만들거나 요구하면 Notion 기획 문서 해당 파트에 반영할 것

## 다음 세션 참고
- 세션 시작 시 이 파일을 먼저 읽고 구조/진행상황 파악할 것
- Claude Code가 씬(.unity) YAML을 직접 손으로 편집하는 건 지양 — 컴포넌트 부착/필드 연결 같은 에디터 작업은 Unity AI 지시문으로 안내하고 사용자가 진행
