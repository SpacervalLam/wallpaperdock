using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Diagnostics;
using System.Threading.Tasks;

namespace WallpaperDockWinUI.Views
{
    public sealed partial class ToastNotification : UserControl
    {
        private readonly DispatcherTimer _closeTimer;

        public ToastNotification()
        {
            this.InitializeComponent();

            // Initialize close timer
            _closeTimer = new DispatcherTimer();
            _closeTimer.Interval = TimeSpan.FromSeconds(3);
            _closeTimer.Tick += CloseTimer_Tick;
            _closeTimer.Start();
        }

        public string Title
        {
            get => TitleText.Text;
            set => TitleText.Text = value;
        }

        public string Message
        {
            get => MessageText.Text;
            set => MessageText.Text = value;
        }

        private void CloseTimer_Tick(object sender, object e)
        {
            _closeTimer.Stop();
            Close();
        }

        private void Close()
        {
            if (this.Parent is Panel parent)
            {
                parent.Children.Remove(this);
            }
        }

        private void SpeechPrivacyLink_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Open speech privacy settings
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:privacy-speechtyping",
                UseShellExecute = true
            });
            Close();
        }

        private void MicrophonePrivacyLink_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // Open microphone privacy settings
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:privacy-microphone",
                UseShellExecute = true
            });
            Close();
        }
    }
}
