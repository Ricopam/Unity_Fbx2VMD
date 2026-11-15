namespace Fbx2Vmd.CharacterLibrary
{
    // 로드 상태·오류·활성 엔트리 식별자 상태 분할.
    public sealed partial class CharacterLibraryCoordinator
    {
        private LoadState _state = LoadState.Idle;
        private string _lastError = string.Empty;
        private string _activeEntryId = string.Empty;
    }
}
