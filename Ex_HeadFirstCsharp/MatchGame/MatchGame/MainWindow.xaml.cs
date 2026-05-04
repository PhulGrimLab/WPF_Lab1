using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MatchGame
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        TextBlock lastTextBlockClicked;
        bool findingMatch = false;

        DispatcherTimer timer = new DispatcherTimer();
        int tenthsOfSecondsElapsed;
        int matchesFound;

        public MainWindow()
        {
            InitializeComponent();

            timer.Interval = TimeSpan.FromSeconds(0.1);
            timer.Tick += Timer_Tick;

            SetUpGame();
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void SetUpGame()
        {
            List<string> animalEmoji = new List<string>()
            {
                "🐉","🐉",
                "🦄","🦄",
                "🦓","🦓",
                "🐮","🐮",
                "🐭","🐭",
                "🐰","🐰",
                "🦈","🦈",
                "🐳","🐳",
            };

            Random random = new Random();
            foreach (TextBlock textBlock in MainGrid.Children.OfType<TextBlock>())
            {
                if (textBlock.Name != "timeTextBlock")
                {
                    textBlock.Visibility = Visibility.Visible;
                    int index = random.Next(animalEmoji.Count);

                    string nextemoji = animalEmoji[index];
                    textBlock.Text = nextemoji;
                    animalEmoji.RemoveAt(index);
                }
            }
        }

        private void TextBlock_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TextBlock textBlock = sender as TextBlock;
            if (findingMatch == false)      // 첫번째 동물 클릭
            {
                textBlock.Visibility = Visibility.Hidden;
                lastTextBlockClicked = textBlock;
                findingMatch = true;
            }
            else if (textBlock.Text == lastTextBlockClicked.Text)   // 첫번째 두번째 일치 할때
            {
                textBlock.Visibility = Visibility.Hidden;
                findingMatch = false;
            }
            else   // 첫번째와 다른 동물 클릭 했을때
            {
                lastTextBlockClicked.Visibility = Visibility.Visible;
                findingMatch = false;
            }
        }
    }
}