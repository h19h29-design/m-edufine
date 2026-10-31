# 조사 범위와 설계 근거

작성: 2026-10-31. 기관 내부 화면을 보았다고 간주하지 않는다.

## 확인한 사항

- 사용자 지정 저장소 `h19h29-design/m-edufine`은 처음 확인할 때 비어 있었고 쓰기 권한이 확인되었다.
- 참고 프로젝트 README 및 `.agents/skills/clone-website/SKILL.md`를 읽었다. URL/화면 관찰 → 구현 → 실제 비교를 요구하며, 템플릿 자체는 원본 서버 권한이나 DB를 복사하지 않는다.
- 서울 K-에듀파인 루트 `https://klef.sen.go.kr/`는 외부 조사 도구에서 502, 업무포털 `https://sen.eduptl.kr/`는 시간초과였다. 이것은 사이트의 실제 장애나 기관 내 접속 불가를 증명하지 않는다.
- 연결 브라우저 읽기 도구는 현재 대화의 originating browser context가 없어 실패했다. 로그인된 사용자 탭이나 인증 후 메뉴를 읽지 못했다.

## 그래서 선택한 구조

화면을 추측해 가짜 물품 DB를 만드는 대신, 공식 브라우저가 이미 처리한 로그인 세션을 그대로 유지하고 선택한 탭의 실제 DOM을 읽고 조작한다. 인증서 모듈이 임베디드 브라우저에서 동작한다고 가정하지 않기 위해 v0.1.0은 WebView2를 사용하지 않는다. 원본 Edge/Chrome으로 전환하는 F4를 제공한다.

공개 메뉴 설명만으로는 동적 DOM 선택자, 전용 그리드, 저장 완료 조건을 확정할 수 없으므로 화면별 어댑터는 현장검증 이후 추가한다. 네트워크 요청 재생, API 주소 추정, 로그인 우회, 인증서 복사는 구현하지 않는다.

## 기술 문서

- https://github.com/JCodesMore/ai-website-cloner-template
- https://github.com/JCodesMore/ai-website-cloner-template/blob/master/.agents/skills/clone-website/SKILL.md
- https://learn.microsoft.com/en-us/microsoft-edge/extensions/developer-guide/native-messaging
- https://developer.chrome.com/docs/extensions/develop/concepts/native-messaging
- https://developer.chrome.com/docs/extensions/reference/api/scripting
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core

## 화면 검토 범위

요청한 Mdir식 파란색 2패널, 키보드 탐색, 18px 기본 글자, 명확한 읽기 전용 상태를 구현했다. 실제 K-에듀파인 화면과의 동일성은 주장하지 않는다. Windows CI에서 생성하는 `windows-ui.png`는 이 앱의 실제 WPF 렌더 결과이며, 원본 K-에듀파인 스크린샷이 아니다. 이미지 생성 도구를 사용할 수 없어 별도 이미지 컨셉은 만들지 않았다.
