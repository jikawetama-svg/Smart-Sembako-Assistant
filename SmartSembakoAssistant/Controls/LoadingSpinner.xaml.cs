using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartSembakoAssistant.Controls
{
    public partial class LoadingSpinner : UserControl
    {
        public static readonly DependencyProperty IsLoadingProperty =
            DependencyProperty.Register(nameof(IsLoading), typeof(bool), typeof(LoadingSpinner),
                new PropertyMetadata(false, OnIsLoadingChanged));

        public static readonly DependencyProperty LoadingTextProperty =
            DependencyProperty.Register(nameof(LoadingText), typeof(string), typeof(LoadingSpinner),
                new PropertyMetadata("Loading..."));

        public bool IsLoading
        {
            get => (bool)GetValue(IsLoadingProperty);
            set => SetValue(IsLoadingProperty, value);
        }

        public string LoadingText
        {
            get => (string)GetValue(LoadingTextProperty);
            set => SetValue(LoadingTextProperty, value);
        }

        public LoadingSpinner()
        {
            InitializeComponent();
            Loaded += LoadingSpinner_Loaded;
        }

        private void LoadingSpinner_Loaded(object sender, RoutedEventArgs e)
        {
            if (IsLoading)
                StartAnimation();
        }

        private static void OnIsLoadingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var spinner = (LoadingSpinner)d;
            if ((bool)e.NewValue)
                spinner.StartAnimation();
            else
                spinner.StopAnimation();
        }

        private void StartAnimation()
        {
            var duration = TimeSpan.FromMilliseconds(600);
            var ease = new SineEase { EasingMode = EasingMode.EaseInOut };

            // Animate dots bouncing
            var anim1 = new DoubleAnimation
            {
                From = 0,
                To = -8,
                Duration = duration,
                EasingFunction = ease,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            var anim2 = new DoubleAnimation
            {
                From = 0,
                To = -8,
                Duration = duration,
                EasingFunction = ease,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(150)
            };

            var anim3 = new DoubleAnimation
            {
                From = 0,
                To = -8,
                Duration = duration,
                EasingFunction = ease,
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = TimeSpan.FromMilliseconds(300)
            };

            Dot1.RenderTransform.BeginAnimation(TranslateTransform.YProperty, anim1);
            Dot2.RenderTransform.BeginAnimation(TranslateTransform.YProperty, anim2);
            Dot3.RenderTransform.BeginAnimation(TranslateTransform.YProperty, anim3);
        }

        private void StopAnimation()
        {
            Dot1.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
            Dot2.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);
            Dot3.RenderTransform.BeginAnimation(TranslateTransform.YProperty, null);

            // Reset position
            Dot1.RenderTransform = new TranslateTransform(0, 0);
            Dot2.RenderTransform = new TranslateTransform(0, 0);
            Dot3.RenderTransform = new TranslateTransform(0, 0);
        }
    }
}
