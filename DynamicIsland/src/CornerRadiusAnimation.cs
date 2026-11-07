using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Island
{
    /// <summary>
    /// WPF 没有内置的 CornerRadiusAnimation，这里补一个。
    /// 注意：From/To/Radius 必须是依赖属性 —— 动画时间轴会被 WPF 克隆，
    /// 普通字段在克隆时会丢失，导致圆角动画变成 0。
    /// </summary>
    internal class CornerRadiusAnimation : AnimationTimeline
    {
        public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
            "From", typeof(CornerRadius), typeof(CornerRadiusAnimation),
            new PropertyMetadata(new CornerRadius(0)));

        public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
            "To", typeof(CornerRadius), typeof(CornerRadiusAnimation),
            new PropertyMetadata(new CornerRadius(0)));

        public static readonly DependencyProperty EasingProperty = DependencyProperty.Register(
            "Easing", typeof(EasingFunctionBase), typeof(CornerRadiusAnimation),
            new PropertyMetadata(null));

        public CornerRadius From
        {
            get { return (CornerRadius)GetValue(FromProperty); }
            set { SetValue(FromProperty, value); }
        }

        public CornerRadius To
        {
            get { return (CornerRadius)GetValue(ToProperty); }
            set { SetValue(ToProperty, value); }
        }

        public EasingFunctionBase Easing
        {
            get { return (EasingFunctionBase)GetValue(EasingProperty); }
            set { SetValue(EasingProperty, value); }
        }

        public override Type TargetPropertyType { get { return typeof(CornerRadius); } }

        protected override Freezable CreateInstanceCore()
        {
            return new CornerRadiusAnimation();
        }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock clock)
        {
            double p = clock.CurrentProgress.HasValue ? clock.CurrentProgress.Value : 1.0;
            EasingFunctionBase ease = Easing;
            if (ease != null) p = ease.Ease(p);
            return Lerp(p);
        }

        CornerRadius Lerp(double p)
        {
            CornerRadius a = From, b = To;
            return new CornerRadius(
                a.TopLeft + (b.TopLeft - a.TopLeft) * p,
                a.TopRight + (b.TopRight - a.TopRight) * p,
                a.BottomRight + (b.BottomRight - a.BottomRight) * p,
                a.BottomLeft + (b.BottomLeft - a.BottomLeft) * p);
        }
    }
}
