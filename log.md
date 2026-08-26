# 프로젝트 로그

## 개요
- FPS Manager: Unity 신규 프로젝트 (2026-08-26 생성)
- 프로젝트 루트(작업 디렉토리): `E:\Git\Fps Manager`
- Unity 6000.5.8f1, URP(Universal Render Pipeline) 3D 템플릿으로 시작
- 버전관리: Git/GitHub 사용 예정 (`github.com/ARHIENE/FPS-Manager`, public). 원래 Plastic SCM으로 초기화되어 있었으나 Git으로 전환

## Git 브랜치 전략 (1VS1 Game과 동일)
- `master`: 실제 작업 브랜치. 프로젝트 전체 파일 포함, **README.md 없음**
- `main`: GitHub 기본 브랜치. **README.md만 관리**(프로젝트 전체 파일 없음), 저장소 메인 페이지 노출용
- 두 브랜치는 공통 조상이 없는 별개 히스토리
- **SAVE 명령 시 main의 README.md도 그날 작업 반영해 최신화할 것** (전역 CLAUDE.md 규칙)

## 현재 상태
- Unity 3D(URP) 기본 템플릿 그대로인 초기 상태 (SampleScene, 기본 렌더 파이프라인 에셋 외 커스텀 스크립트/씬 없음)
- 게임 기획/구체적 기능은 아직 미정 — 다음 세션에서 사용자와 논의 필요

## 다음 계획
- (미정) 게임 컨셉/장르 구체화
- 기능별 스크립트 모듈화 방침 유지
