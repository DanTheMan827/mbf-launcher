namespace MBF_Launcher
{
    public partial class App : Application
    {
        private MainPage mainPage = new MainPage();
        public App()
        {
            InitializeComponent();
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            mainPage.newWindow = true;
            return new Window(new NavigationPage(mainPage));
        }
    }
}
