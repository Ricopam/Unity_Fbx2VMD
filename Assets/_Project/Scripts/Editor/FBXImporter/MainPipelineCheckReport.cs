using System;
using System.Collections.Generic;
using System.IO;

namespace Fbx2Vmd.FBXImporter
{
    /// <summary>
    /// Main 씬 파이프라인 점검 결과와 합격 판정. Unity 객체 없이 판정만 하므로 편집 모드 테스트로 검증함.
    /// </summary>
    [Serializable]
    internal sealed class MainPipelineCheckReport
    {
        // 녹화 프레임이 기대치보다 이만큼 이상 모자라면 저장 누락으로 봄.
        internal const float MinRecordedFrameRatio = 0.95f;
        // 재생 중 접지 표본이 이 비율보다 적으면 하체 보정이 동작하지 않은 것으로 봄.
        internal const float MinGroundedRatio = 0.9f;

        public string started_at = string.Empty;
        public string finished_at = string.Empty;
        public string failed_stage = string.Empty;
        public string failure_message = string.Empty;
        public string character_name = string.Empty;
        public int material_count;
        public int textured_material_count;
        public int cloth_count;
        public int valid_cloth_count;
        public int hair_spring_count;
        public bool thumb_guard_enabled;
        public bool surface_correction_enabled;
        public string surface_correction_failure = string.Empty;
        public float correction_prepare_seconds;
        public int grounded_samples;
        public int playback_samples;
        public bool has_nan_bounds;
        public string video_path = string.Empty;
        public long video_size_bytes;
        public bool is_video_finalized;
        public int expected_frames;
        public int recorded_frames;
        public int error_log_count;
        public List<string> error_logs = new List<string>();
        public List<string> failures = new List<string>();
        public bool passed;

        /// <summary>수집한 값으로 실패 항목을 채우고 합격 여부를 정함.</summary>
        internal void Evaluate()
        {
            failures.Clear();
            if (!string.IsNullOrEmpty(failed_stage))
                failures.Add($"{failed_stage} 단계 중단: {failure_message}");
            if (material_count == 0 || textured_material_count == 0)
                failures.Add("캐릭터 재질에 텍스처가 없음");
            if (cloth_count == 0 || valid_cloth_count < cloth_count)
                failures.Add($"의상 클로스 유효 {valid_cloth_count}/{cloth_count}");
            if (hair_spring_count == 0)
                failures.Add("머리카락 스프링 없음");
            if (!thumb_guard_enabled)
                failures.Add("엄지 보정 꺼짐");
            if (!surface_correction_enabled || !string.IsNullOrEmpty(surface_correction_failure))
                failures.Add($"표면 보정 비활성: {surface_correction_failure}");
            if (playback_samples == 0 || grounded_samples < playback_samples * MinGroundedRatio)
                failures.Add($"하체 접지 {grounded_samples}/{playback_samples}");
            if (has_nan_bounds)
                failures.Add("메시 경계에 NaN");
            if (!is_video_finalized)
                failures.Add("녹화 영상이 완성되지 않음(재생 불가)");
            if (expected_frames <= 0 || recorded_frames < expected_frames * MinRecordedFrameRatio)
                failures.Add($"녹화 프레임 {recorded_frames}/{expected_frames}");
            if (error_log_count > 0)
                failures.Add($"오류 로그 {error_log_count}개");
            passed = failures.Count == 0;
        }

        /// <summary>
        /// MP4·MOV 파일이 끝까지 저장됐는지. 저장이 끝나야 생기는 moov 상자가 최상위에 있어야 재생됨.
        /// </summary>
        internal static bool IsFinalizedMp4(Stream stream)
        {
            var header = new byte[8];
            long position = 0;
            bool hasFileType = false;
            while (position + 8 <= stream.Length)
            {
                stream.Position = position;
                if (stream.Read(header, 0, 8) < 8)
                    return false;
                long size = ((long)header[0] << 24) | ((long)header[1] << 16) | ((long)header[2] << 8) | header[3];
                string type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
                if (size == 1)
                {
                    // 64비트 크기 상자
                    var large = new byte[8];
                    if (stream.Read(large, 0, 8) < 8)
                        return false;
                    size = 0;
                    for (int i = 0; i < 8; i++)
                        size = (size << 8) | large[i];
                }
                if (type == "ftyp")
                    hasFileType = true;
                // 녹화가 중간에 끊기면 moov 머리만 쓰이고 본문이 잘릴 수 있어 크기도 확인함.
                if (type == "moov")
                    return hasFileType && size >= 8 && position + size <= stream.Length;
                // 크기 0은 파일 끝까지인 마지막 상자 — 그 뒤에 moov가 올 수 없음.
                if (size < 8)
                    return false;
                position += size;
            }
            return false;
        }
    }
}
