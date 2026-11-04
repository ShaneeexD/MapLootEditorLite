namespace ULE.SpawnEditor
{
    // A completion from an old spawn or closed window cannot reveal current content.
    internal sealed class EditorPresentationState
    {
        private int _generation;
        private bool _binding;
        internal bool Ready { get; private set; }
        internal int BeginBinding(bool preserveVisible = false)
        {
            Ready = preserveVisible && Ready;
            _binding = true;
            return ++_generation;
        }
        internal bool IsCurrent(int ticket) => _binding && ticket == _generation;
        internal bool Complete(int ticket)
        {
            if (!IsCurrent(ticket)) return false;
            _binding = false;
            Ready = true;
            return true;
        }
        internal void Hide() { ++_generation; _binding = false; Ready = false; }
    }
}
