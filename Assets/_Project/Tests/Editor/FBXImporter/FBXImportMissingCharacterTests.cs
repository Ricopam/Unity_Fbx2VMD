using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using UnityEngine;

namespace Tests.Editor.FBXImporter
{
    /// <summary>
    /// 캐릭터 없이 시작하는 Main 씬에서 설정창 FBX 가져오기 요청 거절 검증.
    /// 예전에는 요청을 받아들인 뒤 처리 도중 영어 섞인 메시지로 실패해 설정창에는 성공처럼 보였음.
    /// </summary>
    public class FBXImportMissingCharacterTests
    {
        [Test]
        public void Given_NoTargetCharacter_When_ImportRequested_Then_RejectsWithGuidance()
        {
            var host = new GameObject("PipelineWithoutCharacter");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();

                bool started = pipeline.TryStartFbxImportFromSharedSettings("Assets/Resources/Import_FBX/satisfaction_2.fbx");

                Assert.That(started, Is.False);
                Assert.That(pipeline.IsProcessing, Is.False);
                Assert.That(pipeline.SessionState, Is.EqualTo(FBXVmdPipeline.FBXSessionState.Failed));
                Assert.That(pipeline.LastSessionMessage, Is.EqualTo(FBXImportController.MissingCharacterMessage));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }
    }
}
