using UnityEngine;

namespace Fbx2Vmd.FBXImporter
{
    public partial class HumanoidArmSwingLimitGuard
    {
            private bool _warningLogged;
            private HumanPoseHandler _diagnosticPoseHandler;
            private HumanPose _diagnosticPose;
            private int _leftForearmStretchMuscleIndex = -2;
            private int _rightForearmStretchMuscleIndex = -2;

            public int LastLeftApplied { get; private set; }
            public int LastLeftHorizontalReachApplied { get; private set; }
            public int LastLeftRaisedReachApplied { get; private set; }
            public float LastLeftForearmStretchBefore { get; private set; } = float.NaN;
            public float LastLeftForearmStretchAfter { get; private set; } = float.NaN;
            public float LastLeftForearmStretchDelta { get; private set; } = float.NaN;
            public int LastRightApplied { get; private set; }
            public int LastRightHorizontalReachApplied { get; private set; }
            public int LastRightRaisedReachApplied { get; private set; }
            public float LastRightForearmStretchBefore { get; private set; } = float.NaN;
            public float LastRightForearmStretchAfter { get; private set; } = float.NaN;
            public float LastRightForearmStretchDelta { get; private set; } = float.NaN;

            private void LogOnce(string message)
            {
                if (_warningLogged || !logCorrections)
                {
                    _warningLogged = true;
                    return;
                }

                Debug.LogWarning($"[HumanoidArmSwingLimitGuard] {message}");
                _warningLogged = true;
            }

            private void ResetDiagnostics()
            {
                LastLeftApplied = 0;
                LastLeftHorizontalReachApplied = 0;
                LastLeftRaisedReachApplied = 0;
                LastLeftForearmStretchBefore = float.NaN;
                LastLeftForearmStretchAfter = float.NaN;
                LastLeftForearmStretchDelta = float.NaN;
                LastRightApplied = 0;
                LastRightHorizontalReachApplied = 0;
                LastRightRaisedReachApplied = 0;
                LastRightForearmStretchBefore = float.NaN;
                LastRightForearmStretchAfter = float.NaN;
                LastRightForearmStretchDelta = float.NaN;
            }

            private void RecordArmLimitDiagnostics(
                HumanBodyBones upperBone,
                bool standardLimitApplied,
                bool horizontalReachLimited,
                bool raisedReachLimited,
                float forearmStretchBefore)
            {
                float forearmStretchAfter = CaptureForearmStretchMuscle(upperBone);
                float forearmStretchDelta = IsFinite(forearmStretchBefore) && IsFinite(forearmStretchAfter)
                    ? forearmStretchAfter - forearmStretchBefore
                    : float.NaN;
                int anyApplied = standardLimitApplied || horizontalReachLimited || raisedReachLimited ? 1 : 0;
                int horizontalApplied = horizontalReachLimited ? 1 : 0;
                int raisedApplied = raisedReachLimited ? 1 : 0;

                if (upperBone == HumanBodyBones.LeftUpperArm)
                {
                    LastLeftApplied = anyApplied;
                    LastLeftHorizontalReachApplied = horizontalApplied;
                    LastLeftRaisedReachApplied = raisedApplied;
                    LastLeftForearmStretchBefore = forearmStretchBefore;
                    LastLeftForearmStretchAfter = forearmStretchAfter;
                    LastLeftForearmStretchDelta = forearmStretchDelta;
                    return;
                }

                LastRightApplied = anyApplied;
                LastRightHorizontalReachApplied = horizontalApplied;
                LastRightRaisedReachApplied = raisedApplied;
                LastRightForearmStretchBefore = forearmStretchBefore;
                LastRightForearmStretchAfter = forearmStretchAfter;
                LastRightForearmStretchDelta = forearmStretchDelta;
            }

            private float CaptureForearmStretchMuscle(HumanBodyBones upperBone)
            {
                if (!EnsureDiagnosticPoseHandler())
                {
                    return float.NaN;
                }

                _diagnosticPoseHandler.GetHumanPose(ref _diagnosticPose);
                if (_diagnosticPose.muscles == null || _diagnosticPose.muscles.Length == 0)
                {
                    return float.NaN;
                }

                int muscleIndex = upperBone == HumanBodyBones.LeftUpperArm
                    ? GetLeftForearmStretchMuscleIndex()
                    : GetRightForearmStretchMuscleIndex();
                if (muscleIndex < 0 || muscleIndex >= _diagnosticPose.muscles.Length)
                {
                    return float.NaN;
                }

                return _diagnosticPose.muscles[muscleIndex];
            }

            private bool EnsureDiagnosticPoseHandler()
            {
                if (_diagnosticPoseHandler != null)
                {
                    return true;
                }

                if (!InitializeIfNeeded())
                {
                    return false;
                }

                _diagnosticPoseHandler = new HumanPoseHandler(_animator.avatar, _animator.transform);
                _diagnosticPose = new HumanPose();
                return true;
            }

            private void DisposeDiagnosticPoseHandler()
            {
                if (_diagnosticPoseHandler == null)
                {
                    return;
                }

                _diagnosticPoseHandler.Dispose();
                _diagnosticPoseHandler = null;
            }

            private int GetLeftForearmStretchMuscleIndex()
            {
                if (_leftForearmStretchMuscleIndex == -2)
                {
                    _leftForearmStretchMuscleIndex = FindMuscleIndex("left", "forearm", "stretch");
                }

                return _leftForearmStretchMuscleIndex;
            }

            private int GetRightForearmStretchMuscleIndex()
            {
                if (_rightForearmStretchMuscleIndex == -2)
                {
                    _rightForearmStretchMuscleIndex = FindMuscleIndex("right", "forearm", "stretch");
                }

                return _rightForearmStretchMuscleIndex;
            }

            private static int FindMuscleIndex(params string[] tokens)
            {
                for (int i = 0; i < HumanTrait.MuscleCount; i++)
                {
                    string muscleName = NormalizeMuscleName(HumanTrait.MuscleName[i]);
                    bool matches = true;
                    foreach (string token in tokens)
                    {
                        if (!muscleName.Contains(NormalizeMuscleName(token)))
                        {
                            matches = false;
                            break;
                        }
                    }

                    if (matches)
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static string NormalizeMuscleName(string value)
            {
                return string.IsNullOrEmpty(value)
                    ? ""
                    : value.Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
            }
    }
}
