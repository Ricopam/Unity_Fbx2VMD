using System.Collections.Generic;
using Fbx2Vmd.Settings;
using NUnit.Framework;

namespace Tests.Editor.Settings
{
    public sealed class RecordingFileFinalizationTrackerTests
    {
        private const string Output = "C:/Recordings/satisfaction_2.mp4";

        [Test]
        public void Recording_RemembersOutputPathAndIsNotFinalized()
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(true, Output, _ => 100);
            Assert.That(tracker.OutputPath, Is.EqualTo(Output));
            Assert.That(tracker.IsFinalized, Is.False);
        }

        [Test]
        public void AfterStop_SizeStableForTwoPolls_IsFinalized()
        {
            var tracker = new RecordingFileFinalizationTracker();
            var sizes = new Queue<long>(new long[] { 100, 200, 200, 200 });
            tracker.Observe(true, Output, _ => 0);
            tracker.Observe(false, Output, _ => sizes.Dequeue());
            tracker.Observe(false, Output, _ => sizes.Dequeue());
            tracker.Observe(false, Output, _ => sizes.Dequeue());
            Assert.That(tracker.IsFinalized, Is.False);
            tracker.Observe(false, Output, _ => sizes.Dequeue());
            Assert.That(tracker.IsFinalized, Is.True);
            Assert.That(tracker.OutputPath, Is.EqualTo(Output));
        }

        [TestCase(0L)]
        [TestCase(-1L)]
        public void EmptyOrMissingFile_IsNeverFinalized(long size)
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(true, Output, _ => 0);
            for (int i = 0; i < 5; i++)
            {
                tracker.Observe(false, Output, _ => size);
            }

            Assert.That(tracker.IsFinalized, Is.False);
        }

        [Test]
        public void NewRecording_ResetsPreviousFinalizedResult()
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(true, Output, _ => 0);
            for (int i = 0; i < 3; i++)
            {
                tracker.Observe(false, Output, _ => 300);
            }

            Assert.That(tracker.IsFinalized, Is.True);
            tracker.Observe(true, "C:/Recordings/next.mp4", _ => 0);
            Assert.That(tracker.IsFinalized, Is.False);
            Assert.That(tracker.OutputPath, Is.EqualTo("C:/Recordings/next.mp4"));
        }

        // 인코딩 실패 시 레코더가 출력 경로를 비움 — 남은 일부 파일을 완성으로 판정하면 안 됨.
        [Test]
        public void RecorderClearsPathAfterStop_IsFailedNotFinalized()
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(true, Output, _ => 0);
            for (int i = 0; i < 4; i++)
            {
                tracker.Observe(false, string.Empty, _ => 300);
            }

            Assert.That(tracker.IsFinalized, Is.False);
            Assert.That(tracker.HasFailed, Is.True);
            Assert.That(tracker.OutputPath, Is.Empty);
        }

        // 보정 준비로 녹화 시작이 미뤄지는 동안 이전 녹화 결과를 보이지 않게 함.
        [Test]
        public void BeginNewRecording_ClearsPreviousResultBeforeRecordingStarts()
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(true, Output, _ => 0);
            for (int i = 0; i < 3; i++)
            {
                tracker.Observe(false, Output, _ => 300);
            }

            Assert.That(tracker.IsFinalized, Is.True);
            tracker.BeginNewRecording();
            tracker.Observe(false, Output, _ => 300);
            Assert.That(tracker.OutputPath, Is.Empty);
            Assert.That(tracker.IsFinalized, Is.False);
            Assert.That(tracker.HasFailed, Is.False);
        }

        [Test]
        public void NeverRecorded_StaysEmpty()
        {
            var tracker = new RecordingFileFinalizationTracker();
            tracker.Observe(false, string.Empty, _ => 500);
            Assert.That(tracker.OutputPath, Is.Empty);
            Assert.That(tracker.IsFinalized, Is.False);
        }

        [TestCase("{\"frameIndex\":1244}", true, 1244)]
        [TestCase("{\"frameIndex\":0}", true, 0)]
        [TestCase("{\"frameIndex\":-3}", false, 0)]
        [TestCase("{}", false, 0)]
        [TestCase("", false, 0)]
        [TestCase("not json", false, 0)]
        public void SeekParams_ParsesOnlyNonNegativeFrameIndex(string json, bool expected, int frame)
        {
            Assert.That(MainRecordingSettingsSeekParams.TryReadFrameIndex(json, out int parsed), Is.EqualTo(expected));
            Assert.That(parsed, Is.EqualTo(frame));
        }
    }
}
