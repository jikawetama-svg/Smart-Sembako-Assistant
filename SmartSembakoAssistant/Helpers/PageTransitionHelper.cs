using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SmartSembakoAssistant.Helpers
{
    /// <summary>
    /// Provides page transition animations for view switching
    /// </summary>
    public static class PageTransitionHelper
    {
        /// <summary>
        /// Apply fade-in + slide-up animation to a page
        /// </summary>
        public static async Task FadeInAsync(UIElement element, int durationMs = 300)
        {
            if (element == null) return;

            // Set initial state
            element.Opacity = 0;
            var transform = new TranslateTransform(0, 20);
            element.RenderTransform = transform;

            // Fade-in animation
            var fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // Slide-up animation
            var slideUp = new DoubleAnimation
            {
                From = 20,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            // Start animations
            element.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            transform.BeginAnimation(TranslateTransform.YProperty, slideUp);

            // Wait for animation to complete
            await Task.Delay(durationMs);
        }

        /// <summary>
        /// Apply fade-out animation before removing a page
        /// </summary>
        public static async Task FadeOutAsync(UIElement element, int durationMs = 200)
        {
            if (element == null) return;

            var fadeOut = new DoubleAnimation
            {
                From = 1.0,
                To = 0.0,
                Duration = TimeSpan.FromMilliseconds(durationMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (s, e) => element.Visibility = Visibility.Collapsed;
            element.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            await Task.Delay(durationMs);
        }

        /// <summary>
        /// Apply smooth transition when switching content in a ContentControl
        /// </summary>
        public static async Task SwitchContentAsync(ContentControl container, UIElement newContent, int durationMs = 300)
        {
            if (container == null || newContent == null) return;

            // Fade out current content
            var currentContent = container.Content as UIElement;
            if (currentContent != null)
            {
                await FadeOutAsync(currentContent, durationMs / 2);
            }

            // Set new content
            container.Content = newContent;
            newContent.Visibility = Visibility.Visible;

            // Fade in new content
            await FadeInAsync(newContent, durationMs / 2);
        }
    }
}
