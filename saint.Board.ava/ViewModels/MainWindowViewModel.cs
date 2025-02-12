using System.Collections.Generic;
using Avalonia.Input;

namespace saint.Board.ava.ViewModels
{
    public partial class MainWindowViewModel : ViewModelBase
    {
        
        private int _threadSleep = 10;
        
        public int ThreadSleep => _threadSleep;
        
        private List<PointerType> _inputDevices = [PointerType.Pen];

        public List<PointerType> InputDevices => _inputDevices;
    }
}
