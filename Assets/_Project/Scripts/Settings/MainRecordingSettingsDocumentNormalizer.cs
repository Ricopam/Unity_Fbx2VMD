namespace Fbx2Vmd.Settings
{
    internal static class MainRecordingSettingsDocumentNormalizer
    {
        internal static MainRecordingSettingsDocument Normalize(
            MainRecordingSettingsDocument document)
        {
            if (document == null)
            {
                return new MainRecordingSettingsDocument();
            }

            NormalizeDocumentRoot(document);
            NormalizePendingCommand(document);
            NormalizeLastCommandResult(document);
            return document;
        }

        private static void NormalizeDocumentRoot(MainRecordingSettingsDocument document)
        {
            if (document.schemaVersion <= 0)
            {
                document.schemaVersion = 1;
            }

            if (document.captureWidth <= 0)
            {
                document.captureWidth = 1920;
            }

            if (document.captureHeight <= 0)
            {
                document.captureHeight = 1080;
            }

            EnsureNonEmpty(ref document.updatedAtUtc);
            EnsureNonEmpty(ref document.fbxPath);
            EnsureNonEmpty(ref document.characterModelPath);

            if (document.runtimeState == null)
            {
                document.runtimeState = new MainRecordingSettingsState();
            }

            document.runtimeState.Normalize();
        }

        private static void NormalizePendingCommand(MainRecordingSettingsDocument document)
        {
            if (document.pendingCommand == null)
            {
                document.pendingCommand = new MainRecordingSettingsCommandEnvelope();
            }

            MainRecordingSettingsCommandEnvelope command = document.pendingCommand;
            EnsureNonEmpty(ref command.commandId);
            EnsureNonEmpty(ref command.action);
            EnsureNonEmpty(ref command.fbxPath);
            EnsureNonEmpty(ref command.characterId);
            EnsureNonEmpty(ref command.presetId);
            EnsureNonEmpty(ref command.path);
            EnsureNonEmpty(ref command.paramsJson);
            EnsureNonEmpty(ref command.requestedAtUtc);
        }

        private static void NormalizeLastCommandResult(MainRecordingSettingsDocument document)
        {
            if (document.lastCommandResult == null)
            {
                document.lastCommandResult = new MainRecordingSettingsCommandResult();
            }

            MainRecordingSettingsCommandResult lastResult = document.lastCommandResult;
            EnsureNonEmpty(ref lastResult.commandId);
            EnsureNonEmpty(ref lastResult.action);
            EnsureNonEmpty(ref lastResult.state);
            EnsureNonEmpty(ref lastResult.message);
            EnsureNonEmpty(ref lastResult.finishedAtUtc);
        }

        private static void EnsureNonEmpty(ref string value)
        {
            if (value == null)
            {
                value = string.Empty;
            }
        }
    }
}
