using Fbx2Vmd.CharacterLibrary;
using Fbx2Vmd.FBXImporter;
using NUnit.Framework;
using System;
using System.IO;
using System.Reflection;
using UnityEngine;
using VRM;

namespace Tests.Editor.CharacterLibrary
{
    public class CharacterPresetTests
    {
        [Test]
        public void Given_PresetDocument_When_SaveAndLoad_Then_RoundTrips()
        {
            string folder = CreateTempFolder();
            string path = Path.Combine(folder, "presets", "abc.json");
            var store = new CharacterPresetStore(path);
            var document = new CharacterPresetDocument { characterId = "abc" };
            document.presets.Add(new CharacterPreset
            {
                id = "p1",
                name = "기본 포즈",
                sections = new CharacterPresetSections
                {
                    retarget =
                    {
                        new PresetFieldValue
                        {
                            key = "_GroundedFootLockWeight",
                            type = PresetFieldType.Float,
                            value = "0.7",
                        },
                    },
                    placement =
                    {
                        new PresetFieldValue
                        {
                            key = "position",
                            type = PresetFieldType.Vector3,
                            value = "1,2,3",
                        },
                    },
                },
            });

            store.Save(document);
            CharacterPresetDocument loaded = store.LoadOrCreateDefault();

            Assert.That(loaded.characterId, Is.EqualTo("abc"));
            Assert.That(loaded.presets.Count, Is.EqualTo(1));
            Assert.That(loaded.presets[0].name, Is.EqualTo("기본 포즈"));
            Assert.That(loaded.presets[0].sections.retarget.Count, Is.EqualTo(1));
            Assert.That(loaded.presets[0].sections.placement[0].value, Is.EqualTo("1,2,3"));
        }

        [Test]
        public void Given_CorruptPresetFile_When_Loading_Then_BacksUpAndReturnsDefault()
        {
            string folder = CreateTempFolder();
            string path = Path.Combine(folder, "abc.json");
            File.WriteAllText(path, "{ broken");

            var store = new CharacterPresetStore(path);
            CharacterPresetDocument loaded = store.LoadOrCreateDefault();

            Assert.That(loaded.presets, Is.Empty);
            Assert.That(Directory.GetFiles(folder, "abc.json.corrupt-*"), Has.Length.EqualTo(1));
        }

