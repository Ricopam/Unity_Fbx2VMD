namespace Fbx2Vmd.CharacterLibrary
{
    // 코디네이터의 로드 상태 전이와 파괴 시점 정리 책임 partial.
    public sealed partial class CharacterLibraryCoordinator
    {
        private void SetState(LoadState state)
        {
            if (_state == state)
            {
                return;
            }

            _state = state;
            StateChanged?.Invoke(state);
        }

        private void OnDestroy()
        {
            CancelLoading();
            if (_cancellation != null)
            {
                _cancellation.Dispose();
                _cancellation = null;
            }
            if (_current == this)
            {
                _current = null;
            }
        }

    }
}
