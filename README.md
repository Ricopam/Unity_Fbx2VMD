# Unity_Fbx2VMD

모션캡처로 움직이는 홀로그램 공연 도구.

모션캡처 FBX를 MMD·VRM·사람형 FBX 캐릭터에 자동으로 입히고, 발 접지·관절 표면·의상 물리를 자동 보정해 재생하고 영상으로 녹화하는 Unity 에디터 제작 도구입니다.

## 시연 영상

캐릭터 선택부터 녹화 결과까지 전체 흐름을 담은 52초 영상입니다(소리 없음).

https://github.com/user-attachments/assets/07eb81c7-b58e-4a6e-a1b6-a76bea73bf1c

[영상 파일 받기 (MP4, 1920×1080, 52초)](Media/Readme/fbx2vmd-demo.mp4)

## 처리 파이프라인

설정창 하나로 아래 순서를 진행합니다. Inspector는 개발·진단용으로만 씁니다.

1. 캐릭터 불러오기: 라이브러리에서 캐릭터를 고릅니다.

2. 보정 자동 연결: 하체 접지, 표면·엄지 보정, 의상 물리를 컴포넌트를 직접 붙이지 않아도 자동으로 연결합니다.

3. FBX 적용: 모션 FBX를 골라 캐릭터 체형에 맞게 자동 리타게팅합니다.

4. 재생: 보정을 반영한 움직임을 미리 봅니다.

5. 영상 녹화: 재생 화면을 MP4로 저장합니다.

![처리 파이프라인](Media/Readme/08-pipeline.gif)

## 시연

### 설정창 조작

캐릭터를 고르고 FBX를 가져옵니다. 설정창(Electron)이 Unity 에디터와 연결되어 단계별로 진행 상태를 보여 줍니다.

![설정창 조작](Media/Readme/02-settings-flow.gif)

### 자동 리타게팅

FBX를 가져오면 캐릭터 체형에 맞춘 리타게팅과 표면 보정 준비를 자동으로 수행합니다. 아래 화면은 실제 처리 과정을 빨리 감은 것입니다.

![자동 리타게팅](Media/Readme/03-auto-retarget.gif)

### 자동으로 적용되는 보정 3가지

- 하체 접지: 발이 바닥에 붙도록 접지를 보정합니다.

- 표면·엄지: 관절과 엄지 주변 메시 변형을 막습니다.

- 의상 물리: 치마와 머리카락이 움직임을 따라 자연스럽게 흔들립니다.

![보정 3가지](Media/Readme/04-corrections.gif)

### 하체 보정 ON / OFF

같은 모션·같은 구간을 보정을 켜고 끈 상태로 녹화해 비교했습니다. 보정을 끄면 발이 바닥선 아래로 파고듭니다.

![하체 보정 비교](Media/Readme/05-lower-body-compare.gif)

### 의상 물리 ON / OFF

![의상 물리 비교](Media/Readme/06-cloth-physics-compare.gif)

### 녹화 결과

![녹화 결과](Media/Readme/07-recording-result.gif)

## 개발 환경

- Unity 6000.3.12f1, Universal Render Pipeline

- 캐릭터 로딩: MMD4Mecanim(MMD), UniVRM 0.130.1(VRM), FBX

- 의상 물리: MagicaCloth2

- 녹화: Unity Recorder

- 설정창: Electron