        [Test]
        public void Given_LockedPresetFile_When_Saving_Then_BlockedAndFilePreserved()
        {
            string folder = CreateTempFolder();
            string path = Path.Combine(folder, "abc.json");
            string originalJson = "{\"schemaVersion\":1,\"presets\":[]}";
            File.WriteAllText(path, originalJson);

            var store = new CharacterPresetStore(path);
            using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error, new System.Text.RegularExpressions.Regex("파일을 읽지 못했습니다"));
                store.LoadOrCreateDefault();
                Assert.That(store.LastReadFailed, Is.True);
                Assert.Throws<InvalidOperationException>(
                    () => store.Save(new CharacterPresetDocument()));
            }
            Assert.That(File.ReadAllText(path), Is.EqualTo(originalJson));
        }

        [Test]
        public void Given_DuplicatePresetIds_When_Normalizing_Then_KeepsFirstOnly()
        {
            var document = new CharacterPresetDocument();
            document.presets.Add(new CharacterPreset { id = "same", name = "첫째" });
            document.presets.Add(new CharacterPreset { id = "same", name = "둘째" });

            CharacterPresetDocument normalized =
                CharacterPresetDocumentNormalizer.Normalize(document);

            Assert.That(normalized.presets.Count, Is.EqualTo(1));
            Assert.That(normalized.presets[0].name, Is.EqualTo("첫째"));
        }

        [Test]
        public void Given_NullPresetDocument_When_Normalizing_Then_ReturnsDefault()
        {
            CharacterPresetDocument normalized =
                CharacterPresetDocumentNormalizer.Normalize(null);

            Assert.That(normalized, Is.Not.Null);
            Assert.That(normalized.schemaVersion, Is.EqualTo(1));
            Assert.That(normalized.presets, Is.Not.Null);
        }

        [Test]
        public void Given_PipelineValues_When_CaptureAndApply_Then_RestoresFields()
        {
            var host = new GameObject("pipeline");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();
                FieldInfo weightField = typeof(FBXVmdPipeline).GetField(
                    "_GroundedFootLockWeight",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(weightField, Is.Not.Null, "프리셋 대상 필드가 없습니다");
                weightField.SetValue(pipeline, 0.9f);

                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                CharacterPresetSnapshot.CaptureRetarget(preset, pipeline);

                weightField.SetValue(pipeline, 0.1f);
                var warnings = CharacterPresetSnapshot.Apply(preset, pipeline, null, null);

                Assert.That(
                    (float)weightField.GetValue(pipeline),
                    Is.EqualTo(0.9f).Within(0.0001f));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Given_PresetWithUnknownField_When_Applying_Then_SkipsWithWarning()
        {
            var host = new GameObject("pipeline");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();
                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                preset.sections.retarget.Add(new PresetFieldValue
                {
                    key = "_notARealField",
                    type = PresetFieldType.Float,
                    value = "1.0",
                });
                // 직렬화되지 않은 필드는 적용하지 않고 경고만 낸다.
                preset.sections.retarget.Add(new PresetFieldValue
                {
                    key = "_targetCharacter",
                    type = PresetFieldType.String,
                    value = "x",
                });

                var warnings = CharacterPresetSnapshot.Apply(preset, pipeline, null, null);

                Assert.That(warnings.Count, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Given_CharacterTransform_When_CaptureAndApplyPlacement_Then_Restores()
        {
            var root = new GameObject("char");
            var applied = new GameObject("char2");
            try
            {
                root.transform.position = new Vector3(1f, 2f, 3f);
                root.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
                root.transform.localScale = new Vector3(2f, 2f, 2f);

                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                CharacterPresetSnapshot.CapturePlacement(preset, root, null);

                applied.transform.position = Vector3.zero;
                applied.transform.rotation = Quaternion.identity;
                var warnings = CharacterPresetSnapshot.Apply(preset, null, applied, null);

                Assert.That(applied.transform.position, Is.EqualTo(root.transform.position));
                Assert.That(applied.transform.rotation.eulerAngles.y,
                    Is.EqualTo(45f).Within(0.01f));
                Assert.That(applied.transform.localScale, Is.EqualTo(root.transform.localScale));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(applied);
            }
        }

        [Test]
        public void Given_SpringBoneValues_When_CaptureAndApplyPhysics_Then_Restores()
        {
            var root = new GameObject("char");
            var boneA = new GameObject("a");
            var boneB = new GameObject("b");
            try
            {
                boneA.transform.SetParent(root.transform);
                boneB.transform.SetParent(root.transform);
                VRMSpringBone spring = boneA.AddComponent<VRMSpringBone>();
                spring.m_stiffnessForce = 2.5f;
                spring.m_dragForce = 0.7f;

                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                CharacterPresetSnapshot.CapturePhysics(preset, root);
                Assert.That(preset.sections.physics.Count, Is.GreaterThan(0));

                spring.m_stiffnessForce = 0.1f;
                spring.m_dragForce = 0.1f;
                var warnings = CharacterPresetSnapshot.Apply(preset, null, root, null);

                Assert.That(spring.m_stiffnessForce, Is.EqualTo(2.5f).Within(0.0001f));
                Assert.That(spring.m_dragForce, Is.EqualTo(0.7f).Within(0.0001f));
                Assert.That(warnings, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Given_CharacterIdWithTraversal_When_ResolvingPresetPath_Then_RejectsIt()
        {
            string libraryFile = Path.Combine("C:", "lib", "characters.json");

            Assert.That(
                CharacterLibraryPathResolver.ResolvePresetFilePath(libraryFile, "..\\evil"),
                Is.EqualTo(string.Empty));
            Assert.That(
                CharacterLibraryPathResolver.ResolvePresetFilePath(libraryFile, "abc"),
                Is.EqualTo(Path.Combine("C:", "lib", "presets", "abc.json")));
        }

        [Test]
        public void Given_PipelineGlobals_When_CaptureRetarget_Then_ExcludedFromPreset()
        {
            var host = new GameObject("pipeline");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();
                // 전역 설정 계열 필드가 실제로 존재하는지 먼저 확인한다.
                Assert.That(typeof(FBXVmdPipeline).GetField("_motionVideoOutputFolder",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);
                Assert.That(typeof(FBXVmdPipeline).GetField("_showBoneMappingLog",
                    BindingFlags.Instance | BindingFlags.NonPublic), Is.Not.Null);

                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                CharacterPresetSnapshot.CaptureRetarget(preset, pipeline);

                for (int i = 0; i < preset.sections.retarget.Count; i++)
                {
                    string key = preset.sections.retarget[i].key;
                    Assert.That(key, Does.Not.Contain("Folder"));
                    Assert.That(key, Does.Not.Contain("_show"));
                    Assert.That(key, Does.Not.Contain("Recording"));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Given_GuardKeysWithoutGuard_When_Applying_Then_WarnsInsteadOfDropping()
        {
            var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
            preset.sections.placement.Add(new PresetFieldValue
            {
                key = "guard._lockTargetPoseUntilImport",
                type = PresetFieldType.Bool,
                value = "false",
            });

            var warnings = CharacterPresetSnapshot.Apply(preset, null, null, null);

            Assert.That(warnings.Count, Is.EqualTo(1));
            Assert.That(warnings[0], Does.Contain("가드"));
        }

        [Test]
        public void Given_MismatchedEnumType_When_Applying_Then_SkipsWithWarning()
        {
            var root = new GameObject("char");
            var bone = new GameObject("b");
            try
            {
                bone.transform.SetParent(root.transform);
                var spring = bone.AddComponent<VRMSpringBone>();
                spring.m_updateType = VRMSpringBone.SpringBoneUpdateType.LateUpdate;

                var preset = CharacterPresetSnapshot.CreateEmpty("테스트");
                preset.sections.physics.Add(new PresetFieldValue
                {
                    key = "spring[0].m_updateType",
                    type = PresetFieldType.EnumPrefix + "Some.Other.Enum",
                    value = "0",
                });

                var warnings = CharacterPresetSnapshot.Apply(preset, null, root, null);

                Assert.That(warnings.Count, Is.EqualTo(1));
                Assert.That(spring.m_updateType,
                    Is.EqualTo(VRMSpringBone.SpringBoneUpdateType.LateUpdate));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Given_Registry_When_OpeningPresetStoreTwice_Then_ReusesInstance()
        {
            string folder = CreateTempFolder();
            string libraryPath = Path.Combine(folder, "characters.json");
            var registry = new CharacterLibraryRegistry(libraryPath);

            CharacterPresetStore first = registry.OpenPresetStore("entry-a");
            CharacterPresetStore second = registry.OpenPresetStore("entry-a");

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.SameAs(first),
                "같은 엔트리의 스토어는 재사용돼 LastReadFailed 보호가 유지돼야 합니다");
        }

        [Test]
        public void Given_RegistryPresetStoreReadFailure_When_Saving_Then_Blocked()
        {
            string folder = CreateTempFolder();
            string libraryPath = Path.Combine(folder, "characters.json");
            var registry = new CharacterLibraryRegistry(libraryPath);
            string presetPath = registry.ResolvePresetFilePath("entry-b");
            Directory.CreateDirectory(Path.GetDirectoryName(presetPath));
            File.WriteAllText(presetPath, "{\"schemaVersion\":1,\"presets\":[]}");

            CharacterPresetStore store = registry.OpenPresetStore("entry-b");
            using (new FileStream(presetPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                UnityEngine.TestTools.LogAssert.Expect(
                    LogType.Error, new System.Text.RegularExpressions.Regex("파일을 읽지 못했습니다"));
                registry.LoadPresets("entry-b");
                Assert.Throws<InvalidOperationException>(
                    () => store.Save(new CharacterPresetDocument()),
                    "읽기 실패가 관찰된 스토어는 저장이 차단돼야 합니다");
            }
        }

        [Test]
        public void Given_FbxOverride_When_UpsertFindRemove_Then_RoundTripsAndNormalizesKey()
        {
            string folder = CreateTempFolder();
            var registry = new CharacterLibraryRegistry(Path.Combine(folder, "characters.json"));

            registry.UpsertFbxOverride("entry-fbx", new CharacterFbxOverride
            {
                fbxKey = Path.Combine(folder, "Import_FBX", "motion.fbx"),
                name = "모션 예외",
                sections = new CharacterPresetSections
                {
                    retarget =
                    {
                        new PresetFieldValue
                        {
                            key = "_GroundedFootLockWeight",
                            type = PresetFieldType.Float,
                            value = "0.9",
                        },
                    },
                },
            });

            // 경로가 달라도 파일명이 같으면 같은 예외 키다.
            CharacterFbxOverride found = registry.FindFbxOverride(
                "entry-fbx", Path.Combine("D:\\", "other", "motion.fbx"));
            Assert.That(found, Is.Not.Null);
            Assert.That(found.fbxKey, Is.EqualTo("motion.fbx"));
            Assert.That(found.name, Is.EqualTo("모션 예외"));
            Assert.That(found.sections.retarget.Count, Is.EqualTo(1));

            // 다른 엔트리·다른 키에는 영향이 없다.
            Assert.That(registry.FindFbxOverride("entry-fbx", "walk.fbx"), Is.Null);
            Assert.That(registry.FindFbxOverride("entry-other", "motion.fbx"), Is.Null);

            Assert.That(registry.RemoveFbxOverride("entry-fbx", "motion.fbx"), Is.True);
            Assert.That(registry.FindFbxOverride("entry-fbx", "motion.fbx"), Is.Null);
            Assert.That(registry.RemoveFbxOverride("entry-fbx", "motion.fbx"), Is.False);
        }

        [Test]
        public void Given_DuplicateFbxKeys_When_Normalized_Then_FirstKept()
        {
            var document = new CharacterPresetDocument
            {
                fbxOverrides =
                {
                    new CharacterFbxOverride { fbxKey = "motion.fbx", name = "첫 번째" },
                    null,
                    new CharacterFbxOverride { fbxKey = "MOTION.FBX", name = "중복" },
                    new CharacterFbxOverride { fbxKey = "  ", name = "빈키" },
                },
            };

            CharacterPresetDocument normalized =
                CharacterPresetDocumentNormalizer.Normalize(document);

            Assert.That(normalized.fbxOverrides.Count, Is.EqualTo(1));
            Assert.That(normalized.fbxOverrides[0].name, Is.EqualTo("첫 번째"));
        }

        [Test]
        public void Given_BasePresetAndOverride_When_AppliedSequentially_Then_OverrideWinsAndInherits()
        {
            var host = new GameObject("pipeline");
            try
            {
                var pipeline = host.AddComponent<FBXVmdPipeline>();
                FieldInfo weightField = typeof(FBXVmdPipeline).GetField(
                    "_GroundedFootLockWeight",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                FieldInfo stepField = typeof(FBXVmdPipeline).GetField(
                    "_MaxGroundedFootLockStep",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(weightField, Is.Not.Null, "프리셋 대상 필드가 없습니다");
                Assert.That(stepField, Is.Not.Null, "프리셋 대상 필드가 없습니다");

                // 기본값과 다른 값부터 세팅해 적용 효과를 관찰한다.
                weightField.SetValue(pipeline, 0.7f);
                stepField.SetValue(pipeline, 0.7f);

                var basePreset = CharacterPresetSnapshot.CreateEmpty("기본");
                basePreset.sections.retarget.Add(new PresetFieldValue
                {
                    key = "_GroundedFootLockWeight",
                    type = PresetFieldType.Float,
                    value = "0.1",
                });
                basePreset.sections.retarget.Add(new PresetFieldValue
                {
                    key = "_MaxGroundedFootLockStep",
                    type = PresetFieldType.Float,
                    value = "0.05",
                });
                // 예외는 덮어쓸 값만 담는다 — 없는 키는 기본 프리셋 값이 유지돼야 한다.
                var overrideSections = new CharacterPresetSections();
                overrideSections.retarget.Add(new PresetFieldValue
                {
                    key = "_GroundedFootLockWeight",
                    type = PresetFieldType.Float,
                    value = "0.9",
                });

                CharacterPresetSnapshot.Apply(basePreset, pipeline, null, null);
                CharacterPresetSnapshot.ApplySections(overrideSections, pipeline, null, null);

                Assert.That((float)weightField.GetValue(pipeline),
                    Is.EqualTo(0.9f).Within(0.0001f), "예외 값이 기본값을 덮어써야 합니다");
                Assert.That((float)stepField.GetValue(pipeline),
                    Is.EqualTo(0.05f).Within(0.0001f), "예외에 없는 값은 기본 프리셋을 상속해야 합니다");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Given_MergeSections_When_OverlayApplied_Then_KeysOverrideAndPositionKept()
        {
            var baseSections = new CharacterPresetSections
            {
                retarget =
                {
                    new PresetFieldValue { key = "a", type = PresetFieldType.Float, value = "1" },
                    new PresetFieldValue { key = "b", type = PresetFieldType.Float, value = "2" },
                    new PresetFieldValue { key = "a", type = PresetFieldType.Float, value = "3" },
                },
                placement =
                {
                    new PresetFieldValue { key = "position", type = PresetFieldType.Vector3, value = "1,2,3" },
                },
            };
            var overlaySections = new CharacterPresetSections
            {
                retarget =
                {
                    new PresetFieldValue { key = "b", type = PresetFieldType.Float, value = "9" },
                    new PresetFieldValue { key = "c", type = PresetFieldType.Float, value = "7" },
                },
            };

            CharacterPresetSections merged =
                CharacterPresetSnapshot.MergeSections(baseSections, overlaySections);

            // 오버레이 키는 덮어쓰고, 기본 전용 키는 위치 그대로 유지한다.
            // 리스트 내 중복 키는 마지막 값이 이긴다(적용 경로와 동일 규칙).
            Assert.That(merged.retarget.Count, Is.EqualTo(3));
            Assert.That(merged.retarget[0].key, Is.EqualTo("a"));
            Assert.That(merged.retarget[0].value, Is.EqualTo("3"));
            Assert.That(merged.retarget[1].key, Is.EqualTo("b"));
            Assert.That(merged.retarget[1].value, Is.EqualTo("9"));
            Assert.That(merged.retarget[2].key, Is.EqualTo("c"));
            Assert.That(merged.placement.Count, Is.EqualTo(1));

            // 입력은 변경되지 않는다.
            Assert.That(baseSections.retarget[1].value, Is.EqualTo("2"));

            // null 조합: 오버레이만 있으면 오버레이만, 둘 다 null이면 빈 섹션.
            CharacterPresetSections overlayOnly =
                CharacterPresetSnapshot.MergeSections(null, overlaySections);
            Assert.That(overlayOnly.retarget.Count, Is.EqualTo(2));
            CharacterPresetSections empty =
                CharacterPresetSnapshot.MergeSections(null, null);
            Assert.That(empty.retarget, Is.Empty);
        }

        private static string CreateTempFolder()
        {
            string folder = Path.Combine(
                Path.GetTempPath(),
                "UnityFbx2Vmd-CharacterPresetTests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }
    }
}
