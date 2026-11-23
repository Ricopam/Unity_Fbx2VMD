using System.Runtime.CompilerServices;

// CharacterLibrary는 임포트 계약의 내부 구현 타입을 소비한다 — 공용 표면을 넓히지 않고 내부만 개방한다.
[assembly: InternalsVisibleTo("Fbx2Vmd.CharacterLibrary")]
[assembly: InternalsVisibleTo("Fbx2Vmd.Settings")]
// 머리카락 스프링이 보정 패스 실행 순서 상수를 공유함.
[assembly: InternalsVisibleTo("Fbx2Vmd.ClothPhysics")]
[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]
