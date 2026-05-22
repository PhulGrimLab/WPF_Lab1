using System.Windows;

namespace WpfSamples
{
    public partial class MainWindow : Window
    {
        private readonly MainWindowViewModel _viewModel = new();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        protected override async void OnClosed(EventArgs e)
        {
            await _viewModel.DisposeAsync();
            base.OnClosed(e);

            if (Application.Current is not null)
            {
                Application.Current.Shutdown();
            }
        }
    }
}
