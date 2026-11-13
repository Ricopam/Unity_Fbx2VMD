using System;

namespace Fbx2Vmd.Settings
{
    internal sealed class MainRecordingSettingsFbxImportCommandProcessor
    {
        private string lastHandledCommandId = string.Empty;

        internal bool TryProcess(
            MainRecordingSettingsDocument document,
            bool shouldStartFbxImport,
            bool shouldClearSkippedCommand,
            Func<string, bool> fileExists,
            Func<string, bool> tryStartFbxImport,
            Action<MainRecordingSettingsDocument> persistConsumedDocument,
            out MainRecordingSettingsActionResult result)
        {
            result = default;
            MainRecordingSettingsCommandEnvelope command = document?.pendingCommand;
            if (command == null || !command.IsImportFbxCommand())
            {
                return false;
            }

            if (TryConsumePreHandled(
                document,
                command,
                shouldStartFbxImport,
                shouldClearSkippedCommand,
                persistConsumedDocument,
                out result))
            {
                return true;
            }

            result = ExecuteImportCommand(
                document,
                command,
                fileExists,
                tryStartFbxImport,
                persistConsumedDocument);
            return true;
        }

        private bool TryConsumePreHandled(
            MainRecordingSettingsDocument document,
            MainRecordingSettingsCommandEnvelope command,
            bool shouldStartFbxImport,
            bool shouldClearSkippedCommand,
            Action<MainRecordingSettingsDocument> persistConsumedDocument,
            out MainRecordingSettingsActionResult result)
        {
            if (string.Equals(lastHandledCommandId, command.commandId, StringComparison.Ordinal))
            {
                ConsumeCommand(document, persistConsumedDocument);
                result = MainRecordingSettingsActionResult.Success("공유 설정 명령을 이미 처리했습니다.");
                return true;
            }

            if (!shouldStartFbxImport)
            {
                if (shouldClearSkippedCommand)
                {
                    ConsumeCommand(document, persistConsumedDocument);
                }

                result = MainRecordingSettingsActionResult.Success(
                    "Skipped pending FBX import command during initial settings load.");
                return true;
            }

            result = default;
            return false;
        }

        private MainRecordingSettingsActionResult ExecuteImportCommand(
            MainRecordingSettingsDocument document,
            MainRecordingSettingsCommandEnvelope command,
            Func<string, bool> fileExists,
            Func<string, bool> tryStartFbxImport,
            Action<MainRecordingSettingsDocument> persistConsumedDocument)
        {
            if (!TryValidateImportCommand(
                command,
                fileExists,
                tryStartFbxImport,
                out string commandFbxPath,
                out string failureMessage))
            {
                ConsumeCommand(document, persistConsumedDocument);
                return MainRecordingSettingsActionResult.Failure(failureMessage);
            }

            if (!tryStartFbxImport(commandFbxPath))
            {
                ConsumeCommand(document, persistConsumedDocument);
                return MainRecordingSettingsActionResult.Failure("FBX 가져오기를 시작하지 못했습니다.");
            }

            lastHandledCommandId = command.commandId;
            ConsumeCommand(document, persistConsumedDocument);
            return MainRecordingSettingsActionResult.Success("FBX 가져오기를 시작했습니다.");
        }

        private static bool TryValidateImportCommand(
            MainRecordingSettingsCommandEnvelope command,
            Func<string, bool> fileExists,
            Func<string, bool> tryStartFbxImport,
            out string commandFbxPath,
            out string failureMessage)
        {
            commandFbxPath = string.IsNullOrWhiteSpace(command.fbxPath)
                ? string.Empty
                : command.fbxPath.Trim();
            if (string.IsNullOrEmpty(commandFbxPath))
            {
                failureMessage = "FBX 명령 경로가 비어 있습니다.";
                return false;
            }

            if (tryStartFbxImport == null)
            {
                failureMessage = "FBX 명령을 적용할 FBXVmdPipeline를 찾지 못했습니다.";
                return false;
            }

            if (!fileExists(commandFbxPath))
            {
                failureMessage = $"FBX 파일을 찾을 수 없습니다: {commandFbxPath}";
                return false;
            }

            failureMessage = null;
            return true;
        }

        private static void ConsumeCommand(
            MainRecordingSettingsDocument document,
            Action<MainRecordingSettingsDocument> persistConsumedDocument)
        {
            document.pendingCommand = new MainRecordingSettingsCommandEnvelope();
            persistConsumedDocument?.Invoke(document);
        }
    }
}
