using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using FluentAssertions;
using JunkManager.App.Controls.Behaviors;
using Xunit;

namespace JunkManager.Tests.Ui;

[Trait("Class", "Ui")]
[Collection(UiNabor.Imya)]
public sealed class WelcomeEntranceTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1031:Do not catch general exception types",
        Justification = "The STA thread transports the original exception to the test thread through ExceptionDispatchInfo and rethrows it there.")]
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Entrance_moves_after_render_honours_reduced_motion_and_does_not_repeat(bool motion)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                var mark = new Border { Width = 100, Height = 100, Background = Brushes.MediumPurple };
                WelcomeEntrance.SetEnabled(mark, true);
                window = new Window { Width = 220, Height = 220, Content = mark, ShowInTaskbar = false };
                window.Resources["MotionEnabled"] = motion;
                window.Show();
                Pump(TimeSpan.FromMilliseconds(160));
                if (motion)
                {
                    mark.Opacity.Should().BeInRange(0.01, 0.99);
                    ((TranslateTransform)mark.RenderTransform).Y.Should().BeGreaterThan(0);
                }
                else mark.Opacity.Should().Be(1);
                Pump(TimeSpan.FromMilliseconds(750));
                mark.Opacity.Should().Be(1);
                window.Hide();
                window.Show();
                Pump(TimeSpan.FromMilliseconds(120));
                mark.Opacity.Should().Be(1);
            }
            catch (Exception error) { failure = ExceptionDispatchInfo.Capture(error); }
            finally { window?.Close(); }
        });
        // WPF wants its own STA; democracy was never on the compositor's roadmap.
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    private static void Pump(TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = duration };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
}
